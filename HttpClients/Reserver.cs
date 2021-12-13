﻿namespace VanBot.HttpClients {
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
	using VanBot.Settings;

	internal class Reserver {
		private readonly CookieContainer cookies;
		private readonly HttpClient httpClient;
		private readonly string password;
		private readonly PaymentMethod paymentMethod;
		private readonly string username;

		internal Reserver(string username, string password, PaymentMethod paymentMethod) {
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
				throw new ReservationException($"Could not reserve auction '{auction.Name}': {e.Message}", e);
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
			_ = this.GetReservedAuctionName(out expirationTime);
		}

		public string GetReservedAuctionName(out long expirationTime) {
			// ReSharper disable once StringLiteralTypo
			const string Url = "https://www.vaurioajoneuvo.fi/kayttajalle/omat-tiedot/#tilaukset";
			var html = this.GetHtml(Url, out _);
			var htmlParser = new HtmlParser(html);
			expirationTime = htmlParser.GetReservedAuctionExpiration();
			// ReSharper disable once StringLiteralTypo
			return htmlParser.GetReservedAuctionUri().Replace("/tuote/", string.Empty);
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
			const string LoginUrl = "https://www.vaurioajoneuvo.fi/kayttajalle/kirjaudu-sisaan/";
			const string Referer = "https://www.vaurioajoneuvo.fi/kayttajalle/kirjaudu-sisaan/";

			var loginPageHtml = this.GetHtml(LoginUrl, out var status);
			if(status != HttpStatusCode.OK) {
				return $"Failed to fetch cm_token from login page: status {(int) status}";
			}
			var htmlParser = new HtmlParser(loginPageHtml);
			var cmToken = htmlParser.GetLoginCmToken();

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
			var reservationUrl = $"https://www.vaurioajoneuvo.fi/api/1.0.0/product/{productUuid}/reserve/";
			using var request = new HttpRequestMessage(HttpMethod.Post, reservationUrl);
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
			const string Url = "https://www.vaurioajoneuvo.fi/api/1.0.0/token/";
			const string Referer = "https://www.vaurioajoneuvo.fi/";

			using var request = new HttpRequestMessage(HttpMethod.Get, Url);
			request.Headers.Referrer = new Uri(Referer);
			var response = await this.httpClient.SendAsync(request);
			return response.StatusCode != HttpStatusCode.OK ? $"Failed to fetch svt-buyers cookie: status {(int) response.StatusCode}" : null;
		}
	}

	public record CmTokenDto([property: JsonProperty("cm_token")] string Token);

	public record ContactDetails(string FirstName, string LastName, string PhoneNumber, string Street, string Zip, string City, string Country);
}