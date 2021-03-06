namespace VanBot.Logger {
	using System;
	using System.Runtime.InteropServices;

	// https://www.jerriepelser.com/blog/using-ansi-color-codes-in-net-console-apps/
	public static class ColoredConsoleSupportForWindows {
		[DllImport("kernel32.dll")]
		public static extern uint GetLastError();

		public static void Initialize() {
			var iStdOut = GetStdHandle(STD_OUTPUT_HANDLE);
			if(!GetConsoleMode(iStdOut, out uint outConsoleMode)) {
				Console.WriteLine("failed to get output console mode");
				Console.ReadKey();
				return;
			}

			outConsoleMode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN;
			if(!SetConsoleMode(iStdOut, outConsoleMode)) {
				Console.WriteLine($"failed to set output console mode, error code: {GetLastError()}");
				Console.ReadKey();
			}
		}

		[DllImport("kernel32.dll")]
		private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr GetStdHandle(int nStdHandle);

		[DllImport("kernel32.dll")]
		private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

		// ReSharper disable InconsistentNaming
		private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;
		private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

		private const int STD_OUTPUT_HANDLE = -11;
		// ReSharper restore InconsistentNaming
	}
}