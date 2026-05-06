using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mars.UI.ViewModels;

namespace Mars.UI.Views.Windows;

public partial class FileExplorerWindow : Window
{
    private FileExplorerViewModel? _vm;

    public FileExplorerWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        if (DataContext is not ConnectionViewModel connVm) return;

        _vm = new FileExplorerViewModel();
        DataContext = _vm;

        // Wire dialog delegates
        _vm.ShowConfirmDialog = msg => ConfirmDialog.Show(this, msg);
        _vm.ShowOverwriteDialog = fileName => OverwriteDialog.Show(this, fileName);
        _vm.ShowRenameDialog = ShowInputDialog;
        _vm.ShowNewFolderDialog = title => ShowInputDialog(title, "");

        // Wire selection sync
        LocalFileList.SelectionChanged += OnLocalSelectionChanged;
        RemoteFileList.SelectionChanged += OnRemoteSelectionChanged;

        // Wire double-click navigation
        LocalFileList.DoubleTapped += OnLocalDoubleTapped;
        RemoteFileList.DoubleTapped += OnRemoteDoubleTapped;

        // Wire context menu Open items
        if (this.FindControl<MenuItem>("LocalOpenMenuItem") is MenuItem localOpen)
            localOpen.Click += (_, _) => OpenLocalSelected();
        if (this.FindControl<MenuItem>("RemoteOpenMenuItem") is MenuItem remoteOpen)
            remoteOpen.Click += (_, _) => OpenRemoteSelected();
        
        // Wire drag-drop
        LocalFileList.AddHandler(DragDrop.DropEvent, OnLocalDrop);
        LocalFileList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        RemoteFileList.AddHandler(DragDrop.DropEvent, OnRemoteDrop);
        RemoteFileList.AddHandler(DragDrop.DragOverEvent, OnDragOver);

        // Initialize connection
        string ip = string.IsNullOrWhiteSpace(connVm.ActiveIpAddress) ? "localhost" : connVm.ActiveIpAddress;
        string port = string.IsNullOrWhiteSpace(connVm.ActivePort) ? "8000" : connVm.ActivePort;
        string token = connVm.ActiveToken ?? "";

        await _vm.InitializeAsync(ip, port, token);
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm?.Cleanup();
        base.OnClosed(e);
    }

    // ===================================================================
    // Selection sync: ListBox selection → VM IsSelected
    // ===================================================================

    private void OnLocalSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        SyncSelection(LocalFileList, _vm.LocalItems);
    }

    private void OnRemoteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        SyncSelection(RemoteFileList, _vm.RemoteItems);
    }

    private static void SyncSelection(ListBox listBox, System.Collections.ObjectModel.ObservableCollection<FileItemViewModel> items)
    {
        var selectedSet = listBox.SelectedItems?.Cast<FileItemViewModel>().ToHashSet() ?? new();
        foreach (var item in items)
            item.IsSelected = selectedSet.Contains(item);
    }

    // ===================================================================
    // Double-click navigation
    // ===================================================================

    private void OnLocalDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm == null) return;
        var item = LocalFileList.SelectedItems?.Cast<FileItemViewModel>().FirstOrDefault();
        if (item is { IsDirectory: true })
            _vm.LocalNavigateTo(item.Path);
    }

    private async void OnRemoteDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm == null) return;
        var item = RemoteFileList.SelectedItems?.Cast<FileItemViewModel>().FirstOrDefault();
        if (item is { IsDirectory: true })
            await _vm.RemoteNavigateToAsync(item.Path);
    }

    private void OpenLocalSelected()
    {
        if (_vm == null) return;
        var item = _vm.LocalItems.FirstOrDefault(x => x.IsSelected);
        if (item is { IsDirectory: true })
            _vm.LocalNavigateTo(item.Path);
    }

    private async void OpenRemoteSelected()
    {
        if (_vm == null) return;
        var item = _vm.RemoteItems.FirstOrDefault(x => x.IsSelected);
        if (item is { IsDirectory: true })
            await _vm.RemoteNavigateToAsync(item.Path);
    }

    // ===================================================================
    // Drag and Drop
    // ===================================================================

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
    }

    private void OnLocalDrop(object? sender, DragEventArgs e)
    {
        // Dropped onto local panel = download from remote
        if (_vm != null) _vm.TransferToLocalCommand.Execute(null);
    }

    private void OnRemoteDrop(object? sender, DragEventArgs e)
    {
        // Dropped onto remote panel = upload from local
        if (_vm != null) _vm.TransferToRemoteCommand.Execute(null);
    }

    // ===================================================================
    // Input dialog (for rename / new folder)
    // ===================================================================

    private async Task<string?> ShowInputDialog(string title, string defaultValue)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica },
            ExtendClientAreaToDecorationsHint = true
        };
        
        // Use theme background
        if (this.TryFindResource("SystemControlBackgroundAltHighBrush", this.ActualThemeVariant, out var bg) && bg is Avalonia.Media.IBrush brush)
            dialog.Background = brush;

        var textBox = new TextBox { Text = defaultValue, Margin = new Thickness(20, 40, 20, 10) };
        var okButton = new Button 
        { 
            Content = "OK", 
            Padding = new Thickness(20, 8), 
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, 
            Margin = new Thickness(0, 0, 20, 15),
            Classes = { "Accent" }
        };
        
        string? result = null;
        okButton.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
        textBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { result = textBox.Text; dialog.Close(); } };

        var panel = new StackPanel();
        panel.Children.Add(textBox);
        panel.Children.Add(okButton);
        dialog.Content = panel;
        
        await dialog.ShowDialog(this);
        return result;
    }
}
