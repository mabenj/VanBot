namespace VanBot.Services {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Net;
	using System.Net.Http;
	using System.Text;
	using System.Threading.Tasks;

	using Newtonsoft.Json;

	using VanBot.Auctions;
	using VanBot.Exceptions;
	using VanBot.Helpers;
	using VanBot.Logger;
	using VanBot.Settings;

	internal class ReserveService {
		private readonly CookieContainer cookies;
		private readonly HttpClient httpClient;
		private readonly string password;
		private readonly PaymentMethod paymentMethod;
		private readonly string username;

		internal ReserveService(string username, string password, PaymentMethod paymentMethod) {
			this.username = username;
			this.password = password;
			this.paymentMethod = paymentMethod;
			this.cookies = new CookieContainer();
			var handler = new HttpClientHandler() {
				CookieContainer = this.cookies,
				UseCookies = true,
				UseDefaultCredentials = false
			};
			this.httpClient = new HttpClient(handler);
		}

		public void AttemptToReserveAuction(Auction auction) {
			try {
				var (productUuid, cmToken) = this.GetProductUuidAndCmToken(auction.FullProductPageUri, out var error);
				if(error != null) {
					throw new ReservationException(error);
				}

				this.SendReservationRequest(auction.FullProductPageUri, productUuid, cmToken);
			} catch(Exception e) {
				throw new ReservationException(e.Message, e);
			}
		}

		public void ExtendReservation(Auction auction, ref long expirationTime) {
			var orderUrl = auction.FullOrderPageUri;
			var (stageToken, contactDetails) = this.GetStageTokenAndContactDetails(orderUrl, out var error);
			if(error != null) {
				throw new ReservationException(error);
			}
			if(stageToken == null) {
				throw new ReservationException($"Could not fetch stage token from '{orderUrl}'");
			}
			if(contactDetails == null) {
				throw new ReservationException($"Could not fetch contact details from '{orderUrl}'");
			}

			using var request = new HttpRequestMessage(HttpMethod.Post, orderUrl);
			var postData = new Dictionary<string, string>() {
				{ "stage_token", stageToken },
				{ "first_name", contactDetails.FirstName },
				{ "last_name", contactDetails.LastName },
				{ "phone", contactDetails.PhoneNumber },
				{ "address_street", contactDetails.Street },
				{ "address_zip", contactDetails.Zip },
				{ "address_city", contactDetails.City },
				{ "address_country", contactDetails.Country },
				{ "details_ok[]", "1" },
				{ "payment_method", "9" },
				{ "stage-payment-provider", this.paymentMethod.Name }
			};
			request.Content = new FormUrlEncodedContent(postData);
			request.Headers.Referrer = new Uri(orderUrl);
			// ReSharper disable once AccessToDisposedClosure
			var response = Task.Run(() => this.httpClient.SendAsync(request)).Result;
			if(!response.IsSuccessStatusCode) {
				throw new ReservationException($"Extend-reservation request responded with status {(int) response.StatusCode} ({response.StatusCode})");
			}
			_ = this.GetReservedAuctionSlug(out expirationTime);
		}

		public string GetReservedAuctionSlug(out long expirationTime) {
			try {
				// ReSharper disable once StringLiteralTypo
				var html = this.GetHtml(UrlConstants.OrdersUrl, out _);
				var htmlParser = new HtmlParser(html);
				expirationTime = htmlParser.GetReservedAuctionExpiration();
				// ReSharper disable once StringLiteralTypo
				return htmlParser.GetReservedAuctionUri()?.Replace("/tuote/", string.Empty);
			} catch(Exception e) {
				Log.Error("Error fetching reserved auction slug: " + e.Message);
				expirationTime = -1;
				return null;
			}
		}

		internal void Initialize(out string[] errors) {
			var errorList = new List<string>();

			var buyersError = Task.Run(this.SetSvtBuyersCookie).Result;
			if(buyersError != null) {
				errorList.Add(buyersError);
			}

			var loginError = Task.Run(this.SendLoginRequest).Result;
			if(loginError != null) {
				errorList.Add(loginError);
			}

			errors = errorList.ToArray();
		}

		private string GetHtml(string url, out HttpStatusCode status) {
			var response = Task.Run(() => this.httpClient.GetAsync(url)).Result;
			status = response.StatusCode;
			return Task.Run(() => response.Content.ReadAsStringAsync()).Result;
		}

		private (string, string) GetProductUuidAndCmToken(string productPageUrl, out string error) {
			error = null;

			var html = this.GetHtml(productPageUrl, out var status);
			if(status != HttpStatusCode.OK) {
				error = $"Product page did not respond with expected status (expected 200 : got {(int) status})";
			}

			var htmlParser = new HtmlParser(html);
			if(htmlParser.IsAuctionAlreadyReserved()) {
				error = "Auction is already reserved";
				return (null, null);
			}
			var productUuid = htmlParser.GetProductUuid();
			var cmToken = htmlParser.GetProductCmToken();

			return (productUuid, cmToken);
		}

		private (string, ContactDetails) GetStageTokenAndContactDetails(string orderUrl, out string error) {
			error = null;

			var html = this.GetHtml(orderUrl, out var status);
			if(status != HttpStatusCode.OK) {
				error = $"Order page did not respond with expected status (expected 200 : got {(int) status})";
			}

			var htmlParser = new HtmlParser(html);
			var stageToken = htmlParser.GetOrderStageToken();
			var contactDetails = htmlParser.GetOrderContactDetails();

			return (stageToken, contactDetails);
		}

		private async Task<string> SendLoginRequest() {
			const string LoginUrl = UrlConstants.LoginUrl;
			const string Referer = UrlConstants.LoginRefererUrl;

			var loginPageHtml = this.GetHtml(LoginUrl, out var status);
			if(status != HttpStatusCode.OK) {
				return $"Failed to fetch login page HTML: status {(int) status}";
			}
			var htmlParser = new HtmlParser(loginPageHtml);
			var cmToken = htmlParser.GetLoginCmToken();
			if(cmToken == null) {
				return "Failed to fetch cm_token from login page";
			}

			using var request = new HttpRequestMessage(HttpMethod.Post, LoginUrl);
			var postData = new Dictionary<string, string>() {
				{ "cm_token", cmToken },
				// ReSharper disable once StringLiteralTypo
				{ "stage-extranet-from", string.Empty },
				{ "username", this.username },
				{ "password", this.password }
			};
			request.Content = new FormUrlEncodedContent(postData);
			request.Headers.Referrer = new Uri(Referer);
			await this.httpClient.SendAsync(request);
			var responseCookies = this.cookies.GetCookies(new Uri(LoginUrl)).Cast<Cookie>();
			return responseCookies.Any(c => c.Name == "svt-extranet-name") ? null : "Failed to login";
		}

		private void SendReservationRequest(string productPageUrl, string productUuid, string cmToken) {
			var apiUrl = UrlConstants.GetReservationApiUrl(productUuid);
			using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
			var payload = JsonConvert.SerializeObject(new CmTokenDto(cmToken));
			request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
			if(request.Content.Headers.ContentType != null) {
				request.Content.Headers.ContentType.CharSet = string.Empty;
			}
			request.Headers.Referrer = new Uri(productPageUrl);
			request.Headers.Add("X-Requested-With", "XMLHttpRequest");
			this.httpClient.SendAsync(request);
		}

		private async Task<string> SetSvtBuyersCookie() {
			using var request = new HttpRequestMessage(HttpMethod.Get, UrlConstants.TokenApiUrl);
			request.Headers.Referrer = new Uri(UrlConstants.FrontPage);
			var response = await this.httpClient.SendAsync(request);
			return response.StatusCode != HttpStatusCode.OK ? $"Failed to fetch svt-buyers cookie: status {(int) response.StatusCode}" : null;
		}
	}

	public record CmTokenDto([property: JsonProperty("cm_token")] string Token);

	public record ContactDetails(string FirstName, string LastName, string PhoneNumber, string Street, string Zip, string City, string Country);
}