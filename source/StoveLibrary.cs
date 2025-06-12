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
            var allGames = new List<GameMetadata>();
            Exception importError = null;

            if (!settingsVm.Settings.ConnectAccount)
            {
                return allGames;
            }

            try
            {
                if (!StoveApi.GetIsUserLoggedIn())
                {
                    throw new Exception("User is not logged in.");
                }

                allGames = StoveApi.GetOwnedGames();

                if (!settingsVm.Settings.ImportUninstalledGames)
                {
                    allGames.RemoveAll(a => !a.IsInstalled);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to import STOVE games.");
                importError = ex;
            }

            if (importError != null)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "stove-import-error",
                    string.Format(PlayniteApi.Resources.GetString("LOCLibraryImportError"), Name) +
                    System.Environment.NewLine + importError.Message,
                    NotificationType.Error,
                    () => OpenSettingsView()));
            }
            else
            {
                PlayniteApi.Notifications.Remove("stove-import-error");
            }

            return allGames;
        }

        public override LibraryMetadataProvider GetMetadataDownloader() =>
            new StoveMetadataProvider(this, settingsVm.Settings);

        public override ISettings GetSettings(bool firstRunSettings) => settingsVm;
        public override UserControl GetSettingsView(bool firstRunSettings) =>
            new StoveLibrarySettingsView(settingsVm.Settings);

        public override LibraryClient Client => new StoveClient();
    }
}