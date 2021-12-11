namespace VanBot.Bots {
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Net;
	using System.Threading;
	using System.Threading.Tasks;

	using global::VanBot.Exceptions;
	using global::VanBot.Helpers;
	using global::VanBot.HttpClients;
	using global::VanBot.Logger;

	using HtmlAgilityPack;

	public class VanBot {
		private const string DefaultScrapingUrl = "https://www.vaurioajoneuvo.fi/";
		private const string MockAuctionUri = "/tuote/toyota-toyota-yaris-hybrid-monikayttoajoneuvo-af-4ov-1497cm3-mlc-104/?foo=bar";

		private readonly TimeSpan interval;
		private readonly Reserver reserver;
		private readonly Scraper scraper;
		private readonly TelegramBot telegramBot;
		private readonly int testIteration = 15;
		private readonly Stopwatch timer;
		private readonly string urlToScrape;
		private Auctions allAuctions;
		private int iterations;
		private int iterationsSinceNew;
		private bool shouldTryToReserve;

		public VanBot(Options options) {
			this.urlToScrape = string.IsNullOrWhiteSpace(options.Url) ? DefaultScrapingUrl : options.Url;
			this.iterations = 0;
			this.iterationsSinceNew = 0;
			this.reserver = new Reserver(options.Username, options.Password);
			this.scraper = new Scraper();
			this.telegramBot = new TelegramBot(options.TelegramKey);
			this.interval = TimeSpan.FromMilliseconds(options.Interval);
			this.timer = new Stopwatch();
			this.allAuctions = new Auctions();
			this.shouldTryToReserve = !options.NoSignIn;
			if(!Utilities.IsDebug()) {
				this.testIteration = -1;
			}
		}

		internal void Run(CancellationToken cancellationToken) {
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
			this.allAuctions = Auctions.ParseFromHtml(pageHtml, (message) => Log.Info(message, LoggerColor.Cyan));
			var hashValue = Utilities.CalculateCrc32(pageHtml);

			Log.Info("Waiting for new auctions...");
			while(!cancellationToken.IsCancellationRequested) {
				this.timer.Restart();
				this.iterations++;
				this.iterationsSinceNew++;

				try {
					var currentHtml = this.scraper.GetHtml(this.urlToScrape, out status);
					if(this.iterations == this.testIteration) {
						currentHtml = AddMockAuction(currentHtml, MockAuctionUri);
					}
					var currentHashValue = Utilities.CalculateCrc32(currentHtml);

					var pageUpdated = hashValue != currentHashValue;
					if(pageUpdated) {
						this.iterationsSinceNew = 0;

						var currentAuctions = Auctions.ParseFromHtml(currentHtml, null, this.allAuctions);
						var (addedKeys, removedKeys) = Utilities.GetAddedAndRemoved(this.allAuctions.GetKeys(), currentAuctions.GetKeys());
						var addedAuctions = addedKeys.Select(key => currentAuctions[key]).ToArray();

						if(this.shouldTryToReserve) {
							this.NotifyAndReserveAuctions(ref addedAuctions);
						} else {
							this.NotifyAuctions(addedAuctions);
						}

						foreach(var oldKey in removedKeys) {
							Log.Info($"Auction '{oldKey}' expired", LoggerColor.Purple);
						}

						this.allAuctions = currentAuctions;
					}

					hashValue = currentHashValue;
					this.timer.Stop();
					Log.Info(
						$"[req: {this.iterations}]"
						+ $" [req_since_new: {this.iterationsSinceNew}]"
						+ $" [page_updated: {(pageUpdated ? "yes]" : "no]"),-4}"
						+ $" [status: {$"{(int) status} ({status})]",-10}"
						+ $" [time: {$"{this.timer.ElapsedMilliseconds}",-4} ms]"
					);
					var sleepTime = this.interval - this.timer.Elapsed;
					if(sleepTime.TotalMilliseconds > 0) {
						cancellationToken.WaitHandle.WaitOne(Convert.ToInt32(sleepTime.TotalMilliseconds));
					}
				} catch(Exception e) {
					Log.Error($"Error: {e.Message}");
				}
			}
		}

		private static string AddMockAuction(string html, string mockUri) {
			var htmlDoc = new HtmlDocument();
			htmlDoc.LoadHtml(html);
			var searchResultNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@id=\"cars-search-results\"]");
			var auctionNode = searchResultNode.SelectSingleNode(".//a[@href]");
			auctionNode.SetAttributeValue("href", mockUri);
			return htmlDoc.DocumentNode.OuterHtml;
		}

		private bool CheckCredentials(string[] errors) {
			if(!errors.Any()) {
				return true;
			}
			foreach(var error in errors) {
				Log.Error(error);
			}
			Console.WriteLine("Continue anyway? (y/n)");
			var answer = Console.ReadLine()?.ToLower();
			if(new[] {"y", "yes", "true"}.Contains(answer)) {
				this.shouldTryToReserve = false;
				return true;
			}
			return false;
		}

		private bool CheckTelegramBot() {
			var isChatKeyOk = Task.Run(() => this.telegramBot.TestChatKey()).Result;
			if(isChatKeyOk) {
				return true;
			}

			Console.WriteLine("Continue anyway? (y/n)");
			var answer = Console.ReadLine()?.ToLower();
			return new[] {"y", "yes", "true"}.Contains(answer);
		}

		private void NotifyAndReserveAuctions(ref Auction[] auctions) {
			foreach(var auction in auctions) {
				Log.Info(auction.ToString(), LoggerColor.Cyan);

				if(auction.IsForScrapyards) {
					continue;
				}

				var elapsedWhileReserving = 0L;
				try {
					if(!this.reserver.ReserveAuction(auction, out var alreadyReserved, out elapsedWhileReserving)) {
						Log.Warning(
							$@"Could not reserve auction '{auction.Uri}'{(alreadyReserved
								? " because it is already reserved" : string.Empty)} ({elapsedWhileReserving} ms)");
						continue;
					}
					auction.ReservationSuccess = true;
				} catch(ReservationException e) {
					Log.Error($"Error while reserving auction '{auction.Uri}'  ({elapsedWhileReserving} ms): {e.Message}");
				} finally {
					auction.ElapsedWhileReserving = elapsedWhileReserving;
				}
			}

			foreach(var auction in auctions) {
				if(auction.ReservationSuccess) {
					Log.Info($"Auction '{auction.Uri}' successfully reserved ({auction.ElapsedWhileReserving} ms)", LoggerColor.Green);
					this.telegramBot.NotifyNewReservation(auction);
				} else {
					this.telegramBot.NotifyNewAuction(auction);
				}
			}
		}

		private void NotifyAuctions(IEnumerable<Auction> auctions) {
			foreach(var auction in auctions) {
				Log.Info(auction.ToString(), LoggerColor.Cyan);
				this.telegramBot.NotifyNewAuction(auction);
			}
		}
	}
}