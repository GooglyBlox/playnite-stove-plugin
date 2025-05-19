
using AngleSharp.Dom;
using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

using HttpCookie = Playnite.SDK.HttpCookie;

namespace StoveLibrary.Services
{

    public class StoveApi
    {
        private const int ExtraSettlingDelayMs = 1200;

        private readonly ILogger              logger   = LogManager.GetLogger();
        private readonly IPlayniteAPI         api;
        private readonly StoveLibrarySettings settings;

        /* ──────────────────────────────────────────────────────────── */
        public StoveApi(IPlayniteAPI playniteApi, StoveLibrarySettings pluginSettings)
        {
            api      = playniteApi;
            settings = pluginSettings;
        }

        /* ──────────────────────────────────────────────────────────── */
        /*  PUBLIC FACADE                                               */
        /* ──────────────────────────────────────────────────────────── */

        public List<GameMetadata> GetOwnedGames(string rawProfileUrl)
        {
            var owned = new List<GameMetadata>();
            if (string.IsNullOrWhiteSpace(rawProfileUrl))
            {
                logger.Warn("[STOVE] Blank profile URL.");
                return owned;
            }

            var gamesUrl = BuildGamesUrl(rawProfileUrl);
            logger.Info($"[STOVE] Fetching profile list → {gamesUrl}");

            var html = DownloadWithWebView(gamesUrl, "/en/games/");
            if (string.IsNullOrEmpty(html))
            {
                logger.Warn("[STOVE] Profile page returned empty HTML after WebView load.");
                return owned;
            }

            var ids = ExtractGameIds(html);
            logger.Info($"[STOVE] Found {ids.Count} game products on profile page.");

            foreach (var id in ids)
            {
                var meta = GetGameMetadata(id);
                // Skip invalid/placeholder entries
                if (meta != null &&
                    !string.IsNullOrEmpty(meta.Name) &&
                    !meta.Name.StartsWith("STOVE Game"))
                {
                    owned.Add(meta);
                }
            }
            return owned;
        }

