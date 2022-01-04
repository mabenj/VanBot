namespace VanBot.Bots {
	using System;
	using System.Diagnostics;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	using global::VanBot.Auctions;
	using global::VanBot.Exceptions;
	using global::VanBot.Helpers;
	using global::VanBot.Logger;
	using global::VanBot.Services;
	using global::VanBot.Settings;

	public class VanBot {
		internal const int CriticalRateLimitThreshold = 3;
		internal const int LowRateLimitThreshold = 5;
		private const int ConsecutiveErrorsThreshold = 10;
		private const int NumberOfMockAuctions = 4;
		private const int RateLimitCooldownMinutes = 4;

		private readonly AuctionsService auctionsService;
		private readonly TimeSpan interval;
		private readonly ReserveService reserveService;
		private readonly bool shouldTryToExtend;
		private readonly TelegramBot telegramBot;
		private readonly int testIteration;
		private readonly Stopwatch timer;
		private AuctionCollection allAuctions;
		private CancellationToken cancellationToken;
		private int requestsSinceNew;
		private bool shouldTryToReserve;

		public VanBot(Options options) {
			this.reserveService = new ReserveService(options.Username, options.Password, options.PaymentMethod);
			this.auctionsService = new AuctionsService(options.Url);
			this.telegramBot = new TelegramBot(options.TelegramKey);
			this.timer = new Stopwatch();
			this.allAuctions = new AuctionCollection();
			this.shouldTryToReserve = !options.NoSignIn;
			this.shouldTryToExtend = options.PaymentMethod != PaymentMethod.None;
			this.testIteration = options.TestIteration;
			this.requestsSinceNew = this.auctionsService.RequestsMade;
			this.interval = TimeSpan.FromMilliseconds(options.Interval);
		}

		private bool IsTestRun => this.auctionsService.RequestsMade == this.testIteration;

		internal void Run(CancellationToken token) {
			this.cancellationToken = token;

			if(this.shouldTryToReserve) {
				Log.Info("Testing given credentials");
				this.reserveService.Initialize(out var errors);
				if(!this.CheckCredentials(errors)) {
					return;
				}
			}

			Log.Info("Testing Telegram bot chat key");
			if(!this.CheckTelegramBot()) {
				return;
			}

			Log.Info("Fetching initial auctions");
			this.allAuctions = this.auctionsService.GetAuctions(out var rateLimitRemaining, out _);
			foreach(var auction in this.allAuctions) {
				Log.Info(auction.ToString(), LoggerColor.Cyan);
			}

			var consecutiveErrors = 0;
			Log.Info("Waiting for new auctions...");
			while(!this.cancellationToken.IsCancellationRequested && consecutiveErrors < ConsecutiveErrorsThreshold) {
				this.timer.Restart();
				this.requestsSinceNew++;
				AuctionCollection currentAuctions = null;

				try {
					//currentAuctions = this.auctionsService.GetAuctions(out rateLimitRemaining, out _);

					currentAuctions = this.auctionsService.WaitForNewAuctions(out rateLimitRemaining);

					if(this.IsTestRun) {
						currentAuctions = currentAuctions.Copy();
						currentAuctions.AddMockAuctions(NumberOfMockAuctions);
					}

					var (addedAuctions, removedAuctions) = AuctionCollection.GetAddedAndRemoved(this.allAuctions, currentAuctions);

					if(this.shouldTryToReserve) {
						this.NotifyAndReserveAuctions(addedAuctions);
					} else {
						this.NotifyAuctions(addedAuctions);
					}

					foreach(var oldAuction in removedAuctions) {
						Log.Info($"Auction '{oldAuction.Id} ({oldAuction.Slug})' expired", LoggerColor.Purple);
					}

					if(addedAuctions.Any()) {
						this.requestsSinceNew = 0;
					}
					consecutiveErrors = 0;
				} catch(CaptchaException e) {
					Log.Error(e.Message);
					Log.Error("Complete the captcha before continuing");
					this.telegramBot.NotifyCaptcha();
					if(!Utilities.PromptForConfirmation("Continue? (y/n):")) {
						break;
					}
					consecutiveErrors++;
				} catch(Exception e) {
					Log.Error(e.Message);
					consecutiveErrors++;
				} finally {
					this.allAuctions = currentAuctions;
					this.timer.Stop();
					LogStatus(
						this.auctionsService.RequestsMade,
						this.requestsSinceNew,
						this.timer.ElapsedMilliseconds,
						rateLimitRemaining,
						Proxies.GetNameOfCurrent()
					);
					var sleepTime = this.interval - this.timer.Elapsed;

					if(rateLimitRemaining is > -1 and < CriticalRateLimitThreshold) {
						Log.Warning($"Remaining rate limit is below {LowRateLimitThreshold}");
						Log.Warning($"Cooling down for {RateLimitCooldownMinutes} minutes");
						sleepTime = TimeSpan.FromMinutes(RateLimitCooldownMinutes);
					}

					if(sleepTime.TotalMilliseconds > 0) {
						this.Wait(Convert.ToInt32(sleepTime.TotalMilliseconds));
					}
				}
			}

			if(consecutiveErrors >= ConsecutiveErrorsThreshold) {
				Log.Error($"Encountered {consecutiveErrors} errors in a row");
				Log.Error("Aborting...");
				this.telegramBot.NotifyCrash();
			}
		}

		private static void LogStatus(int requests, int requestsSinceNew, long latencyMillis, int rateLimitRemaining, string currentProxyName) {
			var reqColor = LoggerColor.Reset;
			var latencyColor = latencyMillis > 500 ? LoggerColor.Red : latencyMillis > 300 ? LoggerColor.Yellow : LoggerColor.Green;
			var rateLimitColor = rateLimitRemaining < 10 ? LoggerColor.Red : rateLimitRemaining < 20 ? LoggerColor.Yellow : LoggerColor.Green;

			var formattedStatus = $"[req: {reqColor}{requests}{LoggerColor.Reset}]"
				+ $" [req_new: {reqColor}{requestsSinceNew}{LoggerColor.Reset}]"
				+ $" [lat: {latencyColor}{$"{latencyMillis}",-4}{LoggerColor.Reset} ms]"
				+ $" [rate_limit: {rateLimitColor}{$"{rateLimitRemaining}",-2}{LoggerColor.Reset}]"
				+ $" [proxy: {currentProxyName}]";
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
			var isChatKeyOk = Task.Run(() => this.telegramBot.TestChatKey(), this.cancellationToken).Result;
			return isChatKeyOk || Utilities.PromptForConfirmation("Continue anyway? (y/n): ");
		}

		private void NotifyAndReserveAuctions(AuctionCollection auctions) {
			if(!auctions.Any()) {
				return;
			}

			foreach(var auction in auctions) {
				Log.Info(auction.ToString(), LoggerColor.Cyan);
				if(auction.IsForScrapyards) {
					continue;
				}

				try {
					Log.Info($"Attempting to reserve '{auction.Id}'");
					this.reserveService.AttemptToReserveAuction(auction);
				} catch(ReservationException e) {
					Log.Error($"Error while reserving '{auction.Id}': {e.Message}");
				}
			}

			this.Wait(4000); // wait for the reservation to go through
			var reservedAuctionSlug = this.reserveService.GetReservedAuctionSlug(out var expirationTime);

			if(reservedAuctionSlug == null) {
				Log.Error("Could not reserve any auctions");
			} else if(this.shouldTryToExtend) {
				Log.Info("Slug = " + reservedAuctionSlug);
				var reservedAuction = auctions.First(a => a.Slug == reservedAuctionSlug);
				Log.Info($"Extending reservation of '{reservedAuction.Id}'");
				try {
					this.reserveService.ExtendReservation(reservedAuction, ref expirationTime);
				} catch(ReservationException e) {
					Log.Error($"Error while extending reservation '{reservedAuction.Id}': {e.Message}");
				}
			}

			foreach(var auction in auctions) {
				if(auction.Slug == reservedAuctionSlug) {
					var reservedFor = TimeSpan.FromMilliseconds(expirationTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
					Log.Info($"Auction '{auction.Id}' successfully reserved for {reservedFor.Minutes} min {reservedFor.Seconds} s", LoggerColor.Green);
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
			foreach(var auction in auctions) {
				Log.Info(auction.ToString(), LoggerColor.Cyan);
				this.telegramBot.NotifyNewAuction(auction);
			}
		}

		private void Wait(int millis) {
			this.cancellationToken.WaitHandle.WaitOne(millis);
		}
	}
}