namespace VanBot.BrowserAgents {
	using System;
	using System.Diagnostics;

	using OpenQA.Selenium;

	using VanBot.Browsers;
	using VanBot.Exceptions;

	public class ReservationAgent: BrowserAgent {
		private const string FrontPageUrl = "https://www.vaurioajoneuvo.fi/";
		private const string LoginUrl = "https://www.vaurioajoneuvo.fi/kayttajalle/kirjaudu-sisaan/";
		internal readonly string Username;
		private readonly string password;

		public ReservationAgent(string username, string password, bool isHeadless): base(new ChromeBrowser(isHeadless)) {
			this.Username = username;
			this.password = password;
			this.IsLoggedIn = false;
		}

		internal bool IsLoggedIn {
			get;
			private set;
		}

		internal override string MainUrl => LoginUrl;

		public void LogIn() {
			try {
				this.GoToPage(LoginUrl);
				this.Browser.SendKeysToElement(By.Id("username"), this.Username);
				this.Browser.SendKeysToElement(By.Id("password"), this.password, Keys.Return);
				// ReSharper disable StringLiteralTypo
				var siteHeaderLinks = this.Browser.GetElements(By.CssSelector("#header-actions-desktop>a"));
				this.IsLoggedIn = siteHeaderLinks.Length < 2;
			} catch(Exception e) {
				throw new LoginException($"Could not log in as '{this.Username}'", e);
			}
			if(!this.IsLoggedIn) {
				throw new LoginException($"Could not log in as '{this.Username}'");
			}
		}

		public bool LogOut() {
			try {
				this.GoToPage(LoginUrl);
				this.Browser.ClickElement(By.CssSelector("input.logout"));
				this.IsLoggedIn = !this.Browser.ElementExists(By.Id("Username"));
				return !this.IsLoggedIn;
			} catch(Exception e) {
				throw new LoginException($"Could not log out as '{this.Username}'", e);
			}
		}

		internal bool ReserveAuction(Auction auction, out bool alreadyReserved, out long elapsedMilliseconds) {
			elapsedMilliseconds = 0;
			alreadyReserved = false;

			var timer = new Stopwatch();
			timer.Start();

			var buyBtnBy = By.CssSelector("button.button.button-buy");
			try {
				this.GoToPage(auction.FullUri);
				var buyBtn = this.Browser.GetElement(buyBtnBy);
				if(!buyBtn.Enabled) {
					alreadyReserved = true;
					return false;
				}
				buyBtn.Click();

				return this.Browser.ElementExists(By.CssSelector("div.proceed-to-payment"));
			} catch(Exception e) {
				throw new ReservationException($"Could not reserve auction '{auction.Uri}'", e);
			} finally {
				timer.Stop();
				elapsedMilliseconds = timer.ElapsedMilliseconds;
			}
		}

		//public IEnumerable<Auction2> ReserveAuctions(IEnumerable<Auction2> auctions) {
		//	var reserved = new List<Auction2>();
		//	foreach(var auction in auctions) {
		//		if(reserved.Count > 0) {
		//			break;
		//		}
		//		if(auction.IsForScrapyards) {
		//			continue;
		//		}
		//		if(!this.ReserveAuction(auction, out _)) {
		//			continue;
		//		}
		//		reserved.Add(auction);
		//	}
		//	return reserved;
		//}

		//internal string ResolveAuctionIdToUrl(string auctionId) {
		//	if(this.Browser.GetCurrentPageUrl() != FrontPageUrl) {
		//		this.GoToPage(FrontPageUrl);
		//	}
		//	var auctionElement = this.Browser.GetElement(By.CssSelector($"div[data-auction-id='{auctionId}']"));
		//	var url = auctionElement.FindElement(By.XPath("./..")).GetAttribute("href");
		//	return url;
		//}
	}
}