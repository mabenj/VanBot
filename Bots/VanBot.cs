namespace VanBot.Bots {
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Net;
	using System.Net.Http;
	using System.Threading;
	using System.Threading.Tasks;

	using global::VanBot.BrowserAgents;
	using global::VanBot.Exceptions;
	using global::VanBot.Utilities;

	using HtmlAgilityPack;

	public class VanBot {
		private const string MockAuctionUri = "/tuote/toyota-hiace-van-2-5td-klh12l-rbmryw-299-hrz-576-2/?foo=bar";
		private const int ReservationAgentRefreshInterval = -25000;

		// ReSharper disable once IdentifierTypo
		private static readonly HashSet<int> AllBrowserPids = new();
		private static readonly object LockObject = new();
		private readonly HttpClient httpClient;
		private readonly TimeSpan interval;
		private readonly ReservationAgent reservationAgent;
		private readonly bool shouldSignIn;
		private readonly TelegramBot telegramBot;
		private readonly int testIteration = 500;
		private readonly Stopwatch timer;
		private readonly string urlToScrape;
		private Auctions allAuctions;
		private int iterations;
		private int iterationsSinceNew;

		public VanBot(Options options, string url) {
			this.urlToScrape = url;
			this.httpClient = new HttpClient();
			this.iterations = 0;
			this.iterationsSinceNew = 0;
			this.reservationAgent = new ReservationAgent(options.Username, options.Password, !options.ShowHead);
			this.telegramBot = new TelegramBot(options.TelegramKey);
			this.interval = TimeSpan.FromMilliseconds(options.Interval);
			this.timer = new Stopwatch();
			this.allAuctions = new Auctions();
			this.shouldSignIn = !options.NoSignIn;
			if(!Tools.IsDebug()) {
				this.testIteration = -1;
			}
		}

		internal static void Stop() {
			lock(LockObject) {
				Log.Info("Stopping...");
				try {
					Log.Info("Killing browsers");
					foreach(var pid in AllBrowserPids) {
						try {
							Process.GetProcessById(pid).Kill();
							AllBrowserPids.Remove(pid);
						} catch(Exception) {
							Log.Warning($"Error while killing pid {pid}");
							// do nothing
						}
					}
				} catch(Exception e) {
					// ReSharper disable once StringLiteralTypo
					Log.Error($"Could not stop: {e.Message}");
					return;
				}

				Log.Info("Stopped successfully");
			}
		}

		internal void Start(CancellationToken cancellationToken) {
			// ReSharper disable once StringLiteralTypo
			Log.Info("Initializing chromedriver");
			this.reservationAgent.Initialize();
			lock(LockObject) {
				AllBrowserPids.AddRange(this.reservationAgent.GetBrowserPids());
			}

			if(this.shouldSignIn) {
				try {
					Log.Info($"Logging in as '{this.reservationAgent.Username}'");
					this.reservationAgent.LogIn();
				} catch(LoginException e) {
					Log.Warning(e.Message);
					Log.Warning("Continue anyway? (y/n)");
					var answer = Console.ReadLine()?.ToLower();
					if(!new[] { "y", "yes", "true" }.Contains(answer)) {
						Stop();
						return;
					}
				}
			}

			Log.Info("Testing Telegram bot chat key");
			if(!this.CheckTelegramBot()) {
				Stop();
				return;
			}

			var pageHtml = this.GetHtml(this.urlToScrape, out var status);
			if(status != HttpStatusCode.OK) {
				Log.Error($"Request to '{this.urlToScrape}' responded with status '{status}'");
				Log.Error("Aborting...");
				return;
			}

			Log.Info("Fetching initial auctions");
			this.allAuctions = Auctions.ParseFromHtml(pageHtml, Log.Info);
			var hashValue = Tools.CalculateCrc32(pageHtml);

			Log.Info("Waiting for new auctions...");
			while(!cancellationToken.IsCancellationRequested) {
				this.timer.Restart();
				this.iterations++;
				this.iterationsSinceNew++;

				lock(LockObject) {
					AllBrowserPids.Clear();
					AllBrowserPids.AddRange(this.reservationAgent.GetBrowserPids());
				}

				try {
					if(this.iterations % ReservationAgentRefreshInterval == 0) {
						this.reservationAgent.GoToPage(this.reservationAgent.MainUrl);
					}
					var currentHtml = this.GetHtml(this.urlToScrape, out status);
					if(this.iterations == this.testIteration) {
						currentHtml = AddMockAuction(currentHtml, MockAuctionUri);
					}
					var currentHashValue = Tools.CalculateCrc32(currentHtml);

					var pageUpdated = hashValue != currentHashValue;
					if(pageUpdated) {
						this.iterationsSinceNew = 0;

						var pastAuctions = this.allAuctions;
						var currentAuctions = Auctions.ParseFromHtml(currentHtml, null, this.allAuctions);

						var pastKeys = pastAuctions.GetKeys();
						var currentKeys = currentAuctions.GetKeys();
						var intersection = pastKeys.Intersect(currentKeys).ToArray();

						var addedKeys = currentKeys.Except(intersection).ToArray();
						var removedKeys = pastKeys.Except(intersection);

						var addedAuctions = addedKeys.Select(key => currentAuctions[key]).ToArray();
						if(this.reservationAgent.IsLoggedIn) {
							foreach(var auction in addedAuctions) {
								Log.Info(auction.ToString());

								if(auction.IsForScrapyards) {
									continue;
								}

								var elapsedWhileReserving = 0L;
								try {
									if(!this.reservationAgent.ReserveAuction(auction, out var alreadyReserved, out elapsedWhileReserving)) {
										Log.Warning($"Could not reserve auction '{auction.Uri}'{(alreadyReserved ? " because it is already reserved" : string.Empty)} ({elapsedWhileReserving} ms)");
										continue;
									}
									auction.ReservationSuccess = true;
								} catch(ReservationException e) {
									Log.Error($"Error while reserving auction '{auction.Uri}'  ({elapsedWhileReserving} ms): {e.Message}");
								} finally {
									auction.ElapsedWhileReserving = elapsedWhileReserving;
								}
							}

							foreach(var auction in addedAuctions) {
								if(auction.ReservationSuccess) {
									Log.Info($"Auction '{auction.Uri}' successfully reserved ({auction.ElapsedWhileReserving} ms)");
									this.telegramBot.NotifyNewReservation(auction);
								} else {
									this.telegramBot.NotifyNewAuction(auction);
								}
							}
						} else {
							foreach(var auction in addedAuctions) {
								Log.Info(auction.ToString());
								this.telegramBot.NotifyNewAuction(auction);
							}
						}

						foreach(var oldKey in removedKeys) {
							Log.Info($"Auction '{oldKey}' expired");
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
					if(this.interval.TotalMilliseconds == 0) {
						continue;
					}
					var sleepTime = this.interval - this.timer.Elapsed;
					if(sleepTime.TotalMilliseconds > 0) {
						cancellationToken.WaitHandle.WaitOne(Convert.ToInt32(sleepTime.TotalMilliseconds));
					}
				} catch(CaptchaException e) {
					Log.Error(e.Message);
					this.telegramBot.NotifyCaptcha();
					break;
				} catch(Exception e) {
					Log.Error($"Error: {e.Message}");
					this.reservationAgent.Initialize();
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

		private bool CheckTelegramBot() {
			var isChatKeyOk = Task.Run(() => this.telegramBot.TestChatKey()).Result;
			if(isChatKeyOk) {
				return true;
			}

			Log.Warning("Continue anyway? (y/n)");
			var answer = Console.ReadLine()?.ToLower();
			return new[] { "y", "yes", "true" }.Contains(answer);
		}

		private string GetHtml(string url, out HttpStatusCode status) {
			var response = Task.Run(() => this.httpClient.GetAsync(url)).Result;
			status = response.StatusCode;
			return Task.Run(() => response.Content.ReadAsStringAsync()).Result;
		}
	}
}