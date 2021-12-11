namespace VanBot.Logger {
	using System;
	using System.Reflection;
	using System.Text;

	using log4net;
	using log4net.Appender;
	using log4net.Config;
	using log4net.Core;
	using log4net.Layout;

	public class Log {
		private const string DatePattern = "yyyy-MM-dd HH:mm:ss";
		private static readonly string AppenderPattern = $"%date{{{DatePattern}}} %-5level %message%newline";

		private static readonly ILog Logger;

		static Log() {
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
			var consoleAppender = new ConsoleAppenderWithColorSwitching() {
				Layout = new PatternLayout(AppenderPattern),
				Threshold = Level.Info
			};
			consoleAppender.ActivateOptions();
			BasicConfigurator.Configure(consoleAppender);
		}

		public static void Error(string message) {
			Logger.Error($"{LoggerColor.Red}{message}{LoggerColor.Reset}");
		}

		public static void Info(string message, LoggerColor color = null) {
			if(color != null) {
				message = $"{color}{message}{LoggerColor.Reset}";
			}
			Logger.Info(message);
		}

		public static void Warning(string message) {
			Logger.Warn($"{LoggerColor.Yellow}{message}{LoggerColor.Reset}");
		}
	}

	// https://stackoverflow.com/a/67729162
	public class ConsoleAppenderWithColorSwitching : ConsoleAppender {
		// forfeits the auto switch from Console.Error to Console.Out
		// of the original appender :/
		protected override void Append(LoggingEvent loggingEvent) {
			var renderedLayout = this.RenderLoggingEvent(loggingEvent);
			var ansi = renderedLayout.Replace(@"\e", "\u001b");
			Console.Write(ansi);
		}
	}
}