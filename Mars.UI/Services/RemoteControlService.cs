using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;

namespace Mars.UI.Services;

public class RemoteControlService : IRemoteControlService
{
    private ClientWebSocket? _videoWs;
    private ClientWebSocket? _audioWs;
    private ClientWebSocket? _clipboardWs;
    private CancellationTokenSource? _cts;
    
    private VideoFrameDecoder? _decoder;
    private AudioPcmPlayer? _audioPlayer;

    private bool _isAudioEnabled;
    private bool _isClipboardSyncEnabled;
    private string _lastClipboardText = "";
    private string? _serverAddress;
    private string? _serverQuery;

    public bool IsConnected => _videoWs?.State == WebSocketState.Open;
    
    public event Action<byte[], int, int>? OnVideoFrame;

    public async Task ConnectAsync(string ip, string port, string token)
    {
        Disconnect();
        
        _cts = new CancellationTokenSource();
        _serverAddress = $"{ip}:{port}";
        _serverQuery = string.IsNullOrEmpty(token) ? "" : $"?token={token}";
        
        // Start FFmpeg decoder for video
        _decoder = new VideoFrameDecoder();
        _decoder.OnFrame += (data, w, h) => OnVideoFrame?.Invoke(data, w, h);
        _decoder.Start();
        
        // Start ffplay for audio
        _audioPlayer = new AudioPcmPlayer();
        _audioPlayer.Start();
        _audioPlayer.SetMuted(!_isAudioEnabled);
        
        _videoWs = new ClientWebSocket();
        
        string query = _serverQuery;
        var videoUri = new Uri($"ws://{_serverAddress}/ws/v1/remote/stream{query}");

        await _videoWs.ConnectAsync(videoUri, _cts.Token);
        _ = Task.Run(() => ReceiveVideoLoop(_videoWs, _cts.Token));
        
        // Connect audio WS
        _audioWs = new ClientWebSocket();
        var audioUri = new Uri($"ws://{_serverAddress}/ws/v1/remote/audio{query}");
        await _audioWs.ConnectAsync(audioUri, _cts.Token);
        _ = Task.Run(() => ReceiveAudioLoop(_audioWs, _cts.Token));
    }

    private async Task ReceiveVideoLoop(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[65536];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                
                if (result.MessageType == WebSocketMessageType.Binary && _decoder != null)
                {
                    _decoder.Feed(buffer, 0, result.Count);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private async Task ReceiveAudioLoop(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                
                if (result.MessageType == WebSocketMessageType.Binary && _audioPlayer != null)
                {
                    _audioPlayer.Feed(buffer, 0, result.Count);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        
        _decoder?.Dispose();
        _decoder = null;
        
        _audioPlayer?.Dispose();
        _audioPlayer = null;

        CloseWebSocket(ref _videoWs);
        CloseWebSocket(ref _audioWs);
        CloseWebSocket(ref _clipboardWs);
    }

    private void CloseWebSocket(ref ClientWebSocket? ws)
    {
        if (ws != null)
        {
            if (ws.State == WebSocketState.Open)
                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            ws.Dispose();
            ws = null;
        }
    }

    public void SendCommand(object command)
    {
        if (_videoWs?.State == WebSocketState.Open)
        {
            try
            {
                string json = JsonSerializer.Serialize(command);
                var bytes = Encoding.UTF8.GetBytes(json);
                _videoWs.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }
    }

    public void SetQuality(string mode) => SendCommand(new { action = "set_quality", mode });
    public void ToggleCursor(bool state) => SendCommand(new { action = "toggle_cursor", state });
    
    public void SetAudioStream(bool state)
    {
        _isAudioEnabled = state;
        _audioPlayer?.SetMuted(!state);
    }

    public void SetClipboardSync(bool enabled)
    {
        _isClipboardSyncEnabled = enabled;
        if (enabled && _clipboardWs == null && _serverAddress != null)
            _ = Task.Run(ConnectClipboardAsync);
        else if (!enabled)
            CloseWebSocket(ref _clipboardWs);
    }

    private async Task ConnectClipboardAsync()
    {
        if (_cts == null || _serverAddress == null) return;
        try
        {
            _clipboardWs = new ClientWebSocket();
            var uri = new Uri($"ws://{_serverAddress}/ws/v1/clipboard/stream{_serverQuery}");
            await _clipboardWs.ConnectAsync(uri, _cts.Token);
            _ = Task.Run(() => ClipboardReceiveLoop(_clipboardWs, _cts.Token));
            _ = Task.Run(() => ClipboardSendLoop(_clipboardWs, _cts.Token));
        }
        catch { _clipboardWs = null; }
    }

    private async Task ClipboardReceiveLoop(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[65536];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("action", out var action) && 
                        action.GetString() == "update" &&
                        doc.RootElement.TryGetProperty("text", out var text))
                    {
                        string newText = text.GetString() ?? "";
                        _lastClipboardText = newText;
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            var clipboard = Avalonia.Application.Current?.ApplicationLifetime is 
                                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                                ? desktop.MainWindow?.Clipboard : null;
                            if (clipboard != null)
                                await clipboard.SetTextAsync(newText);
                        });
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task ClipboardSendLoop(ClientWebSocket ws, CancellationToken ct)
    {
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested && _isClipboardSyncEnabled)
            {
                await Task.Delay(1000, ct);
                string? currentText = null;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var clipboard = Avalonia.Application.Current?.ApplicationLifetime is
                        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow?.Clipboard : null;
                    if (clipboard != null)
                        currentText = await clipboard.TryGetTextAsync();
                });
                
                if (currentText != null && currentText != _lastClipboardText)
                {
                    _lastClipboardText = currentText;
                    string json = JsonSerializer.Serialize(new { action = "update", text = currentText });
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
