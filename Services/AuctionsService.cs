namespace VanBot.Services {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Net;
	using System.Net.Http;
	using System.Net.Http.Headers;
	using System.Text;
	using System.Threading.Tasks;
	using System.Web;

	using Newtonsoft.Json;

	using VanBot.Auctions;
	using VanBot.Exceptions;

	public class AuctionsService {
		private const string AreaKey = "area";
		private const string BaseApiUrl = "https://www.vaurioajoneuvo.fi/api/1.0.0/products/";
		private const string BrandKey = "brand";
		private const string ConditionKey = "condition";
		private const string MaxPriceKey = "price_max";
		private const string MaxYearKey = "model_year_max";
		private const string MinPriceKey = "price_min";
		private const string MinYearKey = "model_year_min";
		private const string Referer = "https://www.vaurioajoneuvo.fi/";
		private const string SaleConditionKey = "sale_condition";
		private const string TypeKey = "type";

		private readonly string nonce;
		private readonly string originalFullApiUrl;

		private HttpClient httpClient;

		internal AuctionsService(string urlWithQuery) {
			this.originalFullApiUrl = GetFullApiUrl(urlWithQuery);
			this.nonce = Guid.NewGuid().ToString("n")[..4];
			this.RequestsMade = 0;
			this.InitHttpClient();
		}

		public int RequestsMade {
			get;
			private set;
		}

		private string FullApiUrl => $"{this.originalFullApiUrl}&[{this.nonce}{this.RequestsMade}]";

		internal AuctionCollection GetAuctions(out int rateLimitRemaining) {
			this.InitHttpClient();
			this.RequestsMade++;
			var response = Task.Run(() => this.httpClient.GetAsync(this.FullApiUrl)).Result;

			if(response.Headers.Contains("X-Robots-Tag")) {
				throw new CaptchaException("Encountered captcha while fetching auctions");
			}

			var json = Task.Run(() => response.Content.ReadAsStringAsync()).Result;
			var result = JsonConvert.DeserializeObject<AuctionsApiResult>(json);
			if(result?.Status != "OK") {
				throw new Exception($"Could not fetch auctions: {result?.Status}");
			}
			rateLimitRemaining = Convert.ToInt32(response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) ? values.First() : "-1");
			return AuctionCollection.FromEnumerable(result.Auctions);
		}

		private static void AppendQueryParamIfNotNullOrEmpty(ref StringBuilder strBuilder, string key, string value) {
			if(!string.IsNullOrWhiteSpace(value)) {
				strBuilder.Append($"{key}[]={value}&");
			}
		}

		private static string GetFullApiUrl(string urlWithQuery) {
			var query = HttpUtility.ParseQueryString(new Uri(urlWithQuery).Query);

			var types = query.Get(TypeKey)?.Split(",") ?? Array.Empty<string>();
			var brand = query.Get(BrandKey);
			var minYear = query.Get(MinYearKey);
			var maxYear = query.Get(MaxYearKey);
			var minPrice = query.Get(MinPriceKey);
			var maxPrice = query.Get(MaxPriceKey);
			var area = query.Get(AreaKey);
			var saleCondition = query.Get(SaleConditionKey);
			var condition = query.Get(ConditionKey);

			var result = new StringBuilder(BaseApiUrl + "?");

			foreach(var type in types) {
				AppendQueryParamIfNotNullOrEmpty(ref result, TypeKey, type);
			}
			AppendQueryParamIfNotNullOrEmpty(ref result, BrandKey, brand);
			AppendQueryParamIfNotNullOrEmpty(ref result, MinYearKey, minYear);
			AppendQueryParamIfNotNullOrEmpty(ref result, MaxYearKey, maxYear);
			AppendQueryParamIfNotNullOrEmpty(ref result, MinPriceKey, minPrice);
			AppendQueryParamIfNotNullOrEmpty(ref result, MaxPriceKey, maxPrice);
			AppendQueryParamIfNotNullOrEmpty(ref result, AreaKey, area);
			AppendQueryParamIfNotNullOrEmpty(ref result, SaleConditionKey, saleCondition);
			AppendQueryParamIfNotNullOrEmpty(ref result, ConditionKey, condition);

			return result.ToString().TrimEnd('&');
		}

		private void InitHttpClient() {
			var proxy = new WebProxy {
				//Address = new Uri("http://proxy.crawlera.com:8011"),
				Address = new Uri(Proxies.GetOne()),
				BypassProxyOnLocal = true,
				UseDefaultCredentials = false,
				Credentials = new NetworkCredential(
					userName: "APIKEY",
					password: "")
			};
			var httpClientHandler = new HttpClientHandler() {
				Proxy = proxy,
				ServerCertificateCustomValidationCallback =
					(httpRequestMessage, cert, cetChain, policyErrors) => true
			};
			this.httpClient = new HttpClient(httpClientHandler);
			this.httpClient.DefaultRequestHeaders.Referrer = new Uri(Referer);
			this.httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
			this.httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue() {
				NoCache = true
			};
		}
	}

	internal record AuctionsApiResult([property: JsonProperty("status")] string Status, [property: JsonProperty("items")] IEnumerable<Auction> Auctions);
}