namespace VanBot.Auctions {
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Linq;
	using System.Web;

	using HtmlAgilityPack;

	using VanBot.Helpers;
	using VanBot.Logger;

	public class AuctionCollection: IEnumerable<Auction> {
		private readonly Dictionary<string, Auction> auctions;

		public AuctionCollection() {
			this.auctions = new Dictionary<string, Auction>();
		}

		public Auction this[string key] {
			get => this.GetAuction(key);

			set => this.SetAuction(key, value);
		}

		public static (AuctionCollection added, AuctionCollection removed) GetAddedAndRemoved(AuctionCollection previous, AuctionCollection current) {
			var (addedKeys, removedKeys) = Utilities.GetAddedAndRemoved(previous.GetKeys(), current.GetKeys());

			var added = new AuctionCollection();
			foreach(var key in addedKeys) {
				added[key] = current[key];
			}

			var removed = new AuctionCollection();
			foreach(var key in removedKeys) {
				removed[key] = previous[key];
			}

			return (added, removed);
		}

		public bool ContainsKey(string key) {
			return this.auctions.ContainsKey(key);
		}

		public IEnumerator<Auction> GetEnumerator() {
			foreach(var (_, value) in this.auctions) {
				yield return value;
			}
		}

		public string[] GetKeys() => this.auctions.Keys.ToArray();

		internal static AuctionCollection ParseFromHtml(string html, Action<string> logFunction, AuctionCollection existingAuctionCollection = null) {
			var result = new AuctionCollection();
			var htmlDoc = new HtmlDocument();
			htmlDoc.LoadHtml(html);

			var searchResultNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@id=\"cars-search-results\"]");
			var auctionNodes = searchResultNode.SelectNodes(".//a[@href]");
			foreach(var auctionNode in auctionNodes) {
				try {
					var url = HttpUtility.HtmlDecode(auctionNode.Attributes["href"].Value);
					// ReSharper disable once StringLiteralTypo
					var name = url.Replace("/tuote/", string.Empty).TrimEnd('/');
					if(existingAuctionCollection != null && existingAuctionCollection.ContainsKey(name)) {
						result[name] = existingAuctionCollection[name];
						continue;
					}
					var priceDiv = auctionNode.SelectSingleNode(".//div[contains(concat(' ',normalize-space(@class),' '),' item-lift-price ')]");
					var priceTag = HttpUtility.HtmlDecode(priceDiv.ChildNodes["strong"]?.InnerHtml) ?? string.Empty;
					var price = -1.0;
					if(double.TryParse(priceTag.Replace(" ", string.Empty).Replace("€", string.Empty), out var doubleValue)) {
						price = doubleValue;
					}
					result[name] = new Auction(name, isForScrapyards: Math.Abs(price - (-1.0)) < double.Epsilon, price);

					logFunction?.Invoke(result[name].ToString());
				} catch(Exception e) {
					Log.Warning(e.Message);
				}
			}

			return result;
		}

		private Auction GetAuction(string key) {
			return this.auctions[key];
		}

		IEnumerator IEnumerable.GetEnumerator() {
			return this.GetEnumerator();
		}

		private void SetAuction(string key, Auction value) {
			this.auctions[key] = value;
		}
	}
}