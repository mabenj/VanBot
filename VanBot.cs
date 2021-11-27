#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using RandomUserAgent;

#endregion

namespace VanBot {
    public class VanBot {
        private const string MainUrl = "https://www.vaurioajoneuvo.fi/";
        private const string LoginUrl = "https://www.vaurioajoneuvo.fi/kayttajalle/kirjaudu-sisaan/";
        private const int MaxCaptchaAttempts = 5;

        // ReSharper disable once IdentifierTypo
        private static readonly HashSet<int> BrowserPids = new();

        private readonly TimeSpan interval;
        private readonly bool isHeadless;
        private readonly string password;
        private readonly TelegramBot telegramBot;
        private readonly int testIteration = Utilities.IsDebug() ? 1 : -1;
        private readonly string username;
        private Dictionary<string, Auction> allAuctions;
        private bool canLogIn;
        private IWebDriver driver;
        private int iterations;
        private int iterationsSinceNew;

        public VanBot(Options options) {
            this.username = options.Username;
            this.password = options.Password;
            this.interval = TimeSpan.FromSeconds(options.Interval);
            this.isHeadless = !options.ShowHead;
            this.telegramBot = new TelegramBot(options.TelegramKey);
            this.canLogIn = false;
            this.allAuctions = new Dictionary<string, Auction>();
            this.iterations = 0;
            this.iterationsSinceNew = 0;
            this.driver = null;
        }

        public void Start(CancellationToken token) {
            // ReSharper disable once StringLiteralTypo
            Log.Info("Initializing chromedriver");
            this.InitializeChromeDriver();
            this.OpenPage(MainUrl);

            Log.Info("Testing credentials");
            if (!this.CheckCredentials()) {
                Stop();
                return;
            }

            Log.Info("Testing Telegram bot chat key");
            if (!this.CheckTelegramBot()) {
                Stop();
                return;
            }

            Log.Info("Fetching initial auctions");
            this.allAuctions = this.FetchAllAuctions(true);

            Log.Info("Waiting for new auctions...");
            while (!token.IsCancellationRequested) {
                var loopStart = DateTime.Now;
                this.iterations++;
                this.iterationsSinceNew++;
                try {
                    if (this.driver.Url != MainUrl) {
                        Log.Warning("Not on main url");
                        Log.Warning("Navigating back to main url");
                        this.OpenPage(MainUrl);
                    }

                    this.driver.Navigate().Refresh();

                    var pastAuctions = this.allAuctions;
                    var currentAuctions = this.FetchAllAuctions();

                    var pastIds = pastAuctions.Keys;
                    var currentIds = currentAuctions.Keys;
                    var intersection = pastIds.Intersect(currentIds).ToList();

                    var addedIds = currentIds.Except(intersection).ToList();
                    var removedIds = pastIds.Except(intersection);

                    if (this.iterations == testIteration && !addedIds.Any()) {
                        addedIds = new List<string>() { "37284" };
                    }

                    if (addedIds.Any()) {
                        var isLoggedIn = false;
                        var somethingReserved = false;
                        this.iterationsSinceNew = 0;

                        foreach (var newId in addedIds) {
                            var auction = currentAuctions[newId];
                            Log.Info("New auction found!");
                            Log.Info(auction.ToString());
                            var reservationSuccess = false;
                            if (this.canLogIn && !auction.IsForScrapyards && !somethingReserved) {
                                if (!isLoggedIn) {
                                    isLoggedIn = this.LogIn();
                                }

                                reservationSuccess = somethingReserved = this.ReserveAuction(currentAuctions[newId]);
                            }

                            this.NotifyNewAuction(currentAuctions[newId], reservationSuccess);
                        }

                        if (isLoggedIn) {
                            this.LogOut();
                            Log.Info("Resetting driver");
                            this.InitializeChromeDriver();
                            this.OpenPage(MainUrl);
                        }
                    } else {
                        Log.Info($"No new auctions found... [all: {$"{this.iterations}",5}] [since_new: {$"{this.iterationsSinceNew}]",5}");
                    }

                    foreach (var oldId in removedIds) {
                        Log.Info($"Auction '{oldId}' expired");
                    }

                    this.allAuctions = currentAuctions;

                    var elapsed = DateTime.Now - loopStart;
                    var sleepTime = this.interval - elapsed;
                    if (sleepTime.Milliseconds > 0) {
                        token.WaitHandle.WaitOne(Convert.ToInt32(sleepTime.TotalMilliseconds));
                    }
                } catch (Exception e) {
                    Log.Error($"Error: {e.Message}");
                    this.OpenPage(MainUrl);
                }
            }

