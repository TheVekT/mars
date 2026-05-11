using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Mars.UI.Services;

public class HttpPollingService : IDisposable
{
    private readonly IMarsApiClient _apiClient;
    private readonly string _ip;
    private readonly string _port;
    private readonly string _token;

    private class EndpointSubscription
    {
        public string Endpoint { get; }
        public int IntervalMs { get; set; }
        public List<Action<string, JsonElement?>> Callbacks { get; } = new();
        public Func<bool> IsActive { get; set; }
        public JsonElement? LastJsonPayload { get; set; }
        public string LastRawPayload { get; set; } = string.Empty;
        public bool IsRunning { get; set; }
        public CancellationTokenSource? Cts { get; set; }

        public EndpointSubscription(string endpoint, int intervalMs, Func<bool> isActive)
        {
            Endpoint = endpoint;
            IntervalMs = intervalMs;
            IsActive = isActive;
        }
    }

    private readonly ConcurrentDictionary<string, EndpointSubscription> _subscriptions = new();
    private int _generation = 0;
    
    private int _consecutiveFailures = 0;
    private readonly Action? _onConnectionLost;
    private const int MaxConsecutiveFailures = 8; // If 8 requests in a row fail, assume connection is lost

    public HttpPollingService(IMarsApiClient apiClient, string ip, string port, string token, Action? onConnectionLost = null)
    {
        _apiClient = apiClient;
        _ip = ip;
        _port = port;
        _token = token;
        _onConnectionLost = onConnectionLost;
    }

    public void Subscribe(string endpoint, int intervalMs, Func<bool> isActive, Action<string, JsonElement?> onData)
    {
        if (string.IsNullOrEmpty(endpoint) || intervalMs <= 0)
        {
            return;
        }

        var entry = _subscriptions.GetOrAdd(endpoint, ep =>
        {
            var newEntry = new EndpointSubscription(ep, intervalMs, isActive);
            StartPollingLoop(newEntry, _generation);
            return newEntry;
        });

        // Update interval if the new one is shorter
        if (intervalMs < entry.IntervalMs)
        {
            entry.IntervalMs = intervalMs;
        }

        lock (entry.Callbacks)
        {
            entry.Callbacks.Add(onData);
        }

        // Immediately trigger with last known payload if active
        if (isActive() && !string.IsNullOrEmpty(entry.LastRawPayload))
        {
            onData(entry.LastRawPayload, entry.LastJsonPayload);
        }
    }

    private void StartPollingLoop(EndpointSubscription entry, int generation)
    {
        entry.Cts = new CancellationTokenSource();
        var token = entry.Cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && generation == _generation)
            {
                if (!entry.IsActive())
                {
                    await Task.Delay(entry.IntervalMs, token);
                    continue;
                }

                entry.IsRunning = true;
                try
                {
                    var rawText = await _apiClient.FetchTextAsync(_ip, _port, _token, entry.Endpoint);
                    Interlocked.Exchange(ref _consecutiveFailures, 0); // Reset on success
                    
                    entry.LastRawPayload = rawText;
                    
                    JsonElement? json = null;
                    try
                    {
                        using var doc = JsonDocument.Parse(rawText);
                        json = doc.RootElement.Clone();
                    }
                    catch { /* Not JSON */ }
                    
                    entry.LastJsonPayload = json;

                    lock (entry.Callbacks)
                    {
                        foreach (var cb in entry.Callbacks)
                        {
                            if (entry.IsActive())
                            {
                                Dispatcher.UIThread.Post(() => cb(rawText, json));
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    int currentFails = Interlocked.Increment(ref _consecutiveFailures);
                    if (currentFails >= MaxConsecutiveFailures)
                    {
                        if (generation == _generation)
                        {
                            Dispatcher.UIThread.Post(() => _onConnectionLost?.Invoke());
                        }
                    }
                    
                    lock (entry.Callbacks)
                    {
                        foreach (var cb in entry.Callbacks)
                        {
                            if (entry.IsActive())
                            {
                                Dispatcher.UIThread.Post(() => cb(string.Empty, null));
                            }
                        }
                    }
                }
                finally
                {
                    entry.IsRunning = false;
                }

                try
                {
                    await Task.Delay(entry.IntervalMs, token);
                }
                catch (TaskCanceledException) { }
            }
        }, token);
    }

    public void StopAll()
    {
        Interlocked.Increment(ref _generation);
        foreach (var sub in _subscriptions.Values)
        {
            sub.Cts?.Cancel();
            sub.Cts?.Dispose();
        }
        _subscriptions.Clear();
    }

    public void Dispose()
    {
        StopAll();
    }
}
