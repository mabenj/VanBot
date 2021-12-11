namespace VanBot.HttpClients {
	using System.Net;
	using System.Net.Http;
	using System.Threading.Tasks;

	internal class Scraper {
		private readonly HttpClient httpClient;

		internal Scraper() {
			this.httpClient = new HttpClient();
		}

		internal string GetHtml(string url, out HttpStatusCode status) {
			var response = Task.Run(() => this.httpClient.GetAsync(url)).Result;
			status = response.StatusCode;
			return Task.Run(() => response.Content.ReadAsStringAsync()).Result;
		}
	}
}