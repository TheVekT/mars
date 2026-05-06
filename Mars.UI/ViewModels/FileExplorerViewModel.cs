using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mars.UI.Services;

namespace Mars.UI.ViewModels;

public partial class FileExplorerViewModel : ObservableObject
{
    private readonly LocalFileSystemService _localFs = new();
    private readonly RemoteFileSystemService _remoteFs = new();

    // --- State ---
    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private string _remotePath = "";
    [ObservableProperty] private bool _showLocalHidden;
    [ObservableProperty] private bool _showRemoteHidden;
    [ObservableProperty] private bool _isTransferring;
    [ObservableProperty] private double _transferProgress;
    [ObservableProperty] private string _transferStatus = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isConnected;

    public ObservableCollection<FileItemViewModel> LocalItems { get; } = new();
    public ObservableCollection<FileItemViewModel> RemoteItems { get; } = new();

    private List<string> _localClipboard = new();
    private string _localClipboardMode = "copy"; // "copy" or "move"
    private List<string> _remoteClipboard = new();
    private string _remoteClipboardMode = "copy";
    
    private CancellationTokenSource? _transferCts;

    // --- Dialog delegates (set by code-behind) ---
    public Func<string, Task<bool>>? ShowConfirmDialog { get; set; }
    public Func<string, Task<OverwriteResult>>? ShowOverwriteDialog { get; set; }
    public Func<string, string, Task<string?>>? ShowRenameDialog { get; set; }
    public Func<string, Task<string?>>? ShowNewFolderDialog { get; set; }

    public async Task InitializeAsync(string ip, string port, string token)
    {
        _remoteFs.OnError += msg => ErrorMessage = msg;
        
        try
        {
            await _remoteFs.ConnectAsync(ip, port, token);
            IsConnected = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
            return;
        }

        LocalPath = _localFs.GetHomePath();
        RefreshLocal();
        await RefreshRemoteAsync();
    }

    public void Cleanup()
    {
        _transferCts?.Cancel();
        _remoteFs.Disconnect();
    }

    // ===================================================================
    // Local panel
    // ===================================================================

    [RelayCommand]
    private void RefreshLocal()
    {
        ErrorMessage = "";
        var items = _localFs.ListDirectory(LocalPath, ShowLocalHidden);
        LocalItems.Clear();
        foreach (var item in items)
        {
            LocalItems.Add(new FileItemViewModel
            {
                Name = item.Name,
                Path = item.Path,
                IsDirectory = item.IsDirectory,
                Size = item.Size,
                ModifiedDate = item.ModifiedDate,
                IsHidden = item.IsHidden
            });
        }
    }

    [RelayCommand]
    private void LocalNavigateBack()
    {
        if (string.IsNullOrEmpty(LocalPath)) return; // already at drives view
        
        string parent = _localFs.GetParentPath(LocalPath);
        if (string.IsNullOrEmpty(parent) || parent == LocalPath)
        {
            // At root — show drives list
            ShowDrives();
            return;
        }
        LocalPath = parent;
        RefreshLocal();
    }

    private void ShowDrives()
    {
        LocalPath = "";
        LocalItems.Clear();
        foreach (var drive in _localFs.GetDrives())
        {
            LocalItems.Add(new FileItemViewModel
            {
                Name = drive,
                Path = drive,
                IsDirectory = true,
                Size = 0,
                ModifiedDate = DateTime.MinValue
            });
        }
    }

    [RelayCommand]
    private void LocalToggleHidden()
    {
        ShowLocalHidden = !ShowLocalHidden;
        RefreshLocal();
    }

    public void LocalNavigateTo(string path)
    {
        if (Directory.Exists(path))
        {
            LocalPath = path;
            RefreshLocal();
        }
    }

    [RelayCommand]
    private void LocalCopy()
    {
        var selected = GetSelectedPaths(LocalItems);
        if (selected.Count == 0) return;
        _localClipboard = selected;
        _localClipboardMode = "copy";
    }

    [RelayCommand]
    private void LocalCut()
    {
        var selected = GetSelectedPaths(LocalItems);
        if (selected.Count == 0) return;
        _localClipboard = selected;
        _localClipboardMode = "move";
    }

