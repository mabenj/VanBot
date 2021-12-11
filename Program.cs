#region

using VanBotScraper = VanBot.Bots.VanBot;

#endregion

namespace VanBot {
	using System;
	using System.Collections.Generic;
	using System.Threading;

	using CommandLine;

	using VanBot.Helpers;
	using VanBot.Logger;

	internal class Program {
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

		private static int Main(string[] args) {
			return Parser.Default.ParseArguments<Options>(args).MapResult(RunOptions, HandleParseError);
		}

		private static int RunOptions(Options options) {
			if(string.IsNullOrWhiteSpace(options.Url)) {
				Console.Write("Custom url (optional):");
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
				Console.Write("Telegram chat key (optional):");
				options.TelegramKey = Console.ReadLine();
			}

			try {
				Console.CancelKeyPress += CancelHandler;
				var vanBot = new VanBotScraper(options);
				vanBot.Run(CancellationTokenSource.Token);
			} catch(Exception e) {
				Log.Error(e.Message);
				if(!Utilities.IsDebug()) {
					Console.WriteLine("Press any key to close...");
					Console.ReadKey();
				}

				return 1;
			}

			return 0;
		}
	}
}