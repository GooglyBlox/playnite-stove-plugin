using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace StoveLibrary.Services
{
    public class StoveGameMonitor : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI api;
        private readonly StoveLibrarySettings settings;
        private Timer monitorTimer;
        private readonly Dictionary<string, GameTrackingInfo> trackedGames = new Dictionary<string, GameTrackingInfo>();
        private List<StoveGameInstallInfo> cachedInstalledGames = new List<StoveGameInstallInfo>();
        private DateTime lastGameListRefresh = DateTime.MinValue;
        private readonly TimeSpan gameListRefreshInterval = TimeSpan.FromMinutes(5);
        private bool disposed = false;
        private int tickRunning;

        private class GameTrackingInfo
        {
            public string GameName { get; set; }
            public string GameId { get; set; }
            public DateTime StartTime { get; set; }
            public List<int> ProcessIds { get; set; } = new List<int>();
            public bool NotifiedStarted { get; set; }
        }

        public StoveGameMonitor(IPlayniteAPI playniteApi, StoveLibrarySettings stoveSettings)
        {
            api = playniteApi;
            settings = stoveSettings;
            StartMonitoring();
        }

        private void StartMonitoring()
        {
            monitorTimer = new Timer(MonitorRunningGames, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        private void MonitorRunningGames(object state)
        {
            if (Interlocked.CompareExchange(ref tickRunning, 1, 0) != 0)
                return;

            try
            {
                if (disposed || !settings.ConnectAccount)
                    return;

                RefreshInstalledGamesIfNeeded();

                if (cachedInstalledGames.Count == 0 && trackedGames.Count == 0)
                    return;

                var snapshot = StoveProcessHelper.TakeSnapshot();
                var currentlyTracked = new HashSet<string>();

                foreach (var installedGame in cachedInstalledGames)
                {
                    try
                    {
                        var runningPids = GetGameProcessIds(snapshot, installedGame);
                        var gameKey = installedGame.GameId ?? installedGame.DisplayName;

                        if (runningPids.Count > 0)
                        {
                            currentlyTracked.Add(gameKey);

                            if (!trackedGames.ContainsKey(gameKey))
                            {
                                var playniteGame = FindPlayniteGame(installedGame.GameId, installedGame.DisplayName);
                                if (playniteGame != null && !IsGameCurrentlyTrackedByPlaynite(playniteGame))
                                {
                                    var trackingInfo = new GameTrackingInfo
                                    {
                                        GameName = installedGame.DisplayName,
                                        GameId = playniteGame.Id.ToString(),
                                        StartTime = DateTime.Now,
                                        ProcessIds = runningPids,
                                        NotifiedStarted = false
                                    };

                                    trackedGames[gameKey] = trackingInfo;

                                    NotifyGameStarted(playniteGame, runningPids[0]);
                                    trackingInfo.NotifiedStarted = true;
                                }
                            }
                            else
                            {
                                trackedGames[gameKey].ProcessIds = runningPids;
                            }
                        }
                        else if (trackedGames.ContainsKey(gameKey))
                        {
                            StopTracking(gameKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Error monitoring game: {installedGame.DisplayName}");
                    }
                }

                var keysToRemove = trackedGames.Keys.Where(k => !currentlyTracked.Contains(k)).ToList();
                foreach (var key in keysToRemove)
                {
                    StopTracking(key);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in game monitoring");
            }
            finally
            {
                Interlocked.Exchange(ref tickRunning, 0);
            }
        }

        private void StopTracking(string gameKey)
        {
            GameTrackingInfo trackingInfo;
            if (!trackedGames.TryGetValue(gameKey, out trackingInfo))
                return;

            trackedGames.Remove(gameKey);

            var playTime = DateTime.Now.Subtract(trackingInfo.StartTime).TotalSeconds;
            var playniteGame = api.Database.Games[Guid.Parse(trackingInfo.GameId)];
            if (playniteGame != null && trackingInfo.NotifiedStarted)
            {
                NotifyGameStopped(playniteGame, (ulong)Math.Max(0, playTime));
            }
        }

        private void RefreshInstalledGamesIfNeeded()
        {
            if (DateTime.Now - lastGameListRefresh > gameListRefreshInterval)
            {
                cachedInstalledGames = StoveRegistryHelper.GetInstalledStoveGames(true);
                lastGameListRefresh = DateTime.Now;
            }
        }

        private static List<int> GetGameProcessIds(List<StoveProcessInfo> snapshot, StoveGameInstallInfo gameInfo)
        {
            var pids = new List<int>();

            try
            {
                pids = StoveProcessHelper.FindByDirectory(snapshot, gameInfo.InstallDirectory)
                    .Select(p => p.Pid).Distinct().ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error getting processes for game: {gameInfo.DisplayName}");
            }

            return pids;
        }

        private Playnite.SDK.Models.Game FindPlayniteGame(string gameId, string gameName)
        {
            try
            {
                var pluginGames = api.Database.Games.Where(g => g.PluginId == StoveLibrary.PluginGuid);
                return SelectPlayniteGame(pluginGames, gameId, gameName);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error finding Playnite game: {gameName}");
                return null;
            }
        }

        internal static Playnite.SDK.Models.Game SelectPlayniteGame(IEnumerable<Playnite.SDK.Models.Game> pluginGames, string gameId, string gameName)
        {
            if (pluginGames == null)
                return null;

            var list = pluginGames as IList<Playnite.SDK.Models.Game> ?? pluginGames.ToList();

            if (!string.IsNullOrEmpty(gameId))
            {
                var byId = list.FirstOrDefault(g => !string.IsNullOrEmpty(g.GameId) &&
                    string.Equals(g.GameId, gameId, StringComparison.OrdinalIgnoreCase));
                if (byId != null)
                    return byId;
            }

            if (string.IsNullOrEmpty(gameName))
                return null;

            return list.FirstOrDefault(g => string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsGameCurrentlyTrackedByPlaynite(Playnite.SDK.Models.Game game)
        {
            try
            {
                return game.IsRunning || game.IsLaunching;
            }
            catch
            {
                return false;
            }
        }

        private void NotifyGameStarted(Playnite.SDK.Models.Game game, int processId)
        {
            try
            {
                game.IsRunning = true;
                game.LastActivity = DateTime.Now;
                api.Database.Games.Update(game);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error notifying game started: {game.Name}");
            }
        }

        private void NotifyGameStopped(Playnite.SDK.Models.Game game, ulong playTimeSeconds)
        {
            try
            {
                game.IsRunning = false;
                if (playTimeSeconds > 0)
                {
                    game.Playtime += playTimeSeconds;
                    game.LastActivity = DateTime.Now;
                }
                api.Database.Games.Update(game);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error notifying game stopped: {game.Name}");
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                monitorTimer?.Dispose();

                foreach (var tracking in trackedGames.Values)
                {
                    try
                    {
                        var playniteGame = api.Database.Games[Guid.Parse(tracking.GameId)];
                        if (playniteGame != null && tracking.NotifiedStarted)
                        {
                            var playTime = DateTime.Now.Subtract(tracking.StartTime).TotalSeconds;
                            NotifyGameStopped(playniteGame, (ulong)Math.Max(0, playTime));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error cleaning up tracked game");
                    }
                }

                trackedGames.Clear();
                disposed = true;
            }
        }
    }
}
