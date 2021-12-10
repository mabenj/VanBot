namespace VanBot.BrowserAgents {
	using System;
	using System.Collections.Generic;
	using System.Linq;

	using OpenQA.Selenium;

	using VanBot.Bots;
	using VanBot.Browsers;
	using VanBot.Utilities;

	public class CrawlerAgent: BrowserAgent {
		private const string FrontPageUrl = "https://www.vaurioajoneuvo.fi/";

		public CrawlerAgent(bool isHeadless = true): base(new ProxyScrapeBrowser(isHeadless)) { }

		internal override string MainUrl => FrontPageUrl;

		public Dictionary<string, Auction2> FetchAllAuctions(Action<string> logFunction = null, IEnumerable<string> exceptUrls = null) {
			try {
				exceptUrls = exceptUrls?.ToArray() ?? Array.Empty<string>();
				var auctions = new Dictionary<string, Auction2>();
				//var auctions = new ConcurrentDictionary<string, Auction>();
				if(!this.Browser.GetCurrentPageUrl().StartsWith(FrontPageUrl)) {
					this.GoToPage(FrontPageUrl);
				}

				//var searchResultsElement = this.Browser.GetElement(By.Id("cars-search-results"));
				//var auctionElements = this.Browser.GetElements(By.CssSelector("div[data-auction-id]"));
				var auctionElements = this.Browser.GetElements(By.CssSelector("div.item-lift-container a"));

				if(auctionElements.Length == exceptUrls.Count()) {
					// this does not account for situations where there is one new auction and one removed auction
					return null;
				}

				foreach(var auctionElement in auctionElements) {
					//var id = auctionElement.GetAttribute("data-auction-id");
					// ReSharper disable once StringLiteralTypo
					//var url = auctionElement.FindElement(By.XPath("./..")).GetAttribute("__cporiginalvalueofhref");
					var url = auctionElement.GetAttribute("__cporiginalvalueofhref");
					if(exceptUrls.Contains(url)) {
						continue;
					}
					//var url = auctionElement.FindElement(By.XPath("./..")).GetAttribute("href");
					if(!url.StartsWith("http")) {
						url = new Uri(FrontPageUrl).Append(url).AbsoluteUri;
					}
					// ReSharper disable once StringLiteralTypo
					var isForScrapyards = auctionElement.FindElement(By.CssSelector(".item-lift-price-now-title")).Text.ToLower() == "vain purkamoille";
					var price = -1.0;
					if(!isForScrapyards) {
						//var priceString = auctionElement.FindElement(By.CssSelector("strong.item-lift-price-now")).Text;
						//price = Convert.ToDouble(priceString.Replace(" ", "").Replace("€", ""));
					}

					auctions[url] = new Auction2() {
						Id = url,
						IsForScrapyards = isForScrapyards,
						Price = price,
						Url = url
					};

					logFunction?.Invoke(auctions[url].ToString());
				}

				return auctions;
			} catch(Exception e) {
				throw new Exception("Error while fetching all auctions", e);
			}
		}

		public void Refresh() {
			this.Browser.Refresh();
		}
	}
}