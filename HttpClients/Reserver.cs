namespace VanBot.HttpClients {
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Net;
	using System.Net.Http;
	using System.Text;
	using System.Threading.Tasks;

	using Newtonsoft.Json;

	using VanBot.Exceptions;
	using VanBot.Helpers;

	internal class Reserver {
		private const string SelectedPaymentMethod = "Siirto";

		private static readonly Dictionary<string, string> PaymentMethods = new() {
			// ReSharper disable StringLiteralTypo
			{"Handelsbanken", "handelsbanken-e-payment"},
			{"Danske Bank", "sampo-web-payment"},
			{"S-Pankki", "s-pankki-verkkomaksu"},
			{"Nordea", "nordea-e-payment"},
			{"Pop", "pop-pankin-verkkomaksu"},
			{"Säästöpankki", "saastopankin-verkkomaksu"},
			{"Siirto", "siirto"},
			{"Oma Säästöpankki", "oma-saastopankin-verkkomaksu"},
			{"Ålandsbanken", "alandsbanken-e-payment"},
			{"Osuuspankki", "op-pohjola-verkkomaksu"},
			{"Aktia", "aktia-maksu"},
			// ReSharper restore StringLiteralTypo
		};

		private readonly CookieContainer cookies;
		private readonly HttpClient httpClient;
		private readonly string password;
		private readonly Stopwatch timer;
		private readonly string username;

		internal Reserver(string username, string password) {
			this.username = username;
			this.password = password;
			this.cookies = new CookieContainer();
			var handler = new HttpClientHandler() {
				CookieContainer = this.cookies,
				UseCookies = true,
				UseDefaultCredentials = false
			};
			this.httpClient = new HttpClient(handler);
			this.timer = new Stopwatch();
		}

		public bool ExtendReservation(Auction auction) {
			var orderUrl = auction.FullOrderPageUri;
			var (stageToken, contactDetails) = this.GetStageTokenAndContactDetails(orderUrl, out var error);

			using var request = new HttpRequestMessage(HttpMethod.Post, orderUrl);
			var postData = new Dictionary<string, string>() {
				{"stage_token", stageToken},
				{"first_name", contactDetails.FirstName},
				{"last_name", contactDetails.LastName},
				{"phone", contactDetails.PhoneNumber},
				{"address_street", contactDetails.Street},
				{"address_zip", contactDetails.Zip},
				{"address_city", contactDetails.City},
				{"address_country", contactDetails.Country},
				{"details_ok[]", "1"},
				{"payment_method", "9"},
				{"stage-payment-provider", PaymentMethods[SelectedPaymentMethod]}
			};
			request.Content = new FormUrlEncodedContent(postData);
			request.Headers.Referrer = new Uri(orderUrl);
			// ReSharper disable once AccessToDisposedClosure
			var response = Task.Run(() => this.httpClient.SendAsync(request)).Result;
			return response.StatusCode == HttpStatusCode.OK;
		}

		public bool ReserveAuction(Auction auction, out bool alreadyReserved, out long elapsedWhileReserving) {
			alreadyReserved = false;
			this.timer.Restart();

			try {
				var (productUuid, cmToken) = this.GetProductUuidAndCmToken(auction.FullProductPageUri, out var error);
				if(error != null) {
					throw new ReservationException(error);
				}

				return this.SendReservationRequest(auction.FullProductPageUri, productUuid, cmToken);
			} catch(Exception e) {
				throw new ReservationException($"Could not reserve auction '{auction.ProductPageUri}': {e.Message}", e);
			} finally {
				this.timer.Stop();
				elapsedWhileReserving = this.timer.ElapsedMilliseconds;
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
				{"cm_token", cmToken},
				// ReSharper disable once StringLiteralTypo
				{"stage-extranet-from", string.Empty},
				{"username", this.username},
				{"password", this.password}
			};
			request.Content = new FormUrlEncodedContent(postData);
			request.Headers.Referrer = new Uri(Referer);
			await this.httpClient.SendAsync(request);
			var responseCookies = this.cookies.GetCookies(new Uri(LoginUrl)).Cast<Cookie>();
			return responseCookies.Any(c => c.Name == "svt-extranet-name") ? null : "Failed to login";
		}

		private bool SendReservationRequest(string productPageUrl, string productUuid, string cmToken) {
			var reservationUrl = $"https://www.vaurioajoneuvo.fi/api/1.0.0/product/{productUuid}/reserve/";
			using var request = new HttpRequestMessage(HttpMethod.Post, reservationUrl);
			var payload = JsonConvert.SerializeObject(new CmTokenDto(cmToken));
			request.Content = new StringContent(payload, Encoding.ASCII, "application/json");
			if(request.Content.Headers.ContentType != null) {
				request.Content.Headers.ContentType.CharSet = string.Empty;
			}
			request.Headers.Referrer = new Uri(productPageUrl);
			request.Headers.Add("X-Requested-With", "XMLHttpRequest");
			// ReSharper disable once AccessToDisposedClosure
			var response = Task.Run(() => this.httpClient.SendAsync(request)).Result; // TODO: might want to continue once sent and not wait for response
			return response.IsSuccessStatusCode;
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