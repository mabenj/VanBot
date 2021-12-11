namespace VanBot.Logger {
	public class LoggerColor {
		private readonly string value;

		private LoggerColor(string value) {
			this.value = value;
		}

		public static LoggerColor Black => new(@"\e[90m");
		public static LoggerColor Blue => new(@"\e[94m");
		public static LoggerColor Cyan => new(@"\e[96m");
		public static LoggerColor Green => new(@"\e[92m");
		public static LoggerColor Purple => new(@"\e[95m");
		public static LoggerColor Red => new(@"\e[91m");
		public static LoggerColor Reset => new(@"\e[0m");
		public static LoggerColor White => new(@"\e[97m");
		public static LoggerColor Yellow => new(@"\e[93m");

		public override string ToString() {
			return this.value;
		}
	}
}