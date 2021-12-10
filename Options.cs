#region

#endregion

namespace VanBot {
	using CommandLine;

	public class Options {
		private const int DefaultInterval = 30;

		[Option('i', "interval", Required = false, Default = DefaultInterval, HelpText = "Minimum allowed refresh interval in milliseconds")]
		public int Interval {
			get;
			set;
		}

		// ReSharper disable once StringLiteralTypo
		[Option("nosignin", Required = false, HelpText = "Do not sign in")]
		public bool NoSignIn {
			get;
			set;
		}

		[Option('p', "password", Required = false, HelpText = "Password")]
		public string Password {
			get;
			set;
		}

		// ReSharper disable once StringLiteralTypo
		[Option("showhead", Required = false, HelpText = "Should open up Browser")]
		public bool ShowHead {
			get;
			set;
		}

		// ReSharper disable once StringLiteralTypo
		[Option('t', "telegramkey", Required = false, HelpText = "Key to connect to Telegram bot")]
		public string TelegramKey {
			get;
			set;
		}

		[Option('u', "Username", Required = false, HelpText = "Username/email")]
		public string Username {
			get;
			set;
		}
	}
}