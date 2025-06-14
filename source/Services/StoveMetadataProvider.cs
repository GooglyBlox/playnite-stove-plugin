using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StoveLibrary.Services
{
    public class StoveMetadataProvider : LibraryMetadataProvider
    {
        private static ILogger Logger => LogManager.GetLogger();

        private StoveLibrary Plugin { get; }
        private StoveLibrarySettings Settings { get; }

        public StoveMetadataProvider(StoveLibrary plugin, StoveLibrarySettings settings)
        {
            Plugin = plugin;
            Settings = settings;
        }

        public override GameMetadata GetMetadata(Game game)
        {
            var meta = new GameMetadata();

            if (!Settings.ImportMetadata)
            {
                return meta;
            }

            try
            {
                int productNo = GetProductNumberFromGame(game);
                if (productNo <= 0)
                {
                    Logger.Warn($"Could not determine product number for game: {game.Name}");
                    return meta;
                }

                meta.Links = new List<Link>
                {
                    new Link("Store Page", $"https://store.onstove.com/en/games/{productNo}")
                };

                var storeDetails = StoveLibrary.StoveApi.GetGameStoreDetails(productNo);
                if (storeDetails == null)
                {
                    Logger.Warn($"No store details found for product {productNo}");
                    return meta;
                }

                try
                {
                    var description = StoveLibrary.StoveApi.GetGameDescription($"https://store.onstove.com/en/games/{productNo}");
                    if (!string.IsNullOrEmpty(description))
                    {
                        meta.Description = description;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to get description for game: {game.Name}");
                }

                try
                {
                    var developer = StoveLibrary.StoveApi.GetGameDeveloper(game.GameId);
                    if (!string.IsNullOrEmpty(developer))
                    {
                        var developers = developer.Split(',')
                            .Select(dev => dev.Trim())
                            .Where(dev => !string.IsNullOrEmpty(dev))
                            .ToList();

                        if (developers.Any())
                        {
                            meta.Developers = new HashSet<MetadataProperty>(
                                developers.Select(dev => new MetadataNameProperty(dev))
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to get developer information for game: {game.Name}");
                }

                if (storeDetails.Genres != null && storeDetails.Genres.Any())
                {
                    meta.Genres = new HashSet<MetadataProperty>(
                        storeDetails.Genres.Select(g => new MetadataNameProperty(g.TagName))
                    );
                }

                if (Settings.ImportTags && storeDetails.Tags != null && storeDetails.Tags.Any())
                {
                    meta.Tags = new HashSet<MetadataProperty>(
                        storeDetails.Tags.Select(t => new MetadataNameProperty(t.TagName))
                    );
                }

                if (!string.IsNullOrEmpty(storeDetails.TitleImageSquare))
                {
                    meta.Icon = new MetadataFile(storeDetails.TitleImageSquare);
                }

                if (!string.IsNullOrEmpty(storeDetails.TitleImageRectangle))
                {
                    meta.CoverImage = new MetadataFile(GetVerticalCoverUrl(storeDetails.TitleImageRectangle));
                }

                return meta;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting metadata for game: {game.Name}");
                return meta;
            }
        }

        private int GetProductNumberFromGame(Game game)
        {
            var storeLink = game.Links?.FirstOrDefault(x => x.Name == "Store Page");
            if (storeLink != null && !string.IsNullOrEmpty(storeLink.Url))
            {
                var urlParts = storeLink.Url.Split('/');
                if (urlParts.Length >= 2 && int.TryParse(urlParts.Last(), out int productNoFromLink))
                {
                    return productNoFromLink;
                }
            }

            try
            {
                var sessionData = StoveLibrary.StoveApi.GetSessionData();
                if (sessionData?.Value?.Member?.MemberNo != null && !string.IsNullOrEmpty(sessionData.Value.AccessToken))
                {
                    var memberNo = sessionData.Value.Member.MemberNo;
                    var accessToken = sessionData.Value.AccessToken;
                    var games = StoveLibrary.StoveApi.GetOwnedGamesFromApi(memberNo, accessToken);

                    var gameData = games.FirstOrDefault(g => g.GameId == game.GameId);
                    if (gameData != null)
                    {
                        return gameData.ProductNo;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get product number from API");
            }

            if (int.TryParse(game.GameId, out int legacyProductNo))
            {
                return legacyProductNo;
            }

            return 0;
        }

        private string GetVerticalCoverUrl(string horizontalUrl)
        {
            if (string.IsNullOrEmpty(horizontalUrl))
                return null;

            return $"https://image.onstove.com/222x294/{horizontalUrl}";
        }
    }
}