using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StoveLibrary.Controllers
{
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

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = $"sgup://run/{Game.GameId}",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
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

                    var installInfo = stoveLibrary.GetGameInstallInfo(Game.Name);
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

            var installInfo = stoveLibrary.GetGameInstallInfo(Game.Name);
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
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = $"sgup://run/{Game.GameId}",
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
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

                    var currentInstallInfo = stoveLibrary.GetGameInstallInfo(Game.Name);
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
        private readonly StoveLibrary stoveLibrary;
        private Timer processWatcher;
        private DateTime gameStartTime;
        private bool gameHasStarted = false;
        private string gameDirectory;
        private Process trackedProcess;
        private string executableName;
        private List<Process> allGameProcesses = new List<Process>();

        public StovePlayController(Game game, StoveLibrary library) : base(game)
        {
            Name = "Start using STOVE";
            stoveLibrary = library;
        }

        public override void Dispose()
        {
            processWatcher?.Dispose();
            trackedProcess?.Dispose();

            foreach (var process in allGameProcesses)
            {
                try
                {
                    process?.Dispose();
                }
                catch { }
            }
            allGameProcesses.Clear();
        }

        public override void Play(PlayActionArgs args)
        {
            Dispose();

            var installInfo = stoveLibrary.GetGameInstallInfo(Game.Name);
            if (installInfo != null && File.Exists(installInfo.ExecutablePath))
            {
                gameDirectory = installInfo.InstallDirectory;
                executableName = Path.GetFileNameWithoutExtension(installInfo.ExecutablePath);

                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = installInfo.ExecutablePath,
                        WorkingDirectory = gameDirectory,
                        UseShellExecute = true
                    };

                    Process.Start(startInfo);
                    StartGameProcessMonitoring();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Failed to start game: {Game.Name}");
                    InvokeOnStopped(new GameStoppedEventArgs());
                }
            }
            else
            {
                logger.Error($"Game executable not found for: {Game.Name}");
                InvokeOnStopped(new GameStoppedEventArgs());
            }
        }

        public void StopGame()
        {
            try
            {
                logger.Info($"Manually stopping game: {Game.Name}");

                var currentGameProcesses = GetGameProcesses();

                foreach (var process in currentGameProcesses)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            logger.Info($"Killing process: {process.ProcessName} (PID: {process.Id})");
                            process.Kill();

                            if (!process.WaitForExit(3000))
                            {
                                logger.Warn($"Process {process.ProcessName} (PID: {process.Id}) did not exit within timeout");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Failed to kill process {process.ProcessName} (PID: {process.Id})");
                    }
                }

                processWatcher?.Dispose();
                processWatcher = null;

                var playTime = 0.0;
                if (gameHasStarted)
                {
                    playTime = DateTime.Now.Subtract(gameStartTime).TotalSeconds;
                }

                InvokeOnStopped(new GameStoppedEventArgs(Convert.ToUInt64(Math.Max(0, playTime))));
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error manually stopping game: {Game.Name}");
                InvokeOnStopped(new GameStoppedEventArgs());
            }
        }

        private void StartGameProcessMonitoring()
        {
            gameHasStarted = false;
            gameStartTime = DateTime.Now;
            processWatcher = new Timer(CheckForGameProcesses, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void CheckForGameProcesses(object state)
        {
            try
            {
                var gameProcesses = GetGameProcesses();

                allGameProcesses.Clear();
                allGameProcesses.AddRange(gameProcesses);

                if (!gameHasStarted && gameProcesses.Any())
                {
                    gameHasStarted = true;
                    gameStartTime = DateTime.Now;
                    trackedProcess = gameProcesses.First();

                    InvokeOnStarted(new GameStartedEventArgs() { StartedProcessId = trackedProcess.Id });
                }
                else if (gameHasStarted && !gameProcesses.Any())
                {
                    processWatcher?.Dispose();
                    var playTime = DateTime.Now.Subtract(gameStartTime).TotalSeconds;

                    InvokeOnStopped(new GameStoppedEventArgs(Convert.ToUInt64(Math.Max(0, playTime))));
                }
                else if (gameHasStarted && trackedProcess != null)
                {
                    try
                    {
                        if (trackedProcess.HasExited)
                        {
                            if (!gameProcesses.Any())
                            {
                                processWatcher?.Dispose();
                                var playTime = DateTime.Now.Subtract(gameStartTime).TotalSeconds;

                                InvokeOnStopped(new GameStoppedEventArgs(Convert.ToUInt64(Math.Max(0, playTime))));
                            }
                            else
                            {
                                trackedProcess?.Dispose();
                                trackedProcess = gameProcesses.First();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error checking tracked process status");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in game process monitoring");
                processWatcher?.Dispose();
                InvokeOnStopped(new GameStoppedEventArgs());
            }
        }

        private List<Process> GetGameProcesses()
        {
            var processes = new List<Process>();

            try
            {
                var processesByDirectory = GetProcessesInDirectory(gameDirectory);
                processes.AddRange(processesByDirectory);

                if (!processes.Any())
                {
                    var processesByName = GetProcessesByName(executableName);
                    processes.AddRange(processesByName);
                }

                if (!processes.Any())
                {
                    var gameNameParts = Game.Name.Split(' ', '-', '_');
                    foreach (var part in gameNameParts)
                    {
                        if (part.Length > 3)
                        {
                            var relatedProcesses = Process.GetProcesses()
                                .Where(p => p.ProcessName.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                                .ToList();
                            processes.AddRange(relatedProcesses);
                        }
                    }
                }

                processes = processes.GroupBy(p => p.Id).Select(g => g.First()).ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting game processes");
            }

            return processes;
        }

        private List<Process> GetProcessesInDirectory(string directory)
        {
            var processes = new List<Process>();

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
                            try
                            {
                                processPath = process.StartInfo?.FileName;
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        if (!string.IsNullOrEmpty(processPath))
                        {
                            var processDirectory = Path.GetDirectoryName(processPath);

                            if (!string.IsNullOrEmpty(processDirectory) &&
                                (string.Equals(processDirectory, directory, StringComparison.OrdinalIgnoreCase) ||
                                 processDirectory.StartsWith(directory, StringComparison.OrdinalIgnoreCase)))
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

        private List<Process> GetProcessesByName(string executableName)
        {
            var processes = new List<Process>();

            try
            {
                var exactMatches = Process.GetProcessesByName(executableName);
                processes.AddRange(exactMatches);

                if (!processes.Any())
                {
                    var allProcesses = Process.GetProcesses();
                    var partialMatches = allProcesses.Where(p =>
                        p.ProcessName.IndexOf(executableName, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    processes.AddRange(partialMatches);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error getting processes by name '{executableName}'");
            }

            return processes;
        }
    }
}