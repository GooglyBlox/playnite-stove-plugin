using System.Windows.Controls;

namespace StoveLibrary.Views
{
    public partial class StoveLibrarySettingsView : UserControl
    {
        private StoveLibrarySettingsViewModel ViewModel { get; }

        public StoveLibrarySettingsView(StoveLibrarySettings settings)
        {
            InitializeComponent();

            ViewModel = new StoveLibrarySettingsViewModel(
                            StoveLibrary.Instance ??
                            new StoveLibrary(Playnite.SDK.API.Instance));

            ViewModel.Settings = settings;
            DataContext        = ViewModel;
        }
    }
}
