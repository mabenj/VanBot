namespace VanBot.Services {
	using System.Collections.Generic;

	public static class Proxies {
		private static readonly IEnumerator<string> Generator = GetProxies().GetEnumerator();

		private static readonly string[] AllProxies = {
			"foo:bar"
		};

		public static string GetOne() {
			Generator.MoveNext();
			return $"http://{Generator.Current}";
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