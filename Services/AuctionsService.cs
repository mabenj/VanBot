﻿using VanBotClass = VanBot.Bots.VanBot;

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
	using VanBot.Logger;

	public class AuctionsService {
		private readonly Dictionary<string, AuctionCollection> auctionsByETag;
		private readonly string nonce;
		private readonly string queryString;
		private string currentBaseApiUrl;
		private HttpClient httpClient;
		private string newestETag;

		internal AuctionsService(string urlWithQuery) {
			this.currentBaseApiUrl = Proxies.GetOne();
			this.queryString = GetQueryString(urlWithQuery);
			this.nonce = Guid.NewGuid().ToString("n")[..4];
			this.RequestsMade = 0;
			this.auctionsByETag = new Dictionary<string, AuctionCollection>();
			this.newestETag = null;
			this.InitHttpClient();
		}

		public int RequestsMade {
			get;
			private set;
		}

		private string FullApiUrl => $"{this.currentBaseApiUrl}?{this.queryString}&[{this.nonce}{this.RequestsMade}]";

		internal AuctionCollection GetAuctions(out int rateLimitRemaining) {
			var request = new HttpRequestMessage(HttpMethod.Get, this.FullApiUrl);
			if(this.newestETag != null) {
				request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(this.newestETag));
			}
			this.RequestsMade++;
			var response = this.httpClient.Send(request);

			if(response.Headers.Contains("X-Robots-Tag")) {
				throw new CaptchaException("Encountered captcha while fetching auctions");
			}

			if(response.StatusCode == HttpStatusCode.TooManyRequests) {
				throw new Exception("Too many requests");
			}

			rateLimitRemaining = Convert.ToInt32(response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) ? values.First() : "-1");
			if(rateLimitRemaining is < VanBotClass.LowRateLimitThreshold and > -1) {
				Log.Warning("Changing proxy");
				this.currentBaseApiUrl = Proxies.GetOne();
			}

			var eTag = response.Headers.ETag?.Tag;
			if(response.StatusCode == HttpStatusCode.NotModified) {
				return this.auctionsByETag.TryGetValue(eTag ?? "-1", out var result) ? result : null;
			}

			this.newestETag = eTag;
			var auctions = ParseAuctions(response.Content);
			this.auctionsByETag.TryAdd(eTag, auctions);
			return auctions;
		}

		private static void AppendQueryParamIfNotNullOrEmpty(ref StringBuilder strBuilder, string key, string value) {
			if(!string.IsNullOrWhiteSpace(value)) {
				strBuilder.Append($"{key}[]={value}&");
			}
		}

		private static string GetQueryString(string urlWithQuery) {
			var query = HttpUtility.ParseQueryString(new Uri(urlWithQuery).Query);

			var types = query.Get(QueryKeys.Type)?.Split(",") ?? Array.Empty<string>();
			var brand = query.Get(QueryKeys.Brand);
			var minYear = query.Get(QueryKeys.MinYear);
			var maxYear = query.Get(QueryKeys.MaxYear);
			var minPrice = query.Get(QueryKeys.MinPrice);
			var maxPrice = query.Get(QueryKeys.MaxPrice);
			var area = query.Get(QueryKeys.Area);
			var saleCondition = query.Get(QueryKeys.SaleCondition);
			var condition = query.Get(QueryKeys.Condition);

			var result = new StringBuilder();
			foreach(var type in types) {
				AppendQueryParamIfNotNullOrEmpty(ref result, QueryKeys.Type, type);
			}
			AppendQueryParamIfNotNullOrEmpty(ref result, QueryKeys.Brand, brand);
			AppendQueryParamIfNotNullOrEmpty(ref result, QueryKeys.MinYear, minYear);
			AppendQueryParamIfNotNullOrEmpty(ref result, QueryKeys.MaxYear, maxYear);
			AppendQueryParamIfNotNullOrEmpty(ref result, QueryKeys.MinPrice, minPrice);
			AppendQueryParamIfNotNullOrEmpty(ref result, QueryKeys.MaxPrice, maxPrice);
			AppendQueryParamIfNotNullOrEmpty(ref result, QueryKeys.Area, area);
			AppendQueryParamIfNotNullOrEmpty(ref result, QueryKeys.SaleCondition, saleCondition);
			AppendQueryParamIfNotNullOrEmpty(ref result, QueryKeys.Condition, condition);

			return result.ToString().TrimEnd('&');
		}

		private static AuctionCollection ParseAuctions(HttpContent httpContent) {
			var json = Task.Run(httpContent.ReadAsStringAsync).Result;
			var result = JsonConvert.DeserializeObject<AuctionsApiResult>(json);
			if(result?.Status != "OK") {
				throw new Exception($"Could not fetch auctions: {result?.Status}");
			}
			var auctions = AuctionCollection.FromEnumerable(result.Auctions);
			return auctions;
		}

		private void InitHttpClient() {
			const string Referer = "https://www.vaurioajoneuvo.fi/";
			this.httpClient = new HttpClient();
			this.httpClient.DefaultRequestHeaders.Referrer = new Uri(Referer);
			this.httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
			this.httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue() {
				NoCache = true
			};
		}
	}

	internal record AuctionsApiResult([property: JsonProperty("status")] string Status, [property: JsonProperty("items")] IEnumerable<Auction> Auctions);

	internal static class QueryKeys {
		internal const string Area = "area";
		internal const string Brand = "brand";
		internal const string Condition = "condition";
		internal const string MaxPrice = "price_max";
		internal const string MaxYear = "model_year_max";
		internal const string MinPrice = "price_min";
		internal const string MinYear = "model_year_min";
		internal const string SaleCondition = "sale_condition";
		internal const string Type = "type";
	}
}