#region

#endregion

namespace VanBot.Helpers {
	using System;
	using System.Linq;

	public static class Extensions {
		public static Uri Append(this Uri uri, params string[] paths) {
			return new(paths.Aggregate(uri.AbsoluteUri, (current, path) => $"{current.TrimEnd('/')}/{path.TrimStart('/')}"));
		}
	}
}