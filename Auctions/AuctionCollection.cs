namespace VanBot.Auctions {
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Linq;

	using VanBot.Helpers;

	public class AuctionCollection: IEnumerable<Auction> {
		private readonly Dictionary<int, Auction> auctions;

		public AuctionCollection() {
			this.auctions = new Dictionary<int, Auction>();
		}

		public Auction this[int key] {
			get => this.GetAuction(key);

			set => this.SetAuction(key, value);
		}

		public static AuctionCollection FromEnumerable(IEnumerable<Auction> auctions) {
			var result = new AuctionCollection();
			foreach(var auction in auctions) {
				result[auction.Id] = auction;
			}
			return result;
		}

		public static (AuctionCollection added, AuctionCollection removed) GetAddedAndRemoved(AuctionCollection previous, AuctionCollection current) {
			var (addedKeys, removedKeys) = Utilities.GetAddedAndRemoved(previous.GetKeys(), current.GetKeys());

			var added = new AuctionCollection();
			foreach(var key in addedKeys) {
				added[key] = current[key];
			}

			var removed = new AuctionCollection();
			foreach(var key in removedKeys) {
				removed[key] = previous[key];
			}

			return (added, removed);
		}

		public IEnumerator<Auction> GetEnumerator() {
			foreach(var (_, value) in this.auctions) {
				yield return value;
			}
		}

		public int[] GetKeys() => this.auctions.Keys.ToArray();

		internal void AddMockAuctions(int numberOfAuctions) {
			var random = new Random(123);
			for(var i = 0; i < numberOfAuctions; i++) {
				var auction = this.auctions.ElementAt(random.Next(0, this.auctions.Count)).Value;
				auction.ChangeIdToMockId();
				this.auctions[auction.Id] = auction;
			}
		}

		private Auction GetAuction(int key) {
			return this.auctions[key];
		}

		IEnumerator IEnumerable.GetEnumerator() {
			return this.GetEnumerator();
		}

		private void SetAuction(int key, Auction value) {
			this.auctions[key] = value;
		}
	}
}