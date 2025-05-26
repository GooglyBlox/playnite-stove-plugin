using Playnite.SDK;
using Playnite.SDK.Data;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StoveLibrary
{
    public class StoveLibrarySettings : ObservableObject
    {
        private string profileUrl = string.Empty;
        public string ProfileUrl
        {
            get => profileUrl;
            set => SetValue(ref profileUrl, value);
        }

        private bool importMetadata = true;
        public bool ImportMetadata
        {
            get => importMetadata;
            set => SetValue(ref importMetadata, value);
        }

        private bool importTags = true;
        public bool ImportTags
        {
            get => importTags;
            set => SetValue(ref importTags, value);
        }

        private bool allowAdultGames = false;
        public bool AllowAdultGames
        {
            get => allowAdultGames;
            set => SetValue(ref allowAdultGames, value);
        }
    }

    public class StoveLibrarySettingsViewModel : ObservableObject, ISettings
    {
        private static readonly Regex ProfileUrlRegex = new Regex(
            @"^https://profile\.onstove\.com/[a-zA-Z0-9_-]+/\d+(/.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private StoveLibrary Plugin { get; }
        private StoveLibrarySettings EditingClone { get; set; }

        private StoveLibrarySettings settings;
        public StoveLibrarySettings Settings
        {
            get => settings;
            set => SetValue(ref settings, value);
        }

        public StoveLibrarySettingsViewModel(StoveLibrary plugin)
        {
            Plugin = plugin;
            Settings = plugin.LoadPluginSettings<StoveLibrarySettings>() ?? new StoveLibrarySettings();
        }

        public void BeginEdit()
        {
            EditingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = EditingClone;
        }

        public void EndEdit()
        {
            Plugin.SavePluginSettings(Settings);
            OnPropertyChanged();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Settings.ProfileUrl))
            {
                errors.Add(Plugin.PlayniteApi.Resources.GetString("LOCStoveProfileUrlEmpty"));
            }
            else if (!ProfileUrlRegex.IsMatch(Settings.ProfileUrl.Trim()))
            {
                errors.Add(Plugin.PlayniteApi.Resources.GetString("LOCStoveProfileUrlInvalid"));
            }

            return errors.Count == 0;
        }
    }
}