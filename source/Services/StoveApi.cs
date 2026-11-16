using Playnite.SDK;
using Playnite.SDK.Models;
using StoveLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StoveLibrary.Services
{
    public class StoveApi : IDisposable
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI api;
        private readonly StoveAuthService authService;
        private readonly StoveGamesService gamesService;
        private readonly StoveStoreService storeService;
        private readonly StoveHttpService httpService;

        public StoveApi(IPlayniteAPI playniteApi, StoveLibrarySettings pluginSettings, string dataPath = null)
        {
            api = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            var settings = pluginSettings ?? throw new ArgumentNullException(nameof(pluginSettings));

            httpService = new StoveHttpService();
            authService = new StoveAuthService(api, settings, dataPath);
            gamesService = new StoveGamesService(httpService, authService);
            storeService = new StoveStoreService(api, settings, httpService);
        }

        public List<GameMetadata> GetOwnedGames()
        {
            var owned = new List<GameMetadata>();

            try
            {
                var sessionData = GetSessionData();
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
                var games = GetOwnedGamesFromApi(memberNo, accessToken);
                logger.Info($"Found {games.Count} owned games.");

                var existingGames = api.Database.Games.Where(g => g.PluginId == StoveLibrary.PluginGuid).ToList();

                MigrateLegacyGameIds(existingGames, games);

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
                            Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") }
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

        // Old plugin versions stored product_no as GameId.
        internal static List<KeyValuePair<Game, string>> PlanLegacyIdMigrations(IEnumerable<Game> existingGames, List<StoveGameData> apiGames)
        {
            var plan = new List<KeyValuePair<Game, string>>();
            if (existingGames == null || apiGames == null)
                return plan;

            var existing = existingGames.Where(g => g != null).ToList();
            var usedIds = new HashSet<string>(
                existing.Where(g => !string.IsNullOrEmpty(g.GameId)).Select(g => g.GameId),
                StringComparer.OrdinalIgnoreCase);
            var plannedGames = new HashSet<Game>();

            foreach (var item in apiGames)
            {
                if (item == null || !item.HasOwnership || string.IsNullOrWhiteSpace(item.GameId))
                    continue;

                var legacyId = item.ProductNo.ToString();
                if (usedIds.Contains(item.GameId))
                    continue;

                var legacyGame = existing.FirstOrDefault(g => !plannedGames.Contains(g) && g.GameId == legacyId);
                if (legacyGame == null)
                    continue;

                plannedGames.Add(legacyGame);
                usedIds.Add(item.GameId);
                plan.Add(new KeyValuePair<Game, string>(legacyGame, item.GameId));
            }

            return plan;
        }

        private void MigrateLegacyGameIds(List<Game> existingGames, List<StoveGameData> apiGames)
        {
            var plan = PlanLegacyIdMigrations(existingGames, apiGames);
            if (plan.Count == 0)
                return;

            using (api.Database.BufferedUpdate())
            {
                foreach (var entry in plan)
                {
                    var game = entry.Key;
                    var oldId = game.GameId;
                    try
                    {
                        game.GameId = entry.Value;
                        api.Database.Games.Update(game);
                        logger.Info($"Migrated legacy GameId for '{game.Name}': {oldId} -> {entry.Value}");
                    }
                    catch (Exception ex)
                    {
                        game.GameId = oldId;
                        logger.Error(ex, $"Failed to migrate legacy GameId for '{game.Name}'");
                    }
                }
            }
        }

        public SessionResponse GetSessionData()
        {
            return authService.GetSessionData();
        }

        public List<StoveGameData> GetOwnedGamesFromApi(long memberNo, string accessToken)
        {
            return gamesService.GetOwnedGamesFromApi(memberNo, accessToken);
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

        public string GetGamePublisher(string gameId)
        {
            return storeService.GetGamePublisher(gameId);
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

        public void Dispose()
        {
            httpService?.Dispose();
        }
    }
}