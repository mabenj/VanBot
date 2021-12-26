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
	using VanBot.Settings;

	internal class Program {
		internal static volatile CancellationTokenSource CancellationTokenSource = new();

		private static void CancelHandler(object sender, ConsoleCancelEventArgs args) {
			Log.Info("Stopping...");
			args.Cancel = true;
			CancellationTokenSource.Cancel(false);
			if(!Utilities.IsDebug()) {
				Console.WriteLine("Press any key to close...");
				Console.ReadKey();
			}
		}

		private static int HandleParseError(IEnumerable<Error> errs) {
			Log.Error("Could not parse the command line options");
			return 1;
		}

		private static int Main(string[] args) {
			ColoredConsoleSupportForWindows.Initialize();
			return Parser.Default.ParseArguments<Options>(args).MapResult(RunOptions, HandleParseError);
		}

		private static int RunOptions(Options options) {
			options = OptionsHelper.PromptForMissingOptions(options);
			OptionsHelper.LogSettings(options);
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