using StoveLibrary.Controllers;
using StoveLibrary.Services;
using StoveLibrary.Views;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;

namespace StoveLibrary
{
    public class StoveLibrary : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static Dictionary<Guid, StovePlayController> activeControllers = new Dictionary<Guid, StovePlayController>();

        public override Guid Id => Guid.Parse("2a62a584-2cc3-4220-8da6-cf4ac588a439");
        public override string Name => "STOVE";
        public override string LibraryIcon => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "icon.png");

        public static StoveLibrary Instance { get; private set; }
        internal static StoveApi StoveApi { get; private set; }

        private readonly StoveLibrarySettingsViewModel settingsVm;
        private StoveGameMonitor gameMonitor;
        private bool disposed = false;

        public StoveLibrary(IPlayniteAPI api) : base(api)
        {
            Instance = this;
            Properties = new LibraryPluginProperties
            {
                CanShutdownClient = false,
                HasSettings = true
            };
            settingsVm = new StoveLibrarySettingsViewModel(this);

            StoveApi = new StoveApi(api, settingsVm.Settings);

            gameMonitor = new StoveGameMonitor(api, settingsVm.Settings);
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
                allGames = GetOwnedGamesWithRetry();

                if (settingsVm.Settings.ImportInstalledGames)
                {
                    var installedGames = StoveRegistryHelper.GetInstalledStoveGames();

                    foreach (var game in allGames)
                    {
                        var installInfo = installedGames.FirstOrDefault(ig =>
                            string.Equals(ig.DisplayName, game.Name, StringComparison.OrdinalIgnoreCase));

                        if (installInfo != null)
                        {
                            game.IsInstalled = true;
                            game.InstallDirectory = installInfo.InstallDirectory;
                            logger.Debug($"Game {game.Name} is installed at {installInfo.InstallDirectory}");
                        }
                    }
                }

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

        private List<GameMetadata> GetOwnedGamesWithRetry()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!StoveApi.GetIsUserLoggedIn())
                    {
                        if (attempt == 0)
                        {
                            logger.Info($"User not logged in on attempt {attempt + 1}, retrying...");
                            System.Threading.Thread.Sleep(2000);
                            continue;
                        }
                        else
                        {
                            throw new Exception("User is not logged in.");
                        }
                    }

                    var games = StoveApi.GetOwnedGames();
                    logger.Info($"Successfully retrieved {games.Count} games on attempt {attempt + 1}");
                    return games;
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"Attempt {attempt + 1} failed to get owned games");
                    if (attempt == 2)
                    {
                        throw;
                    }
                    System.Threading.Thread.Sleep(2000);
                }
            }

            throw new Exception("Failed to get owned games after 3 attempts");
        }

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new StoveInstallController(args.Game, this);
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new StoveUninstallController(args.Game, this);
        }

        public override IEnumerable<PlayController> GetPlayActions(GetPlayActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            var controller = new StovePlayController(args.Game, this);
            activeControllers[args.Game.Id] = controller;
            yield return controller;
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                if (args.Game.PluginId == Id && args.ManuallyStopped)
                {
                    if (activeControllers.TryGetValue(args.Game.Id, out var controller))
                    {
                        controller.StopGame();
                        activeControllers.Remove(args.Game.Id);
                    }
                }
                else if (activeControllers.ContainsKey(args.Game.Id))
                {
                    activeControllers.Remove(args.Game.Id);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error handling game stopped event");
            }
        }

        public StoveGameInstallInfo GetGameInstallInfo(string gameName)
        {
            return StoveRegistryHelper.GetGameInstallInfo(gameName);
        }

        public override LibraryMetadataProvider GetMetadataDownloader() =>
            new StoveMetadataProvider(this, settingsVm.Settings);

        public override ISettings GetSettings(bool firstRunSettings) => settingsVm;
        public override UserControl GetSettingsView(bool firstRunSettings) =>
            new StoveLibrarySettingsView(settingsVm.Settings);

        public override LibraryClient Client => new StoveClient();

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    try
                    {
                        gameMonitor?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error disposing game monitor");
                    }

                    try
                    {
                        StoveApi?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error disposing StoveApi");
                    }

                    foreach (var controller in activeControllers.Values)
                    {
                        try
                        {
                            controller?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Error disposing controller");
                        }
                    }
                    activeControllers.Clear();
                }
                disposed = true;
            }
        }

        ~StoveLibrary()
        {
            Dispose(false);
        }
    }
}