        public GameMetadata GetGameMetadata(string gameId)
        {
            var gameUrl = $"https://store.onstove.com/en/games/{gameId}";
            try
            {
                var page = DownloadWithWebView(gameUrl, "features=");
                if (string.IsNullOrWhiteSpace(page) ||
                    page.IndexOf("requested page cannot be found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    logger.Info($"[STOVE] {gameUrl} returned not-found page.");
                    return null;
                }

                return ParseGamePage(page, gameUrl, gameId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[STOVE] Failed to get metadata for {gameUrl}");
                return null;
            }
        }

        /* ──────────────────────────────────────────────────────────── */
        /*  INTERNAL HELPERS                                            */
        /* ──────────────────────────────────────────────────────────── */

        #region Profile-page helpers

        private static string BuildGamesUrl(string input)
        {
            var uri = new Uri(input, UriKind.Absolute);
            if (uri.AbsolutePath.IndexOf("/game", StringComparison.OrdinalIgnoreCase) >= 0)
                return uri.ToString();

            var basePath = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            // Only list full games, no demos.
            return $"{basePath}/game?types=GAME";
        }

        private static List<string> ExtractGameIds(string html)
        {
            var doc = new HtmlParser().Parse(html);
            var ids = new HashSet<string>();

            foreach (var link in doc.QuerySelectorAll("a[href*='/en/games/']"))
            {
                var href = link.GetAttribute("href") ?? string.Empty;
                var m    = Regex.Match(href, "/en/games/(\\d+)");
                if (!m.Success)
                    continue;

                IElement node = link.ParentElement;
                string   type = null;
                while (node != null)
                {
                    if (node.HasAttribute("productdetailtype"))
                    {
                        type = node.GetAttribute("productdetailtype");
                        break;
                    }
                    node = node.ParentElement;
                }

                if (string.Equals(type, "DLC", StringComparison.OrdinalIgnoreCase))
                    continue;

                ids.Add(m.Groups[1].Value);
            }
            return ids.ToList();
        }

        #endregion

        #region Product-page parsing

        private GameMetadata ParseGamePage(string html, string gameUrl, string gameId)
        {
            var doc = new HtmlParser().Parse(html);

            var meta = new GameMetadata
            {
                Source    = new MetadataNameProperty("STOVE"),
                GameId    = gameId,
                Links     = new List<Link> { new Link("Store Page", gameUrl) },
                Platforms = new HashSet<MetadataProperty>
                {
                    new MetadataSpecProperty("pc_windows")
                }
            };

            /* ── Name ──────────────────────────────────────────── */
            var rawName = doc.QuerySelector("meta[property='og:title']")?.GetAttribute("content")
                       ?? doc.QuerySelector("title")?.TextContent
                       ?? string.Empty;

            if (rawName.Contains("|"))
                rawName = rawName.Split('|')[0];

            rawName = Regex.Replace(rawName,
                                    "\\s*\\|\\s*STOVE STORE\\s*$",
                                    "",
                                    RegexOptions.IgnoreCase).Trim();

            meta.Name = string.IsNullOrWhiteSpace(rawName)
                        ? $"STOVE Game {gameId}"
                        : rawName;

            /* ── Details (Genre, Creator, etc.) ───────────────── */
            foreach (var (label, value) in ReadDlPairs(doc))
            {
                switch (label)
                {
                    case "genre":     meta.Genres     = NameSet(value); break;
                    case "creator":   meta.Developers = NameSet(value); break;
                    case "publisher": meta.Publishers = NameSet(value); break;
                    case "release":
                        if (DateTime.TryParseExact(value,
                                                   "yyyy.MM.dd",
                                                   null,
                                                   System.Globalization.DateTimeStyles.None,
                                                   out var d))
                            meta.ReleaseDate = new ReleaseDate(d);
                        break;
                }
            }

            /* ── Tags ──────────────────────────────────────────── */
            var tagEls = doc.QuerySelectorAll("a[href*='features=']")
                            .Select(a => a.TextContent.Trim())
                            .Where(t => t.StartsWith("#"))
                            .Select(t => t.Substring(1).Trim())
                            .Where(t => t.Length > 0)
                            .Distinct();

            if (tagEls.Any())
            {
                meta.Tags = new HashSet<MetadataProperty>(
                                tagEls.Select(t => new MetadataNameProperty(t)));
            }

            /* ── Description ──────────────────────────────────── */
            meta.Description = SelectProductDescription(doc);

            /* ── Cover image ──────────────────────────────────── */
            var coverSrc = doc.QuerySelector("meta[property='og:image']")?.GetAttribute("content");
            if (string.IsNullOrEmpty(coverSrc))
            {
                var imgEl = doc.QuerySelector("img[src*='타이틀섬네일']")
                         ?? doc.QuerySelector("img[src*='cloudfront']")
                         ?? doc.QuerySelector("img[src*='cdn.onstove.com']")
                         ?? doc.QuerySelector("img[src*='image.onstove.com']");
                coverSrc = imgEl?.GetAttribute("src");
            }

            if (!string.IsNullOrEmpty(coverSrc))
            {
                if (coverSrc.StartsWith("//"))
                    coverSrc = "https:" + coverSrc;

                meta.CoverImage = new MetadataFile(coverSrc);
            }

            return meta;
        }

        private static string SelectProductDescription(IHtmlDocument doc)
        {
            foreach (var view in doc.QuerySelectorAll("div.inds-editor-view"))
            {
                var heading = view.ParentElement?.QuerySelector("h3");
                var txt     = heading?.TextContent?.Trim() ?? string.Empty;

                if (Regex.IsMatch(txt, "product\\s+description", RegexOptions.IgnoreCase))
                    return view.InnerHtml.Trim();
            }

            string bestHtml = null;
            int    bestLen  = 0;

            foreach (var view in doc.QuerySelectorAll("div.inds-editor-view"))
            {
                var len = Regex.Replace(view.TextContent ?? string.Empty, "\\s+", "").Length;
                if (len > bestLen)
                {
                    bestLen  = len;
                    bestHtml = view.InnerHtml.Trim();
                }
            }

            return bestHtml;
        }

        private static IEnumerable<(string label, string value)> ReadDlPairs(IHtmlDocument doc)
        {
            var nodes = doc.QuerySelectorAll("dl dt, dl dd").ToArray();
            for (int i = 0; i < nodes.Length - 1; i += 2)
            {
                var lbl = nodes[i]?.TextContent?.Trim().ToLowerInvariant();
                var val = nodes[i + 1]?.InnerHtml?.Trim();
                if (!string.IsNullOrEmpty(lbl) && !string.IsNullOrEmpty(val))
                    yield return (lbl, val);
            }
        }

        private static HashSet<MetadataProperty> NameSet(string htmlAnchor)
        {
            var text = StripAnchor(htmlAnchor);
            return new HashSet<MetadataProperty>
            {
                new MetadataNameProperty(text)
            };
        }

        private static string StripAnchor(string htmlAnchor)
        {
            var doc = new HtmlParser().Parse($"<div>{htmlAnchor}</div>");
            return doc.QuerySelector("a")?.TextContent.Trim() ?? htmlAnchor.Trim();
        }

        #endregion

        #region WebView download helper (adult-gate aware)

        private string DownloadWithWebView(string url, string waitForSubstring)
        {
            try
            {
                using (var view = api.WebViews.CreateOffscreenView())
                {
                    /* Inject ADULT_GAME_AGREE cookie if the user opted in */
                    if (settings?.AllowAdultGames == true)
                    {
                        var adultCookie = new HttpCookie
                        {
                            Name    = "ADULT_GAME_AGREE",
                            Value   = "Y",
                            Domain  = ".onstove.com",
                            Path    = "/",
                            Expires = DateTime.UtcNow.AddYears(1)
                        };

                        // IWebView.SetCookies(string url, HttpCookie cookie)
                        view.SetCookies("https://store.onstove.com", adultCookie);
                        logger.Debug("[STOVE] Injected ADULT_GAME_AGREE=Y cookie");
                    }

                    view.Navigate(url);

                    var sw        = Stopwatch.StartNew();
                    string source = string.Empty;
                    bool triedGate = false;

                    while (sw.Elapsed < TimeSpan.FromSeconds(30))
                    {
                        Thread.Sleep(300);
                        source = view.GetPageSource();

                        // If auto-cookie wasn’t injected, fall back to old redirect dance
                        if (settings?.AllowAdultGames != true &&
                            !triedGate &&
                            (source.IndexOf("/restrictions/agree",
                                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                             source.IndexOf("not suitable for players under the age",
                                            StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            triedGate = true;
                            logger.Debug("[STOVE] Detected adult gate – navigating to agree endpoint.");

                            // Try to pull product id from original URL or page source
                            var pidMatch = Regex.Match(url, "/games/(\\d+)", RegexOptions.IgnoreCase);
                            var pid      = pidMatch.Success ? pidMatch.Groups[1].Value : null;

                            if (string.IsNullOrEmpty(pid))
                            {
                                pidMatch = Regex.Match(source, "productNo=(\\d+)");
                                pid      = pidMatch.Success ? pidMatch.Groups[1].Value : null;
                            }

                            if (!string.IsNullOrEmpty(pid))
                            {
                                var agreeUrl = $"https://store.onstove.com/en/restrictions/agree?productNo={pid}";
                                view.Navigate(agreeUrl);
                                Thread.Sleep(500);
                                continue;
                            }
                        }

                        if (!string.IsNullOrEmpty(source) &&
                            (waitForSubstring == null ||
                             source.IndexOf(waitForSubstring,
                                            StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(source))
                    {
                        Thread.Sleep(ExtraSettlingDelayMs);
                        var updated = view.GetPageSource();
                        if (!string.IsNullOrEmpty(updated))
                            source = updated;
                    }

                    return source;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[STOVE] WebView download failed for {url}");
                return string.Empty;
            }
        }

        #endregion
    }
}
