#region

#endregion

namespace VanBot.Bots {
	using System;
	using System.Net;
	using System.Net.Http;
	using System.Text;
	using System.Threading.Tasks;

	using global::VanBot.Helpers;
	using global::VanBot.Logger;

	using Newtonsoft.Json;
	using Newtonsoft.Json.Serialization;

	public class TelegramBot {
		private readonly string chatKey;
		private readonly HttpClient http;
		private readonly string sendMessageApiUrl;
		private readonly string testChatKeyApiUrl;

		public TelegramBot(string chatKey) {
			this.chatKey = chatKey;
			this.http = new HttpClient();
			var baseApiUrl = "https://telegram-botti.herokuapp.com/bot";
			if(Utilities.IsDebug()) {
				//baseApiUrl = "https://b8e0-80-221-79-96.ngrok.io/bot";
			}

			this.sendMessageApiUrl = $"{baseApiUrl}/sendMessage";
			this.testChatKeyApiUrl = $"{baseApiUrl}/chatKey";
		}

		public async void NotifyCaptcha() {
			// ReSharper disable StringLiteralTypo
			await this.SendMessage("Ne luulee et oon botti. Käy tekemäs captcha ja käynnistä uudelleen.");
			// ReSharper restore StringLiteralTypo
		}

		public void NotifyNewAuction(Auction auction) {
			// ReSharper disable StringLiteralTypo
			const string Prefix = "Uus vehje ois tarjolla:";
			var priceTag = auction.IsForScrapyards ? "Vain purkamoille" : $"{auction.Price}€";
			var message = $"{Prefix} {auction.FullProductPageUri} \r\nHinta: <b>{priceTag}</b>";
			// ReSharper restore StringLiteralTypo
			Task.Run(() => this.SendMessage(message));
		}

		public void NotifyNewReservation(Auction auction, int reservationMinutes) {
			// ReSharper disable StringLiteralTypo
			var message = $"Tää vehje ois <b>varattu</b> (<i>{reservationMinutes} min</i>)\r\n{auction.FullProductPageUri}";
			// ReSharper restore StringLiteralTypo
			Task.Run(() => this.SendMessage(message));
		}

		public async Task<bool> TestChatKey() {
			try {
				var response = await this.http.GetAsync($"{this.testChatKeyApiUrl}/{this.chatKey}");
				if(response.StatusCode == HttpStatusCode.NotFound) {
					Log.Warning("Telegram bot chat key is invalid");
					return false;
				}

				var responseBody = JsonConvert.DeserializeObject<ChatDto>(await response.Content.ReadAsStringAsync());
				return responseBody?.ChatId > 0;
			} catch(Exception e) {
				Log.Error($"Error while testing chat key: {e.Message}");
				return false;
			}
		}

		private async Task<bool> SendMessage(string message) {
			try {
				var payload = JsonConvert.SerializeObject(
					new MessageDto(this.chatKey, message),
					new JsonSerializerSettings() {
						ContractResolver = new CamelCasePropertyNamesContractResolver()
					});
				var body = new StringContent(payload, Encoding.UTF8, "application/json");
				var response = await this.http.PostAsync(this.sendMessageApiUrl, body);

				if(response.StatusCode == HttpStatusCode.NotFound) {
					Log.Error("Invalid chat key used for telegram bot");
					return false;
				}

				var responseBody = JsonConvert.DeserializeObject<MessageResponseDto>(await response.Content.ReadAsStringAsync());
				return responseBody?.Ok ?? false;
			} catch(Exception e) {
				Log.Error($"Error while sending message to the telegram bot: {e.Message}");
				return false;
			}
		}
	}

	public record MessageDto(string ChatKey, string Message);

	public record MessageResponseDto(bool Ok);

	public record ChatDto(int ChatId, int CreatedById, string ChatKey, DateTime CreationDate);
}