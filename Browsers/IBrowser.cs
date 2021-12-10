#region

#endregion

namespace VanBot.Browsers {
	using System;

	using OpenQA.Selenium;

	public interface IBrowser: IDisposable {
		public void ClickElement(By by);

		public bool ElementExists(By by);

		// ReSharper disable once IdentifierTypo
		public int[] GetBrowserPids();

		public string GetCurrentPageUrl();

		public IWebElement GetElement(By by);

		public IWebElement[] GetElements(By by);

		public string GetPageTitle();

		public void Initialize();

		public void KillBrowsers();

		public void OpenPage(string url);

		public void Refresh();

		public void SendKeysToElement(By by, params string[] keys);
	}
}