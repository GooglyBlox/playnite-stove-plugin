using Playnite.SDK;
using Playnite.SDK.Models;
using StoveLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StoveLibrary.Services
{
    public class StoveApi
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI api;
        private readonly StoveLibrarySettings settings;
        private readonly StoveAuthService authService;
        private readonly StoveGamesService gamesService;
        private readonly StoveStoreService storeService;

        public StoveApi(IPlayniteAPI playniteApi, StoveLibrarySettings pluginSettings)
        {
            api = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            settings = pluginSettings ?? throw new ArgumentNullException(nameof(pluginSettings));
            
            authService = new StoveAuthService(api);
            gamesService = new StoveGamesService();
            storeService = new StoveStoreService(api, settings);
        }

        public List<GameMetadata> GetOwnedGames()
        {
            var owned = new List<GameMetadata>();

            try
            {
                var sessionData = authService.GetSessionData();
                if (sessionData?.Value?.Member?.MemberNo == null)
                {
                    logger.Error("Could not get member number from session");
                    return owned;
                }

                if (string.IsNullOrEmpty(sessionData.Value.AccessToken))
                {
                    logger.Error("No access token in session response");
                    return owned;
                }

                var memberNo = sessionData.Value.Member.MemberNo;
                var accessToken = sessionData.Value.AccessToken;
                var games = gamesService.GetOwnedGamesFromApi(memberNo, accessToken);
                logger.Info($"Found {games.Count} owned games.");

                var existingGames = api.Database.Games.Where(g => g.PluginId == Guid.Parse("2a62a584-2cc3-4220-8da6-cf4ac588a439")).ToList();

                foreach (var game in games)
                {
                    try
                    {
                        if (!game.HasOwnership)
                            continue;

                        var legacyGameId = game.ProductNo.ToString();
                        if (existingGames.Any(g => g.GameId == legacyGameId))
                        {
                            continue;
                        }

                        var meta = new GameMetadata
                        {
                            GameId = game.GameId,
                            Name = game.ProductName,
                            Source = new MetadataNameProperty("STOVE"),
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") },
                            Links = new List<Link> { new Link("Store Page", $"https://store.onstove.com/en/games/{game.ProductNo}") }
                        };

                        if (game.ReleaseCreatedAt > 0)
                        {
                            var releaseDate = DateTimeOffset.FromUnixTimeMilliseconds(game.ReleaseCreatedAt).DateTime;
                            meta.ReleaseDate = new ReleaseDate(releaseDate);
                        }

                        if (game.PlayTime > 0)
                        {
                            meta.Playtime = (ulong)(game.PlayTime / 60);
                        }

                        if (game.LastPlayDate > 0)
                        {
                            meta.LastActivity = DateTimeOffset.FromUnixTimeMilliseconds(game.LastPlayDate).DateTime;
                        }

                        owned.Add(meta);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Failed to process game: {game.GameId}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Critical error in GetOwnedGames");
                throw;
            }

            return owned;
        }

        public StoreGameDetails GetGameStoreDetails(int productNo)
        {
            return storeService.GetGameStoreDetails(productNo);
        }

        public string GetGameDescription(string storeUrl)
        {
            return storeService.GetGameDescription(storeUrl);
        }

        public string GetGameDeveloper(string gameId)
        {
            return storeService.GetGameDeveloper(gameId);
        }

        public bool GetIsUserLoggedIn()
        {
            return authService.GetIsUserLoggedIn();
        }

        public void Login()
        {
            authService.Login();
        }

        public void Logout()
        {
            authService.Logout();
        }
    }
}