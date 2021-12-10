#region

using VanBotScraper = VanBot.Bots.VanBot;

#endregion

namespace VanBot {
	using System;
	using System.Collections.Generic;
	using System.Runtime.InteropServices;
	using System.Threading;

	using CommandLine;

	using VanBot.Utilities;

	internal class Program {
		internal static volatile CancellationTokenSource CancellationTokenSource = new();
		private static bool exitRequested = false;

		// https://docs.microsoft.com/en-us/windows/console/handlerroutine?WT.mc_id=DT-MVP-5003978
		private delegate bool SetConsoleCtrlEventHandler(CtrlType sig);

		// ReSharper disable InconsistentNaming
		private enum CtrlType {
			CTRL_C_EVENT = 0,
			CTRL_BREAK_EVENT = 1,
			CTRL_CLOSE_EVENT = 2,
			CTRL_LOGOFF_EVENT = 5,
			CTRL_SHUTDOWN_EVENT = 6
		}
		// ReSharper restore InconsistentNaming

		private static bool CloseHandler(CtrlType signal) {
			switch(signal) {
			case CtrlType.CTRL_BREAK_EVENT:
			case CtrlType.CTRL_C_EVENT:
			case CtrlType.CTRL_LOGOFF_EVENT:
			case CtrlType.CTRL_SHUTDOWN_EVENT:
			case CtrlType.CTRL_CLOSE_EVENT:
				exitRequested = true;
				VanBotScraper.Stop();
				CancellationTokenSource.Cancel(false);
				if(!Tools.IsDebug()) {
					Console.WriteLine("Press any key to close...");
					Console.ReadKey();
				}

				Environment.Exit(0);
				return false;
			default:
				return false;
			}
		}

		private static int HandleParseError(IEnumerable<Error> errs) {
			Log.Error("Could not parse the command line options");
			return 1;
		}

		private static int Main(string[] args) {
			SetConsoleCtrlHandler(CloseHandler, true);
			return Parser.Default.ParseArguments<Options>(args).MapResult(RunOptions, HandleParseError);
		}

		private static int RunOptions(Options options) {
			if(!options.NoSignIn) {
				if(string.IsNullOrWhiteSpace(options.Username)) {
					Console.Write("Username:");
					options.Username = Console.ReadLine();
				}

				if(string.IsNullOrWhiteSpace(options.Password)) {
					Console.Write("Password:");
					options.Password = Tools.ReadPassword();
				}
			}

			if(string.IsNullOrWhiteSpace(options.TelegramKey)) {
				Console.Write("Telegram chat key:");
				options.TelegramKey = Console.ReadLine();
			}

			try {
				var vanBot = new VanBotScraper(options, "https://www.vaurioajoneuvo.fi/?type=PassengerCar");
				vanBot.Start(CancellationTokenSource.Token);
			} catch(Exception e) {
				if(!exitRequested) {
					Log.Error($"Error: {e.Message}");
					VanBotScraper.Stop();
					if(!Tools.IsDebug()) {
						Console.WriteLine("Press any key to close...");
						Console.ReadKey();
					}
				}

				return 1;
			}

			return 0;
		}

		[DllImport("Kernel32")]
		private static extern bool SetConsoleCtrlHandler(SetConsoleCtrlEventHandler handler, bool add);
	}
}