using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
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

                return StoveLibrary.StoveApi.GetGameMetadata(gameUrl) ?? new GameMetadata();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting metadata for game: {game.Name}");
                return new GameMetadata();
            }
        }
    }
}