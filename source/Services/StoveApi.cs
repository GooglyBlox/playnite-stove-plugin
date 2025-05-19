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

        private readonly ILogger              logger   = LogManager.GetLogger();
        private readonly IPlayniteAPI         api;
        private readonly StoveLibrarySettings settings;

        public StoveApi(IPlayniteAPI playniteApi, StoveLibrarySettings pluginSettings)
        {
            api      = playniteApi;
            settings = pluginSettings;
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

            var gamesUrl = BuildGamesUrl(rawProfileUrl);
            var html     = DownloadWithWebView(gamesUrl, "/en/games/");
            if (string.IsNullOrEmpty(html))
                return owned;

            var entries = ExtractGameEntries(html);
            logger.Info($"[STOVE] Found {entries.Count} games on profile page.");

            foreach (var kv in entries)
            {
                var id    = kv.Key;
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
            return owned;
        }

        public GameMetadata GetGameMetadata(string gameId)
        {
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
            var uri = new Uri(input, UriKind.Absolute);
            if (uri.AbsolutePath.IndexOf("/game", StringComparison.OrdinalIgnoreCase) >= 0)
                return uri.ToString();

            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/game?types=GAME";
        }

        private static Dictionary<string, string> ExtractGameEntries(string html)
        {
            var doc  = new HtmlParser().Parse(html);
            var dict = new Dictionary<string, string>();

            foreach (var a in doc.QuerySelectorAll("a[href*='/en/games/']"))
            {
                var m = Regex.Match(a.GetAttribute("href") ?? "", "/en/games/(\\d+)");
                if (!m.Success) continue;

                var id = m.Groups[1].Value;

                /* skip DLC cards */
                IElement node = a.ParentElement;
                while (node != null && !node.HasAttribute("productdetailtype"))
                    node = node.ParentElement;

                if (node != null &&
                    "DLC".Equals(node.GetAttribute("productdetailtype"),
                                 StringComparison.OrdinalIgnoreCase))
                    continue;

                var src = a.ParentElement?.QuerySelector("img[src]")?.GetAttribute("src") ?? "";
                if (!dict.ContainsKey(id)) dict[id] = src;
            }
            return dict;
        }

        /*────────────────── PRODUCT PAGE PARSE ─────────────────*/

        private GameMetadata ParseGamePage(string html, string url, string gameId)
        {
            var doc  = new HtmlParser().Parse(html);
            var meta = new GameMetadata
            {
                Source    = new MetadataNameProperty("STOVE"),
                GameId    = gameId,
                Links     = new List<Link> { new Link("Store Page", url) },
                Platforms = new HashSet<MetadataProperty>
                            { new MetadataSpecProperty("pc_windows") }
            };

            /* name */
            var rawName = doc.QuerySelector("meta[property='og:title']")?.GetAttribute("content")
                       ?? doc.QuerySelector("title")?.TextContent
                       ?? "";
            if (rawName.Contains("|")) rawName = rawName.Split('|')[0];
            rawName = Regex.Replace(rawName, "\\s*\\|\\s*STOVE STORE\\s*$",
                                    "", RegexOptions.IgnoreCase).Trim();
            meta.Name = string.IsNullOrWhiteSpace(rawName) ? "STOVE Game " + gameId : rawName;

            /* details */
            foreach (var pair in ReadDlPairs(doc))
            {
                var label = pair.label;
                var value = pair.value;

                switch (label)
                {
                    case "genre":     meta.Genres     = NameSet(value); break;
                    case "creator":   meta.Developers = NameSet(value); break;
                    case "publisher": meta.Publishers = NameSet(value); break;
                    case "release":
                        DateTime dt;
                        if (DateTime.TryParseExact(value, "yyyy.MM.dd",
                                                   CultureInfo.InvariantCulture,
                                                   DateTimeStyles.None, out dt))
                            meta.ReleaseDate = new ReleaseDate(dt);
                        break;
                }
            }

            /* tags */
            var tags = doc.QuerySelectorAll("a[href*='features=']")
                          .Select(a => a.TextContent.Trim())
                          .Where(t => t.StartsWith("#"))
                          .Select(t => t.Substring(1).Trim())
                          .Distinct()
                          .ToList();
            if (tags.Count > 0)
                meta.Tags = new HashSet<MetadataProperty>(
                                tags.Select(t => new MetadataNameProperty(t)));

            /* description */
            meta.Description = SelectProductDescription(doc);

            /* icon */
            var icon = FindIcon(doc);
            if (!string.IsNullOrEmpty(icon))
            {
                if (icon.StartsWith("//")) icon = "https:" + icon;
                meta.Icon = new MetadataFile(icon);
            }

            return meta;
        }

        /*─────────────────── ICON SELECTION ───────────────────*/

        private static string FindIcon(IHtmlDocument doc)
        {
            foreach (var div in doc.QuerySelectorAll("div.absolute.overflow-hidden"))
            {
                var cls = div.GetAttribute("class") ?? "";

                if (!Regex.IsMatch(cls, @"h-\[(?:7|8|9|10|11)(?:\.\d+)?rem\]",
                                RegexOptions.IgnoreCase)) continue;
                if (!Regex.IsMatch(cls, @"w-\[(?:7|8|9|10|11)(?:\.\d+)?rem\]",
                                RegexOptions.IgnoreCase)) continue;

                var img = div.QuerySelector("img[src]");
                if (img == null) continue;

                var src = img.GetAttribute("src") ?? "";
                if (string.IsNullOrEmpty(src)) continue;

                if (NegativeBannerRegex.IsMatch(src)) continue;

                return src;
            }

            foreach (var img in doc.QuerySelectorAll("img[src]"))
            {
                var src = img.GetAttribute("src") ?? "";
                if (!PositiveIconRegex.IsMatch(src)) continue;
                if (NegativeBannerRegex.IsMatch(src)) continue;

                IElement n = img.ParentElement;
                bool inCarousel = false;
                while (n != null)
                {
                    var c = n.GetAttribute("class") ?? "";
                    if (c.IndexOf("carousel", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        inCarousel = true; break;
                    }
                    n = n.ParentElement;
                }
                if (!inCarousel) return src;
            }

            return string.Empty;
        }

        /*──────────────── DESCRIPTION + MISC HELPERS ──────────*/

        private static string SelectProductDescription(IHtmlDocument doc)
        {
            foreach (var view in doc.QuerySelectorAll("div.inds-editor-view"))
            {
                var head = view.ParentElement?.QuerySelector("h3")?.TextContent.Trim() ?? "";
                if (Regex.IsMatch(head, "product\\s+description", RegexOptions.IgnoreCase))
                    return view.InnerHtml.Trim();
            }

            /* fallback – longest text block */
            string bestHtml = null; int bestLen = 0;
            foreach (var view in doc.QuerySelectorAll("div.inds-editor-view"))
            {
                var len = Regex.Replace(view.TextContent ?? "", "\\s+", "").Length;
                if (len > bestLen) { bestLen = len; bestHtml = view.InnerHtml.Trim(); }
            }
            return bestHtml;
        }

        private static IEnumerable<(string label, string value)> ReadDlPairs(IHtmlDocument doc)
        {
            var nodes = doc.QuerySelectorAll("dl dt, dl dd").ToArray();
            for (int i = 0; i < nodes.Length - 1; i += 2)
                yield return (nodes[i].TextContent.Trim().ToLowerInvariant(),
                              nodes[i + 1].InnerHtml.Trim());
        }

        private static HashSet<MetadataProperty> NameSet(string htmlAnchor)
        {
            var doc = new HtmlParser().Parse("<div>" + htmlAnchor + "</div>");
            var txt = doc.QuerySelector("a")?.TextContent.Trim() ?? htmlAnchor.Trim();
            return new HashSet<MetadataProperty> { new MetadataNameProperty(txt) };
        }

        /*───────────────────── WEBVIEW FETCH ──────────────────*/

        private string DownloadWithWebView(string url, string waitFor)
        {
            try
            {
                using (var vw = api.WebViews.CreateOffscreenView())
                {
                    if (settings != null && settings.AllowAdultGames)
                        vw.SetCookies("https://store.onstove.com",
                            new HttpCookie { Name = "ADULT_GAME_AGREE", Value = "Y",
                                             Domain = ".onstove.com", Path = "/",
                                             Expires = DateTime.UtcNow.AddYears(1) });

                    vw.Navigate(url);
                    var sw = Stopwatch.StartNew(); string src = ""; bool gate = false;

                    while (sw.Elapsed < TimeSpan.FromSeconds(30))
                    {
                        Thread.Sleep(300);
                        src = vw.GetPageSource();

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

                    if (!string.IsNullOrEmpty(src))
                    {
                        Thread.Sleep(ExtraSettlingDelayMs);
                        var upd = vw.GetPageSource();
                        if (!string.IsNullOrEmpty(upd)) src = upd;
                    }
                    return src;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[STOVE] WebView download failed: " + url);
                return string.Empty;
            }
        }
    }
}
