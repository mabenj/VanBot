namespace VanBot.Browsers {
	using OpenQA.Selenium;

	public class HMAProxyBrowser: ChromeBrowser {
		private const string ProxyUrl = "https://www.hidemyass-freeproxy.com/";

		public HMAProxyBrowser(bool isHeadless): base(isHeadless) { }

		public override string GetCurrentPageUrl() {
			if(!base.GetCurrentPageUrl().StartsWith(ProxyUrl)) {
				return base.GetCurrentPageUrl();
			}
			var urlBar = this.GetElement(By.Id("hma-top-input-url"));
			return urlBar.GetAttribute("value");
		}

		public override void OpenPage(string url) {
			if(base.GetCurrentPageUrl().StartsWith(ProxyUrl)) {
				var urlBar = this.GetElement(By.Id("hma-top-input-url"));
				urlBar.Clear();
				urlBar.SendKeys(url);
				urlBar.SendKeys(Keys.Return);
			} else {
				base.OpenPage(ProxyUrl);
				this.SendKeysToElement(By.Id("form_url_fake"), url, Keys.Return);
			}
		}

		public override void Refresh() {
			this.OpenPage(this.GetCurrentPageUrl());
		}
	}
}