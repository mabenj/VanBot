namespace VanBot.Helpers {
	using System.Linq;

	using HtmlAgilityPack;

	internal class HtmlParser {
		private readonly HtmlDocument htmlDoc;

		internal HtmlParser(string html) {
			this.htmlDoc = new HtmlDocument();
			this.htmlDoc.LoadHtml(html);
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