﻿namespace VanBot {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Web;

	using HtmlAgilityPack;

	using VanBot.Utilities;

	public class Auctions {
		private readonly Dictionary<string, Auction> auctions;

		public Auctions() {
			this.auctions = new Dictionary<string, Auction>();
		}

		public Auction this[string key] {
			get => this.GetAuction(key);

			set => this.SetAuction(key, value);
		}

		public bool ContainsKey(string key) {
			return this.auctions.ContainsKey(key);
		}

		public string[] GetKeys() => this.auctions.Keys.ToArray();

		internal static Auctions ParseFromHtml(string html, Action<string> logFunction = null, Auctions existingAuctions = null) {
			var result = new Auctions();
			var htmlDoc = new HtmlDocument();
			htmlDoc.LoadHtml(html);

			var searchResultNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@id=\"cars-search-results\"]");
			var auctionNodes = searchResultNode.SelectNodes(".//a[@href]");
			foreach(var auctionNode in auctionNodes) {
				try {
					var url = HttpUtility.HtmlDecode(auctionNode.Attributes["href"].Value);
					if(existingAuctions != null && existingAuctions.ContainsKey(url)) {
						result[url] = existingAuctions[url];
						continue;
					}
					var priceDiv = auctionNode.SelectSingleNode(".//div[contains(concat(' ',normalize-space(@class),' '),' item-lift-price ')]");
					var priceTag = HttpUtility.HtmlDecode(priceDiv.ChildNodes["strong"]?.InnerHtml) ?? string.Empty;
					var price = -1.0;
					if(double.TryParse(priceTag.Replace(" ", string.Empty).Replace("€", string.Empty), out var doubleValue)) {
						price = doubleValue;
					}
					result[url] = new Auction() {
						IsForScrapyards = Math.Abs(price - (-1.0)) < double.Epsilon,
						Price = price,
						Uri = url,
						ReservationSuccess = false
					};

					logFunction?.Invoke(result[url].ToString());
				} catch(Exception e) {
					Log.Warning(e.Message);
				}
			}

			return result;
		}

		private Auction GetAuction(string key) {
			return this.auctions[key];
		}

		private void SetAuction(string key, Auction value) {
			this.auctions[key] = value;
		}
	}

	public record Auction {
		private const string MainUrl = "https://www.vaurioajoneuvo.fi";

		public string Uri {
			get;
			internal set;
		}

		public double Price {
			get;
			init;
		}

		public bool IsForScrapyards {
			get;
			init;
		}

		public bool ReservationSuccess {
			get;
			set;
		}

		public string FullUri => new Uri(MainUrl).Append(this.Uri).AbsoluteUri;

		public long ElapsedWhileReserving {
			get;
			set;
		}

		public override string ToString() {
			return $"{$"[price: {this.Price}]",-15} {$"[scrapyards only: {(this.IsForScrapyards ? "yes" : "no")}]",-25} {$"[uri: {this.Uri}]",-50}";
		}
	}
}