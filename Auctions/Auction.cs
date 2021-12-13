namespace VanBot.Auctions {
	using System;

	using VanBot.Helpers;

	public class Auction {
		private const string MainUrl = "https://www.vaurioajoneuvo.fi";

		public Auction(string name, bool isForScrapyards, double price) {
			// ReSharper disable once StringLiteralTypo
			this.Name = name;
			this.IsForScrapyards = isForScrapyards;
			this.Price = price;
		}

		// ReSharper disable once StringLiteralTypo
		public string FullOrderPageUri => new Uri(MainUrl).Append("tilaus", this.Name).AbsoluteUri;

		// ReSharper disable once StringLiteralTypo
		public string FullProductPageUri => new Uri(MainUrl).Append("tuote", this.Name).AbsoluteUri;

		public bool IsForScrapyards {
			get;
		}

		public string Name {
			get;
		}

		public double Price {
			get;
		}

		public override string ToString() {
			// ReSharper disable once StringLiteralTypo
			return $"{$"[price: {this.Price}]",-15} {$"[dismantlers only: {(this.IsForScrapyards ? "yes" : "no")}]",-25} {$"[uri: {this.Name}]",-50}";
		}
	}
}