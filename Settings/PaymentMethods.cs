namespace VanBot.Settings {
	using System.Linq;
#pragma warning disable 660,661
	public class PaymentMethod {
		public static readonly PaymentMethod[] AllPaymentMethods = {
			Handelsbank,
			DanskeBank,
			Spankki,
			Nordea,
			Pop,
			Saastopankki,
			Siirto,
			OmaSaastopankki,
			Alandsbanken,
			Osuuspankki,
			Aktia,
			None
		};

		public PaymentMethod(string name) {
			name = name.ToLowerInvariant();
			var paymentMethod = AllPaymentMethods
					.FirstOrDefault(pm => pm.Name == name || pm.DisplayName.ToLowerInvariant() == name)
				// ReSharper disable once StringLiteralTypo
				?? AllPaymentMethods.First(pm => pm.Name == "none");
			this.Name = paymentMethod.Name;
			this.DisplayName = paymentMethod.DisplayName;
		}

		private PaymentMethod(string name, string displayName) {
			this.Name = name;
			this.DisplayName = displayName;
		}

		public string DisplayName {
			get;
		}

		public string Name {
			get;
		}

		public static bool operator ==(PaymentMethod a, PaymentMethod b) {
			return a?.Name == b?.Name && a?.DisplayName == b?.DisplayName;
		}

		public static bool operator !=(PaymentMethod a, PaymentMethod b) {
			return !(a == b);
		}

		public override string ToString() {
			return this.DisplayName;
		}
#pragma warning restore 660,661

		// ReSharper disable StringLiteralTypo
		// ReSharper disable IdentifierTypo
		public static PaymentMethod Handelsbank => new("handelsbanken-e-payment", "Handelsbanken");
		public static PaymentMethod DanskeBank => new("sampo-web-payment", "Danske Bank");
		public static PaymentMethod Spankki => new("s-pankki-verkkomaksu", "S-Pankki");
		public static PaymentMethod Nordea => new("nordea-e-payment", "Nordea");
		public static PaymentMethod Pop => new("pop-pankin-verkkomaksu", "Pop");
		public static PaymentMethod Saastopankki => new("saastopankin-verkkomaksu", "Säästopankki");
		public static PaymentMethod Siirto => new("siirto", "Siirto");
		public static PaymentMethod OmaSaastopankki => new("oma-saastopankin-verkkomaksu", "Oma Säästopankki");
		public static PaymentMethod Alandsbanken => new("alandsbanken-e-payment", "Ålandsbanken");
		public static PaymentMethod Osuuspankki => new("op-pohjola-verkkomaksu", "Osuuspankki");

		public static PaymentMethod Aktia => new("aktia-maksu", "Aktia");

		public static PaymentMethod None => new("none", "None (Do not extend reservation)");
		// ReSharper restore StringLiteralTypo
		// ReSharper restore IdentifierTypo
	}
}