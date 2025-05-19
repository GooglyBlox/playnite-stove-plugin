using StoveLibrary.Services;
using StoveLibrary.Views;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Controls;

namespace StoveLibrary
{
    public class StoveLibrary : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id => Guid.Parse("2a62a584-2cc3-4220-8da6-cf4ac588a439");
        public override string Name => "STOVE";
        public override string LibraryIcon => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "icon.png");

        public static StoveLibrary Instance { get; private set; }
        internal static StoveApi StoveApi { get; private set; }

        private readonly StoveLibrarySettingsViewModel settingsVm;

        public StoveLibrary(IPlayniteAPI api) : base(api)
        {
            Instance = this;
            Properties = new LibraryPluginProperties { CanShutdownClient = false };
            settingsVm = new StoveLibrarySettingsViewModel(this);

            StoveApi = new StoveApi(api, settingsVm.Settings);
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var profile = settingsVm.Settings.ProfileUrl?.Trim();
            if (string.IsNullOrWhiteSpace(profile))
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "stove-profile-missing",
                    "Please enter your STOVE profile URL in the plugin settings.",
                    NotificationType.Error,
                    () => OpenSettingsView()));
                yield break;
            }

            List<GameMetadata> games;
            try
            {
                games = StoveApi.GetOwnedGames(profile);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[STOVE] Import failed.");
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "stove-import-error",
                    $"STOVE import error: {ex.Message}",
                    NotificationType.Error,
                    () => OpenSettingsView()));
                yield break;
            }

            foreach (var g in games) yield return g;
        }

        public override LibraryMetadataProvider GetMetadataDownloader() =>
            new StoveMetadataProvider(this, settingsVm.Settings);

        public override ISettings GetSettings(bool firstRunSettings) => settingsVm;
        public override UserControl GetSettingsView(bool firstRunSettings) =>
            new StoveLibrarySettingsView(settingsVm.Settings);

        public override LibraryClient Client => new StoveClient();
    }
}
