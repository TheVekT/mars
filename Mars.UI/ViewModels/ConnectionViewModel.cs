using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Models;
using Launcher.Core.Services.IO;
using Mars.UI.Models;
using Mars.UI.Services;
using Mars.UI.Stores;
using Mars.UI.ViewModels.HttpModules;

namespace Mars.UI.ViewModels;

public partial class ConnectionViewModel: ViewModelBase
{
    private readonly ISessionsService _sessionsService;
    private readonly IMarsApiClient _apiClient;
    
    public AppStore AppStore { get; }
    
    public Func<string, Task<string?>>? ShowPasswordDialogAsync { get; set; }
    public Func<string, string, Task>? ShowMessageDialogAsync { get; set; }
    public Func<Task>? OpenRemoteControlWindowAsync { get; set; }
    public Func<Task>? OpenFileExplorerWindowAsync { get; set; }
    
    public ObservableCollection<SessionViewModel> Sessions => _sessionsService.Sessions;
    
    [ObservableProperty]
    private string _serverIpAddress = String.Empty;
    [ObservableProperty]
    private string _serverPort = String.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int _selectedTabIndex;
    
    public string ActiveIpAddress { get; private set; } = "";
    public string ActivePort { get; private set; } = "";
    public string ActiveToken { get; private set; } = "";

    [ObservableProperty]
    private bool _isRemoteControlWindowOpen;

    [ObservableProperty]
    private bool _isAudioEnabled;

    [ObservableProperty]
    private bool _isClipboardSyncEnabled;

    [ObservableProperty]
    private bool _showCursor;

    [ObservableProperty]
    private string _selectedQuality = "optimal";

