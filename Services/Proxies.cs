namespace VanBot.Services {
	using System.Collections.Generic;

	public static class Proxies {
		private static readonly IEnumerator<string> Generator = GetProxies().GetEnumerator();

		private static readonly string[] AllProxies = {
			"https://sv1hbol4bk.execute-api.eu-central-1.amazonaws.com/products",
			"https://hffg4pca16.execute-api.eu-north-1.amazonaws.com/products"
		};

		public static string GetOne() {
			Generator.MoveNext();
			return Generator.Current;
		}

		private static IEnumerable<string> GetProxies() {
			while(true) {
				foreach(var proxy in AllProxies) {
					yield return proxy;
				}
			}
		}
	}
}