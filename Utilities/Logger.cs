#region

using System;
using System.Reflection;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Layout;

#endregion

namespace VanBot.Utilities {
    internal class Log {
        private const string DatePattern = "yyyy-MM-dd HH:mm:ss";
        private static readonly string AppenderPattern = $"%date{{{DatePattern}}} [%p] %m%n";

        private static readonly ILog Logger;

        static Log() {
            Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
            var consoleAppender = new ConsoleAppender() {
                Layout = new PatternLayout(AppenderPattern),
                Threshold = Level.Info
            };
            BasicConfigurator.Configure(consoleAppender);
        }

        internal static void Info(string message) {
            Logger.Info(message);
        }

        internal static void Error(string message, Exception exception = null) {
            Logger.Error(message);
            if (exception != null) {
                Logger.Debug(exception);
            }
        }

        internal static void Warning(string message, Exception exception = null) {
            Logger.Warn(message);
            if (exception != null) {
                Logger.Debug(exception);
            }
        }
    }
}