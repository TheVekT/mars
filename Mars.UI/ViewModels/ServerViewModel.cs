using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Models;
using Launcher.Core.Services.IO;
using Mars.Shared.Models;
using Mars.UI.Helpers;
using Mars.UI.Services;
using Mars.UI.Models;
using Mars.UI.Stores;

namespace Mars.UI.ViewModels;

public partial class ServerViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IIpcClientService _ipcClientService;
    private readonly ILogReaderService _logReaderService;
    private readonly IMarketplaceService _marketplaceService;
    
    public AppStore AppStore { get; }
    
    private readonly CancellationTokenSource _logCts = new();

    public Func<IEnumerable<MarketplaceModule>, Task<IEnumerable<string>>>? ShowMarketplaceDialogAsync { get; set; }
    public Func<string, Task<bool>>? ShowConfirmDialogAsync { get; set; }

    public ObservableRangeCollection<ModuleInfo> Modules { get; } = new();

    [ObservableProperty] 
    private string _consoleLogs = String.Empty;
    [ObservableProperty] 
    private bool _isLogsExpanded;

    [ObservableProperty] [property: SettingProperty]
    private string _serverIpAddress = String.Empty;
    [ObservableProperty] [property: SettingProperty]
    private string _serverPort = String.Empty;
    [ObservableProperty] 
    private string _serverPassword = String.Empty;
    [ObservableProperty] 
    private bool _isAutorunServer;

    [ObservableProperty] 
    private int _enabledModulesCount = 0;
    [ObservableProperty] 
    private int _disabledModulesCount = 0;
    [ObservableProperty] 
    private bool _isServerRunning;
    [ObservableProperty] 
    private bool _isDaemonConnected;
    [ObservableProperty] 
    private string _serverStatus = String.Empty;


    public ServerViewModel(ISettingsService settingsService, 
        IIpcClientService ipcClientService,
        ILogReaderService logReaderService, 
        IMarketplaceService marketplaceService, 
        AppStore appStore)
    {
        _settingsService = settingsService;
        _ipcClientService = ipcClientService;
        _logReaderService = logReaderService;
        _marketplaceService = marketplaceService;
        AppStore = appStore;

        _settingsService.Initialize(this);

        InitializeAsync();
    }

    public void Dispose()
    {
        _pollingCts.Cancel();
        _pollingCts.Dispose();
        _logCts.Cancel();
        _logCts.Dispose();
        _networkDebounceCts?.Cancel();
        _networkDebounceCts?.Dispose();
    }

    private async Task InitializeAsync()
    {
        var status = await _ipcClientService.GetStatusAsync();
        if (status == null)
        {
            IsDaemonConnected = false;
            return;
        }

        IsDaemonConnected = true;
        await ReloadModulesAsync();
        await GetServerNetworkInfoAsync();
        StartStatusPolling();

        var logPath = await _ipcClientService.GetLogPathAsync();
        if (!string.IsNullOrEmpty(logPath))
            _logReaderService.StartTail(logPath, lines => ConsoleLogs += lines, _logCts.Token);
        if (AppStore.AutoUpdateModules)
            await AutoUpdateModules();
    }

    private async Task ReloadModulesAsync()
    {
        try
        {
            var moduleList = await _ipcClientService.GetModulesAsync();
            Modules.ReplaceRange(moduleList);
            UpdateModuleCounts();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServerViewModel] Initialization error: {ex.Message}");
        }
    }

    private void UpdateModuleCounts()
    {
        EnabledModulesCount = 0;
        DisabledModulesCount = 0;

        foreach (var m in Modules)
        {
            if (!m.IsDisabled) EnabledModulesCount++;
            else DisabledModulesCount++;
        }
    }

    private async Task GetServerNetworkInfoAsync()
    {
        var ip = await _ipcClientService.GetHostAsync();
        ServerIpAddress = ip != "0.0.0.0" ? ip : String.Empty;
        var port = await _ipcClientService.GetPortAsync();
        ServerPort = port != "8000" ? port : String.Empty;
        var isAutoruns = await _ipcClientService.GetAutostartAsync();
        IsAutorunServer = isAutoruns;
    }

    [RelayCommand]
    private async Task DeleteModule(ModuleInfo module)
    {
        if (ShowConfirmDialogAsync != null)
        {
            var message = string.Format(LocalizationService.Instance["Dialog.Confirm.DeleteModule"], module.Name);
            var confirmed = await ShowConfirmDialogAsync(message);
            if (!confirmed) return;
        }

        await _ipcClientService.DeleteModuleAsync(module.PackageName);
        await ReloadModulesAsync();
    }

    [RelayCommand]
    private async Task EnableModule(ModuleInfo module)
    {
        module.IsDisabled = false;
        await _ipcClientService.SetModuleStateAsync(module);
        NotifyModuleChanged(module);
        UpdateModuleCounts();
    }

    [RelayCommand]
    private async Task DisableModule(ModuleInfo module)
    {
        module.IsDisabled = true;
        await _ipcClientService.SetModuleStateAsync(module);
        NotifyModuleChanged(module);
        UpdateModuleCounts();
    }

    [RelayCommand]
    private async Task StartStopServer()
    {
        if (!IsDaemonConnected) return;
        if (!IsServerRunning)
        {
            var status = await _ipcClientService.StartServerAsync();
            IsServerRunning = status?.State == "Starting..." || status?.State == "Running" || status?.State == "Error";
            ServerStatus = IsServerRunning
                ? LocalizationService.Instance["ServerPage.Status.Starting"]
                : LocalizationService.Instance["ServerPage.Status.Stopped"];
        }
        else
        {
            var status = await _ipcClientService.StopServerAsync();
            IsServerRunning = status?.State != "Stopped";
            ServerStatus = LocalizationService.Instance["ServerPage.Status.Stopped"];
        }
    }

    [RelayCommand]
    private async Task SetPassword() =>
        await _ipcClientService.SetPasswordAsync(ServerPassword);

    [RelayCommand]
    private async Task Refresh() => 
        await InitializeAsync();

    [RelayCommand]
    private async Task OpenMarketplace()
    {
        if (ShowMarketplaceDialogAsync == null) return;

        var registeredModules = await _marketplaceService.GetRegisteredModulesAsync();

        var installedModules = new List<ModuleInfo>(Modules);

        var availableUpdates = _marketplaceService.GetAvailableUpdates(installedModules, registeredModules);

        if (availableUpdates.Count == 0)
        {
            // You might want to show a message that no updates are available, 
            // but for now we just open the empty marketplace or maybe we don't.
            // Let's open it anyway so the user sees it's empty.
        }

        var packagesToInstall = await ShowMarketplaceDialogAsync(availableUpdates);

        if (packagesToInstall != null && System.Linq.Enumerable.Any(packagesToInstall))
        {
            await _ipcClientService.InstallModuleAsync(packagesToInstall);
            // Installation happens in the background via daemon, we should reload modules.
            await ReloadModulesAsync();
        }
    }

    private async Task AutoUpdateModules()
    {
        var registeredModules = await _marketplaceService.GetRegisteredModulesAsync();
        var installedModules = new List<ModuleInfo>(Modules);
        var availableUpdates = _marketplaceService.GetAvailableUpdates(installedModules, registeredModules);
        if (availableUpdates.Count == 0) return;
        var packagesToInstall = new List<string>();
        foreach (var package in availableUpdates)
            if (package.Status == MarketplaceModuleStatus.Update) packagesToInstall.Add(package.Info.PackageName);
        await _ipcClientService.InstallModuleAsync(packagesToInstall);
    }

    private void NotifyModuleChanged(ModuleInfo module)
    {
        var index = Modules.IndexOf(module);
        if (index != -1)
        {
            // Resetting the item in the collection forces UI to re-bind IsVisible
            Modules[index] = module;
        }
    }
    
    private CancellationTokenSource? _networkDebounceCts;

    partial void OnServerIpAddressChanged(string value) => DebounceNetworkUpdate();
    partial void OnServerPortChanged(string value) => DebounceNetworkUpdate();
    partial void OnIsAutorunServerChanged(bool value) => DebounceNetworkUpdate();

    private void DebounceNetworkUpdate()
    {
        _networkDebounceCts?.Cancel();
        _networkDebounceCts = new CancellationTokenSource();
        var token = _networkDebounceCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, token);
                if (token.IsCancellationRequested) return;

                var ip = string.IsNullOrWhiteSpace(ServerIpAddress) ? "0.0.0.0" : ServerIpAddress;
                var port = string.IsNullOrWhiteSpace(ServerPort) ? "8000" : ServerPort;

                await _ipcClientService.SetHostAsync(ip);
                await _ipcClientService.SetPortAsync(port);
                await _ipcClientService.SetAutostartAsync(IsAutorunServer);
                
                Console.WriteLine($"[ServerViewModel] Network info updated: {ip}:{port}; Auto start set to {IsAutorunServer}");
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServerViewModel] Error updating network info: {ex.Message}");
            }
        }, token);
    }
    
    private readonly CancellationTokenSource _pollingCts = new();
    
    private void StartStatusPolling()
    {
        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

            try {
                while (await timer.WaitForNextTickAsync(_pollingCts.Token))
                    await UpdateServerStatusAsync();
            }
            catch (OperationCanceledException){
                Console.WriteLine("[ServerViewModel] Status polling stopped.");
            }
        }, _pollingCts.Token);
    }

    private async Task UpdateServerStatusAsync()
    {
        try
        {
            var status = await _ipcClientService.GetStatusAsync();
            
            if (status == null)
            {
                IsDaemonConnected = false;
                IsServerRunning = false; 
                return;
            }
            
            IsDaemonConnected = true;
            IsServerRunning = status.State == "Running" || status.State == "Starting..." || status.State == "Error" || status.State == "Stopping...";
            ServerStatus = status.State switch
            {
                "Stopped" => LocalizationService.Instance["ServerPage.Status.Stopped"],
                "Starting..." => LocalizationService.Instance["ServerPage.Status.Starting"],
                "Stopping..." => LocalizationService.Instance["ServerPage.Status.Stopping"],
                "Running" => LocalizationService.Instance["ServerPage.Status.Running"],
                "Error" => LocalizationService.Instance["ServerPage.Status.Error"],
                _ => LocalizationService.Instance["ServerPage.Status.Unknown"]
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServerViewModel] Error polling status: {ex.Message}");
            IsDaemonConnected = false;
        }
    }
}