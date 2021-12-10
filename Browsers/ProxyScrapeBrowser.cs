// ReSharper disable IdentifierTypo

namespace VanBot.Browsers {
	using OpenQA.Selenium;

	public class ProxyScrapeBrowser: ChromeBrowser {
		private const string ProxyUrl = "https://proxyscrape.com/web-proxy?__cpo=1";
		private const string ProxyUrl2 = "https://hosteagle.club/";

		public ProxyScrapeBrowser(bool isHeadless): base(isHeadless) { }

		public override string GetCurrentPageUrl() {
			if(!base.GetCurrentPageUrl().StartsWith(ProxyUrl)) {
				return base.GetCurrentPageUrl();
			}
			var urlBar = this.GetElement(By.Id("__cpsUrl"));
			return urlBar.GetAttribute("value");
		}

		public override void OpenPage(string url) {
			var currentUrl = base.GetCurrentPageUrl();
			if(currentUrl.StartsWith(ProxyUrl)) {
				var urlBar = this.GetElement(By.CssSelector("input[name='url']"));
				urlBar.Clear();
				urlBar.SendKeys(url);
				urlBar.SendKeys(Keys.Return);
			} else if(currentUrl.StartsWith(ProxyUrl2)) {
				//this.SendKeysToElement(By.Id("__cpsUrl"), url, Keys.Return);
			} else if(currentUrl == url) {
				base.Refresh();
			} else {
				base.OpenPage(ProxyUrl);
				// ReSharper disable once TailRecursiveCall
				this.OpenPage(url);
			}
		}
	}
}