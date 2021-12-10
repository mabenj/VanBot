#region

#endregion

namespace VanBot.Bots {
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	using global::VanBot.BrowserAgents;
	using global::VanBot.Exceptions;
	using global::VanBot.Utilities;

	public class VanBotOld {
		private const int ReservationAgentRefreshInterval = 2500;

		// ReSharper disable once IdentifierTypo
		private static readonly HashSet<int> AllBrowserPids = new();
		private static readonly object LockObj = new();

		private readonly CrawlerAgent crawlerAgent;
		private readonly TimeSpan interval;
		private readonly ReservationAgent reservationAgent;
		private readonly TelegramBot telegramBot;
		private readonly int testIteration = -5;
		private Dictionary<string, Auction2> allAuctions;
		private int iterations;
		private int iterationsSinceNew;

		public VanBotOld(Options options) {
			this.interval = TimeSpan.FromSeconds(options.Interval);
			this.telegramBot = new TelegramBot(options.TelegramKey);
			this.allAuctions = new Dictionary<string, Auction2>();
			this.iterations = 0;
			this.iterationsSinceNew = 0;
			this.reservationAgent = new ReservationAgent(options.Username, options.Password, !options.ShowHead);
			this.crawlerAgent = new CrawlerAgent();
			if(!Tools.IsDebug()) {
				this.testIteration = -1;
			}
		}

		public void Start(CancellationToken token) {
			// ReSharper disable once StringLiteralTypo
			Log.Info("Initializing chromedriver (no proxy)");
			this.reservationAgent.Initialize();
			lock(LockObj) {
				AllBrowserPids.AddRange(this.reservationAgent.GetBrowserPids());
			}

			// ReSharper disable once StringLiteralTypo
			Log.Info("Initializing chromedriver (proxy)");
			this.crawlerAgent.Initialize();
			lock(LockObj) {
				AllBrowserPids.AddRange(this.crawlerAgent.GetBrowserPids());
			}

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

			Log.Info("Testing Telegram bot chat key");
			if(!this.CheckTelegramBot()) {
				Stop();
				return;
			}

			Log.Info("Fetching initial auctions");
			this.allAuctions = this.crawlerAgent.FetchAllAuctions(Log.Info);

			Log.Info("Waiting for new auctions...");
			while(!token.IsCancellationRequested) {
				var loopStart = DateTime.Now;
				this.iterations++;
				this.iterationsSinceNew++;

				lock(LockObj) {
					AllBrowserPids.Clear();
					AllBrowserPids.AddRange(this.reservationAgent.GetBrowserPids());
					AllBrowserPids.AddRange(this.crawlerAgent.GetBrowserPids());
				}

				try {
					//this.crawlerAgent.GoToPage(this.crawlerAgent.MainUrl);
					this.crawlerAgent.Refresh();
					if(this.iterations % ReservationAgentRefreshInterval == 0) {
						this.reservationAgent.GoToPage(this.reservationAgent.MainUrl);
					}

					var pastAuctions = this.allAuctions;
					var currentAuctions = this.crawlerAgent.FetchAllAuctions(null, pastAuctions.Keys) ?? pastAuctions;

					var pastIds = pastAuctions.Keys;
					var currentIds = currentAuctions.Keys;
					var intersection = pastIds.Intersect(currentIds).ToList();

					var addedIds = currentIds.Except(intersection).ToList();
					var removedIds = pastIds.Except(intersection);

					if(this.iterations == this.testIteration && !addedIds.Any()) {
						//addedIds = new List<string>() { "49092" };
						addedIds = new List<string>() { "https://www.vaurioajoneuvo.fi/tuote/toyota-yaris-monikayttoajoneuvo-af-4ov-1298cm3-eby-909/" };
					}

					if(addedIds.Any()) {
						Log.Info($"New auction{(addedIds.Count > 1 ? "s" : "")} found!");
						var newAuctions = addedIds.Select(newId => currentAuctions[newId]).ToList();

						if(this.reservationAgent.IsLoggedIn) {
							foreach(var auction in newAuctions) {
								Log.Info(auction.ToString());

								if(auction.IsForScrapyards) {
									continue;
								}

								try {
									if(!this.reservationAgent.ReserveAuction( /*auction*/ null, out var alreadyReserved, out _)) {
										Log.Warning($"Could not reserve auction '{auction.Id}'{(alreadyReserved ? " because it is already reserved" : string.Empty)}");
										continue;
									}
									auction.ReservationSuccess = true;
								} catch(ReservationException e) {
									Log.Error($"Error while reserving auction '{auction.Id}': {e.Message}");
								}
							}

							foreach(var auction in newAuctions) {
								if(auction.ReservationSuccess) {
									Log.Info($"Auction '{auction.Id}' successfully reserved");
									this.telegramBot.NotifyNewReservation( /*auction*/ null);
								} else {
									this.telegramBot.NotifyNewAuction( /*auction*/ null);
								}
							}
						} else {
							foreach(var auction in newAuctions) {
								Log.Info(auction.ToString());
								this.telegramBot.NotifyNewAuction( /*auction*/ null);
							}
						}

						this.iterationsSinceNew = 0;
					} else {
						Log.Info($"No new auctions found... [all: {$"{this.iterations}",5}] [since_new: {$"{this.iterationsSinceNew}]",5}");
					}

					foreach(var oldId in removedIds) {
						Log.Info($"Auction '{oldId}' expired");
					}

					this.allAuctions = currentAuctions;

					if(this.interval.TotalSeconds == 0) {
						continue;
					}

					var elapsed = DateTime.Now - loopStart;
					var sleepTime = this.interval - elapsed;
					if(sleepTime.Milliseconds > 0) {
						token.WaitHandle.WaitOne(Convert.ToInt32(sleepTime.TotalMilliseconds));
					}
				} catch(CaptchaException e) {
					Log.Error(e.Message);
					this.telegramBot.NotifyCaptcha();
					break;
				} catch(Exception e) {
					Log.Error($"Error: {e.Message}");
					this.crawlerAgent.Initialize();
					this.reservationAgent.Initialize();
				}
			}

			Stop();
		}

		internal static void Stop() {
			lock(LockObj) {
				Log.Info("Stopping...");
				try {
					Log.Info("Killing browsers");
					foreach(var pid in AllBrowserPids) {
						try {
							Process.GetProcessById(pid).Kill();
							AllBrowserPids.Remove(pid);
						} catch(Exception) {
							Log.Warning($"error while killing pid {pid}");
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

		private bool CheckTelegramBot() {
			var isChatKeyOk = Task.Run(() => this.telegramBot.TestChatKey()).Result;
			if(isChatKeyOk) {
				return true;
			}

			Log.Warning("Continue anyway? (y/n)");
			var answer = Console.ReadLine()?.ToLower();
			return new[] { "y", "yes", "true" }.Contains(answer);
		}
	}

	public record Auction2 {
		public string Id {
			get;
			init;
		}

		public string Url {
			get;
			internal set;
		}

		public double Price {
			get;
			init;
		}

		public bool IsForScrapyards {
			get;
			init;
		}

		public bool ReservationSuccess {
			get;
			set;
		}

		public override string ToString() {
			return $"{$"[price: {this.Price}]",-15} {$"[scrapyards only: {(this.IsForScrapyards ? "yes" : "no")}]",-25} {$"[url: {this.Url}]",-50}";
			//return $"{$"[auction id: {this.Id}]",-20} {$"[price: {this.Price}]",-15} {$"[scrapyards only: {(this.IsForScrapyards ? "yes" : "no")}]",-25} {$"[url: {this.Uri}]",-50}";
		}
	}
}