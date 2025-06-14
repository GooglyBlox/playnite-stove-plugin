using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private bool disposed = false;

        private class GameTrackingInfo
        {
            public string GameName { get; set; }
            public string GameId { get; set; }
            public DateTime StartTime { get; set; }
            public List<Process> Processes { get; set; } = new List<Process>();
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
            try
            {
                if (!settings.ConnectAccount)
                    return;

                var installedGames = StoveRegistryHelper.GetInstalledStoveGames();
                var currentlyTracked = new HashSet<string>();

                foreach (var installedGame in installedGames)
                {
                    try
                    {
                        var runningProcesses = GetGameProcesses(installedGame);
                        var gameKey = installedGame.DisplayName;

                        if (runningProcesses.Any())
                        {
                            currentlyTracked.Add(gameKey);

                            if (!trackedGames.ContainsKey(gameKey))
                            {
                                var playniteGame = FindPlayniteGame(installedGame.DisplayName);
                                if (playniteGame != null && !IsGameCurrentlyTrackedByPlaynite(playniteGame))
                                {
                                    var trackingInfo = new GameTrackingInfo
                                    {
                                        GameName = installedGame.DisplayName,
                                        GameId = playniteGame.Id.ToString(),
                                        StartTime = DateTime.Now,
                                        Processes = runningProcesses,
                                        NotifiedStarted = false
                                    };

                                    trackedGames[gameKey] = trackingInfo;
                                    logger.Info($"Started tracking externally launched game: {installedGame.DisplayName}");

                                    NotifyGameStarted(playniteGame, runningProcesses.First().Id);
                                    trackingInfo.NotifiedStarted = true;
                                }
                            }
                            else
                            {
                                trackedGames[gameKey].Processes = runningProcesses;
                            }
                        }
                        else if (trackedGames.ContainsKey(gameKey))
                        {
                            var trackingInfo = trackedGames[gameKey];
                            var playTime = DateTime.Now.Subtract(trackingInfo.StartTime).TotalSeconds;
                            
                            var playniteGame = FindPlayniteGame(trackingInfo.GameName);
                            if (playniteGame != null && trackingInfo.NotifiedStarted)
                            {
                                logger.Info($"Game stopped: {trackingInfo.GameName}, played for {playTime:F0} seconds");
                                NotifyGameStopped(playniteGame, (ulong)Math.Max(0, playTime));
                            }

                            trackedGames.Remove(gameKey);
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
                    var trackingInfo = trackedGames[key];
                    var playTime = DateTime.Now.Subtract(trackingInfo.StartTime).TotalSeconds;
                    
                    var playniteGame = FindPlayniteGame(trackingInfo.GameName);
                    if (playniteGame != null && trackingInfo.NotifiedStarted)
                    {
                        logger.Info($"Game stopped: {trackingInfo.GameName}, played for {playTime:F0} seconds");
                        NotifyGameStopped(playniteGame, (ulong)Math.Max(0, playTime));
                    }

                    trackedGames.Remove(key);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in game monitoring");
            }
        }

        private List<Process> GetGameProcesses(StoveGameInstallInfo gameInfo)
        {
            var processes = new List<Process>();

            try
            {
                var processesByDirectory = GetProcessesInDirectory(gameInfo.InstallDirectory);
                processes.AddRange(processesByDirectory);

                if (File.Exists(gameInfo.ExecutablePath))
                {
                    var executableName = Path.GetFileNameWithoutExtension(gameInfo.ExecutablePath);
                    var processesByName = Process.GetProcessesByName(executableName);
                    processes.AddRange(processesByName);
                }

                processes = processes.GroupBy(p => p.Id).Select(g => g.First()).ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error getting processes for game: {gameInfo.DisplayName}");
            }

            return processes;
        }

        private List<Process> GetProcessesInDirectory(string directory)
        {
            var processes = new List<Process>();

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return processes;

            try
            {
                var allProcesses = Process.GetProcesses();

                foreach (var process in allProcesses)
                {
                    try
                    {
                        if (process.Id <= 4)
                            continue;

                        string processPath = null;
                        try
                        {
                            processPath = process.MainModule?.FileName;
                        }
                        catch
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(processPath))
                        {
                            var processDirectory = Path.GetDirectoryName(processPath);

                            if (!string.IsNullOrEmpty(processDirectory) &&
                                (string.Equals(processDirectory, directory, StringComparison.OrdinalIgnoreCase) ||
                                 processDirectory.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                            {
                                processes.Add(process);
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting processes in directory");
            }

            return processes;
        }

        private Playnite.SDK.Models.Game FindPlayniteGame(string gameName)
        {
            try
            {
                return api.Database.Games.FirstOrDefault(g => 
                    g.PluginId == Guid.Parse("2a62a584-2cc3-4220-8da6-cf4ac588a439") &&
                    string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error finding Playnite game: {gameName}");
                return null;
            }
        }

        private bool IsGameCurrentlyTrackedByPlaynite(Playnite.SDK.Models.Game game)
        {
            try
            {
                return game.IsRunning;
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

                logger.Info($"Notified Playnite that game started: {game.Name} (PID: {processId})");
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

                logger.Info($"Notified Playnite that game stopped: {game.Name}, session time: {playTimeSeconds} seconds");
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
                        var playniteGame = FindPlayniteGame(tracking.GameName);
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