using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

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

        private long? storedMemberNo = null;
        public long? StoredMemberNo
        {
            get => storedMemberNo;
            set => SetValue(ref storedMemberNo, value);
        }

        [DontSerialize]
        public string StoredSuatToken
        {
            get => GetDecryptedSuatToken();
            set => SetEncryptedSuatToken(value);
        }

        [DontSerialize]
        private DateTime? suatTokenExpiry = null;
        [DontSerialize]
        public DateTime? SuatTokenExpiry
        {
            get => suatTokenExpiry;
            set => SetValue(ref suatTokenExpiry, value);
        }

        private string encryptedSuatToken = null;
        public string EncryptedSuatToken
        {
            get => encryptedSuatToken;
            set => SetValue(ref encryptedSuatToken, value);
        }

        private string GetDecryptedSuatToken()
        {
            if (string.IsNullOrEmpty(EncryptedSuatToken))
            {
                return null;
            }

            try
            {
                return DecryptString(EncryptedSuatToken);
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error(ex, "Failed to decrypt SUAT token");
                return null;
            }
        }

        private void SetEncryptedSuatToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                EncryptedSuatToken = null;
            }
            else
            {
                try
                {
                    EncryptedSuatToken = EncryptString(token);
                }
                catch (Exception ex)
                {
                    LogManager.GetLogger().Error(ex, "Failed to encrypt SUAT token");
                    EncryptedSuatToken = null;
                }
            }
        }

        private static string EncryptString(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            var entropy = Encoding.UTF8.GetBytes("StoveLibrary_2a62a584-2cc3-4220-8da6-cf4ac588a439");
            var data = Encoding.UTF8.GetBytes(input);
            var encrypted = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        private static string DecryptString(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            var entropy = Encoding.UTF8.GetBytes("StoveLibrary_2a62a584-2cc3-4220-8da6-cf4ac588a439");
            var data = Convert.FromBase64String(input);
            var decrypted = ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
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