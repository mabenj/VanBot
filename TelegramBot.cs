#region

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

#endregion

namespace VanBot {
    public class TelegramBot {
        private readonly string chatKey;
        private readonly HttpClient http;
        private readonly string sendMessageApiUrl;
        private readonly string testChatKeyApiUrl;

        public TelegramBot(string chatKey) {
            this.chatKey = chatKey;
            this.http = new HttpClient();
            var baseApiUrl = "https://telegram-botti.herokuapp.com/bot";
            if (Utilities.IsDebug()) {
                baseApiUrl = "https://b8e0-80-221-79-96.ngrok.io/bot";
            }

            this.sendMessageApiUrl = $"{baseApiUrl}/sendMessage";
            this.testChatKeyApiUrl = $"{baseApiUrl}/chatKey";
        }

        public async Task<bool> TestChatKey() {
            try {
                var response = await this.http.GetAsync($"{testChatKeyApiUrl}/{this.chatKey}");
                if (response.StatusCode == HttpStatusCode.NotFound) {
                    Log.Warning("Telegram bot chat key is invalid");
                    return false;
                }

                var responseBody = JsonConvert.DeserializeObject<ChatDto>(await response.Content.ReadAsStringAsync());
                return responseBody.ChatId > 0;
            } catch (Exception e) {
                Log.Error($"Error while testing chat key: {e.Message}");
                return false;
            }
        }

        public async Task<bool> SendMessage(string message) {
            try {
                var payload = JsonConvert.SerializeObject(
                    new MessageDto(this.chatKey, message),
                    new JsonSerializerSettings() {
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    });
                var body = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await this.http.PostAsync(sendMessageApiUrl, body);

                if (response.StatusCode == HttpStatusCode.NotFound) {
                    Log.Error("Invalid chat key used for telegram bot");
                    return false;
                }

                var responseBody = JsonConvert.DeserializeObject<MessageResponseDto>(await response.Content.ReadAsStringAsync());
                return responseBody.Ok;
            } catch (Exception e) {
                Log.Error($"Error while sending message to the telegram bot: {e.Message}");
                return false;
            }
        }
    }

    public record MessageDto(string ChatKey, string Message);

    public record MessageResponseDto(bool Ok);

    public record ChatDto(int ChatId, int CreatedById, string ChatKey, DateTime CreationDate);
}