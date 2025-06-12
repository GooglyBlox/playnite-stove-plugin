using Playnite.SDK;
using Playnite.SDK.Data;
using System.Collections.Generic;

namespace StoveLibrary
{
    public class StoveLibrarySettings : ObservableObject
    {
        private bool connectAccount = false;
        public bool ConnectAccount
        {
            get => connectAccount;
            set => SetValue(ref connectAccount, value);
        }

        private bool importInstalledGames = true;
        public bool ImportInstalledGames
        {
            get => importInstalledGames;
            set => SetValue(ref importInstalledGames, value);
        }

        private bool importUninstalledGames = false;
        public bool ImportUninstalledGames
        {
            get => importUninstalledGames;
            set => SetValue(ref importUninstalledGames, value);
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
        private StoveLibrary Plugin { get; }
        private StoveLibrarySettings EditingClone { get; set; }

        private StoveLibrarySettings settings;
        public StoveLibrarySettings Settings
        {
            get => settings;
            set => SetValue(ref settings, value);
        }

        public bool IsUserLoggedIn
        {
            get
            {
                try
                {
                    return StoveLibrary.StoveApi?.GetIsUserLoggedIn() ?? false;
                }
                catch
                {
                    return false;
                }
            }
        }

        public RelayCommand LoginCommand { get; }

        public StoveLibrarySettingsViewModel(StoveLibrary plugin)
        {
            Plugin = plugin;
            var savedSettings = plugin.LoadPluginSettings<StoveLibrarySettings>();
            if (savedSettings != null)
            {
                Settings = savedSettings;
            }
            else
            {
                Settings = new StoveLibrarySettings();
            }

            LoginCommand = new RelayCommand(() =>
            {
                Login();
            });
        }

        private void Login()
        {
            try
            {
                StoveLibrary.StoveApi?.Login();
                OnPropertyChanged(nameof(IsUserLoggedIn));
            }
            catch (System.Exception ex)
            {
                LogManager.GetLogger().Error(ex, "Failed to authenticate user.");
            }
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
            return true;
        }
    }
}