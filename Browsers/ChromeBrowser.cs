namespace VanBot.Browsers {
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Diagnostics.CodeAnalysis;
	using System.Linq;
	using System.Management;

	using OpenQA.Selenium;
	using OpenQA.Selenium.Chrome;

	using RandomUserAgent;

	using VanBot.Utilities;

	public class ChromeBrowser: IBrowser {
		private const int DefaultElementTimeout = 10;

		// ReSharper disable once IdentifierTypo
		private readonly HashSet<int> browserPids;
		private readonly bool isHeadless;
		private IWebDriver driver;

		public ChromeBrowser(bool isHeadless) {
			this.isHeadless = isHeadless;
			this.browserPids = new HashSet<int>();
		}

		public void ClickElement(By by) {
			var element = this.GetElement(by);
			element.Click();
		}

		public void Dispose() {
			this.KillBrowsers();
			this.driver?.Dispose();
		}

		public bool ElementExists(By by) {
			try {
				this.driver.FindElement(by);
				return true;
			} catch(NoSuchElementException) {
				return false;
			}
		}

		// ReSharper disable once IdentifierTypo
		public int[] GetBrowserPids() {
			return this.browserPids.ToArray();
		}

		public virtual string GetCurrentPageUrl() {
			return this.driver.Url;
		}

		public IWebElement GetElement(By by) {
			return this.driver.FindElement(by, DefaultElementTimeout);
		}

		public IWebElement[] GetElements(By by) {
			return this.driver.FindElements(by).ToArray();
		}

		public string GetPageTitle() {
			return this.driver.Title;
		}

		[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
		public void Initialize() {
			this.KillBrowsers();

			var options = new ChromeOptions();
			options.AddArguments(
				$"user-agent={RandomUa.RandomUserAgent}",
				"--window-size=1920,1080",
				"--disable-extensions",
				"--disable-gpu",
				"--disable-logging",
				"--log-level=3",
				"--disk-cache-size=0"
			);
			options.AddExcludedArguments("enable-logging", "enable-automation");
			options.AddAdditionalChromeOption("useAutomationExtension", false);
			//options.AddUserProfilePreference("profile.managed_default_content_settings.javascript", 2);
			if(this.isHeadless) {
				options.AddUserProfilePreference("profile.default_content_setting_values.images", 2);
				options.AddArgument("headless");
			}

			var service = ChromeDriverService.CreateDefaultService(Tools.ExtractChromeDriverResource());
			service.SuppressInitialDiagnosticInformation = true;

			this.driver = new ChromeDriver(service, options);
			this.driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
			this.driver.Manage().Window.Minimize();

			this.browserPids.Add(service.ProcessId);
			var mos = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={service.ProcessId}");
			foreach(var mo in mos.Get()) {
				this.browserPids.Add(Convert.ToInt32(mo["ProcessID"]));
			}
		}

		public void KillBrowsers() {
			foreach(var pid in this.browserPids.ToList()) {
				Process.GetProcessById(pid).Kill();
				this.browserPids.Remove(pid);
			}
		}

		public virtual void OpenPage(string url) {
			this.driver.Navigate().GoToUrl(url);
		}

		public virtual void Refresh() {
			//this.driver.Navigate().Refresh();
			var jsExecutor = (IJavaScriptExecutor) this.driver;
			jsExecutor.ExecuteScript("location.reload(true)");
		}

		public void SendKeysToElement(By by, params string[] keys) {
			var element = this.GetElement(by);
			foreach(var key in keys) {
				element.SendKeys(key);
			}
		}
	}
}