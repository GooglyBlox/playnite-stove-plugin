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
            if (!Settings.ImportMetadata)
            {
                return new GameMetadata();
            }

            try
            {
                string gameUrl = game.Links?.FirstOrDefault(x => x.Name == "Store Page")?.Url;
                if (string.IsNullOrEmpty(gameUrl))
                {
                    Logger.Warn($"No store URL found for game: {game.Name}");
                    return new GameMetadata();
                }

                var urlParts = gameUrl.Split('/');
                if (urlParts.Length < 2)
                {
                    Logger.Error($"Invalid store URL format: {gameUrl}");
                    return new GameMetadata();
                }

                if (!int.TryParse(urlParts.Last(), out int productNo))
                {
                    Logger.Error($"Could not extract product number from URL: {gameUrl}");
                    return new GameMetadata();
                }

                var storeDetails = StoveLibrary.StoveApi.GetGameStoreDetails(productNo);
                if (storeDetails == null)
                {
                    Logger.Warn($"No store details found for product {productNo}");
                    return new GameMetadata();
                }

                var meta = new GameMetadata();

                try
                {
                    var description = StoveLibrary.StoveApi.GetGameDescription(gameUrl);
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
                return new GameMetadata();
            }
        }

        private string GetVerticalCoverUrl(string horizontalUrl)
        {
            if (string.IsNullOrEmpty(horizontalUrl))
                return null;

            return $"https://image.onstove.com/222x294/{horizontalUrl}";
        }
    }
}