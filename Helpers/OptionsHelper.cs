namespace VanBot.Helpers {
	using System;
	using System.IO;

	using Salaros.Configuration;

	using VanBot.Logger;
	using VanBot.Settings;

	public static class OptionsHelper {
		private const string ConfigFileName = "van_bot.cfg";

		internal static void LogSettings(Options options) {
			const int Padding = -18;
			Log.Info("SETTINGS:");
			Log.Info($"{"Update interval ms",Padding} = {options.Interval}");
			Log.Info($"{"Username",Padding} = {options.Username}");
			Log.Info($"{"Password",Padding} = {new string('*', options.Password.Length)}");
			Log.Info($"{"Telegram chat key",Padding} = {options.TelegramKey}");
			Log.Info($"{"Url",Padding} = {options.Url}");
			Log.Info($"{"Should sign in",Padding} = {(!options.NoSignIn ? "yes" : "no")}");
			Log.Info($"{"Test run",Padding} = {(options.TestIteration > 0 ? options.TestIteration : "")}");
			Log.Info($"{"Payment method",Padding} = {options.PaymentMethod}");
		}

		internal static Options PromptForMissingOptions(Options options) {
			if(File.Exists(ConfigFileName)) {
				var configFile = new ConfigParser(
					ConfigFileName,
					new ConfigParserSettings() {
						CommentCharacters = new[] { "#" }
					});
				Log.Info($"Config '{ConfigFileName}' loaded", LoggerColor.Blue);
				if(string.IsNullOrWhiteSpace(options.Url)) {
					options.Url = configFile.GetValue("Config", "url", string.Empty);
				}
				if(string.IsNullOrWhiteSpace(options.Username)) {
					options.Username = configFile["Config"]["username"];
				}
				if(string.IsNullOrWhiteSpace(options.Password)) {
					options.Password = configFile["Config"]["password"];
				}
				if(options.Interval == 0) {
					options.Interval = configFile.GetValue("Config", "interval-ms", 0);
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
	}
}