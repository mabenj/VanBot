using VanBotClass = VanBot.Bots.VanBot;

namespace VanBot.Services {
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Net;
	using System.Net.Http;
	using System.Net.Http.Headers;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Web;

	using Newtonsoft.Json;

	using VanBot.Auctions;
	using VanBot.Exceptions;
	using VanBot.Logger;

	public class AuctionsService {
		private readonly Dictionary<string, AuctionCollection> auctionsByETag;
		private readonly HttpClient httpClient;
		private readonly object lockObject = new();
		private readonly string nonce;
		private readonly string queryString;

		private string currentBaseApiUrl;
		private string newestETag;

		internal AuctionsService(string urlWithQuery) {
			this.currentBaseApiUrl = Proxies.GetOne();
			this.queryString = GetQueryString(urlWithQuery);
			this.nonce = Guid.NewGuid().ToString("n")[..4];
			this.RequestsMade = 0;
			this.auctionsByETag = new Dictionary<string, AuctionCollection>();
			this.newestETag = null;
			this.httpClient = new HttpClient();
			this.httpClient.DefaultRequestHeaders.Referrer = new Uri(UrlConstants.FrontPage);
			this.httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
			this.httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue() {
				NoCache = true
			};
		}

		public int RequestsMade {
			get;
			private set;
		}

		private string FullApiUrl => $"{this.currentBaseApiUrl}?{this.queryString}&[{this.nonce}{this.RequestsMade}]";

		public AuctionCollection WaitForNewAuctions(int interval, CancellationToken token, out int rateLimitRemaining) {
			rateLimitRemaining = -1;
			_ = this.GetAuctions(out _, out var etag);
			AuctionCollection newAuctions = null;
			var newAuctionsFound = false;

			var r = new Random();

			while(!newAuctionsFound && !token.IsCancellationRequested) {
				Task.Run(
					() => {
						var timer = new Stopwatch();
						timer.Start();
						var proxyName = Proxies.GetNameOfCurrent();
						if(!this.TryGetAuctions(etag, out var auctions, out var rLimitRemaining)) {
							timer.Stop();
							this.LogStatus(rLimitRemaining, timer.ElapsedMilliseconds, proxyName);
							return;
						}
						lock(this.lockObject) {
							newAuctions = auctions;
							newAuctionsFound = true;
						}
					},
					token);

				if(newAuctionsFound) {
					break;
				}
				Thread.Sleep(interval);
				//if(r.Next(0, 100) < 0.3) {
				//	this.queryString = GetQueryString("https://www.vaurioajoneuvo.fi/?type=PassengerCar&brand=Volvo&price_min=1000&condition=no_demo");
				//}
			}
			return newAuctions;
		}

		internal AuctionCollection GetAuctions(out int rateLimitRemaining, out string eTag) {
			var request = new HttpRequestMessage(HttpMethod.Get, this.FullApiUrl);
			if(this.newestETag != null) {
				request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(this.newestETag));
			}
			this.RequestsMade++;
			var response = Task.Run(() => this.httpClient.SendAsync(request)).Result;
			var content = Task.Run(() => response.Content.ReadAsStringAsync()).Result;

			if(response.Headers.Contains("X-Robots-Tag")) {
				throw new CaptchaException("Encountered captcha while fetching auctions");
			}

			if(response.StatusCode == HttpStatusCode.TooManyRequests) {
				throw new Exception("Too many requests");
			}

			rateLimitRemaining = Convert.ToInt32(response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) ? values.First() : "-1");
			if(rateLimitRemaining is < VanBotClass.LowRateLimitThreshold and > -1) {
				this.currentBaseApiUrl = Proxies.GetOne();
			}

			eTag = response.Headers.ETag?.Tag;
			if(response.StatusCode == HttpStatusCode.NotModified) {
				return this.auctionsByETag.TryGetValue(eTag ?? "-1", out var result) ? result : null;
			}

			this.newestETag = eTag;
			var auctions = ParseAuctions(content);
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

		private static AuctionCollection ParseAuctions(string json) {
			var result = JsonConvert.DeserializeObject<AuctionsApiResult>(json);
			if(result?.Status != "OK") {
				throw new Exception($"Could not fetch auctions: {result?.Status}");
			}
			var auctions = AuctionCollection.FromEnumerable(result.Auctions);
			return auctions;
		}

		private void LogStatus(int rateLimitRemaining, long latencyMillis, string currentProxyName) {
			var latencyColor = latencyMillis > 500 ? LoggerColor.Red : latencyMillis > 300 ? LoggerColor.Yellow : LoggerColor.Green;
			var rateLimitColor = rateLimitRemaining < 10 ? LoggerColor.Red : rateLimitRemaining < 20 ? LoggerColor.Yellow : LoggerColor.Green;

			var formattedStatus = $" [lat: {latencyColor}{$"{latencyMillis}",-4}{LoggerColor.Reset} ms]"
				+ $" [rate_limit: {rateLimitColor}{$"{rateLimitRemaining}",-2}{LoggerColor.Reset}]"
				+ $" [proxy: {currentProxyName}]";
			Log.Info(formattedStatus);
		}

		private bool TryGetAuctions(string etag, out AuctionCollection auctions, out int rateLimitRemaining) {
			HttpRequestMessage request;
			lock(this.lockObject) {
				request = new HttpRequestMessage(HttpMethod.Get, this.FullApiUrl);
				request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
				this.RequestsMade++;
			}
			var response = this.httpClient.Send(request);

			if(response.Headers.Contains("X-Robots-Tag")) {
				throw new CaptchaException("Encountered captcha while fetching auctions");
			}

			if(response.StatusCode == HttpStatusCode.TooManyRequests) {
				throw new Exception("Too many requests");
			}

			rateLimitRemaining = Convert.ToInt32(response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) ? values.First() : "-1");
			if(rateLimitRemaining is < VanBotClass.LowRateLimitThreshold and > -1) {
				lock(this.lockObject) {
					this.currentBaseApiUrl = Proxies.GetOne();
				}
			}

			if(rateLimitRemaining < 3) {
				Log.Warning("Rate-limit critical");
				Environment.Exit(1);
			}

			if(response.StatusCode == HttpStatusCode.NotModified) {
				auctions = null;
				return false;
			}
			auctions = ParseAuctions(Task.Run(() => response.Content.ReadAsStringAsync()).Result);
			return true;
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