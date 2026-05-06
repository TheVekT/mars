using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mars.UI.Models;
using Mars.UI.ViewModels;

namespace Mars.UI.Services;

public interface ISessionsService
{
    ObservableCollection<SessionViewModel> Sessions { get; }
    void TriggerSave();
}

public class SessionsService : ISessionsService
{
    private readonly string _sessionsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    
    private CancellationTokenSource? _debounceCts;
    public ObservableCollection<SessionViewModel> Sessions { get; }

    public SessionsService(IPathsService pathsService)
    {
        _sessionsFilePath = Path.Combine(pathsService.AppDirectory, "sessions.json");

        _jsonOptions = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        Sessions = new ObservableCollection<SessionViewModel>();
        
        LoadRawJson();
        
        Sessions.CollectionChanged += OnCollectionChanged;
    }

    private void LoadRawJson()
    {
        if (!File.Exists(_sessionsFilePath)) return;

        try
        {
            string json = File.ReadAllText(_sessionsFilePath);
            var loadedModels = JsonSerializer.Deserialize<List<SessionModel>>(json, _jsonOptions);

            if (loadedModels != null)
            {
                foreach (var model in loadedModels)
                {
                    Sessions.Add(new SessionViewModel(model, TriggerSave));
                }
            }
        }
        catch (Exception)
        {
            /* Fail silently or log */
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => TriggerSave();
    
    public async void TriggerSave()
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
            // Extract models from viewmodels for serialization
            var models = Sessions.Select(x => x.Model).ToList();
            string json = JsonSerializer.Serialize(models, _jsonOptions);
            File.WriteAllText(_sessionsFilePath, json);
        }
        catch (Exception)
        {
            /* Fail silently or log */
        }
    }
}