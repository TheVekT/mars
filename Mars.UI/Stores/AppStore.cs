using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Models;
using Launcher.Core.Services.IO;
using Mars.UI.Models;
using Mars.UI.Services;
using Mars.UI.ViewModels;

namespace Mars.UI.Stores;

public partial class AppStore: ViewModelBase
{
    private readonly ISettingsService _settingsService;
    
    public ObservableCollection<LanguageModel> AvailableLanguages { get; } = new();

    
    private LanguageModel _selectedLanguage;
    [ObservableProperty]
    private object _currentView;
    [ObservableProperty]
    [property: SettingProperty]
    private int _selectedThemeIndex;
    [ObservableProperty]
    [property: SettingProperty]
    private bool _autoUpdateModules;
    [ObservableProperty]
    [property: SettingProperty]
    private bool _useOsCredentials;
    [ObservableProperty]
    [property: SettingProperty]
    private bool _useLastPassword;
    [ObservableProperty]
    [property: SettingProperty]
    private bool _showDeviceCursor = true;
    [ObservableProperty]
    [property: SettingProperty]
    private bool _deviceAudioSource = true;
    [ObservableProperty]
    [property: SettingProperty]
    private bool _clipboardSync = true;
    [ObservableProperty]
    [property: SettingProperty]
    private string _selectedQuality = "optimal";

    public AppStore(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        
        LocalizationService.Instance.GetAvailableLanguages().ForEach(lang => AvailableLanguages.Add(lang));
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == "en-US") ?? AvailableLanguages.FirstOrDefault();
        
        SelectedThemeIndex = 0;
        AutoUpdateModules = true;
        _settingsService.Initialize(this);
    }
    

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = value switch
            {
                1 => ThemeVariant.Light,
                2 => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }

    [SettingProperty]
    public LanguageModel SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value == null) return;
            
            var actualLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == value.Code) ?? AvailableLanguages.FirstOrDefault();

            if (_selectedLanguage != actualLanguage)
            {
                _selectedLanguage = actualLanguage;
                OnPropertyChanged(nameof(SelectedLanguage));
                LocalizationService.Instance.LoadLanguage(actualLanguage.Code);
            }
        }
    }

}