#region

using CommandLine;

#endregion

namespace VanBot {
    public class Options {
        private const int DefaultInterval = 30;

        [Option('u', "username", Required = false, HelpText = "Username/email")]
        public string Username {
            get;
            set;
        }

        [Option('p', "password", Required = false, HelpText = "Password")]
        public string Password {
            get;
            set;
        }

        [Option('i', "interval", Required = false, Default = DefaultInterval, HelpText = "Minimum allowed refresh interval in seconds")]
        public int Interval {
            get;
            set;
        }

        // ReSharper disable once StringLiteralTypo
        [Option("showhead", Required = false, HelpText = "Should open up browser")]
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
    }
}