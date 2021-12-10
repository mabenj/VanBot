namespace VanBot.BrowserAgents {
	using VanBot.Browsers;
	using VanBot.Exceptions;
	using VanBot.Utilities;

	public abstract class BrowserAgent {
		private const int MaxCaptchaAttempts = 5;

		protected BrowserAgent(IBrowser browser) {
			this.Browser = browser;
		}

		internal abstract string MainUrl {
			get;
		}

		protected IBrowser Browser {
			get;
		}

		// ReSharper disable once IdentifierTypo
		public int[] GetBrowserPids() {
			return this.Browser.GetBrowserPids();
		}

		public void Initialize() {
			this.Browser.Initialize();
			this.GoToPage(this.MainUrl);
		}

		internal void GoToPage(string url) {
			this.Browser.OpenPage(url);
			var captchaAttempts = 0;
			while(this.Browser.GetPageTitle().ToLower().StartsWith("captcha") && captchaAttempts < MaxCaptchaAttempts) {
				Log.Warning("Encountered captcha");
				Log.Warning("Resetting Driver");
				this.Browser.Initialize();
				captchaAttempts++;
				this.Browser.OpenPage(url);
			}

			if(captchaAttempts == MaxCaptchaAttempts) {
				throw new CaptchaException("Could not bypass captcha");
			}
			if(captchaAttempts != 0) {
				// ReSharper disable once TailRecursiveCall
				this.GoToPage(url);
			}
		}
	}
}