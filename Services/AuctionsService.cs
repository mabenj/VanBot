﻿namespace VanBot.Services {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Net.Http;
	using System.Net.Http.Headers;
	using System.Text;
	using System.Threading.Tasks;
	using System.Web;

	using Newtonsoft.Json;

	using RandomUserAgent;

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

		private readonly HttpClient httpClient;
		private readonly string nonce;
		private readonly string originalFullApiUrl;
		private readonly Random random;
		private int requestCounter;

		internal AuctionsService(string urlWithQuery) {
			this.httpClient = new HttpClient();
			this.httpClient.DefaultRequestHeaders.Referrer = new Uri(Referer);

			this.httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
			this.httpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("compress");
			this.httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-GB,en-US;q=0.9,en;q=0.8");
			this.httpClient.DefaultRequestHeaders.Connection.ParseAdd("close");
			this.httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue() {
				NoCache = true
			};
			this.httpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
			this.httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
			this.httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
			this.httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
			this.httpClient.DefaultRequestHeaders.Add("Sec-GPC", "1");

			this.originalFullApiUrl = GetFullApiUrl(urlWithQuery);
			this.requestCounter = 0;
			this.nonce = Guid.NewGuid().ToString("n")[..4];
			this.random = new Random();
		}

		private string FullApiUrl => $"{this.originalFullApiUrl}&[{this.nonce}{++this.requestCounter}]";

		internal AuctionCollection GetAuctions(out int rateLimitRemaining) {
			this.httpClient.DefaultRequestHeaders.UserAgent.Clear();
			while(!this.httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(RandomUa.RandomUserAgent)) { }

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
	}

	internal record AuctionsApiResult([property: JsonProperty("status")] string Status, [property: JsonProperty("items")] IEnumerable<Auction> Auctions);
}