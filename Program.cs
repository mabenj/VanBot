#region

using VanBotScraper = VanBot.Bots.VanBot;

#endregion

namespace VanBot {
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Threading;

	using CommandLine;

	using Salaros.Configuration;

	using VanBot.Helpers;
	using VanBot.Logger;
	using VanBot.Settings;

	internal class Program {
		private const string ConfigFileName = "van_bot.cfg";

		internal static volatile CancellationTokenSource CancellationTokenSource = new();

		private static void CancelHandler(object sender, ConsoleCancelEventArgs args) {
			Log.Info("Stopping...");
			args.Cancel = true;
			CancellationTokenSource.Cancel(false);
		}

		private static int HandleParseError(IEnumerable<Error> errs) {
			Log.Error("Could not parse the command line options");
			return 1;
		}

		private static void LogSettings(Options options) {
			const int Padding = -18;
			Log.Info("Using settings:");
			Log.Info($"{"Update interval ms",Padding} = {options.Interval}");
			Log.Info($"{"Username",Padding} = {options.Username}");
			Log.Info($"{"Password",Padding} = {new string('*', options.Password.Length)}");
			Log.Info($"{"Telegram chat key",Padding} = {options.TelegramKey}");
			Log.Info($"{"Url",Padding} = {options.Url}");
			Log.Info($"{"Should sign in",Padding} = {(!options.NoSignIn ? "yes" : "no")}");
			Log.Info($"{"Test run",Padding} = {(options.TestIteration > 0 ? options.TestIteration : "")}");
			Log.Info($"{"Payment method",Padding} = {options.PaymentMethod}");
		}

		private static int Main(string[] args) {
			ColoredConsoleSupportForWindows.Initialize();
			return Parser.Default.ParseArguments<Options>(args).MapResult(RunOptions, HandleParseError);
		}

		private static Options PromptForMissingOptions(Options options) {
			if(File.Exists(ConfigFileName)) {
				var configFile = new ConfigParser(
					ConfigFileName,
					new ConfigParserSettings() {
						CommentCharacters = new[] { "#" }
					});
				Log.Info($"Config '{ConfigFileName}' loaded", LoggerColor.Blue);
				if(string.IsNullOrWhiteSpace(options.Url)) {
					options.Url = configFile.GetValue("Config", "url", Options.DefaultScrapingUrl);
				}
				if(string.IsNullOrWhiteSpace(options.Username)) {
					options.Username = configFile["Config"]["username"];
				}
				if(string.IsNullOrWhiteSpace(options.Password)) {
					options.Password = configFile["Config"]["password"];
				}
				if(options.Interval == 0) {
					options.Interval = configFile.GetValue("Config", "interval-ms", Options.DefaultInterval);
				}
				if(string.IsNullOrWhiteSpace(options.TelegramKey)) {
					options.TelegramKey = configFile["Config"]["telegram-key"];
				}
				if(options.TestIteration < 1) {
					options.TestIteration = configFile.GetValue("Config", "test-run", Options.DefaultTestIteration);
				}
				if(options.PaymentMethod == null && !string.IsNullOrWhiteSpace(configFile["Config"]["payment-method"])) {
					options.PaymentMethod = new PaymentMethod(configFile["Config"]["payment-method"]);
				}
			}

			if(string.IsNullOrWhiteSpace(options.Url)) {
				Console.Write("Custom url:");
				options.Url = Console.ReadLine();
			}

			if(!options.NoSignIn) {
				if(string.IsNullOrWhiteSpace(options.Username)) {
					Console.Write("Username:");
					options.Username = Console.ReadLine();
				}

				if(string.IsNullOrWhiteSpace(options.Password)) {
					Console.Write("Password:");
					options.Password = Utilities.ReadPassword();
				}
			}

			if(string.IsNullOrWhiteSpace(options.TelegramKey)) {
				Console.Write("Telegram chat key:");
				options.TelegramKey = Console.ReadLine();
			}

			if(options.PaymentMethod == null) {
				Console.WriteLine("Select payment method used to extend reservations:");
				for(var i = 0; i < PaymentMethod.AllPaymentMethods.Length; i++) {
					Console.WriteLine($"{$"[{i + 1}]",-5}{PaymentMethod.AllPaymentMethods[i].DisplayName}");
				}
				Console.Write($"Payment method (1-{PaymentMethod.AllPaymentMethods.Length}):");
				options.PaymentMethod = PaymentMethod.AllPaymentMethods[Convert.ToInt32(Console.ReadLine()) - 1];
			}

			return options;
		}

		private static int RunOptions(Options options) {
			options = PromptForMissingOptions(options);
			LogSettings(options);
			if(!Utilities.PromptForConfirmation("Continue? (y/n): ")) {
				return 1;
			}

			try {
				Console.CancelKeyPress += CancelHandler;
				var vanBot = new VanBotScraper(options);
				vanBot.Run(CancellationTokenSource.Token);
			} catch(Exception e) {
				Log.Error(e.Message);
				return 1;
			} finally {
				if(!Utilities.IsDebug()) {
					Console.WriteLine("Press any key to close...");
					Console.ReadKey();
				}
			}

			return 0;
		}
	}
}