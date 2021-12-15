namespace VanBot.Bots {
	using System;
	using System.Diagnostics;
	using System.Linq;
	using System.Net;
	using System.Threading;
	using System.Threading.Tasks;

	using global::VanBot.Auctions;
	using global::VanBot.Exceptions;
	using global::VanBot.Helpers;
	using global::VanBot.HttpClients;
	using global::VanBot.Logger;
	using global::VanBot.Settings;

	using HtmlAgilityPack;

	public class VanBot {
		private readonly TimeSpan interval;
		private readonly Reserver reserver;
		private readonly Scraper scraper;
		private readonly bool shouldTryToExtend;
		private readonly TelegramBot telegramBot;
		private readonly int testIteration;
		private readonly Stopwatch timer;
		private readonly string urlToScrape;
		private AuctionCollection allAuctions;
		private CancellationToken cancellationToken;
		private int iterations;
		private int iterationsSinceNew;
		private bool shouldTryToReserve;

		public VanBot(Options options) {
			this.urlToScrape = options.Url;
			this.iterations = 0;
			this.iterationsSinceNew = 0;
			this.reserver = new Reserver(options.Username, options.Password, options.PaymentMethod);
			this.scraper = new Scraper();
			this.telegramBot = new TelegramBot(options.TelegramKey);
			this.interval = TimeSpan.FromMilliseconds(options.Interval);
			this.timer = new Stopwatch();
			this.allAuctions = new AuctionCollection();
			this.shouldTryToReserve = !options.NoSignIn;
			this.shouldTryToExtend = options.PaymentMethod != PaymentMethod.None;
			this.testIteration = options.TestIteration;
		}

		private bool IsTestRun => this.iterations == this.testIteration;

		internal void Run(CancellationToken token) {
			this.cancellationToken = token;

			if(this.shouldTryToReserve) {
				Log.Info("Testing given credentials");
				this.reserver.Initialize(out var errors);
				if(!this.CheckCredentials(errors)) {
					return;
				}
			}

			Log.Info("Testing Telegram bot chat key");
			if(!this.CheckTelegramBot()) {
				return;
			}

			var pageHtml = this.scraper.GetHtml(this.urlToScrape, out var status);
			if(status != HttpStatusCode.OK) {
				Log.Error($"Request to '{this.urlToScrape}' responded with status '{status}'");
				Log.Error("Aborting...");
				return;
			}

			Log.Info("Fetching initial auctions");
			this.allAuctions = AuctionCollection.ParseFromHtml(pageHtml, (message) => Log.Info(message, LoggerColor.Cyan));
			var hashValue = Utilities.CalculateCrc32(pageHtml);

			var currentAuctions = this.allAuctions;
			var currentHashValue = hashValue;

			Log.Info("Waiting for new auctions...");
			while(!this.cancellationToken.IsCancellationRequested) {
				this.timer.Restart();
				this.iterations++;
				this.iterationsSinceNew++;
				var pageUpdated = false;

				try {
					var currentHtml = this.scraper.GetHtml(this.urlToScrape, out status);
					if(this.IsTestRun) {
						currentHtml = AddMockAuction(currentHtml);
					}
					currentHashValue = Utilities.CalculateCrc32(currentHtml);

					pageUpdated = hashValue != currentHashValue;
					if(pageUpdated) {
						currentAuctions = AuctionCollection.ParseFromHtml(currentHtml, this.allAuctions, (message) => Log.Info(message, LoggerColor.Cyan));
						var (addedAuctions, removedAuctions) = AuctionCollection.GetAddedAndRemoved(this.allAuctions, currentAuctions);

						if(this.shouldTryToReserve) {
							this.NotifyAndReserveAuctions(addedAuctions);
						} else {
							this.NotifyAuctions(addedAuctions);
						}

						foreach(var oldAuction in removedAuctions) {
							Log.Info($"Auction '{oldAuction.Name}' expired", LoggerColor.Purple);
						}

						if(addedAuctions.Any()) {
							this.iterationsSinceNew = 0;
						}
					}
				} catch(Exception e) {
					Log.Error(e.Message);
				} finally {
					this.allAuctions = currentAuctions;
					hashValue = currentHashValue;
					this.timer.Stop();
					LogStatus(
						new StatusInfo(
							this.iterations,
							this.iterationsSinceNew,
							pageUpdated,
							status,
							this.timer.ElapsedMilliseconds
						));
					var sleepTime = this.interval - this.timer.Elapsed;
					if(sleepTime.TotalMilliseconds > 0) {
						this.Wait(Convert.ToInt32(sleepTime.TotalMilliseconds));
					}
				}
			}
		}

		private static string AddMockAuction(string html) {
			var htmlDoc = new HtmlDocument();
			htmlDoc.LoadHtml(html);
			var searchResultNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@id=\"cars-search-results\"]");
			var auctionNode = searchResultNode.SelectSingleNode(".//a[@href]");
			var duplicate = auctionNode.CloneNode(true);
			var mockUri = $"{auctionNode.Attributes["href"].Value}?foo=bar";
			duplicate.SetAttributeValue("href", mockUri);
			auctionNode.ParentNode.ChildNodes.Add(duplicate);
			return htmlDoc.DocumentNode.OuterHtml;
		}

		private static void LogStatus(StatusInfo statusInfo) {
			var reqColor = LoggerColor.Reset;
			var updatedColor = statusInfo.PageUpdated ? LoggerColor.Green : LoggerColor.Red;
			var statusColor = statusInfo.ScraperStatus == HttpStatusCode.OK ? LoggerColor.Green : LoggerColor.Yellow;
			var timeColor = statusInfo.LoopTime > 100 ? LoggerColor.Red : statusInfo.LoopTime > 50 ? LoggerColor.Yellow : LoggerColor.Green;

			var formattedStatus = $"[req: {reqColor}{statusInfo.Iterations}{LoggerColor.Reset}]"
				+ $" [req_since_new: {reqColor}{statusInfo.IterationsSinceNew}{LoggerColor.Reset}]"
				+ $" [page_updated: {updatedColor}{(statusInfo.PageUpdated ? $"yes{LoggerColor.Reset}]" : $"no{LoggerColor.Reset}]"),-5}"
				+ $" [scraper_status: {$"{(int) statusInfo.ScraperStatus} ({statusColor}{statusInfo.ScraperStatus}{LoggerColor.Reset})]",-10}"
				+ $" [time: {timeColor}{$"{statusInfo.LoopTime}",-4}{LoggerColor.Reset} ms]";
			Log.Info(formattedStatus);
		}

		private bool CheckCredentials(string[] errors) {
			if(!errors.Any()) {
				return true;
			}
			foreach(var error in errors) {
				Log.Error(error);
			}
			if(!Utilities.PromptForConfirmation("Continue anyway? (y/n): ")) {
				return false;
			}
			this.shouldTryToReserve = false;
			return true;
		}

		private bool CheckTelegramBot() {
			var isChatKeyOk = Task.Run(() => this.telegramBot.TestChatKey()).Result;
			return isChatKeyOk || Utilities.PromptForConfirmation("Continue anyway? (y/n): ");
		}

		private void NotifyAndReserveAuctions(AuctionCollection auctions) {
			if(!auctions.Any()) {
				return;
			}
			foreach(var auction in auctions) {
				if(auction.IsForScrapyards) {
					continue;
				}

				try {
					Log.Info($"Attempting to reserve '{auction.Name}'");
					this.reserver.AttemptToReserveAuction(auction);
				} catch(ReservationException e) {
					Log.Error($"Error while reserving '{auction.Name}': {e.Message}");
				}
			}

			this.Wait(2000); // wait for the reservation to go through
			var reservedAuctionName = this.reserver.GetReservedAuctionName(out var expirationTime);

			if(reservedAuctionName == null) {
				Log.Error("Could not reserve any auctions");
			} else {
				if(this.IsTestRun) {
					reservedAuctionName += "/?foo=bar";
				}
				if(this.shouldTryToExtend) {
					var reservedAuction = auctions[reservedAuctionName];
					Log.Info($"Extending reservation of '{reservedAuction.Name}'");
					try {
						this.reserver.ExtendReservation(reservedAuction, ref expirationTime);
					} catch(ReservationException e) {
						Log.Error($"Error while extending reservation '{reservedAuction.Name}': {e.Message}");
					}
				}
			}

			foreach(var auction in auctions) {
				if(auction.Name == reservedAuctionName) {
					var reservedFor = TimeSpan.FromMilliseconds(expirationTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
					Log.Info($"Auction '{auction.Name}' successfully reserved for {reservedFor.Minutes} min {reservedFor.Seconds} s", LoggerColor.Green);
					this.telegramBot.NotifyNewReservation(auction, reservedFor);
				} else {
					this.telegramBot.NotifyNewAuction(auction);
				}
			}
		}

		private void NotifyAuctions(AuctionCollection auctions) {
			if(!auctions.Any()) {
				return;
			}
			var logColor = LoggerColor.Cyan;
			Log.Info($"New auction{(auctions.Count() > 1 ? "s" : string.Empty)}!", logColor);
			foreach(var auction in auctions) {
				Log.Info(auction.ToString(), logColor);
				this.telegramBot.NotifyNewAuction(auction);
			}
		}

		private void Wait(int millis) {
			this.cancellationToken.WaitHandle.WaitOne(millis);
		}
	}

	public record StatusInfo(int Iterations, int IterationsSinceNew, bool PageUpdated, HttpStatusCode ScraperStatus, long LoopTime);
}