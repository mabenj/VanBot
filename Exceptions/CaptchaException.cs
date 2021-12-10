﻿namespace VanBot.Exceptions {
	using System;

	[Serializable]
	public class CaptchaException: Exception {
		public CaptchaException() { }

		public CaptchaException(string message): base(message) { }

		public CaptchaException(string message, Exception innerException): base(message, innerException) { }
	}
}