            Stop();
        }

        private bool ReserveAuction(Auction auction) {
            if (auction.IsForScrapyards) {
                Log.Info($"Skipping auction '{auction.Id}' because it is for scrapyards only");
                return false;
            }

            Log.Info($"Reserving auction '{auction.Id}'");
            var buyBtnBy = By.CssSelector("button.button.button-buy");
            try {
                this.OpenPage(auction.Url);
                var buyBtn = this.WaitForElementToBeClickable(buyBtnBy);
                buyBtn.Click();
                var proceedDiv = this.WaitForElementToBeClickable(By.CssSelector("div.proceed-to-payment"));
                return proceedDiv != null;
            } catch (Exception e) {
                Log.Error($"Error while reserving auction '{auction.Id}': {e.Message}");
                return false;
            }
        }

        private async void NotifyNewAuction(Auction auction, bool isReserved) {
            // ReSharper disable StringLiteralTypo
            var prefix = isReserved ? $"Uus vehje ois <b>varattu</b> käyttäjälle '<i>{this.username}</i>':" : "Uus vehje ois tarjolla:";
            var priceTag = auction.IsForScrapyards ? "Vain purkamoille" : $"{auction.Price}€";
            var message = $"{prefix} {auction.Url} \r\nHinta: <b>{priceTag}</b>";
            // ReSharper restore StringLiteralTypo
            await this.telegramBot.SendMessage(message);
        }

        private Dictionary<string, Auction> FetchAllAuctions(bool shouldLog = false) {
            try {
                var auctions = new Dictionary<string, Auction>();

                this.OpenPage(MainUrl);
                var searchResultsElement = this.WaitForElementToBeClickable(By.Id("cars-search-results"));
                var auctionElements = searchResultsElement.FindElements(By.CssSelector("div[data-auction-id]"));
                foreach (var auctionElement in auctionElements) {
                    var id = auctionElement.GetAttribute("data-auction-id");
                    var url = auctionElement.FindElement(By.XPath("./..")).GetAttribute("href");
                    // ReSharper disable once StringLiteralTypo
                    var isForScrapyards = auctionElement.FindElement(By.CssSelector(".item-lift-price-now-title")).Text.ToLower() == "vain purkamoille";
                    var price = -1.0;
                    if (!isForScrapyards) {
                        var priceString = auctionElement.FindElement(By.CssSelector("strong.item-lift-price-now")).Text;
                        price = Convert.ToDouble(priceString.Replace(" ", "").Replace("€", ""));
                    }

                    auctions[id] = new Auction() {
                        Id = id,
                        IsForScrapyards = isForScrapyards,
                        Price = price,
                        Url = url
                    };

                    if (shouldLog) {
                        Log.Info(auctions[id].ToString());
                    }
                }

                return auctions;
            } catch (Exception e) {
                Log.Error($"Error while fetching all auctions: {e.Message}");
                throw;
            }
        }

        private bool CheckCredentials() {
            this.canLogIn = this.LogIn();
            if (!this.canLogIn) {
                Log.Warning("Continue anyway? (y/n)");
                var answer = Console.ReadLine()?.ToLower();
                return new[] { "y", "yes", "true" }.Contains(answer);
            } else {
                this.LogOut();
                return true;
            }
        }

        private bool CheckTelegramBot() {
            var isChatKeyOk = Task.Run(() => this.telegramBot.TestChatKey()).Result;
            if (isChatKeyOk) {
                return true;
            }

            Log.Warning("Continue anyway? (y/n)");
            var answer = Console.ReadLine()?.ToLower();
            return new[] { "y", "yes", "true" }.Contains(answer);
        }

