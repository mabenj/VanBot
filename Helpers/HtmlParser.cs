namespace VanBot.Helpers {
	using System.Linq;

	using HtmlAgilityPack;

	using VanBot.HttpClients;

	internal class HtmlParser {
		private readonly HtmlDocument htmlDoc;

		internal HtmlParser(string html) {
			this.htmlDoc = new HtmlDocument();
			this.htmlDoc.LoadHtml(html);
		}

		public ContactDetails GetOrderContactDetails() {
			var firstName = this.htmlDoc.DocumentNode.SelectSingleNode(".//input[@id=\"first_name\"]").Attributes["value"].Value;
			var lastName = this.htmlDoc.DocumentNode.SelectSingleNode(".//input[@id=\"last_name\"]").Attributes["value"].Value;
			var phone = this.htmlDoc.DocumentNode.SelectSingleNode(".//input[@id=\"phone\"]").Attributes["value"].Value;
			var street = this.htmlDoc.DocumentNode.SelectSingleNode(".//input[@id=\"address_street\"]").Attributes["value"].Value;
			var zip = this.htmlDoc.DocumentNode.SelectSingleNode(".//input[@id=\"address_zip\"]").Attributes["value"].Value;
			var city = this.htmlDoc.DocumentNode.SelectSingleNode(".//input[@id=\"address_city\"]").Attributes["value"].Value;
			var country = this.htmlDoc.DocumentNode.SelectNodes(".//select[@id=\"address_country\"]/option[@selected]")
				.First(n => !string.IsNullOrWhiteSpace(n.Attributes["value"].Value))
				.Attributes["value"].Value;
			return new ContactDetails(firstName, lastName, phone, street, zip, city, country);
		}

		public string GetOrderStageToken() {
			const string XPath = "//input[@name=\"stage_token\"]";
			var inputNode = this.htmlDoc.DocumentNode.SelectSingleNode(XPath);
			return inputNode?.Attributes["value"].Value;
		}

		public long GetReservedAuctionExpiration() {
			const string XPath = "//span[@data-expiration]";
			var expirationSpan = this.htmlDoc.DocumentNode.SelectSingleNode(XPath);
			if(long.TryParse(expirationSpan?.Attributes["data-expiration"]?.Value ?? string.Empty, out var unixTimestamp)) {
				return unixTimestamp;
			}
			return -1L;
		}

		public string GetReservedAuctionUri() {
			// ReSharper disable once StringLiteralTypo
			const string XPath = "//td[@data-label=\"Kohde\"]/a";
			var anchorTag = this.htmlDoc.DocumentNode.SelectSingleNode(XPath);
			return anchorTag?.Attributes["href"]?.Value;
		}

		internal string GetLoginCmToken() {
			const string XPath = "//form[@action=\"https://www.vaurioajoneuvo.fi/kayttajalle/kirjaudu-sisaan/\"]/input[@name=\"cm_token\"]";
			var inputNode = this.htmlDoc.DocumentNode.SelectSingleNode(XPath);
			return inputNode?.Attributes["value"].Value;
		}

		internal string GetProductCmToken() {
			const string XPath = "//form[@data-action=\"reserve-product\"]/input[@name=\"cm_token\"]";
			var inputNode = this.htmlDoc.DocumentNode.SelectSingleNode(XPath);
			return inputNode?.Attributes["value"].Value;
		}

		internal string GetProductUuid() {
			var scripts = this.htmlDoc.DocumentNode
				.Descendants("script")
				.Where(node => node?.InnerHtml != null)
				.Select(node => node.InnerHtml);

			var isFirst = true;
			foreach(var script in scripts) {
				if(isFirst) {
					// first script is google analytics and can be skipped
					isFirst = false;
					continue;
				}
				var lines = script.Split(";");
				foreach(var line in lines) {
					var keyValue = line.Split("=");
					if(keyValue.Length != 2) {
						continue;
					}
					if(keyValue[0].Trim() == "window.PRODUCT_UUID") {
						return keyValue[1].Trim().Trim('\'', '\"');
					}
				}
			}

			return null;
		}
	}
}