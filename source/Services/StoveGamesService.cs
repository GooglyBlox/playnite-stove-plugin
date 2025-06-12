using Playnite.SDK;
using StoveLibrary.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json;

namespace StoveLibrary.Services
{
    public class StoveGamesService
    {
        private readonly ILogger logger = LogManager.GetLogger();

        public List<StoveGameData> GetOwnedGamesFromApi(long memberNo, string authToken)
        {
            var allGames = new List<StoveGameData>();
            int page = 1;
            int totalPages = 1;

            do
            {
                try
                {
                    var games = GetGamesPage(memberNo, page, authToken);
                    if (games?.Value?.Content != null)
                    {
                        allGames.AddRange(games.Value.Content);
                        totalPages = games.Value.TotalPages;
                        page++;
                    }
                    else
                    {
                        logger.Warn($"No games data returned for page {page}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error fetching games page {page}");
                    break;
                }
            } while (page <= totalPages);

            return allGames;
        }

        private GamesResponse GetGamesPage(long memberNo, int page, string authToken)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {authToken}");
                    client.DefaultRequestHeaders.Add("X-Lang", "EN");
                    client.DefaultRequestHeaders.Add("X-Nation", "US");
                    client.DefaultRequestHeaders.Add("Origin", "https://profile.onstove.com");
                    client.DefaultRequestHeaders.Add("Referer", "https://profile.onstove.com/");
                    client.DefaultRequestHeaders.Add("User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.6998.205 Safari/537.36");

                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(
                        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var gamesUrl = $"https://api.onstove.com/myindie/v1.1/own-games?member_no={memberNo}&product_type=GAME&size=30&page={page}&timestemp={timestamp}";

                    var response = client.GetAsync(gamesUrl).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        var content = response.Content.ReadAsStringAsync().Result;
                        return JsonConvert.DeserializeObject<GamesResponse>(content);
                    }
                    else
                    {
                        var errorContent = response.Content.ReadAsStringAsync().Result;
                        logger.Error($"API request failed: {response.StatusCode} - {errorContent}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error getting games page {page}");
                return null;
            }
        }
    }
}