using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace StoveLibrary.Services
{
    public class StoveClient : LibraryClient
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string StoveExecutablePath = @"%programdata%\Smilegate\STOVE\STOVE.exe";

        public override string Icon => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"icon.png");

        public override bool IsInstalled => File.Exists(GetStoveExecutablePath());

        private static string GetStoveExecutablePath()
        {
            return Environment.ExpandEnvironmentVariables(StoveExecutablePath);
        }

        public override void Open()
        {
            var stoveExePath = GetStoveExecutablePath();

            if (!File.Exists(stoveExePath))
            {
                logger.Warn($"[STOVE] Client executable not found at {stoveExePath}, opening web store instead");
                // Fallback to opening the web store
                ProcessStartInfo webStartInfo = new ProcessStartInfo
                {
                    FileName = "https://store.onstove.com/en",
                    UseShellExecute = true
                };
                Process.Start(webStartInfo);
                return;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = stoveExePath,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                logger.Info("[STOVE] Client launched successfully");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[STOVE] Failed to launch client at {stoveExePath}");
                // Fallback to opening the web store
                ProcessStartInfo webStartInfo = new ProcessStartInfo
                {
                    FileName = "https://store.onstove.com/en",
                    UseShellExecute = true
                };
                Process.Start(webStartInfo);
            }
        }

        public override void Shutdown()
        {
            try
            {
                var stoveProcesses = Process.GetProcessesByName("STOVE").ToList();
                if (stoveProcesses.Count == 0)
                {
                    logger.Debug("[STOVE] No running STOVE processes found");
                    return;
                }

                foreach (var process in stoveProcesses)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.CloseMainWindow();
                            if (!process.WaitForExit(5000))
                            {
                                process.Kill();
                                logger.Info("[STOVE] Client process forcefully terminated");
                            }
                            else
                            {
                                logger.Info("[STOVE] Client process closed gracefully");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"[STOVE] Failed to close process {process.Id}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[STOVE] Error during client shutdown");
            }
        }
    }
}