using Playnite.SDK;
using Playnite.SDK.Plugins;
using Microsoft.Win32;
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
        private const string StoveLauncherRegistryPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\StoveLauncher";

        public override string Icon
        {
            get
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    if (assembly?.Location != null)
                    {
                        return Path.Combine(Path.GetDirectoryName(assembly.Location), @"icon.png");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error getting icon path");
                }
                return null;
            }
        }

        public override bool IsInstalled
        {
            get
            {
                try
                {
                    var executablePath = GetStoveExecutablePath();
                    return !string.IsNullOrEmpty(executablePath) && File.Exists(executablePath);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error checking if installed");
                    return false;
                }
            }
        }

        private static string GetStoveExecutablePath()
        {
            try
            {
                using (var rootKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var uninstallKey = rootKey.OpenSubKey(StoveLauncherRegistryPath))
                {
                    if (uninstallKey == null)
                    {
                        LogManager.GetLogger().Debug("StoveLauncher registry key not found");
                        return null;
                    }

                    var displayIcon = uninstallKey.GetValue("DisplayIcon")?.ToString();
                    if (string.IsNullOrEmpty(displayIcon))
                    {
                        LogManager.GetLogger().Debug("DisplayIcon value not found in StoveLauncher registry");
                        return null;
                    }

                    var executablePath = displayIcon;
                    if (executablePath.Contains(","))
                    {
                        executablePath = executablePath.Split(',')[0];
                    }

                    executablePath = executablePath.Trim('"');

                    LogManager.GetLogger().Debug($"Found STOVE executable path from registry: {executablePath}");
                    return executablePath;
                }
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error(ex, "Error reading STOVE path from registry");
                return null;
            }
        }

        public override void Open()
        {
            try
            {
                var stoveExePath = GetStoveExecutablePath();

                if (string.IsNullOrEmpty(stoveExePath) || !File.Exists(stoveExePath))
                {
                    logger.Warn($"STOVE client not found or not installed, opening web store instead");
                    OpenWebStore();
                    return;
                }

                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = stoveExePath,
                        UseShellExecute = true
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        if (process == null)
                        {
                            logger.Warn("Process.Start returned null, falling back to web store");
                            OpenWebStore();
                        }
                        else
                        {
                            logger.Info($"Successfully launched STOVE client: {stoveExePath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Failed to launch client at {stoveExePath}");
                    OpenWebStore();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Critical error in Open method");
                OpenWebStore();
            }
        }

        private void OpenWebStore()
        {
            try
            {
                ProcessStartInfo webStartInfo = new ProcessStartInfo
                {
                    FileName = "https://store.onstove.com/en",
                    UseShellExecute = true
                };
                Process.Start(webStartInfo);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to open web store as fallback");
            }
        }

        public override void Shutdown()
        {
            try
            {
                Process[] stoveProcesses = null;

                try
                {
                    stoveProcesses = Process.GetProcessesByName("STOVE");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error getting STOVE processes");
                    return;
                }

                if (stoveProcesses == null || stoveProcesses.Length == 0)
                {
                    return;
                }

                foreach (var process in stoveProcesses)
                {
                    if (process == null)
                        continue;

                    try
                    {
                        if (!process.HasExited)
                        {
                            try
                            {
                                process.CloseMainWindow();
                                if (!process.WaitForExit(5000))
                                {
                                    if (!process.HasExited)
                                    {
                                        process.Kill();
                                    }
                                }
                            }
                            catch (InvalidOperationException)
                            {
                                // Process may have already exited
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Failed to close process {process.Id}");
                    }
                    finally
                    {
                        try
                        {
                            process.Dispose();
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Error disposing process");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during client shutdown");
            }
        }
    }
}