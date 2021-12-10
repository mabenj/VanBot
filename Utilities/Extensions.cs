#region

#endregion

namespace VanBot.Utilities {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using System.Threading.Tasks.Dataflow;

	using OpenQA.Selenium;
	using OpenQA.Selenium.Support.UI;

	public static class Extensions {
		public static void AddRange(this HashSet<int> hashSet, params int[] values) {
			foreach(var value in values.Distinct()) {
				hashSet.Add(value);
			}
		}

		public static Uri Append(this Uri uri, params string[] paths) {
			return new Uri(paths.Aggregate(uri.AbsoluteUri, (current, path) => $"{current.TrimEnd('/')}/{path.TrimStart('/')}"));
		}

		public static IWebElement FindElement(this IWebDriver driver, By by, int timeoutInSeconds) {
			if(timeoutInSeconds <= 0) {
				return driver.FindElement(by);
			}
			var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutInSeconds));
			return wait.Until(drv => drv.FindElement(@by));
		}

		public static Task ParallelForEachAsync<T>(this IEnumerable<T> source, Func<T, Task> body, int maxDop = DataflowBlockOptions.Unbounded, TaskScheduler scheduler = null) {
			var options = new ExecutionDataflowBlockOptions {
				MaxDegreeOfParallelism = maxDop
			};
			if(scheduler != null) {
				options.TaskScheduler = scheduler;
			}

			var block = new ActionBlock<T>(body, options);

			foreach(var item in source) {
				block.Post(item);
			}

			block.Complete();
			return block.Completion;
		}
	}
}