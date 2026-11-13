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
        public string GameId { get; set; }
        public string DisplayName { get; set; }
        public string ExecutablePath { get; set; }
        public string InstallDirectory { get; set; }
        public string UninstallString { get; set; }
        public string Publisher { get; set; }
    }

    public static class StoveRegistryHelper
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<StoveGameInstallInfo> GetInstalledStoveGames(bool logResults = false)
        {
            var installedGames = new List<StoveGameInstallInfo>();

            // STOVE installations may be registered in either Windows registry view.
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var rootKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var uninstallKey = rootKey.OpenSubKey(UninstallRegistryPath))
                    {
                        if (uninstallKey == null)
                        {
                            if (logResults)
                                logger.Warn("Uninstall registry key not found");
                            continue;
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
                                            GameId = subKeyName.StartsWith("Stove App ", StringComparison.OrdinalIgnoreCase) ? subKeyName.Substring("Stove App ".Length) : null,
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

            }

            return installedGames;
        }

        public static StoveGameInstallInfo GetGameInstallInfo(string gameId, string gameName)
        {
            var installedGames = GetInstalledStoveGames();
            return FindInstallInfo(installedGames, gameId, gameName);
        }

        public static StoveGameInstallInfo FindInstallInfo(IEnumerable<StoveGameInstallInfo> games, string gameId, string gameName)
        {
            // Store titles can differ from installed titles. Match by name only for entries without an ID.
            return games.FirstOrDefault(g => !string.IsNullOrEmpty(g.GameId) &&
                string.Equals(g.GameId, gameId, StringComparison.OrdinalIgnoreCase))
                ?? games.FirstOrDefault(g => string.IsNullOrEmpty(g.GameId) &&
                    string.Equals(g.DisplayName, gameName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
