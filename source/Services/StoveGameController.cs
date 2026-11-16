using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using StoveLibrary.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StoveLibrary.Controllers
{
    internal static class StoveLaunchUrl
    {
        public static string Build(string stoveGameId, bool prepareAndLaunch)
        {
            if (string.IsNullOrWhiteSpace(stoveGameId))
                return null;

            var url = "sgup://run/" + Uri.EscapeDataString(stoveGameId);
            return prepareAndLaunch ? url + "?auto_action=PrepareAndLaunch" : url;
        }
    }

    public class StoveInstallController : InstallController
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private CancellationTokenSource watcherToken;
        private StoveLibrary stoveLibrary;

        public StoveInstallController(Game game, StoveLibrary library) : base(game)
        {
            stoveLibrary = library;
            Name = "Install using STOVE";
        }

        public override void Dispose()
        {
            watcherToken?.Cancel();
        }

        public override void Install(InstallActionArgs args)
        {
            Dispose();

            var url = StoveLaunchUrl.Build(Game.GameId, false);
            if (url == null)
            {
                logger.Error($"Cannot install '{Game.Name}': no STOVE game ID");
                return;
            }

            try
            {
                using (Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })) { }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to start STOVE URL for game {Game.GameId}");
            }

            StartInstallWatcher();
        }

        public async void StartInstallWatcher()
        {
            watcherToken = new CancellationTokenSource();

            await Task.Run(async () =>
            {
                while (true)
                {
                    if (watcherToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var installInfo = stoveLibrary.GetGameInstallInfo(Game.GameId, Game.Name);
                    if (installInfo != null && File.Exists(installInfo.ExecutablePath))
                    {
                        var gameInstallData = new GameInstallationData
                        {
                            InstallDirectory = Path.GetDirectoryName(installInfo.ExecutablePath)
                        };

                        InvokeOnInstalled(new GameInstalledEventArgs(gameInstallData));
                        return;
                    }

                    await Task.Delay(5000);
                }
            });
        }
    }

    public class StoveUninstallController : UninstallController
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private CancellationTokenSource watcherToken;
        private StoveLibrary stoveLibrary;

        public StoveUninstallController(Game game, StoveLibrary library) : base(game)
        {
            stoveLibrary = library;
            Name = "Uninstall using STOVE";
        }

        public override void Dispose()
        {
            watcherToken?.Cancel();
        }

        public override void Uninstall(UninstallActionArgs args)
        {
            Dispose();

            var installInfo = stoveLibrary.GetGameInstallInfo(Game.GameId, Game.Name);
            if (installInfo != null && !string.IsNullOrEmpty(installInfo.UninstallString))
            {
                try
                {
                    var uninstallString = installInfo.UninstallString.Trim();
                    string fileName;
                    string arguments = "";

                    if (uninstallString.StartsWith("\""))
                    {
                        var endQuoteIndex = uninstallString.IndexOf('"', 1);
                        if (endQuoteIndex > 0)
                        {
                            fileName = uninstallString.Substring(1, endQuoteIndex - 1);
                            if (endQuoteIndex + 2 < uninstallString.Length)
                            {
                                arguments = uninstallString.Substring(endQuoteIndex + 2);
                            }
                        }
                        else
                        {
                            fileName = uninstallString;
                        }
                    }
                    else
                    {
                        var parts = uninstallString.Split(new[] { ' ' }, 2);
                        fileName = parts[0];
                        if (parts.Length > 1)
                        {
                            arguments = parts[1];
                        }
                    }

                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = true
                    };

                    Process.Start(startInfo);
                    StartUninstallWatcher();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Failed to start uninstaller for game {Game.Name}");
                    InvokeOnUninstalled(new GameUninstalledEventArgs());
                }
            }
            else
            {
                var url = StoveLaunchUrl.Build(installInfo?.GameId, false) ?? StoveLaunchUrl.Build(Game.GameId, false);
                if (url == null)
                {
                    logger.Error($"Cannot uninstall '{Game.Name}': no STOVE game ID");
                    InvokeOnUninstalled(new GameUninstalledEventArgs());
                    return;
                }

                try
                {
                    using (Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })) { }
                    StartUninstallWatcher();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Failed to start STOVE URL for game {Game.GameId}");
                    InvokeOnUninstalled(new GameUninstalledEventArgs());
                }
            }
        }

        public async void StartUninstallWatcher()
        {
            watcherToken = new CancellationTokenSource();

            await Task.Run(async () =>
            {
                while (true)
                {
                    if (watcherToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var currentInstallInfo = stoveLibrary.GetGameInstallInfo(Game.GameId, Game.Name);
                    if (currentInstallInfo == null ||
                        string.IsNullOrEmpty(currentInstallInfo.ExecutablePath) ||
                        !File.Exists(currentInstallInfo.ExecutablePath))
                    {
                        InvokeOnUninstalled(new GameUninstalledEventArgs());
                        return;
                    }

                    await Task.Delay(2000);
                }
            });
        }
    }

    public class StovePlayController : PlayController
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const int LaunchTimeoutMinutes = 5;

        private readonly StoveLibrary stoveLibrary;
        private Timer processWatcher;
        private DateTime gameStartTime;
        private bool gameHasStarted = false;
        private string gameDirectory;
        private string executableName;
        private int trackedPid;
        private int tickRunning;
        private int stopRequested;
        private int stopped;

        public StovePlayController(Game game, StoveLibrary library) : base(game)
        {
            Name = "Start using STOVE";
            stoveLibrary = library;
        }

        public override void Dispose()
        {
            processWatcher?.Dispose();
            processWatcher = null;
        }

        public override void Play(PlayActionArgs args)
        {
            Dispose();
            Interlocked.Exchange(ref stopped, 0);
            Interlocked.Exchange(ref stopRequested, 0);

            var installInfo = stoveLibrary.GetGameInstallInfo(Game.GameId, Game.Name);
            if (installInfo != null && File.Exists(installInfo.ExecutablePath))
            {
                gameDirectory = installInfo.InstallDirectory;
                executableName = Path.GetFileNameWithoutExtension(installInfo.ExecutablePath);

                var stoveId = !string.IsNullOrEmpty(installInfo.GameId) ? installInfo.GameId : Game.GameId;
                var url = StoveLaunchUrl.Build(stoveId, true);
                if (url == null)
                {
                    logger.Error($"Cannot start '{Game.Name}': no STOVE game ID");
                    FireStopped(null);
                    return;
                }

                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = url,
                        WorkingDirectory = gameDirectory,
                        UseShellExecute = true
                    };

                    using (Process.Start(startInfo)) { }
                    StartGameProcessMonitoring();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Failed to start game: {Game.Name}");
                    FireStopped(null);
                }
            }
            else
            {
                logger.Error($"Game executable not found for: {Game.Name}");
                FireStopped(null);
            }
        }

        public void StopGame()
        {
            try
            {
                logger.Info($"Manually stopping game: {Game.Name}");

                Interlocked.Exchange(ref stopRequested, 1);
                processWatcher?.Dispose();
                processWatcher = null;

                var pids = FindGamePids(StoveProcessHelper.TakeSnapshot());

                foreach (var pid in pids)
                {
                    try
                    {
                        using (var process = Process.GetProcessById(pid))
                        {
                            if (!process.HasExited)
                            {
                                logger.Info($"Killing process: {process.ProcessName} (PID: {pid})");
                                process.Kill();

                                if (!process.WaitForExit(3000))
                                {
                                    logger.Warn($"Process {process.ProcessName} (PID: {pid}) did not exit within timeout");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Failed to kill process (PID: {pid})");
                    }
                }

                FireStopped(gameHasStarted ? DateTime.Now.Subtract(gameStartTime).TotalSeconds : (double?)null);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error manually stopping game: {Game.Name}");
                FireStopped(null);
            }
        }

        private void FireStopped(double? playTimeSeconds)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0)
                return;

            processWatcher?.Dispose();

            if (playTimeSeconds.HasValue)
                InvokeOnStopped(new GameStoppedEventArgs(Convert.ToUInt64(Math.Max(0, playTimeSeconds.Value))));
            else
                InvokeOnStopped(new GameStoppedEventArgs());
        }

        private void StartGameProcessMonitoring()
        {
            gameHasStarted = false;
            gameStartTime = DateTime.Now;
            processWatcher = new Timer(CheckForGameProcesses, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void CheckForGameProcesses(object state)
        {
            if (Volatile.Read(ref stopped) != 0 || Volatile.Read(ref stopRequested) != 0)
                return;
            if (Interlocked.CompareExchange(ref tickRunning, 1, 0) != 0)
                return;

            try
            {
                var pids = FindGamePids(StoveProcessHelper.TakeSnapshot());

                if (!gameHasStarted && pids.Count > 0)
                {
                    gameHasStarted = true;
                    gameStartTime = DateTime.Now;
                    trackedPid = pids[0];

                    InvokeOnStarted(new GameStartedEventArgs() { StartedProcessId = trackedPid });
                }
                else if (!gameHasStarted && DateTime.Now - gameStartTime > TimeSpan.FromMinutes(LaunchTimeoutMinutes))
                {
                    FireStopped(null);
                }
                else if (gameHasStarted && pids.Count == 0)
                {
                    FireStopped(DateTime.Now.Subtract(gameStartTime).TotalSeconds);
                }
                else if (gameHasStarted && !pids.Contains(trackedPid))
                {
                    trackedPid = pids[0];
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in game process monitoring");
                FireStopped(null);
            }
            finally
            {
                Interlocked.Exchange(ref tickRunning, 0);
            }
        }

        private List<int> FindGamePids(List<StoveProcessInfo> snapshot)
        {
            try
            {
                var matches = StoveProcessHelper.FindByDirectory(snapshot, gameDirectory);

                // Same-named exes with a readable path outside the install dir are other programs.
                if (matches.Count == 0 && !string.IsNullOrEmpty(executableName))
                    matches = StoveProcessHelper.FindByExactName(
                        snapshot.Where(p => string.IsNullOrEmpty(p.ImagePath)), executableName);

                return matches.Select(p => p.Pid).Distinct().ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting game processes");
                return new List<int>();
            }
        }
    }
}