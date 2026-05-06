using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Launcher.Core.Services.IO;
using Mars.UI.Services;
using Mars.UI.Stores;
using Mars.UI.ViewModels;
using Mars.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Mars.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; }

    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        
        services.AddSingleton<IPathsService, PathsService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IIpcClientService, IpcClientService>();
        services.AddSingleton<ILogReaderService, LogReaderService>();
        services.AddSingleton<IMarketplaceService, MarketplaceService>();
        services.AddSingleton<ISessionsService, SessionsService>();
        services.AddSingleton<IMarsApiClient, MarsApiClient>();
        services.AddTransient<IRemoteControlService, RemoteControlService>();
        
        services.AddSingleton<AppStore>();
        
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ConnectionViewModel>();
        services.AddSingleton<ServerViewModel>();
        
        Services = services.BuildServiceProvider();
        
        LocalizationService.Instance = (LocalizationService)Services.GetRequiredService<ILocalizationService>();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}