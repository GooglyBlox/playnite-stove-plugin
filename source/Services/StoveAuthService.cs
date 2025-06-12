using Playnite.SDK;
using StoveLibrary.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace StoveLibrary.Services
{
    public class StoveAuthService
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI api;

        public StoveAuthService(IPlayniteAPI playniteApi)
        {
            api = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
        }

        public bool GetIsUserLoggedIn()
        {
            try
            {
                var sessionData = GetSessionData();
                return sessionData?.Value?.Member?.MemberNo != null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error checking login status");
                return false;
            }
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

                            await Task.Delay(2000);
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
                try
                {
                    webView?.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error disposing WebView");
                }
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

                try
                {
                    var cookiesToDelete = new[] { "SUAT", "PLD", "SUMT_INFO", "SIGN", "RFT", "SURT" };
                    foreach (var cookieName in cookiesToDelete)
                    {
                        webView.DeleteCookies(cookieName, ".onstove.com");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error deleting specific cookies");
                }

                logger.Info("Logout completed");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during logout");
            }
            finally
            {
                try
                {
                    webView?.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error disposing WebView");
                }
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

                webView.Navigate("https://www.onstove.com/");
                Thread.Sleep(2000);

                var cookies = webView.GetCookies();

                var sumtCookie = cookies.FirstOrDefault(c => c.Name == "SUMT_INFO");
                if (sumtCookie != null && !string.IsNullOrEmpty(sumtCookie.Value))
                {
                    try
                    {
                        var decodedOnce = System.Web.HttpUtility.UrlDecode(sumtCookie.Value);
                        var decodedTwice = System.Web.HttpUtility.UrlDecode(decodedOnce);

                        var memberInfo = JsonConvert.DeserializeObject<dynamic>(decodedTwice);
                        var memberNo = (long)memberInfo.member_no;

                        var suatCookie = cookies.FirstOrDefault(c => c.Name == "SUAT");
                        if (suatCookie != null && !string.IsNullOrEmpty(suatCookie.Value))
                        {
                            return new SessionResponse
                            {
                                Value = new SessionValue
                                {
                                    AccessToken = suatCookie.Value,
                                    Member = new Member
                                    {
                                        MemberNo = memberNo,
                                        UserId = memberInfo.user_id?.ToString(),
                                        Nickname = memberInfo.nickname?.ToString(),
                                        ProfileImg = memberInfo.profile_img?.ToString()
                                    }
                                },
                                Message = "OK",
                                Result = "000"
                            };
                        }
                        else
                        {
                            logger.Error("SUAT cookie not found");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error parsing SUMT_INFO cookie");
                    }
                }
                else
                {
                    logger.Error("SUMT_INFO cookie not found");
                }

                logger.Error("Could not extract session data from cookies");
                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting session data");
                return null;
            }
            finally
            {
                try
                {
                    webView?.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error disposing WebView");
                }
            }
        }
    }
}