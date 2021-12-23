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
		private const int ConsecutiveErrorsThreshold = 10;
		private const int NumberOfMockAuctions = 4;

		private readonly AuctionsService auctionsService;
		private readonly Random random;
		private readonly ReserveService reserveService;
		private readonly bool shouldTryToExtend;
		private readonly int sleepTimeRangeMax;
		private readonly TelegramBot telegramBot;
		private readonly int testIteration;
		private readonly Stopwatch timer;
		private AuctionCollection allAuctions;
		private CancellationToken cancellationToken;
		private readonly TimeSpan interval;
		private int iterations;
		private int iterationsSinceNew;
		private bool shouldTryToReserve;

		public VanBot(Options options) {
			this.iterations = 0;
			this.iterationsSinceNew = 0;
			this.reserveService = new ReserveService(options.Username, options.Password, options.PaymentMethod);
			this.telegramBot = new TelegramBot(options.TelegramKey);
			this.timer = new Stopwatch();
			this.allAuctions = new AuctionCollection();
			this.shouldTryToReserve = !options.NoSignIn;
			this.shouldTryToExtend = options.PaymentMethod != PaymentMethod.None;
			this.testIteration = options.TestIteration;
			this.auctionsService = new AuctionsService(options.Url);
			this.random = new Random();
			this.sleepTimeRangeMax = options.Interval;
			this.interval = TimeSpan.FromMilliseconds(options.Interval);
		}

		private bool IsTestRun => this.iterations == this.testIteration;

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
			this.allAuctions = this.auctionsService.GetAuctions(out var rateLimitRemaining);
			foreach(var auction in this.allAuctions) {
				Log.Info(auction.ToString(), LoggerColor.Cyan);
			}

			var consecutiveErrors = 0;
			Log.Info("Waiting for new auctions...");
			while(!this.cancellationToken.IsCancellationRequested && consecutiveErrors < ConsecutiveErrorsThreshold) {
				this.timer.Restart();
				this.iterations++;
				this.iterationsSinceNew++;
				AuctionCollection currentAuctions = null;

				try {
					currentAuctions = this.auctionsService.GetAuctions(out rateLimitRemaining);

					if(this.IsTestRun) {
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
						this.iterationsSinceNew = 0;
					}
					consecutiveErrors = 0;
				} catch(CaptchaException e) {
					Log.Error(e.Message);
					// TODO: prompt to complete the captcha
					consecutiveErrors++;
					this.telegramBot.NotifyCaptcha();
				} catch(Exception e) {
					Log.Error(e.Message);
					consecutiveErrors++;
				} finally {
					this.allAuctions = currentAuctions;
					this.timer.Stop();
					LogStatus(
						new StatusInfo(
							this.iterations,
							this.iterationsSinceNew,
							this.timer.ElapsedMilliseconds,
							rateLimitRemaining
						));
					var sleepTime = TimeSpan.FromMilliseconds(this.random.Next(0, this.sleepTimeRangeMax));
					sleepTime = this.interval - this.timer.Elapsed;
					if(sleepTime.TotalMilliseconds > 0) {
						this.Wait(Convert.ToInt32(sleepTime.TotalMilliseconds));
					}
				}
			}

			if(consecutiveErrors >= ConsecutiveErrorsThreshold) {
				Log.Error($"Encountered {consecutiveErrors} errors in a row");
				Log.Error($"Aborting...");
				this.telegramBot.NotifyCrash();
			}
		}

		private static void LogStatus(StatusInfo statusInfo) {
			var reqColor = LoggerColor.Reset;
			var timeColor = statusInfo.LoopTime > 100 ? LoggerColor.Red : statusInfo.LoopTime > 50 ? LoggerColor.Yellow : LoggerColor.Green;

			var formattedStatus = $"[req: {reqColor}{statusInfo.Iterations}{LoggerColor.Reset}]"
				+ $" [req_since_new: {reqColor}{statusInfo.IterationsSinceNew}{LoggerColor.Reset}]"
				+ $" [time: {timeColor}{$"{statusInfo.LoopTime}",-4}{LoggerColor.Reset} ms]"
				+ $" [rate_limit_remaining: {statusInfo.RateLimitRemaining}]";
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

	public record StatusInfo(int Iterations, int IterationsSinceNew, long LoopTime, int RateLimitRemaining);
}