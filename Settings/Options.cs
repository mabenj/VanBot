#region

#endregion

namespace VanBot.Settings {
	using CommandLine;

	public class Options {
		public const int DefaultInterval = 1000;
		public const string DefaultScrapingUrl = "https://www.vaurioajoneuvo.fi/";
		public const int DefaultTestIteration = -1;

		[Option('i', "interval", Required = false, HelpText = "Minimum allowed refresh interval in milliseconds")]
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

		[Option('b', "payment", Required = false, Default = null, HelpText = "Payment method used to extend the reservation")]
		public PaymentMethod PaymentMethod {
			get;
			set;
		}

		// ReSharper disable once StringLiteralTypo
		[Option('t', "telegramkey", Required = false, HelpText = "Key to connect to Telegram bot")]
		public string TelegramKey {
			get;
			set;
		}

		// ReSharper disable once StringLiteralTypo
		[Option('r', "testrun", Required = false, HelpText = "Iteration to test by adding a mock auction to the page")]
		public int TestIteration {
			get;
			set;
		}

		[Option('q', "url", Required = false, HelpText = "Custom url to use for auction detection")]
		public string Url {
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