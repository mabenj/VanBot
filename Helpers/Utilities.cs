#region

#endregion

namespace VanBot.Helpers {
	using System;
	using System.Linq;
	using System.Text;

	using Force.Crc32;

	public class Utilities {
		public static (T[], T[]) GetAddedAndRemoved<T>(T[] previous, T[] current) {
			var intersection = previous.Intersect(current).ToArray();
			var addedKeys = current.Except(intersection).ToArray();
			var removedKeys = previous.Except(intersection).ToArray();
			return (addedKeys, removedKeys);
		}

		public static bool IsDebug() {
#if DEBUG
			return true;
#else
        return false;
#endif
		}

		public static string ReadPassword() {
			var pass = string.Empty;
			ConsoleKey key;
			do {
				var keyInfo = Console.ReadKey(intercept: true);
				key = keyInfo.Key;

				if(key == ConsoleKey.Backspace && pass.Length > 0) {
					Console.Write("\b \b");
					pass = pass[0..^1];
				} else if(!char.IsControl(keyInfo.KeyChar)) {
					Console.Write("*");
					pass += keyInfo.KeyChar;
				}
			} while(key != ConsoleKey.Enter);

			Console.WriteLine();

			return pass;
		}

		internal static uint CalculateCrc32(string input) {
			return Crc32Algorithm.Compute(Encoding.ASCII.GetBytes(input));
		}

		internal static bool PromptForConfirmation(string message) {
			Console.Write(message);
			var answer = Console.ReadLine()?.ToLower();
			return new[] { "y", "yes", "true" }.Contains(answer);
		}

		internal static DateTime UnixTimestampToDateTime(long timestamp) {
			var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
			return origin.AddSeconds((double) timestamp);
		}
	}
}