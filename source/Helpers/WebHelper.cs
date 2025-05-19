using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playnite.SDK;

namespace StoveLibrary.Helpers
{
    public class SerializableCookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Domain { get; set; }
        public string Path { get; set; }
        public DateTime? Expires { get; set; }
        public bool HttpOnly { get; set; }
        public bool Secure { get; set; }

        public static SerializableCookie FromHttpCookie(HttpCookie cookie)
        {
            return new SerializableCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                Expires = cookie.Expires,
                HttpOnly = cookie.HttpOnly,
                Secure = cookie.Secure
            };
        }

        public HttpCookie ToHttpCookie()
        {
            return new HttpCookie
            {
                Name = Name,
                Value = Value,
                Domain = Domain,
                Path = Path,
                Expires = Expires,
                HttpOnly = HttpOnly,
                Secure = Secure
            };
        }
    }

    internal static class WebHelper
    {
        private static ILogger Logger => LogManager.GetLogger();

        // ── HTTP GET with optional Playnite cookies ─────────────────────────
        public static string DownloadString(string url, List<HttpCookie> cookies = null)
        {
            var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };

            if (cookies != null)
            {
                foreach (var c in cookies)
                {
                    try
                    {
                        // convert Playnite.HttpCookie → System.Net.Cookie
                        var domain = c.Domain?.TrimStart('.');
                        if (string.IsNullOrEmpty(domain))
                        {
                            domain = new Uri(url).Host;
                        }
                        var ck = new Cookie(c.Name, c.Value, c.Path ?? "/", domain);
                        handler.CookieContainer.Add(ck);
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Failed to add cookie {c.Name}: {ex.Message}");
                    }
                }
                Logger.Debug($"Added {cookies.Count} cookies to request");
            }

            using (var client = new HttpClient(handler, disposeHandler: true))
            {
                client.Timeout = TimeSpan.FromSeconds(30);

                // Add user agent to mimic a real browser
                client.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                try
                {
                    var response = client.GetAsync(url).GetAwaiter().GetResult();

                    Logger.Info($"HTTP Response: {response.StatusCode} ({(int)response.StatusCode})");

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        Logger.Error($"HTTP Error {response.StatusCode}: {errorContent}");
                        throw new HttpRequestException($"HTTP {response.StatusCode}: {response.ReasonPhrase}");
                    }

                    return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    Logger.Error($"Request timeout for {url}");
                    throw new TimeoutException($"Request to {url} timed out", ex);
                }
            }
        }

        // ── Cookie persistence helpers ──────────────────────────────────────
        public static void SaveCookiesToFile(string filePath, List<HttpCookie> cookies)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                var serializableCookies = cookies.Select(SerializableCookie.FromHttpCookie).ToList();
                var json = JsonConvert.SerializeObject(serializableCookies, Formatting.Indented);
                File.WriteAllText(filePath, json);
                Logger.Debug($"Saved {cookies.Count} cookies to {filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to save cookies to {filePath}");
            }
        }

        public static List<HttpCookie> LoadCookiesFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Logger.Debug($"Cookie file {filePath} does not exist");
                    return new List<HttpCookie>();
                }

                var json = File.ReadAllText(filePath);
                var serializableCookies = JsonConvert.DeserializeObject<List<SerializableCookie>>(json);
                var cookies = serializableCookies?.Select(c => c.ToHttpCookie()).ToList() ?? new List<HttpCookie>();
                Logger.Debug($"Loaded {cookies.Count} cookies from {filePath}");
                return cookies;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to load cookies from {filePath}");
                return new List<HttpCookie>();
            }
        }

        // ── JSON persistence helpers ────────────────────────────────────────
        public static void SaveJsonToFile<T>(string filePath, T obj)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonConvert.SerializeObject(obj, Formatting.Indented));
        }

        public static T LoadJsonFromFile<T>(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return default(T);
            }

            return JsonConvert.DeserializeObject<T>(File.ReadAllText(filePath));
        }
    }
}