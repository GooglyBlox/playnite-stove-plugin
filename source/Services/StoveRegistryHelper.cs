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
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var anyViewHasKey = false;

            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var rootKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var uninstallKey = rootKey.OpenSubKey(UninstallRegistryPath))
                    {
                        if (uninstallKey == null)
                            continue;

                        anyViewHasKey = true;

                        foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                        {
                            if (!subKeyName.StartsWith("Stove", StringComparison.OrdinalIgnoreCase) ||
                                subKeyName.Equals("StoveLauncher", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (seenKeys.Contains(subKeyName))
                                continue;

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

                                    var executablePath = ParseDisplayIconPath(displayIcon);

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
                                        seenKeys.Add(subKeyName);
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

            if (!anyViewHasKey && logResults)
                logger.Warn("Uninstall registry key not found in either registry view");

            return installedGames;
        }

        internal static string ParseDisplayIconPath(string displayIcon)
        {
            if (string.IsNullOrWhiteSpace(displayIcon))
                return displayIcon;

            var value = displayIcon.Trim();
            if (value.StartsWith("\""))
            {
                var end = value.IndexOf('"', 1);
                value = end > 0 ? value.Substring(1, end - 1) : value.Substring(1);
            }

            var comma = value.LastIndexOf(',');
            if (comma >= 0)
            {
                var tail = value.Substring(comma + 1).Trim();
                int index;
                if (tail.Length == 0 || int.TryParse(tail, out index))
                    value = value.Substring(0, comma);
            }

            return value.Trim().Trim('"');
        }

        public static StoveGameInstallInfo GetGameInstallInfo(string gameId, string gameName)
        {
            var installedGames = GetInstalledStoveGames();
            return FindInstallInfo(installedGames, gameId, gameName);
        }

        public static StoveGameInstallInfo FindInstallInfo(IEnumerable<StoveGameInstallInfo> games, string gameId, string gameName)
        {
            if (games == null)
                return null;

            var list = games as IList<StoveGameInstallInfo> ?? games.ToList();
            StoveGameInstallInfo byId = null;
            if (!string.IsNullOrEmpty(gameId))
            {
                byId = list.FirstOrDefault(g => !string.IsNullOrEmpty(g.GameId) &&
                    string.Equals(g.GameId, gameId, StringComparison.OrdinalIgnoreCase));
            }

            if (byId != null || string.IsNullOrEmpty(gameName))
                return byId;

            return list.FirstOrDefault(g => string.Equals(g.DisplayName, gameName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