    [RelayCommand]
    private void LocalPaste()
    {
        if (_localClipboard.Count == 0) return;
        try
        {
            if (_localClipboardMode == "move")
            {
                _localFs.Move(_localClipboard, LocalPath);
                _localClipboard.Clear();
            }
            else
            {
                _localFs.Copy(_localClipboard, LocalPath);
            }
            RefreshLocal();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task LocalDeleteAsync()
    {
        var selected = GetSelectedPaths(LocalItems);
        if (selected.Count == 0) return;

        string msg = selected.Count == 1 
            ? $"Delete '{System.IO.Path.GetFileName(selected[0])}'?" 
            : $"Delete {selected.Count} items?";

        if (ShowConfirmDialog != null && !await ShowConfirmDialog(msg)) return;

        try
        {
            _localFs.Delete(selected);
            RefreshLocal();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task LocalRenameAsync()
    {
        var selected = GetSelectedPaths(LocalItems);
        if (selected.Count != 1) return;

        string oldName = System.IO.Path.GetFileName(selected[0]);
        if (ShowRenameDialog == null) return;
        
        string? newName = await ShowRenameDialog("Rename", oldName);
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

        try
        {
            _localFs.Rename(selected[0], newName);
            RefreshLocal();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task LocalNewFolderAsync()
    {
        if (ShowNewFolderDialog == null) return;
        string? name = await ShowNewFolderDialog("New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            _localFs.CreateDirectory(System.IO.Path.Combine(LocalPath, name));
            RefreshLocal();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===================================================================
    // Remote panel
    // ===================================================================

    [RelayCommand]
    private async Task RefreshRemoteAsync()
    {
        if (!IsConnected) return;
        ErrorMessage = "";
        try
        {
            var (resolvedPath, items) = await _remoteFs.ListDirectoryAsync(
                string.IsNullOrEmpty(RemotePath) ? null : RemotePath, ShowRemoteHidden);
            
            RemotePath = resolvedPath;
            RemoteItems.Clear();
            foreach (var item in items)
            {
                RemoteItems.Add(new FileItemViewModel
                {
                    Name = item.Name,
                    Path = item.Path,
                    IsDirectory = item.IsDirectory,
                    Size = item.Size,
                    ModifiedDate = item.ModifiedDate,
                    IsHidden = item.IsHidden
                });
            }
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task RemoteNavigateBackAsync()
    {
        if (string.IsNullOrEmpty(RemotePath)) return;
        string? parent = GetRemoteParentPath(RemotePath);
        if (parent != null && parent != RemotePath)
        {
            RemotePath = parent;
            await RefreshRemoteAsync();
        }
    }

    [RelayCommand]
    private async Task RemoteToggleHiddenAsync()
    {
        ShowRemoteHidden = !ShowRemoteHidden;
        await RefreshRemoteAsync();
    }

    public async Task RemoteNavigateToAsync(string path)
    {
        RemotePath = path;
        await RefreshRemoteAsync();
    }

    [RelayCommand]
    private void RemoteCopy()
    {
        var selected = GetSelectedPaths(RemoteItems);
        if (selected.Count == 0) return;
        _remoteClipboard = selected;
        _remoteClipboardMode = "copy";
    }

    [RelayCommand]
    private void RemoteCut()
    {
        var selected = GetSelectedPaths(RemoteItems);
        if (selected.Count == 0) return;
        _remoteClipboard = selected;
        _remoteClipboardMode = "move";
    }

    [RelayCommand]
    private async Task RemotePasteAsync()
    {
        if (_remoteClipboard.Count == 0) return;
        try
        {
            await _remoteFs.TransferLocalAsync(_remoteClipboard, RemotePath, _remoteClipboardMode);
            if (_remoteClipboardMode == "move") _remoteClipboard.Clear();
            await RefreshRemoteAsync();
        }
        catch (FileExistsException ex)
        {
            if (ShowOverwriteDialog != null)
            {
                var result = await ShowOverwriteDialog(ex.Message);
                if (result == OverwriteResult.Overwrite)
                {
                    await _remoteFs.TransferLocalAsync(_remoteClipboard, RemotePath, _remoteClipboardMode, true);
                    if (_remoteClipboardMode == "move") _remoteClipboard.Clear();
                    await RefreshRemoteAsync();
                }
            }
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task RemoteDeleteAsync()
    {
        var selected = GetSelectedPaths(RemoteItems);
        if (selected.Count == 0) return;

        string msg = selected.Count == 1 
            ? $"Delete '{GetFileName(selected[0])}'?" 
            : $"Delete {selected.Count} items?";

        if (ShowConfirmDialog != null && !await ShowConfirmDialog(msg)) return;

        try
        {
            await _remoteFs.DeleteAsync(selected);
            await RefreshRemoteAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task RemoteRenameAsync()
    {
        var selected = GetSelectedPaths(RemoteItems);
        if (selected.Count != 1) return;

        string oldName = GetFileName(selected[0]);
        if (ShowRenameDialog == null) return;
        
        string? newName = await ShowRenameDialog("Rename", oldName);
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

        try
        {
            await _remoteFs.RenameAsync(selected[0], newName);
            await RefreshRemoteAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task RemoteNewFolderAsync()
    {
        if (ShowNewFolderDialog == null) return;
        string? name = await ShowNewFolderDialog("New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            string fullPath = RemotePath.Contains('\\') 
                ? $"{RemotePath}\\{name}" 
                : $"{RemotePath}/{name}";
            await _remoteFs.CreateDirectoryAsync(fullPath);
            await RefreshRemoteAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===================================================================
    // Cross-panel transfers
    // ===================================================================

    [RelayCommand]
    private async Task TransferToRemoteAsync()
    {
        var selected = GetSelectedPaths(LocalItems);
        if (selected.Count == 0 || !IsConnected) return;
        await TransferFilesAsync(selected, RemotePath, isUpload: true);
    }

    [RelayCommand]
    private async Task TransferToLocalAsync()
    {
        var selected = GetSelectedPaths(RemoteItems);
        if (selected.Count == 0) return;
        await TransferFilesAsync(selected, LocalPath, isUpload: false);
    }

    private async Task TransferFilesAsync(List<string> paths, string dest, bool isUpload)
    {
        IsTransferring = true;
        TransferProgress = 0;
        _transferCts = new CancellationTokenSource();
        var progress = new Progress<double>(p => TransferProgress = p);
        bool overwriteAll = false;

        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string src = paths[i];
                string name = GetFileName(src);
                TransferStatus = $"{(isUpload ? "↑" : "↓")} {name} ({i + 1}/{paths.Count})";
                TransferProgress = 0;

                try
                {
                    if (isUpload)
                        await _remoteFs.UploadAsync(src, dest, overwriteAll, progress, _transferCts.Token);
                    else
                        await _remoteFs.DownloadAsync(src, dest, progress, _transferCts.Token);
                }
                catch (FileExistsException ex)
                {
                    if (overwriteAll)
                    {
                        if (isUpload)
                            await _remoteFs.UploadAsync(src, dest, true, progress, _transferCts.Token);
                        continue;
                    }
                    
                    if (ShowOverwriteDialog != null)
                    {
                        var result = await ShowOverwriteDialog(ex.Message);
                        switch (result)
                        {
                            case OverwriteResult.Overwrite:
                                if (isUpload)
                                    await _remoteFs.UploadAsync(src, dest, true, progress, _transferCts.Token);
                                break;
                            case OverwriteResult.OverwriteAll:
                                overwriteAll = true;
                                if (isUpload)
                                    await _remoteFs.UploadAsync(src, dest, true, progress, _transferCts.Token);
                                break;
                            case OverwriteResult.Skip:
                                continue;
                            case OverwriteResult.Cancel:
                                return;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { TransferStatus = "Cancelled"; }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            IsTransferring = false;
            TransferStatus = "";
            TransferProgress = 0;
            RefreshLocal();
            if (IsConnected) await RefreshRemoteAsync();
        }
    }

    [RelayCommand]
    private void CancelTransfer() => _transferCts?.Cancel();

    // ===================================================================
    // Helpers
    // ===================================================================

    private List<string> GetSelectedPaths(ObservableCollection<FileItemViewModel> items)
        => items.Where(x => x.IsSelected).Select(x => x.Path).ToList();

    private string? GetRemoteParentPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string normalized = path.TrimEnd('\\', '/');
        
        // Windows root: C:
        if (normalized.Length == 2 && normalized[1] == ':') return null;
        // Linux root
        if (normalized == "/" || string.IsNullOrEmpty(normalized)) return null;
        
        int lastSlash = Math.Max(normalized.LastIndexOf('\\'), normalized.LastIndexOf('/'));
        if (lastSlash < 0) return null;
        
        string parent = normalized[..lastSlash];
        if (string.IsNullOrEmpty(parent)) return "/";
        if (parent.Length == 2 && parent[1] == ':') return parent + "\\";
        return parent;
    }

    /// <summary>Cross-platform filename extraction that doesn't use System.IO.Path (which mangles Linux paths on Windows).</summary>
    private static string GetFileName(string path)
    {
        int lastSlash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
    }
}

public enum OverwriteResult
{
    Overwrite,
    OverwriteAll,
    Skip,
    Cancel
}
