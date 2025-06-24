using Microsoft.Win32;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StoveLibrary
{
    public class StoveGameInstallInfo
    {
        public string DisplayName { get; set; }
        public string ExecutablePath { get; set; }
        public string InstallDirectory { get; set; }
        public string UninstallString { get; set; }
        public string Publisher { get; set; }
    }

    public static class StoveRegistryHelper
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string UninstallRegistryPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<StoveGameInstallInfo> GetInstalledStoveGames(bool logResults = false)
        {
            var installedGames = new List<StoveGameInstallInfo>();

            try
            {
                using (var rootKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var uninstallKey = rootKey.OpenSubKey(UninstallRegistryPath))
                {
                    if (uninstallKey == null)
                    {
                        if (logResults)
                            logger.Warn("Uninstall registry key not found");
                        return installedGames;
                    }

                    foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        if (!subKeyName.StartsWith("Stove", StringComparison.OrdinalIgnoreCase) ||
                            subKeyName.Equals("StoveLauncher", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        try
                        {
                            using (var gameKey = uninstallKey.OpenSubKey(subKeyName))
                            {
                                if (gameKey == null)
                                    continue;

                                var displayName = gameKey.GetValue("DisplayName")?.ToString();
                                var displayIcon = gameKey.GetValue("DisplayIcon")?.ToString();
                                var uninstallString = gameKey.GetValue("UninstallString")?.ToString();
                                var publisher = gameKey.GetValue("Publisher")?.ToString();

                                if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(displayIcon))
                                    continue;

                                var executablePath = displayIcon;
                                if (executablePath.Contains(","))
                                {
                                    executablePath = executablePath.Split(',')[0];
                                }

                                executablePath = executablePath.Trim('"');

                                if (File.Exists(executablePath))
                                {
                                    var gameInfo = new StoveGameInstallInfo
                                    {
                                        DisplayName = displayName,
                                        ExecutablePath = executablePath,
                                        InstallDirectory = Path.GetDirectoryName(executablePath),
                                        UninstallString = uninstallString,
                                        Publisher = publisher
                                    };

                                    installedGames.Add(gameInfo);
                                    if (logResults)
                                        logger.Debug($"Found installed STOVE game: {displayName} at {executablePath}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, $"Error reading registry entry: {subKeyName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error accessing registry for installed STOVE games");
            }

            return installedGames;
        }

        public static StoveGameInstallInfo GetGameInstallInfo(string gameName)
        {
            if (string.IsNullOrEmpty(gameName))
                return null;

            var installedGames = GetInstalledStoveGames();
            return installedGames.FirstOrDefault(g =>
                string.Equals(g.DisplayName, gameName, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsGameInstalled(string gameName)
        {
            return GetGameInstallInfo(gameName) != null;
        }
    }
}