    public ConnectionViewModel(ISessionsService sessionsService, IMarsApiClient apiClient, AppStore appStore)
    {
        _sessionsService = sessionsService;
        _apiClient = apiClient;
        AppStore = appStore;
        
        IsAudioEnabled = AppStore.DeviceAudioSource;
        ShowCursor = AppStore.ShowDeviceCursor;
        IsClipboardSyncEnabled = AppStore.ClipboardSync;
        SelectedQuality = AppStore.SelectedQuality;
        
        Sessions.CollectionChanged += (s, e) => 
        {
            if (e.NewItems != null)
                foreach (SessionViewModel item in e.NewItems) item.PropertyChanged += OnSessionPropertyChanged;
            if (e.OldItems != null)
                foreach (SessionViewModel item in e.OldItems) item.PropertyChanged -= OnSessionPropertyChanged;
            RefreshSessionVisibility();
        };
        
        foreach (var session in Sessions)
        {
            session.PropertyChanged += OnSessionPropertyChanged;
        }
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.IsFavourite))
            RefreshSessionVisibility();
    }

    private void RefreshSessionVisibility()
    {
        OnPropertyChanged(nameof(HasFavouriteSessions));
        OnPropertyChanged(nameof(HasRegularSessions));
    }

    public bool HasFavouriteSessions => Sessions.Any(x => x.IsFavourite);
    public bool HasRegularSessions => Sessions.Any(x => !x.IsFavourite);

    [RelayCommand]
    private async Task ConnectAsync(SessionViewModel? sessionViewModel) {
        string ip = ServerIpAddress;
        string port = ServerPort;
        string token = ""; 

        if (string.IsNullOrWhiteSpace(ip)) ip = "localhost";
        if (string.IsNullOrWhiteSpace(port)) port = "8000";

        bool isNewSession = sessionViewModel == null;

        if (sessionViewModel == null)
        {
            sessionViewModel = Sessions.FirstOrDefault(x => x.IpAddress == ip && x.Port == port);
        }
        if (sessionViewModel == null)
        {
            var model = new SessionModel
            {
                Name = $"Unnamed",
                IpAddress = ip,
                Port = port,
                LastUsedPassword = token,
                LastConnectedTime = DateTime.Now
            };
            sessionViewModel = new SessionViewModel(model, _sessionsService.TriggerSave);
            isNewSession = true;
        }
        ip = sessionViewModel.IpAddress;
        port = sessionViewModel.Port;
        token = AppStore.UseLastPassword ? sessionViewModel.LastUsedPassword : token;

        bool success = false;
        while (!success)
        {
            try
            {
                success = await _apiClient.AuthenticateAsync(ip, port, token);
                if (success)
                {
                    sessionViewModel.LastConnectedTime = DateTime.Now;
                    if (isNewSession) Sessions.Add(sessionViewModel);
                    IsConnected = true;
                    ActiveIpAddress = ip;
                    ActivePort = port;
                    ActiveToken = token;
                    SelectedTabIndex = 1;
                    _ = LoadModulesAsync(ip, port, token);
                    break;
                }
                else 
                {
                    if (ShowPasswordDialogAsync != null)
                    {
                        string? newPassword = await ShowPasswordDialogAsync("Server requires a password:");
                        if (newPassword == null) 
                        {
                            break;
                        }
                        token = newPassword;
                        sessionViewModel.LastUsedPassword = newPassword;
                    }
                    else
                    {
                        break; 
                    }
                }
            }
            catch (Exception)
            {
                // In future: show error dialog. For now, stop loop on error.
                break;
            }
        }
    }

    [RelayCommand]
    private async Task OpenRemoteControlAsync()
    {
        if (OpenRemoteControlWindowAsync != null && !IsRemoteControlWindowOpen)
        {
            IsRemoteControlWindowOpen = true;
            await OpenRemoteControlWindowAsync();
        }
    }

    [RelayCommand]
    private async Task OpenFileExplorerAsync()
    {
        if (OpenFileExplorerWindowAsync != null)
        {
            await OpenFileExplorerWindowAsync();
        }
    }

    [ObservableProperty]
    private HttpModuleViewModel? _selectedModule;

    [ObservableProperty]
    private bool _isRemoteControlSupported = false;

    [ObservableProperty]
    private bool _isClipboardSyncSupported = false;

    [ObservableProperty]
    private bool _isAudioStreamSupported = false;

    [ObservableProperty]
    private bool _isFileExplorerSupported = false;

    public ObservableCollection<HttpModuleViewModel> Modules { get; } = new();

    private HttpPollingService? _pollingService;

    [RelayCommand]
    private void Disconnect()
    {
        IsConnected = false;
        SelectedTabIndex = 0;
        
        _pollingService?.StopAll();
        _pollingService?.Dispose();
        _pollingService = null;
        Modules.Clear();
        SelectedModule = null;
    }

    private void OnConnectionLost()
    {
        if (!IsConnected) return;
        Disconnect();
        var title = LocalizationService.Instance["ConnectionDashboard.ConnectionLost.Title"] ?? "Connection Lost";
        var message = LocalizationService.Instance["HttpModule.NetworkError"] ?? "Network error";
        ShowMessageDialogAsync?.Invoke(title, message);
    }

    private async Task LoadModulesAsync(string ip, string port, string token)
    {
        try
        {
            var schemas = await _apiClient.GetSchemaAsync(ip, port, token);
            
            try 
            {
                var wsSchemas = await _apiClient.GetWsSchemaAsync(ip, port, token);
                IsRemoteControlSupported = wsSchemas.Any(x => x.ModuleName == "Remote Control");
                IsClipboardSyncSupported = wsSchemas.Any(x => x.ModuleName == "Clipboard Sync");
                IsAudioStreamSupported = wsSchemas.Any(x => x.ModuleName == "Remote Control");
                IsFileExplorerSupported = wsSchemas.Any(x => x.ModuleName == "File Explorer");
            }
            catch 
            {
                IsRemoteControlSupported = false;
                IsClipboardSyncSupported = false;
                IsAudioStreamSupported = false;
                IsFileExplorerSupported = false;
            }
            
            _pollingService?.StopAll();
            _pollingService?.Dispose();
            _pollingService = new HttpPollingService(_apiClient, ip, port, token, OnConnectionLost);

            Modules.Clear();

            foreach (var s in schemas)
            {
                var moduleVm = new HttpModuleViewModel(s);
                foreach (var actionSchema in s.Actions)
                {
                    HttpActionViewModel? actionVm = null;
                    Func<bool> isActiveFunc = () => SelectedModule == moduleVm;

                    if (actionSchema.InteractionType == "read")
                    {
                        if (actionSchema.DataType == "dataset") actionVm = new DatasetActionViewModel(actionSchema, _pollingService, _apiClient, ip, port, token, isActiveFunc);
                        else if (actionSchema.DataType == "multiline") actionVm = new MultilineActionViewModel(actionSchema, _pollingService, _apiClient, ip, port, token, isActiveFunc);
                        else actionVm = new ScalarActionViewModel(actionSchema, _pollingService, _apiClient, ip, port, token, isActiveFunc);
                    }
                    else if (actionSchema.InteractionType == "update")
                    {
                        if (actionSchema.DataType == "boolean") actionVm = new BooleanActionViewModel(actionSchema, _pollingService, _apiClient, ip, port, token, isActiveFunc);
                        else if (actionSchema.DataType == "range") actionVm = new RangeActionViewModel(actionSchema, _pollingService, _apiClient, ip, port, token, isActiveFunc);
                    }
                    else if (actionSchema.InteractionType == "execute")
                    {
                        if (actionSchema.DataType == "parameterized") actionVm = new ExecuteFormActionViewModel(actionSchema, _pollingService, _apiClient, ip, port, token, isActiveFunc);
                        else actionVm = new ExecuteButtonActionViewModel(actionSchema, _pollingService, _apiClient, ip, port, token, isActiveFunc);
                    }

                    if (actionVm != null)
                    {
                        actionVm.Initialize();
                        moduleVm.Actions.Add(actionVm);
                    }
                }
                Modules.Add(moduleVm);
            }

            if (Modules.Any())
            {
                SelectedModule = Modules.First();
            }
        }
        catch (Exception ex)
        {
            // Log or handle schema fetch failure
        }
    }
}
