namespace VanBot.Services {
	public static class UrlConstants {
		public const string FrontPage = "https://www.vaurioajoneuvo.fi/";
		public const string LoginRefererUrl = "https://www.vaurioajoneuvo.fi/kayttajalle/kirjaudu-sisaan/";
		public const string LoginUrl = "https://www.vaurioajoneuvo.fi/kayttajalle/kirjaudu-sisaan/";
		public const string OrdersUrl = "https://www.vaurioajoneuvo.fi/kayttajalle/omat-tiedot/#tilaukset";
		public const string TokenApiUrl = "https://www.vaurioajoneuvo.fi/api/1.0.0/token/";

		public static string GetReservationApiUrl(string uuid) {
			return $"https://www.vaurioajoneuvo.fi/api/1.0.0/product/{uuid}/reserve/";
		}
	}
}