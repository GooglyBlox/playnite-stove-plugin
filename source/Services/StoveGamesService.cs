using Playnite.SDK;
using StoveLibrary.Models;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StoveLibrary.Services
{
    public class StoveGamesService
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly StoveHttpService httpService;
        private readonly StoveAuthService authService;

        public StoveGamesService(StoveHttpService httpService, StoveAuthService authService = null)
        {
            this.httpService = httpService ?? throw new ArgumentNullException(nameof(httpService));
            this.authService = authService;
        }

        public List<StoveGameData> GetOwnedGamesFromApi(long memberNo, string authToken)
        {
            var allGames = new List<StoveGameData>();
            int page = 1;
            int totalPages = 1;
            bool retried = false;

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
                        retried = false;
                    }
                    else
                    {
                        logger.Warn($"No games data returned for page {page}");
                        break;
                    }
                }
                catch (Exception ex) when (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                {
                    if (!retried && authService != null)
                    {
                        logger.Info("Access token may be expired, attempting to refresh session");
                        retried = true;
                        
                        try
                        {
                            var newSession = authService.RefreshSession();
                            if (newSession?.Value?.AccessToken != null)
                            {
                                authToken = newSession.Value.AccessToken;
                                logger.Info("Session refreshed successfully, retrying request");
                                continue;
                            }
                        }
                        catch (Exception refreshEx)
                        {
                            logger.Error(refreshEx, "Failed to refresh session");
                        }
                    }
                    
                    logger.Error(ex, "Authentication failed after retry - token may be invalid");
                    authService?.InvalidateTokens();
                    throw new UnauthorizedAccessException("Auth token expired or invalid", ex);
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
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var gamesUrl = $"https://api.onstove.com/myindie/v1.1/own-games?member_no={memberNo}&product_type=GAME&size=30&page={page}&timestemp={timestamp}";

                var response = httpService.GetAsync(gamesUrl, authToken).Result;

                if (response.IsSuccessStatusCode)
                {
                    var content = response.Content.ReadAsStringAsync().Result;
                    return JsonConvert.DeserializeObject<GamesResponse>(content);
                }
                else
                {
                    var errorContent = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new Exception($"401 Unauthorized: {errorContent}");
                    }

                    logger.Error($"API request failed: {response.StatusCode} - {errorContent}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error getting games page {page}");
                throw;
            }
        }
    }
}