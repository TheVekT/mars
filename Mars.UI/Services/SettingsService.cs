using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Launcher.Core.Models;
using Mars.UI.Services;

namespace Launcher.Core.Services.IO;

public interface ISettingsService
{
    void Initialize(INotifyPropertyChanged store);
}

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    
    private readonly IPathsService _pathsService;
    
    private readonly HashSet<INotifyPropertyChanged> _registeredStores = new HashSet<INotifyPropertyChanged>();
    
    private Dictionary<string, Dictionary<string, JsonElement>> _cachedJsonData;
    
    private CancellationTokenSource _debounceCts;

    public SettingsService(IPathsService pathsService)
    {
        _pathsService = pathsService;
        
        _settingsFilePath = Path.Combine(_pathsService.ConfigDirectory, "settings.json");
        
        LoadRawJson();
    }

    private void LoadRawJson()
    {
        if (!File.Exists(_settingsFilePath))
        {
            _cachedJsonData = new Dictionary<string, Dictionary<string, JsonElement>>();
            return;
        }

        try
        {
            string json = File.ReadAllText(_settingsFilePath);
            _cachedJsonData = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(json) 
                              ?? new Dictionary<string, Dictionary<string, JsonElement>>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsService] Error reading global json : {ex.Message}");
            _cachedJsonData = new Dictionary<string, Dictionary<string, JsonElement>>();
        }
    }

    public void Initialize(INotifyPropertyChanged store)
    {
        if (store == null || _registeredStores.Contains(store)) return;

        _registeredStores.Add(store);
        
        InjectStoreData(store);
        
        store.PropertyChanged += OnStorePropertyChanged;
    }

    private void InjectStoreData(INotifyPropertyChanged store)
    {
        string storeName = store.GetType().Name;
        
        if (!_cachedJsonData.TryGetValue(storeName, out var storeData)) return;

        try
        {
            var properties = store.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (Attribute.IsDefined(prop, typeof(SettingPropertyAttribute)) && storeData.TryGetValue(prop.Name, out var jsonElement))
                {
                    object value = JsonSerializer.Deserialize(jsonElement.GetRawText(), prop.PropertyType);
                    if (value != null)
                    {
                        prop.SetValue(store, value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsService] Error injecting in {storeName}: {ex.Message}");
        }
    }

    private void OnStorePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is INotifyPropertyChanged store)
        {
            var prop = store.GetType().GetProperty(e.PropertyName);
            if (prop != null && Attribute.IsDefined(prop, typeof(SettingPropertyAttribute)))
            {
                TriggerSave();
            }
        }
    }

    private async void TriggerSave()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        try
        {
            await Task.Delay(500, token); 
            
            if (!token.IsCancellationRequested)
            {
                PerformSave();
            }
        }
        catch (TaskCanceledException) { }
    }

    private void PerformSave()
    {
        try
        {
            var masterDict = new Dictionary<string, Dictionary<string, object>>();
            
            foreach (var store in _registeredStores)
            {
                string storeName = store.GetType().Name;
                var storeSettings = new Dictionary<string, object>();
                var properties = store.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var prop in properties)
                {
                    if (Attribute.IsDefined(prop, typeof(SettingPropertyAttribute)))
                    {
                        storeSettings[prop.Name] = prop.GetValue(store);
                    }
                }
                
                if (storeSettings.Count > 0)
                {
                    masterDict[storeName] = storeSettings;
                }
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(masterDict, options);
            File.WriteAllText(_settingsFilePath, json);
            Console.WriteLine("[SettingsService] Global settings successfully saved!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsService] Saving error: {ex.Message}");
        }
    }
}
