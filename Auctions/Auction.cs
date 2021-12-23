namespace VanBot.Auctions {
	using System;

	using Newtonsoft.Json;

	using VanBot.Helpers;

	public class Auction {
		private const string MainUrl = "https://www.vaurioajoneuvo.fi";
		private static int mockId = 1337;

		private Auction() { }

		[JsonProperty("brand")]
		public string Brand {
			get;
			private set;
		}

		// ReSharper disable once StringLiteralTypo
		public string FullOrderPageUri => new Uri(MainUrl).Append("tilaus", this.Slug).AbsoluteUri;

		// ReSharper disable once StringLiteralTypo
		public string FullProductPageUri => new Uri(MainUrl).Append("tuote", this.Slug).AbsoluteUri;

		[JsonProperty("pid")]
		public int Id {
			get;
			private set;
		}

		public bool IsForScrapyards => this.Price == null;

		[JsonProperty("model")]
		public string Model {
			get;
			private set;
		}

		[JsonProperty("price")]
		public string Price {
			get;
			private set;
		}

		[JsonProperty("slug")]
		public string Slug {
			get;
			private set;
		}

		[JsonProperty("model_year")]
		public string Year {
			get;
			private set;
		}

		public override string ToString() {
			// ReSharper disable once StringLiteralTypo
			return $"[pid: {this.Id}] "
				+ $"[make: {this.Brand.Truncate(15)}] "
				+ $"[model: {this.Model.Truncate(25)}] "
				+ $"[year: {this.Year}] "
				+ $"[price: {this.Price}] "
				+ $"[dismantlers only: {(this.IsForScrapyards ? "yes" : "no")}]";
		}

		internal void ChangeIdToMockId() {
			this.Id = mockId++;
		}
	}
}