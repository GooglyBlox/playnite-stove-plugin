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
                webView.Navigate("https://store.onstove.com/");
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

                webView.Navigate("https://store.onstove.com/");
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

            if (IsTokenExpired(suatCookie.Value))
            {
                logger.Warn("SUAT token has expired");
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

        private bool IsTokenExpired(string jwtToken)
        {
            try
            {
                var parts = jwtToken.Split('.');
                if (parts.Length < 2)
                {
                    return true; // Invalid JWT format, treat as expired
                }

                var payload = parts[1];
                var paddedPayload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                var decodedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(paddedPayload));
                var tokenData = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(decodedJson);

                if (tokenData.exp != null)
                {
                    long expTimestamp = (long)tokenData.exp;
                    var expirationTime = DateTimeOffset.FromUnixTimeSeconds(expTimestamp);
                    var now = DateTimeOffset.UtcNow;

                    if (expirationTime <= now)
                    {
                        logger.Debug($"Token expired at {expirationTime}, current time is {now}");
                        return true;
                    }

                    // Also log if token is about to expire soon (within 5 minutes)
                    if (expirationTime <= now.AddMinutes(5))
                    {
                        logger.Warn($"Token expires soon at {expirationTime}");
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error checking token expiry, assuming valid");
                return false; // If we can't parse, don't block - let the API call determine validity
            }
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
                webView.Navigate("https://store.onstove.com/");
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
                logger.Debug(ex, "Failed to get member number from store page");
            }

            return null;
        }

        private long? GetMemberNoFromCookies(System.Collections.Generic.IEnumerable<HttpCookie> cookies)
        {
            try
            {
                var pldCookie = cookies.FirstOrDefault(c => c.Name == "PLD");
                if (pldCookie != null && !string.IsNullOrEmpty(pldCookie.Value))
                {
                    var decodedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pldCookie.Value));
                    var memberInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(decodedJson);
                    return (long)memberInfo.member_no;
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error parsing member number from PLD cookie");
            }

            try
            {
                var suatCookie = cookies.FirstOrDefault(c => c.Name == "SUAT");
                if (suatCookie != null && !string.IsNullOrEmpty(suatCookie.Value))
                {
                    var parts = suatCookie.Value.Split('.');
                    if (parts.Length >= 2)
                    {
                        var payload = parts[1];
                        var paddedPayload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                        var decodedJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(paddedPayload));
                        var memberInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(decodedJson);
                        return (long)memberInfo.member_no;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error parsing member number from SUAT JWT");
            }

            // Legacy fallback: try SUMT_INFO cookie (old format)
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
                logger.Debug(ex, "Error parsing member number from SUMT_INFO cookie");
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
                        if (currentUrl.StartsWith("https://www.onstove.com/", StringComparison.OrdinalIgnoreCase) ||
                            currentUrl.StartsWith("https://store.onstove.com/", StringComparison.OrdinalIgnoreCase))
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

                DeleteStoveCookies(webView);

                webView.Navigate("https://accounts.onstove.com/logout");
                Thread.Sleep(3000);

                DeleteStoveCookies(webView);

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

        private void DeleteStoveCookies(IWebView webView)
        {
            try
            {
                // Delete auth cookies from all STOVE domains
                var domains = new[]
                {
                    "https://onstove.com",
                    "https://www.onstove.com",
                    "https://store.onstove.com",
                    "https://accounts.onstove.com",
                    "https://api.onstove.com"
                };

                var cookieNames = new[] { "SUAT", "PLD", "SUMT_INFO" };

                foreach (var domain in domains)
                {
                    foreach (var cookieName in cookieNames)
                    {
                        try
                        {
                            webView.DeleteCookies(domain, cookieName);
                        }
                        catch (Exception ex)
                        {
                            logger.Debug(ex, $"Failed to delete cookie {cookieName} from {domain}");
                        }
                    }
                }

                logger.Debug("Deleted STOVE auth cookies");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error deleting STOVE cookies");
            }
        }
    }
}