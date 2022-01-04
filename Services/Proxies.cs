namespace VanBot.Services {
	using System.Collections.Generic;

	public static class Proxies {
		private static readonly IEnumerator<KeyValuePair<string, string>> Generator = GetProxies().GetEnumerator();

		private static readonly Dictionary<string, string> AllProxies = new() {
			{ "eu-north-1 (Stockholm)", "https://hffg4pca16.execute-api.eu-north-1.amazonaws.com/products" },
			{ "eu-central-1 (Frankfurt)", "https://sv1hbol4bk.execute-api.eu-central-1.amazonaws.com/products" },
			{ "eu-west-2 (London)", "https://iikj56ddmh.execute-api.eu-west-2.amazonaws.com/products" },
			{ "eu-west-3 (Paris)", "https://i421zynll1.execute-api.eu-west-3.amazonaws.com/products" },
			{ "eu-west-1 (Ireland)", "https://rtedaw6qql.execute-api.eu-west-1.amazonaws.com/products" },
		};

		public static string GetNameOfCurrent() {
			return Generator.Current.Key;
		}

		public static string GetOne() {
			Generator.MoveNext();
			return Generator.Current.Value;
		}

		private static IEnumerable<KeyValuePair<string, string>> GetProxies() {
			while(true) {
				foreach(var proxy in AllProxies) {
					yield return proxy;
				}
			}
			// ReSharper disable once IteratorNeverReturns
		}
	}
}