        private bool LogIn() {
            Log.Info($"Logging in as {this.username}");
            try {
                this.OpenPage(LoginUrl);

                var usernameInput = this.WaitForElementToBeClickable(By.Id("username"));
                usernameInput.SendKeys(this.username);
                var passwordInput = this.WaitForElementToBeClickable(By.Id("password"));
                passwordInput.SendKeys(this.password);
                passwordInput.SendKeys(Keys.Return);

                // ReSharper disable StringLiteralTypo
                if (this.PageHasElement(By.LinkText("Omat tiedot"))) {
                    // ReSharper restore StringLiteralTypo
                    Log.Info("Successfully logged in");
                    return true;
                } else {
                    Log.Warning("Could not log in");
                    return false;
                }
            } catch (Exception e) {
                Log.Error($"Error while logging in as '{this.username}': {e.Message}");
                throw;
            }
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private bool LogOut() {
            Log.Info($"Logging out as {this.username}");
            try {
                this.OpenPage(LoginUrl);
                var logOutBtn = this.WaitForElementToBeClickable(By.CssSelector("input.logout"));
                logOutBtn.Click();

                if (this.PageHasElement(By.Id("username"))) {
                    Log.Info("Successfully logged out");
                    return true;
                }

                Log.Warning("Could not log out");
                return false;
            } catch (Exception e) {
                Log.Error($"Error while logging out as '{this.username}': {e.Message}");
                throw;
            }
        }

        private bool PageHasElement(By by) {
            try {
                this.driver.FindElement(by);
                return true;
            } catch (NoSuchElementException) {
                return false;
            }
        }

        // ReSharper disable once IdentifierTypo
        private IWebElement WaitForElementToBeClickable(By by, int timeout = 10) {
            return this.driver.FindElement(by /*, timeout*/);
        }


        private void OpenPage(string url) {
            this.driver.Url = url;
            var captchaAttempts = 0;
            while (this.IsCaptchaPage() && captchaAttempts < MaxCaptchaAttempts) {
                Log.Warning("Encountered captcha");
                Log.Warning("Resetting driver");
                this.InitializeChromeDriver();
                captchaAttempts++;
                this.driver.Url = url;
            }

            if (captchaAttempts == MaxCaptchaAttempts) {
                this.NotifyCaptcha();
                Log.Error("Could not bypass captcha");
                Stop();
            } else if (captchaAttempts != 0) {
                // ReSharper disable once TailRecursiveCall
                this.OpenPage(url);
            }
        }

        private async void NotifyCaptcha() {
            // ReSharper disable StringLiteralTypo
            await this.telegramBot.SendMessage("Ne luulee et oon botti. Käy tekemäs captcha ja käynnistä uudelleen.");
            // ReSharper restore StringLiteralTypo
        }

        private bool IsCaptchaPage() {
            return this.driver.Title.ToLower().StartsWith("captcha");
        }

        private void InitializeChromeDriver() {
            KillBrowsers();

            var options = new ChromeOptions();
            options.AddArguments(
                $"user-agent={RandomUa.RandomUserAgent}",
                "--window-size=1920,1080",
                "--disable-extensions",
                "--disable-gpu",
                "--disable-logging",
                "--log-level=3"
            );
            options.AddExcludedArguments("enable-logging", "enable-automation");
            options.AddAdditionalChromeOption("useAutomationExtension", false);
            if (this.isHeadless) {
                options.AddArgument("headless");
            }

            var service = ChromeDriverService.CreateDefaultService(Utilities.ExtractChromeDriverResource());
            service.SuppressInitialDiagnosticInformation = true;

            this.driver = new ChromeDriver(service, options);
            this.driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);

            BrowserPids.Add(service.ProcessId);
            var mos = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={service.ProcessId}");
            foreach (var mo in mos.Get()) {
                BrowserPids.Add(Convert.ToInt32(mo["ProcessID"]));
            }
        }

        internal static void Stop() {
            Log.Info("Stopping...");
            try {
                Log.Info("Killing browsers");
                KillBrowsers();
            } catch (Exception e) {
                // ReSharper disable once StringLiteralTypo
                Log.Error($"Could not stop chromedriver: {e.Message}");
                return;
            }

            // ReSharper disable once StringLiteralTypo
            Log.Info("Chromedriver stopped successfully");
        }

        private static void KillBrowsers() {
            foreach (var pid in BrowserPids.ToList()) {
                System.Diagnostics.Process.GetProcessById(pid).Kill();
                BrowserPids.Remove(pid);
            }
        }
    }

    public record Auction {
        public string Id {
            get;
            init;
        }

        public string Url {
            get;
            init;
        }

        public double Price {
            get;
            init;
        }

        public bool IsForScrapyards {
            get;
            init;
        }

        public override string ToString() {
            return $"{$"[auction id: {this.Id}]",-20} {$"[price: {this.Price}]",-15} {$"[for scrapyards: {this.IsForScrapyards}]",-25} {$"[url: {this.Url}]",-50}";
        }
    }
}