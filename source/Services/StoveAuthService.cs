using Playnite.SDK;
using StoveLibrary.Models;
using System;
using System.Linq;
using System.Threading;

namespace StoveLibrary.Services
{
    public class StoveAuthService
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI api;
        private readonly StoveLibrarySettings settings;

        public StoveAuthService(IPlayniteAPI playniteApi, StoveLibrarySettings pluginSettings)
        {
            api = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            settings = pluginSettings ?? throw new ArgumentNullException(nameof(pluginSettings));
        }

        public bool GetIsUserLoggedIn()
        {
            try
            {
                var sessionData = GetSessionData();
                return sessionData?.Value?.Member?.MemberNo != null && !string.IsNullOrEmpty(sessionData.Value.AccessToken);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error checking login status");
                return false;
            }
        }

        public SessionResponse GetSessionData()
        {
            IWebView webView = null;
            try
            {
                webView = api.WebViews.CreateOffscreenView();
                if (webView == null)
                {
                    logger.Error("Failed to create WebView for session data");
                    return null;
                }

                var sessionData = TryGetSessionFromMainPage(webView);
                if (sessionData != null)
                {
                    return sessionData;
                }

                sessionData = TryGetSessionFromAccountPage(webView);
                if (sessionData != null)
                {
                    return sessionData;
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting session data");
                return null;
            }
            finally
            {
                try { webView?.Dispose(); }
                catch (Exception ex) { logger.Error(ex, "Error disposing WebView"); }
            }
        }

        private SessionResponse TryGetSessionFromMainPage(IWebView webView)
        {
            try
            {
                webView.Navigate("https://www.onstove.com/");
                Thread.Sleep(2000);

                return ExtractSessionFromCookies(webView);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Failed to get session from main page");
                return null;
            }
        }

        private SessionResponse TryGetSessionFromAccountPage(IWebView webView)
        {
            try
            {
                webView.Navigate("https://accounts.onstove.com/");
                Thread.Sleep(2000);

                var sessionData = ExtractSessionFromCookies(webView);
                if (sessionData != null)
                {
                    return sessionData;
                }

                webView.Navigate("https://www.onstove.com/");
                Thread.Sleep(2000);

                return ExtractSessionFromCookies(webView);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Failed to get session from account page");
                return null;
            }
        }

        private SessionResponse ExtractSessionFromCookies(IWebView webView)
        {
            var cookies = webView.GetCookies();
            var suatCookie = cookies.FirstOrDefault(c => c.Name == "SUAT");

            if (suatCookie?.Value == null)
            {
                return null;
            }

            var memberNo = GetMemberNoFromCookiesWithRetry(webView, cookies);
            if (!memberNo.HasValue)
            {
                return null;
            }

            return new SessionResponse
            {
                Value = new SessionValue
                {
                    AccessToken = suatCookie.Value,
                    Member = new Member
                    {
                        MemberNo = memberNo.Value
                    }
                },
                Message = "OK",
                Result = "000"
            };
        }

        private long? GetMemberNoFromCookiesWithRetry(IWebView webView, System.Collections.Generic.IEnumerable<HttpCookie> cookies)
        {
            var memberNo = GetMemberNoFromCookies(cookies);
            if (memberNo.HasValue)
            {
                return memberNo;
            }

            for (int i = 0; i < 3; i++)
            {
                Thread.Sleep(1000);

                cookies = webView.GetCookies();
                memberNo = GetMemberNoFromCookies(cookies);
                if (memberNo.HasValue)
                {
                    return memberNo;
                }
            }

            try
            {
                webView.Navigate("https://library.onstove.com/");
                Thread.Sleep(2000);

                cookies = webView.GetCookies();
                memberNo = GetMemberNoFromCookies(cookies);
                if (memberNo.HasValue)
                {
                    return memberNo;
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Failed to get member number from library page");
            }

            return null;
        }

        private long? GetMemberNoFromCookies(System.Collections.Generic.IEnumerable<HttpCookie> cookies)
        {
            try
            {
                var sumtCookie = cookies.FirstOrDefault(c => c.Name == "SUMT_INFO");
                if (sumtCookie != null && !string.IsNullOrEmpty(sumtCookie.Value))
                {
                    var decodedOnce = System.Web.HttpUtility.UrlDecode(sumtCookie.Value);
                    var decodedTwice = System.Web.HttpUtility.UrlDecode(decodedOnce);
                    var memberInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(decodedTwice);
                    return (long)memberInfo.member_no;
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error parsing member number from cookies");
            }

            return null;
        }

        public void Login()
        {
            IWebView webView = null;
            try
            {
                webView = api.WebViews.CreateView(520, 700);
                if (webView == null)
                {
                    logger.Error("Failed to create WebView for login");
                    return;
                }

                bool loginCompleted = false;

                webView.LoadingChanged += async (s, e) =>
                {
                    try
                    {
                        var currentUrl = webView.GetCurrentAddress();
                        if (currentUrl.StartsWith("https://www.onstove.com/", StringComparison.OrdinalIgnoreCase))
                        {
                            logger.Info("Login successful");
                            loginCompleted = true;
                            await System.Threading.Tasks.Task.Delay(2000);
                            webView.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error during login navigation");
                    }
                };

                webView.Navigate("https://accounts.onstove.com/login");
                webView.OpenDialog();

                if (!loginCompleted)
                {
                    logger.Warn("Login process was cancelled or failed");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Critical error during login");
            }
            finally
            {
                try { webView?.Dispose(); }
                catch (Exception ex) { logger.Error(ex, "Error disposing WebView"); }
            }
        }

        public void Logout()
        {
            IWebView webView = null;
            try
            {
                webView = api.WebViews.CreateOffscreenView();
                if (webView == null)
                {
                    logger.Error("Failed to create WebView for logout");
                    return;
                }

                webView.Navigate("https://accounts.onstove.com/logout");
                Thread.Sleep(3000);

                logger.Info("Logout completed");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during logout");
            }
            finally
            {
                try { webView?.Dispose(); }
                catch (Exception ex) { logger.Error(ex, "Error disposing WebView"); }
            }
        }
    }
}