using Playnite.SDK;
using StoveLibrary.Models;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace StoveLibrary.Services
{
    public class StoveAuthService
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI api;
        private readonly StoveLibrarySettings settings;
        private readonly string sessionFilePath;
        private SessionResponse cachedSession;

        public StoveAuthService(IPlayniteAPI playniteApi, StoveLibrarySettings pluginSettings, string dataPath = null)
        {
            api = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            settings = pluginSettings ?? throw new ArgumentNullException(nameof(pluginSettings));

            if (!string.IsNullOrEmpty(dataPath))
            {
                sessionFilePath = Path.Combine(dataPath, "session.json");
                LoadCachedSession();
            }
        }

        private void LoadCachedSession()
        {
            try
            {
                if (File.Exists(sessionFilePath))
                {
                    var json = File.ReadAllText(sessionFilePath);
                    cachedSession = Newtonsoft.Json.JsonConvert.DeserializeObject<SessionResponse>(json);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to load cached session from disk");
                cachedSession = null;
            }
        }

        private void SaveCachedSession(SessionResponse session)
        {
            cachedSession = session;
            if (string.IsNullOrEmpty(sessionFilePath)) return;

            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(session);
                File.WriteAllText(sessionFilePath, json);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to save session to disk");
            }
        }

        private void ClearCachedSession()
        {
            cachedSession = null;
            if (string.IsNullOrEmpty(sessionFilePath)) return;

            try
            {
                if (File.Exists(sessionFilePath))
                    File.Delete(sessionFilePath);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to delete session file");
            }
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
            if (cachedSession?.Value != null && !string.IsNullOrEmpty(cachedSession.Value.RefreshToken))
            {
                if (!string.IsNullOrEmpty(cachedSession.Value.AccessToken) && !cachedSession.Value.IsExpiringSoon)
                {
                    return cachedSession;
                }
                var renewed = TryRenewSession(cachedSession.Value.AccessToken, cachedSession.Value.RefreshToken);
                if (renewed != null)
                {
                    SaveCachedSession(renewed);
                    return renewed;
                }

                logger.Warn("Token renewal failed, falling back to WebView");
            }

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
                    SaveCachedSession(sessionData);
                    return sessionData;
                }

                sessionData = TryGetSessionFromAccountPage(webView);
                if (sessionData != null)
                {
                    SaveCachedSession(sessionData);
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

        private SessionResponse TryRenewSession(string accessToken, string refreshToken)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
                    httpClient.DefaultRequestHeaders.Add("caller-id", "indie-web-store");
                    httpClient.DefaultRequestHeaders.Add("X-Lang", "en");
                    httpClient.DefaultRequestHeaders.Add("X-Nation", "US");
                    httpClient.DefaultRequestHeaders.Add("X-Device-Type", "pc");
                    httpClient.DefaultRequestHeaders.Add("X-Timezone", "America/Los_Angeles");
                    httpClient.DefaultRequestHeaders.Add("X-Utc-Offset", "-420");
                    httpClient.DefaultRequestHeaders.Add("Origin", "https://store.onstove.com");
                    httpClient.DefaultRequestHeaders.Add("Referer", "https://store.onstove.com/");
                    httpClient.DefaultRequestHeaders.Add("User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.7499.194 Safari/537.36");
                    httpClient.DefaultRequestHeaders.Accept.Clear();
                    httpClient.DefaultRequestHeaders.Accept.Add(
                        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                    var bodyStr = $"refresh_token={Uri.EscapeDataString(refreshToken)}" +
                        "&properties=[\"user_id\",\"country_cd\",\"person_verify_yn\",\"parent_verify_yn\"]";
                    var content = new System.Net.Http.ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(bodyStr));
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");

                    var response = httpClient.PostAsync("https://auth.onstove.com/v1.0/common/renew", content).Result;
                    var responseBody = response.Content.ReadAsStringAsync().Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        logger.Warn($"Token renewal failed: {response.StatusCode}");
                        return null;
                    }

                    var renewResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<SessionResponse>(responseBody);

                    if (renewResponse?.Value == null || string.IsNullOrEmpty(renewResponse.Value.AccessToken))
                    {
                        logger.Warn($"Token renewal returned empty session, result={renewResponse?.Result}");
                        return null;
                    }

                    if (renewResponse.Value.ExpireIn > 0 && renewResponse.Value.ExpireTime == 0)
                    {
                        renewResponse.Value.ExpireTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                            (renewResponse.Value.ExpireIn * 1000L);
                    }

                    logger.Info($"Token renewed, member_no={renewResponse.Value.Member?.MemberNo}");
                    return renewResponse;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error renewing token");
                return null;
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
            catch (Exception)
            {
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
            catch (Exception)
            {
                return null;
            }
        }

        private SessionResponse ExtractSessionFromCookies(IWebView webView)
        {
            var cookies = webView.GetCookies();
            var suatCookie = cookies.FirstOrDefault(c => c.Name == "SUAT");
            var refreshToken = cookies.FirstOrDefault(c => c.Name == "RFT")?.Value
                ?? cookies.FirstOrDefault(c => c.Name == "SURT")?.Value;

            if (suatCookie?.Value == null)
            {
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    var oldSuat = TryReconstructSuat(cookies);
                    if (!string.IsNullOrEmpty(oldSuat))
                    {
                        return TryRenewSession(oldSuat, refreshToken);
                    }
                }

                return null;
            }

            long expireTime = GetExpireTimeFromPld(cookies);

            if (expireTime > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= expireTime)
            {
                if (!string.IsNullOrEmpty(refreshToken))
                    return TryRenewSession(suatCookie.Value, refreshToken);

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
                    RefreshToken = refreshToken,
                    ExpireTime = expireTime,
                    Member = new Member
                    {
                        MemberNo = memberNo.Value
                    }
                },
                Message = "OK",
                Result = "000"
            };
        }

        private long GetExpireTimeFromPld(System.Collections.Generic.IEnumerable<HttpCookie> cookies)
        {
            try
            {
                var pldCookie = cookies.FirstOrDefault(c => c.Name == "PLD");
                if (pldCookie != null && !string.IsNullOrEmpty(pldCookie.Value))
                {
                    var pldJson = System.Text.Encoding.UTF8.GetString(
                        Convert.FromBase64String(PadBase64(pldCookie.Value)));
                    var pldData = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(pldJson);
                    return (long)pldData.expire_time;
                }
            }
            catch (Exception)
            {
            }

            return 0;
        }

        private string TryReconstructSuat(System.Collections.Generic.IEnumerable<HttpCookie> cookies)
        {
            try
            {
                var hd = cookies.FirstOrDefault(c => c.Name == "HD")?.Value;
                var pld = cookies.FirstOrDefault(c => c.Name == "PLD")?.Value;
                var sign = cookies.FirstOrDefault(c => c.Name == "SIGN")?.Value;

                if (!string.IsNullOrEmpty(hd) && !string.IsNullOrEmpty(pld) && !string.IsNullOrEmpty(sign))
                {
                    return $"{hd}.{pld}.{sign}";
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static string PadBase64(string base64)
        {
            var s = base64.Replace('-', '+').Replace('_', '/');
            var padding = (4 - s.Length % 4) % 4;
            return s + new string('=', padding);
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
            catch (Exception)
            {
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
                    var decodedJson = System.Text.Encoding.UTF8.GetString(
                        Convert.FromBase64String(PadBase64(pldCookie.Value)));
                    var memberInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(decodedJson);
                    return (long)memberInfo.member_no;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                var suatCookie = cookies.FirstOrDefault(c => c.Name == "SUAT");
                if (suatCookie != null && !string.IsNullOrEmpty(suatCookie.Value))
                {
                    var parts = suatCookie.Value.Split('.');
                    if (parts.Length >= 2)
                    {
                        var decodedJson = System.Text.Encoding.UTF8.GetString(
                            Convert.FromBase64String(PadBase64(parts[1])));
                        var memberInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(decodedJson);
                        return (long)memberInfo.member_no;
                    }
                }
            }
            catch (Exception)
            {
            }

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
            catch (Exception)
            {
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
            ClearCachedSession();

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
                var domains = new[]
                {
                    "https://onstove.com",
                    "https://www.onstove.com",
                    "https://store.onstove.com",
                    "https://accounts.onstove.com",
                    "https://api.onstove.com"
                };

                var cookieNames = new[] { "SUAT", "PLD", "HD", "SIGN", "RFT", "SURT", "SUAT_EXPIRED_CHECK", "SUMT_INFO" };

                foreach (var domain in domains)
                {
                    foreach (var cookieName in cookieNames)
                    {
                        try
                        {
                            webView.DeleteCookies(domain, cookieName);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error deleting STOVE cookies");
            }
        }
    }
}
