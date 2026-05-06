using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mars.UI.Stores;

namespace Mars.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public AppStore AppStore { get; }
    
    public SettingsViewModel SettingsViewModel { get; }
    public ConnectionViewModel ConnectionViewModel { get; }
    public ServerViewModel ServerViewModel { get; }
    
    [ObservableProperty]
    private bool _isMenuExpanded = false;

    public bool IsConnectionPageChecked => AppStore.CurrentView == ConnectionViewModel;
    public bool IsServerPageChecked => AppStore.CurrentView == ServerViewModel;
    public bool IsSettingsPageChecked => AppStore.CurrentView == SettingsViewModel;

    public MainWindowViewModel(SettingsViewModel settingsViewModel, ConnectionViewModel connectionViewModel, ServerViewModel serverViewModel, AppStore appStore)
    {
        SettingsViewModel = settingsViewModel;
        ConnectionViewModel = connectionViewModel;
        ServerViewModel = serverViewModel;
        AppStore = appStore;
        
        AppStore.CurrentView = ConnectionViewModel;

        AppStore.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AppStore.CurrentView))
            {
                OnPropertyChanged(nameof(IsConnectionPageChecked));
                OnPropertyChanged(nameof(IsServerPageChecked));
                OnPropertyChanged(nameof(IsSettingsPageChecked));
            }
        };
    }
    
    [RelayCommand]
    private void ToggleMenu()
    {
        IsMenuExpanded = !IsMenuExpanded;
    }
    
    [RelayCommand]
    private void OpenConnectionPage() =>
        AppStore.CurrentView = ConnectionViewModel;
    
    [RelayCommand]
    private void OpenServerPage() =>
        AppStore.CurrentView = ServerViewModel;
    
    [RelayCommand]
    private void OpenSettingsPage() =>
        AppStore.CurrentView = SettingsViewModel;
    
}