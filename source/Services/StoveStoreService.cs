using Playnite.SDK;
using StoveLibrary.Models;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using AngleSharp.Dom;
using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;

namespace StoveLibrary.Services
{
    public class StoveStoreService
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI api;
        private readonly StoveLibrarySettings settings;
        private readonly StoveHttpService httpService;

        public StoveStoreService(IPlayniteAPI playniteApi, StoveLibrarySettings pluginSettings, StoveHttpService httpService)
        {
            api = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            settings = pluginSettings ?? throw new ArgumentNullException(nameof(pluginSettings));
            this.httpService = httpService ?? throw new ArgumentNullException(nameof(httpService));
        }

        public StoreGameDetails GetGameStoreDetails(int productNo)
        {
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var storeUrl = $"https://api.onstove.com/store/v1.0/components/groups/product-merge?component_ids=645adb9a10e0716de3792b41&product_no={productNo}&preview=&timestemp={timestamp}";

                var response = httpService.GetAsync(storeUrl).Result;

                if (response.IsSuccessStatusCode)
                {
                    var content = response.Content.ReadAsStringAsync().Result;
                    var storeData = JsonConvert.DeserializeObject<StoreResponse>(content);
                    return storeData?.Value?.Components?.FirstOrDefault()?.Props;
                }
                else
                {
                    var errorContent = response.Content.ReadAsStringAsync().Result;
                    logger.Warn($"Store API request failed: {response.StatusCode} - {errorContent}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error getting store details for product {productNo}");
                return null;
            }
        }

        public string GetGameDescription(string storeUrl)
        {
            if (string.IsNullOrWhiteSpace(storeUrl))
            {
                return null;
            }

            try
            {
                var html = DownloadWithWebView(storeUrl, "features=");
                if (string.IsNullOrWhiteSpace(html) ||
                    html.IndexOf("requested page cannot be found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return null;
                }

                var parser = new HtmlParser();
                var doc = parser.Parse(html);
                if (doc == null)
                {
                    return null;
                }

                return SelectProductDescription(doc);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error getting description for {storeUrl}");
                return null;
            }
        }

        public string GetGameDeveloper(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                return null;
            }

            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var gameDetailsUrl = $"https://api.onstove.com/main-common/v1.0/client/exhibit-games/0?game_id={gameId}&timestemp={timestamp}";

                var response = httpService.GetAsync(gameDetailsUrl).Result;

                if (response.IsSuccessStatusCode)
                {
                    var content = response.Content.ReadAsStringAsync().Result;
                    var gameData = JsonConvert.DeserializeObject<dynamic>(content);

                    if (gameData?.result == "000" && gameData.value?.developer != null)
                    {
                        return gameData.value.developer.ToString();
                    }
                }
                else
                {
                    var errorContent = response.Content.ReadAsStringAsync().Result;
                    logger.Warn($"Game details API request failed: {response.StatusCode} - {errorContent}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error getting developer for game ID: {gameId}");
            }

            return null;
        }

        public string GetGamePublisher(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                return null;
            }

            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var gameMetaUrl = $"https://api.onstove.com/game/v2.2/meta/{gameId}?ts={timestamp}";

                var response = httpService.GetAsync(gameMetaUrl).Result;

                if (response.IsSuccessStatusCode)
                {
                    var content = response.Content.ReadAsStringAsync().Result;
                    var gameData = JsonConvert.DeserializeObject<dynamic>(content);

                    if (gameData?.code == 0 && gameData.value?.distributor != null)
                    {
                        return gameData.value.distributor.ToString();
                    }
                }
                else
                {
                    var errorContent = response.Content.ReadAsStringAsync().Result;
                    logger.Warn($"Game meta API request failed: {response.StatusCode} - {errorContent}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error getting publisher for game ID: {gameId}");
            }

            return null;
        }

        private string SelectProductDescription(IHtmlDocument doc)
        {
            if (doc == null)
                return null;

            try
            {
                var viewElements = doc.QuerySelectorAll("div.inds-editor-view");
                if (viewElements != null)
                {
                    foreach (var view in viewElements)
                    {
                        try
                        {
                            var head = view?.ParentElement?.QuerySelector("h3")?.TextContent?.Trim() ?? "";
                            if (Regex.IsMatch(head, "product\\s+description", RegexOptions.IgnoreCase))
                                return view.InnerHtml?.Trim();
                        }
                        catch (Exception)
                        {
                            // Continue with next view
                        }
                    }

                    string bestHtml = null;
                    int bestLen = 0;

                    foreach (var view in viewElements)
                    {
                        try
                        {
                            var textContent = view?.TextContent ?? "";
                            var len = Regex.Replace(textContent, "\\s+", "").Length;
                            if (len > bestLen)
                            {
                                bestLen = len;
                                bestHtml = view.InnerHtml?.Trim();
                            }
                        }
                        catch (Exception)
                        {
                            // Continue with next view
                        }
                    }
                    return bestHtml;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error selecting description");
            }

            return null;
        }

        private string DownloadWithWebView(string url, string waitFor)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            IWebView vw = null;
            try
            {
                if (api?.WebViews == null)
                {
                    logger.Error("WebViews API not available");
                    return string.Empty;
                }

                vw = api.WebViews.CreateOffscreenView();
                if (vw == null)
                {
                    logger.Error("Failed to create WebView");
                    return string.Empty;
                }

                if (settings != null && settings.AllowAdultGames)
                {
                    try
                    {
                        vw.SetCookies("https://store.onstove.com",
                            new HttpCookie
                            {
                                Name = "ADULT_GAME_AGREE",
                                Value = "Y",
                                Domain = ".onstove.com",
                                Path = "/",
                                Expires = DateTime.UtcNow.AddYears(1)
                            });
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to set adult cookie");
                    }
                }

                vw.Navigate(url);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string src = "";
                bool gate = false;

                while (sw.Elapsed < TimeSpan.FromSeconds(30))
                {
                    try
                    {
                        Thread.Sleep(300);
                        src = vw.GetPageSource() ?? "";

                        if ((settings == null || !settings.AllowAdultGames) &&
                            !gate &&
                            (src.IndexOf("/restrictions/agree", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            gate = true;
                            var m = Regex.Match(url, "/games/(\\d+)");
                            var id = m.Success ? m.Groups[1].Value :
                                     Regex.Match(src, "productNo=(\\d+)").Groups[1].Value;
                            if (!string.IsNullOrEmpty(id))
                            {
                                vw.Navigate("https://store.onstove.com/en/restrictions/agree?productNo=" + id);
                                Thread.Sleep(500);
                                continue;
                            }
                        }

                        if (!string.IsNullOrEmpty(src) &&
                            (waitFor == null ||
                             src.IndexOf(waitFor, StringComparison.OrdinalIgnoreCase) >= 0))
                            break;
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "WebView operation error");
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(src))
                {
                    try
                    {
                        Thread.Sleep(1200);
                        var upd = vw.GetPageSource();
                        if (!string.IsNullOrEmpty(upd)) src = upd;
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Final page source fetch error");
                    }
                }
                return src;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "WebView download failed: " + url);
                return string.Empty;
            }
            finally
            {
                try
                {
                    vw?.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "WebView disposal error");
                }
            }
        }
    }
}