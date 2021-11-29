#region

using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

#endregion

namespace VanBot.Utilities {
    public static class Extensions {
        public static IWebElement FindElement(this IWebDriver driver, By by, int timeoutInSeconds) {
            if (timeoutInSeconds > 0) {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutInSeconds));
                return wait.Until(drv => drv.FindElement(by));
            }

            return driver.FindElement(by);
        }
    }
}