using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mars.UI.Services;

public class RemoteFileSystemService : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    
    private TaskCompletionSource<JsonDocument>? _pendingResponse;
    private TaskCompletionSource<bool>? _readyForBytes;
    private TaskCompletionSource<bool>? _uploadDone;
    
    // Download state
    private string? _downloadName;
    private bool _downloadIsZip;
    private long _downloadTotalSize;
    private MemoryStream? _downloadBuffer;
    private TaskCompletionSource<byte[]>? _downloadComplete;
    
    public bool IsConnected => _ws?.State == WebSocketState.Open;
    
    public event Action<string>? OnError;

    public async Task ConnectAsync(string ip, string port, string token)
    {
        Disconnect();
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        
        string query = string.IsNullOrEmpty(token) ? "" : $"?token={token}";
        var uri = new Uri($"ws://{ip}:{port}/ws/v1/explorer/stream{query}");
        await _ws.ConnectAsync(uri, _cts.Token);
        _ = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    public async Task<(string resolvedPath, List<FileItemInfo> items)> ListDirectoryAsync(string? path, bool showHidden = false)
    {
        var response = await SendAndWaitAsync("list_dir", new { path, show_hidden = showHidden });
        var items = new List<FileItemInfo>();
        
        string resolvedPath = response.RootElement.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        
        if (response.RootElement.TryGetProperty("items", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                items.Add(new FileItemInfo
                {
                    Name = item.GetProperty("name").GetString() ?? "",
                    Path = item.GetProperty("path").GetString() ?? "",
                    IsDirectory = GetBool(item.GetProperty("is_dir")),
                    Size = item.GetProperty("size").GetInt64(),
                    ModifiedDate = DateTimeOffset.FromUnixTimeSeconds((long)item.GetProperty("mtime").GetDouble()).LocalDateTime,
                    IsHidden = GetBool(item.GetProperty("hidden"))
                });
            }
        }
        
        return (resolvedPath, items);
    }

    public async Task DeleteAsync(List<string> paths)
    {
        await SendAndWaitAsync("delete", new { paths });
    }

    public async Task RenameAsync(string path, string newName)
    {
        await SendAndWaitAsync("rename", new { path, new_name = newName });
    }

    public async Task CreateDirectoryAsync(string path)
    {
        await SendAndWaitAsync("create_dir", new { path });
    }

    public async Task TransferLocalAsync(List<string> sources, string destFolder, string mode, bool overwrite = false)
    {
        await SendAndWaitAsync("transfer_local", new { sources, dest_folder = destFolder, mode, overwrite });
    }

    public async Task UploadAsync(string localPath, string remoteDest, bool overwrite, IProgress<double>? progress, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) 
            throw new InvalidOperationException("Not connected");
        
        string name = System.IO.Path.GetFileName(localPath);
        bool isDir = Directory.Exists(localPath);
        
        // Read/zip on background thread to avoid UI freeze
        progress?.Report(0);
        byte[] data = await Task.Run(() =>
        {
            if (isDir)
            {
                using var mem = new MemoryStream();
                using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, true))
                {
                    AddDirectoryToZip(zip, localPath, "");
                }
                return mem.ToArray();
            }
            return File.ReadAllBytes(localPath);
        }, ct);

        // Send upload request
        _readyForBytes = new TaskCompletionSource<bool>();
        _uploadDone = new TaskCompletionSource<bool>();
        
        SendAction("upload_request", new { 
            dest_folder = remoteDest, 
            name, 
            is_zip = isDir, 
            overwrite,
            total_size = data.Length
        });
        
        // Wait for server ready
        var readyTask = _readyForBytes.Task;
        if (await Task.WhenAny(readyTask, Task.Delay(15000, ct)) != readyTask)
            throw new TimeoutException("Server did not respond to upload request");
        
        // Send binary data in chunks (server accumulates until total_size)
        int chunkSize = 64 * 1024; // 64KB
        int sent = 0;
        while (sent < data.Length)
        {
            ct.ThrowIfCancellationRequested();
            int size = Math.Min(chunkSize, data.Length - sent);
            await _ws.SendAsync(new ArraySegment<byte>(data, sent, size), WebSocketMessageType.Binary, true, ct);
            sent += size;
            progress?.Report((double)sent / data.Length * 100);
        }
        
        // Wait for upload confirmation
        var doneTask = _uploadDone.Task;
        if (await Task.WhenAny(doneTask, Task.Delay(30000, ct)) != doneTask)
            throw new TimeoutException("Upload confirmation timeout");
    }

    public async Task DownloadAsync(string remotePath, string localDest, IProgress<double>? progress, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) 
            throw new InvalidOperationException("Not connected");
        
        _downloadBuffer = new MemoryStream();
        _downloadComplete = new TaskCompletionSource<byte[]>();
        
        SendAction("download_request", new { path = remotePath });
        
        // Wait for download completion
        var result = await _downloadComplete.Task;
        ct.ThrowIfCancellationRequested();
        
        if (_downloadIsZip)
        {
            // Extract zip to local destination  
            string extractDir = localDest;
            if (File.Exists(extractDir)) extractDir = System.IO.Path.GetDirectoryName(extractDir)!;
            
            using var mem = new MemoryStream(result);
            using var zip = new ZipArchive(mem, ZipArchiveMode.Read);
            zip.ExtractToDirectory(extractDir, true);
        }
        else
        {
            string targetPath = System.IO.Path.Combine(localDest, _downloadName ?? "download");
            await File.WriteAllBytesAsync(targetPath, result, ct);
        }
        
        progress?.Report(100);
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _pendingResponse?.TrySetCanceled();
        _readyForBytes?.TrySetCanceled();
        _uploadDone?.TrySetCanceled();
        _downloadComplete?.TrySetCanceled();
        
        if (_ws != null)
        {
            if (_ws.State == WebSocketState.Open)
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            _ws.Dispose();
            _ws = null;
        }
    }

    private void SendAction(string action, object payload)
    {
        if (_ws?.State != WebSocketState.Open) return;
        string json = JsonSerializer.Serialize(new { action, payload });
        var bytes = Encoding.UTF8.GetBytes(json);
        _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task<JsonDocument> SendAndWaitAsync(string action, object payload, int timeoutMs = 15000)
    {
        _pendingResponse = new TaskCompletionSource<JsonDocument>();
        SendAction(action, payload);
        
        var task = _pendingResponse.Task;
        if (await Task.WhenAny(task, Task.Delay(timeoutMs)) != task)
            throw new TimeoutException($"Server did not respond to {action}");
        
        return await task;
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[128 * 1024];
        try
        {
            while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                
                if (result.MessageType == WebSocketMessageType.Close) break;
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    HandleTextMessage(json);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    _downloadBuffer?.Write(buffer, 0, result.Count);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private void HandleTextMessage(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string status = root.GetProperty("status").GetString() ?? "";

            if (status == "error")
            {
                string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "Error" : "Error";
                OnError?.Invoke(msg);
                _pendingResponse?.TrySetException(new Exception(msg));
                return;
            }

            if (status == "exists")
            {
                string msg = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                _pendingResponse?.TrySetException(new FileExistsException(msg));
                _uploadDone?.TrySetException(new FileExistsException(msg));
                return;
            }

            if (status == "ready_for_bytes")
            {
                _readyForBytes?.TrySetResult(true);
                return;
            }

            if (status == "incoming_binary")
            {
                _downloadName = root.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                _downloadIsZip = root.TryGetProperty("is_zip", out var iz) && GetBool(iz);
                _downloadTotalSize = root.TryGetProperty("total_size", out var ts) ? ts.GetInt64() : 0;
                _downloadBuffer = new MemoryStream();
                return;
            }

            if (status == "ok")
            {
                string action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
                
                if (action == "upload_success")
                {
                    _uploadDone?.TrySetResult(true);
                    return;
                }
                
                if (action == "download_complete")
                {
                    _downloadComplete?.TrySetResult(_downloadBuffer?.ToArray() ?? Array.Empty<byte>());
                    _downloadBuffer = null;
                    return;
                }
                
                _pendingResponse?.TrySetResult(doc);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Parse error: {ex.Message}");
        }
    }

    private void AddDirectoryToZip(ZipArchive zip, string sourceDir, string entryPrefix)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string entryName = string.IsNullOrEmpty(entryPrefix) 
                ? System.IO.Path.GetFileName(file) 
                : $"{entryPrefix}/{System.IO.Path.GetFileName(file)}";
            zip.CreateEntryFromFile(file, entryName);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            string dirName = System.IO.Path.GetFileName(dir);
            string prefix = string.IsNullOrEmpty(entryPrefix) ? dirName : $"{entryPrefix}/{dirName}";
            AddDirectoryToZip(zip, dir, prefix);
        }
    }

    public void Dispose() => Disconnect();

    private bool GetBool(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.True) return true;
        if (el.ValueKind == JsonValueKind.False) return false;
        if (el.ValueKind == JsonValueKind.Number) return el.GetDouble() != 0;
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString()?.ToLower();
            return s == "true" || s == "1" || s == "yes" || s == "on";
        }
        return false;
    }
}

public class FileExistsException : Exception
{
    public FileExistsException(string fileName) : base(fileName) { }
}
