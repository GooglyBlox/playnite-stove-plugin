using AngleSharp.Dom;
using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using HttpCookie = Playnite.SDK.HttpCookie;

namespace StoveLibrary.Services
{
    public class StoveApi
    {
        private const int ExtraSettlingDelayMs = 1200;

        /* ───── icon filename filters ───── */
        private static readonly Regex PositiveIconRegex = new Regex(
            @"(타이틀섬네일|title[_\-]?thumbnail|아이콘|280[_\-]280)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NegativeBannerRegex = new Regex(
            @"(배경|background|bg)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SquareWidthRegex = new Regex(
            @"\bw-\[(?:7|8|9|10|11)(?:\.\d+)?rem\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI api;
        private readonly StoveLibrarySettings settings;

        public StoveApi(IPlayniteAPI playniteApi, StoveLibrarySettings pluginSettings)
        {
            api = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            settings = pluginSettings ?? throw new ArgumentNullException(nameof(pluginSettings));
        }

        /*──────────────────── PUBLIC FACADE ────────────────────*/

        public List<GameMetadata> GetOwnedGames(string rawProfileUrl)
        {
            var owned = new List<GameMetadata>();

            if (string.IsNullOrWhiteSpace(rawProfileUrl))
            {
                logger.Warn("[STOVE] Blank profile URL.");
                return owned;
            }

            try
            {
                var gamesUrl = BuildGamesUrl(rawProfileUrl);
                if (string.IsNullOrEmpty(gamesUrl))
                {
                    logger.Error("[STOVE] Failed to build valid games URL");
                    return owned;
                }

                var html = DownloadWithWebView(gamesUrl, "/en/games/");
                if (string.IsNullOrEmpty(html))
                    return owned;

                var entries = ExtractGameEntries(html);
                logger.Info($"[STOVE] Found {entries.Count} games on profile page.");

                foreach (var kv in entries)
                {
                    try
                    {
                        var id = kv.Key;
                        var thumb = kv.Value;

                        var meta = GetGameMetadata(id);
                        if (meta == null || string.IsNullOrEmpty(meta.Name))
                            continue;

                        if (!string.IsNullOrEmpty(thumb))
                        {
                            if (thumb.StartsWith("//")) thumb = "https:" + thumb;
                            meta.CoverImage = new MetadataFile(thumb);
                        }
                        owned.Add(meta);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"[STOVE] Failed to process game entry: {kv.Key}");
                        // Continue with other games
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[STOVE] Critical error in GetOwnedGames");
            }

            return owned;
        }

        public GameMetadata GetGameMetadata(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                logger.Warn("[STOVE] Empty game ID provided");
                return null;
            }

            var url = $"https://store.onstove.com/en/games/{gameId}";
            try
            {
                var html = DownloadWithWebView(url, "features=");
                if (string.IsNullOrWhiteSpace(html) ||
                    html.IndexOf("requested page cannot be found",
                                 StringComparison.OrdinalIgnoreCase) >= 0)
                    return null;

                return ParseGamePage(html, url, gameId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[STOVE] metadata fetch failed for {url}");
                return null;
            }
        }

        /*──────────────────── PROFILE HELPERS ──────────────────*/

        private static string BuildGamesUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            try
            {
                if (!Uri.TryCreate(input, UriKind.Absolute, out Uri uri))
                {
                    return null;
                }

                if (uri.AbsolutePath.IndexOf("/game", StringComparison.OrdinalIgnoreCase) >= 0)
                    return uri.ToString();

                return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/game?types=GAME";
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Dictionary<string, string> ExtractGameEntries(string html)
        {
            var dict = new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(html))
                return dict;

            try
            {
                var parser = new HtmlParser();
                var doc = parser.Parse(html);

                if (doc == null)
                    return dict;

                var links = doc.QuerySelectorAll("a[href*='/en/games/']");
                if (links == null)
                    return dict;

                foreach (var a in links)
                {
                    try
                    {
                        var href = a?.GetAttribute("href");
                        if (string.IsNullOrEmpty(href))
                            continue;

                        var m = Regex.Match(href, "/en/games/(\\d+)");
                        if (!m.Success) continue;

                        var id = m.Groups[1].Value;
                        if (string.IsNullOrEmpty(id))
                            continue;

                        /* skip DLC cards */
                        IElement node = a.ParentElement;
                        while (node != null && !node.HasAttribute("productdetailtype"))
                            node = node.ParentElement;

                        if (node != null &&
                            "DLC".Equals(node.GetAttribute("productdetailtype"),
                                         StringComparison.OrdinalIgnoreCase))
                            continue;

                        var imgElement = a.ParentElement?.QuerySelector("img[src]");
                        var src = imgElement?.GetAttribute("src") ?? "";

                        if (!dict.ContainsKey(id))
                            dict[id] = src;
                    }
                    catch (Exception ex)
                    {
                        LogManager.GetLogger().Debug($"[STOVE] Error processing game link: {ex.Message}");
                        // Continue with other links
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error(ex, "[STOVE] Error extracting game entries");
            }

            return dict;
        }

        /*────────────────── PRODUCT PAGE PARSE ─────────────────*/

        private GameMetadata ParseGamePage(string html, string url, string gameId)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(gameId))
                return null;

            try
            {
                var parser = new HtmlParser();
                var doc = parser.Parse(html);

                if (doc == null)
                    return null;

                var meta = new GameMetadata
                {
                    Source = new MetadataNameProperty("STOVE"),
                    GameId = gameId,
                    Links = new List<Link> { new Link("Store Page", url) },
                    Platforms = new HashSet<MetadataProperty>
                        { new MetadataSpecProperty("pc_windows") }
                };

                /* name */
                var titleElement = doc.QuerySelector("meta[property='og:title']");
                var rawName = titleElement?.GetAttribute("content");

                if (string.IsNullOrEmpty(rawName))
                {
                    var titleTag = doc.QuerySelector("title");
                    rawName = titleTag?.TextContent ?? "";
                }

                if (!string.IsNullOrEmpty(rawName))
                {
                    if (rawName.Contains("|"))
                        rawName = rawName.Split('|')[0];

                    rawName = Regex.Replace(rawName, "\\s*\\|\\s*STOVE STORE\\s*$",
                                            "", RegexOptions.IgnoreCase).Trim();
                }

                meta.Name = string.IsNullOrWhiteSpace(rawName) ? "STOVE Game " + gameId : rawName;

                /* details */
                try
                {
                    foreach (var pair in ReadDlPairs(doc))
                    {
                        var label = pair.label;
                        var value = pair.value;

                        if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(value))
                            continue;

                        switch (label)
                        {
                            case "genre":
                                meta.Genres = ParseMultipleAnchors(value);
                                break;
                            case "creator":
                                meta.Developers = NameSet(value);
                                break;
                            case "publisher":
                                meta.Publishers = NameSet(value);
                                break;
                            case "release":
                                if (DateTime.TryParseExact(value, "yyyy.MM.dd",
                                                           CultureInfo.InvariantCulture,
                                                           DateTimeStyles.None, out DateTime dt))
                                    meta.ReleaseDate = new ReleaseDate(dt);
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug($"[STOVE] Error parsing game details: {ex.Message}");
                }

                /* tags */
                try
                {
                    var tagElements = doc.QuerySelectorAll("a[href*='features=']");
                    if (tagElements != null)
                    {
                        var tags = tagElements
                                      .Select(a => a?.TextContent?.Trim())
                                      .Where(t => !string.IsNullOrEmpty(t) && t.StartsWith("#"))
                                      .Select(t => t.Substring(1).Trim())
                                      .Where(t => !string.IsNullOrEmpty(t))
                                      .Distinct()
                                      .ToList();

                        if (tags.Count > 0)
                            meta.Tags = new HashSet<MetadataProperty>(
                                            tags.Select(t => new MetadataNameProperty(t)));
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug($"[STOVE] Error parsing tags: {ex.Message}");
                }

                /* description */
                try
                {
                    meta.Description = SelectProductDescription(doc);
                }
                catch (Exception ex)
                {
                    logger.Debug($"[STOVE] Error parsing description: {ex.Message}");
                }

                /* icon */
                try
                {
                    var icon = FindIcon(doc);
                    if (!string.IsNullOrEmpty(icon))
                    {
                        if (icon.StartsWith("//")) icon = "https:" + icon;
                        meta.Icon = new MetadataFile(icon);
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug($"[STOVE] Error parsing icon: {ex.Message}");
                }

                return meta;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[STOVE] Critical error parsing game page for {gameId}");
                return null;
            }
        }

        private static HashSet<MetadataProperty> ParseMultipleAnchors(string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(htmlContent))
                return new HashSet<MetadataProperty>();

            try
            {
                var parser = new HtmlParser();
                var doc = parser.Parse("<div>" + htmlContent + "</div>");

                var anchors = doc?.QuerySelectorAll("a");
                if (anchors == null || !anchors.Any())
                    return new HashSet<MetadataProperty>();

                var values = new HashSet<MetadataProperty>();

                foreach (var anchor in anchors)
                {
                    var text = anchor?.TextContent?.Trim();
                    if (string.IsNullOrEmpty(text))
                        continue;

                    // Remove trailing comma if present
                    if (text.EndsWith(","))
                        text = text.Substring(0, text.Length - 1).Trim();

                    if (!string.IsNullOrEmpty(text))
                        values.Add(new MetadataNameProperty(text));
                }

                return values;
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Debug($"[STOVE] Error parsing multiple anchors: {ex.Message}");
                return new HashSet<MetadataProperty>();
            }
        }

        /*─────────────────── ICON SELECTION ───────────────────*/

        private static string FindIcon(IHtmlDocument doc)
        {
            if (doc == null)
                return string.Empty;

            try
            {
                var divElements = doc.QuerySelectorAll("div.absolute.overflow-hidden");
                if (divElements != null)
                {
                    foreach (var div in divElements)
                    {
                        try
                        {
                            var cls = div?.GetAttribute("class") ?? "";

                            if (!Regex.IsMatch(cls, @"h-\[(?:7|8|9|10|11)(?:\.\d+)?rem\]",
                                            RegexOptions.IgnoreCase)) continue;
                            if (!Regex.IsMatch(cls, @"w-\[(?:7|8|9|10|11)(?:\.\d+)?rem\]",
                                            RegexOptions.IgnoreCase)) continue;

                            var img = div?.QuerySelector("img[src]");
                            if (img == null) continue;

                            var src = img.GetAttribute("src") ?? "";
                            if (string.IsNullOrEmpty(src)) continue;

                            if (NegativeBannerRegex.IsMatch(src)) continue;

                            return src;
                        }
                        catch (Exception)
                        {
                            // Continue with next div
                        }
                    }
                }

                var imgElements = doc.QuerySelectorAll("img[src]");
                if (imgElements != null)
                {
                    foreach (var img in imgElements)
                    {
                        try
                        {
                            var src = img?.GetAttribute("src") ?? "";
                            if (string.IsNullOrEmpty(src)) continue;

                            if (!PositiveIconRegex.IsMatch(src)) continue;
                            if (NegativeBannerRegex.IsMatch(src)) continue;

                            IElement n = img.ParentElement;
                            bool inCarousel = false;
                            while (n != null)
                            {
                                var c = n.GetAttribute("class") ?? "";
                                if (c.IndexOf("carousel", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    inCarousel = true;
                                    break;
                                }
                                n = n.ParentElement;
                            }
                            if (!inCarousel) return src;
                        }
                        catch (Exception)
                        {
                            // Continue with next image
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Debug($"[STOVE] Error finding icon: {ex.Message}");
            }

            return string.Empty;
        }

        /*──────────────── DESCRIPTION + MISC HELPERS ──────────*/

        private static string SelectProductDescription(IHtmlDocument doc)
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

                    /* fallback – longest text block */
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
                LogManager.GetLogger().Debug($"[STOVE] Error selecting description: {ex.Message}");
            }

            return null;
        }

        private static IEnumerable<(string label, string value)> ReadDlPairs(IHtmlDocument doc)
        {
            if (doc == null)
                yield break;

            IElement[] nodes = null;
            try
            {
                nodes = doc.QuerySelectorAll("dl dt, dl dd")?.ToArray();
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Debug($"[STOVE] Error querying DL pairs: {ex.Message}");
                yield break;
            }

            if (nodes == null || nodes.Length == 0)
                yield break;

            for (int i = 0; i < nodes.Length - 1; i += 2)
            {
                string label = "";
                string value = "";

                try
                {
                    label = nodes[i]?.TextContent?.Trim()?.ToLowerInvariant() ?? "";
                    value = nodes[i + 1]?.InnerHtml?.Trim() ?? "";
                }
                catch (Exception)
                {
                    // Continue with next pair
                    continue;
                }

                if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(value))
                {
                    yield return (label, value);
                }
            }
        }

        private static HashSet<MetadataProperty> NameSet(string htmlAnchor)
        {
            if (string.IsNullOrWhiteSpace(htmlAnchor))
                return new HashSet<MetadataProperty>();

            try
            {
                var parser = new HtmlParser();
                var doc = parser.Parse("<div>" + htmlAnchor + "</div>");
                var txt = doc?.QuerySelector("a")?.TextContent?.Trim() ?? htmlAnchor.Trim();

                if (!string.IsNullOrEmpty(txt))
                {
                    return new HashSet<MetadataProperty> { new MetadataNameProperty(txt) };
                }
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Debug($"[STOVE] Error parsing name set: {ex.Message}");
            }

            return new HashSet<MetadataProperty>();
        }

        /*───────────────────── WEBVIEW FETCH ──────────────────*/

        private string DownloadWithWebView(string url, string waitFor)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            IWebView vw = null;
            try
            {
                if (api?.WebViews == null)
                {
                    logger.Error("[STOVE] WebViews API not available");
                    return string.Empty;
                }

                vw = api.WebViews.CreateOffscreenView();
                if (vw == null)
                {
                    logger.Error("[STOVE] Failed to create WebView");
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
                        logger.Debug($"[STOVE] Failed to set adult cookie: {ex.Message}");
                    }
                }

                vw.Navigate(url);
                var sw = Stopwatch.StartNew();
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
                            (src.IndexOf("/restrictions/agree",
                                         StringComparison.OrdinalIgnoreCase) >= 0))
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
                        logger.Debug($"[STOVE] WebView operation error: {ex.Message}");
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(src))
                {
                    try
                    {
                        Thread.Sleep(ExtraSettlingDelayMs);
                        var upd = vw.GetPageSource();
                        if (!string.IsNullOrEmpty(upd)) src = upd;
                    }
                    catch (Exception ex)
                    {
                        logger.Debug($"[STOVE] Final page source fetch error: {ex.Message}");
                    }
                }
                return src;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[STOVE] WebView download failed: " + url);
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
                    logger.Debug($"[STOVE] WebView disposal error: {ex.Message}");
                }
            }
        }
    }
}