using System;
using System.Windows.Controls;
using Playnite.SDK;

namespace StoveLibrary.Views
{
    public partial class StoveLibrarySettingsView : UserControl
    {
        private StoveLibrarySettingsViewModel ViewModel { get; }

        public StoveLibrarySettingsView(StoveLibrarySettings settings)
        {
            InitializeComponent();

            try
            {
                var instance = StoveLibrary.Instance;
                if (instance == null && API.Instance != null)
                {
                    instance = new StoveLibrary(API.Instance);
                }

                if (instance == null)
                {
                    throw new InvalidOperationException("Cannot create StoveLibrary instance - API not available");
                }

                ViewModel = new StoveLibrarySettingsViewModel(instance);

                if (settings != null)
                {
                    ViewModel.Settings = settings;
                }

                DataContext = ViewModel;
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error(ex, "Error initializing settings view");
                // Create minimal fallback
                ViewModel = null;
                DataContext = settings;
            }
        }
    }
}