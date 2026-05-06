using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Mars.UI.Services;
using Mars.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Mars.UI.Views.Windows;

public partial class RemoteControlWindow : Window
{
    private IRemoteControlService? _remoteService;
    private WriteableBitmap? _bitmap;
    private int _videoWidth, _videoHeight;
    private int _renderPending;

    private LowLevelKeyboardHook? _keyboardHook;

    public RemoteControlWindow()
    {
        InitializeComponent();

        this.Opened += RemoteControlWindow_Opened;
        this.Closed += RemoteControlWindow_Closed;
        this.Activated += OnWindowActivated;
        this.Deactivated += OnWindowDeactivated;

        this.AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        this.AddHandler(KeyUpEvent, Window_KeyUp, RoutingStrategies.Tunnel);

        InputOverlay.PointerMoved += InputOverlay_PointerMoved;
        InputOverlay.PointerPressed += InputOverlay_PointerPressed;
        InputOverlay.PointerReleased += InputOverlay_PointerReleased;
        InputOverlay.PointerWheelChanged += InputOverlay_PointerWheelChanged;
    }

    // ===================================================================
    // Lifecycle
    // ===================================================================

    private async void RemoteControlWindow_Opened(object? sender, EventArgs e)
    {
        _remoteService = App.Services.GetRequiredService<IRemoteControlService>();
        _remoteService.OnVideoFrame += OnVideoFrame;
        InstallKeyboardHook();

        if (DataContext is ConnectionViewModel vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;
            string ip = string.IsNullOrWhiteSpace(vm.ActiveIpAddress) ? "localhost" : vm.ActiveIpAddress;
            string port = string.IsNullOrWhiteSpace(vm.ActivePort) ? "8000" : vm.ActivePort;
            string token = vm.ActiveToken ?? "";

            try
            {
                await _remoteService.ConnectAsync(ip, port, token);
                StatusTextBlock.IsVisible = false;
                _remoteService.SetAudioStream(vm.IsAudioEnabled);
                _remoteService.ToggleCursor(vm.ShowCursor);
                _remoteService.SetQuality(vm.SelectedQuality);
                _remoteService.SetClipboardSync(vm.IsClipboardSyncEnabled);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Connection Failed: " + ex.Message;
            }
        }
    }

    private void RemoteControlWindow_Closed(object? sender, EventArgs e)
    {
        UninstallKeyboardHook();
        if (DataContext is ConnectionViewModel vm)
        {
            vm.IsRemoteControlWindowOpen = false;
            vm.PropertyChanged -= ViewModel_PropertyChanged;
        }
        if (_remoteService != null)
        {
            _remoteService.OnVideoFrame -= OnVideoFrame;
            _remoteService.Dispose();
        }
        _bitmap = null;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        InstallKeyboardHook();
        ReleaseAllModifiersOnServer(); // Clean slate when gaining focus
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        UninstallKeyboardHook();
        ReleaseAllModifiersOnServer(); // Prevent stuck keys when losing focus
    }

    /// <summary>
    /// Sends key-up for all modifier keys to the server.
    /// Prevents "stuck" modifiers when focus changes (e.g., after Alt+Shift layout switch on Linux).
    /// </summary>
    private void ReleaseAllModifiersOnServer()
    {
        if (_remoteService?.IsConnected != true) return;

        // Windows scan codes for all modifiers
        int[] modifierScancodes = {
            0x2A,   // LShift
            0x36,   // RShift
            0x1D,   // LCtrl
            0xE01D, // RCtrl
            0x38,   // LAlt
            0xE038, // RAlt
            0xE05B, // LWin
            0xE05C, // RWin
        };

        foreach (int sc in modifierScancodes)
            _remoteService.SendCommand(new { action = "scancode", code = sc, state = "up" });
    }

    // ===================================================================
    // Low-Level Keyboard Hook (Windows only)
    // ===================================================================

    private void InstallKeyboardHook()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (_keyboardHook is { IsInstalled: true }) return;
        _keyboardHook ??= new LowLevelKeyboardHook();
        _keyboardHook.OnKeyIntercepted = OnLowLevelKeyIntercepted;
        _keyboardHook.Install();
    }

    private void UninstallKeyboardHook() => _keyboardHook?.Uninstall();

    /// <summary>
    /// All keys are sent as hardware scancodes (like RDP/VNC).
    /// The server's keyboard layout determines what characters appear.
    /// Alt+Shift is allowed through locally for client-side layout switching
    /// (useful for UI elements in the control panel).
    /// </summary>
    private bool OnLowLevelKeyIntercepted(int scanCode, int vkCode, bool isKeyDown)
    {
        if (_remoteService?.IsConnected != true) return false;

        string state = isKeyDown ? "down" : "up";
        _remoteService.SendCommand(new { action = "scancode", code = scanCode, state });

        // Allow Alt+Shift through for local layout switching
        bool isShift = vkCode is 0x10 or 0xA0 or 0xA1;
        bool isAlt = vkCode is 0x12 or 0xA4 or 0xA5;
        bool altHeld = (LowLevelKeyboardHook.GetAsyncKeyState(0x12) & 0x8000) != 0;
        bool shiftHeld = (LowLevelKeyboardHook.GetAsyncKeyState(0x10) & 0x8000) != 0;

        if ((isShift && altHeld) || (isAlt && shiftHeld))
            return false; // Let Alt+Shift through for layout switching

        // Suppress everything else locally
        return true;
    }

    // ===================================================================
    // ViewModel property sync
    // ===================================================================

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is ConnectionViewModel vm && _remoteService != null)
        {
            switch (e.PropertyName)
            {
                case nameof(ConnectionViewModel.IsAudioEnabled):
                    _remoteService.SetAudioStream(vm.IsAudioEnabled);
                    break;
                case nameof(ConnectionViewModel.ShowCursor):
                    _remoteService.ToggleCursor(vm.ShowCursor);
                    break;
                case nameof(ConnectionViewModel.SelectedQuality):
                    _remoteService.SetQuality(vm.SelectedQuality);
                    break;
                case nameof(ConnectionViewModel.IsClipboardSyncEnabled):
                    _remoteService.SetClipboardSync(vm.IsClipboardSyncEnabled);
                    break;
            }
        }
    }

    // ===================================================================
    // Video frame rendering
    // ===================================================================

    private void OnVideoFrame(byte[] data, int w, int h)
    {
        if (Interlocked.CompareExchange(ref _renderPending, 1, 0) != 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_bitmap == null || _videoWidth != w || _videoHeight != h)
                {
                    _videoWidth = w;
                    _videoHeight = h;
                    _bitmap = new WriteableBitmap(
                        new PixelSize(w, h),
                        new Vector(96, 96),
                        PixelFormat.Bgra8888,
                        AlphaFormat.Opaque);
                    VideoImage.Source = _bitmap;
                }

                using (var fb = _bitmap.Lock())
                {
                    int rowSize = w * 4;
                    if (fb.RowBytes == rowSize)
                    {
                        Marshal.Copy(data, 0, fb.Address, data.Length);
                    }
                    else
                    {
                        for (int y = 0; y < h; y++)
                        {
                            IntPtr lineDest = IntPtr.Add(fb.Address, y * fb.RowBytes);
                            Marshal.Copy(data, y * rowSize, lineDest, rowSize);
                        }
                    }
                }
                VideoImage.InvalidateVisual();
            }
            finally
            {
                Interlocked.Exchange(ref _renderPending, 0);
            }
        }, DispatcherPriority.Render);
    }

    // ===================================================================
    // Mouse Input
    // ===================================================================

    private DateTime _lastMouseMove = DateTime.MinValue;

    private Rect GetVideoRect()
    {
        double cW = VideoContainer.Bounds.Width, cH = VideoContainer.Bounds.Height;
        if (_videoWidth == 0 || _videoHeight == 0 || cW == 0 || cH == 0)
            return new Rect(0, 0, cW, cH);

        double videoAspect = (double)_videoWidth / _videoHeight;
        double renderW, renderH;
        if (cW / cH > videoAspect) { renderH = cH; renderW = cH * videoAspect; }
        else { renderW = cW; renderH = cW / videoAspect; }
        return new Rect((cW - renderW) / 2, (cH - renderH) / 2, renderW, renderH);
    }

    private (double x, double y)? GetRelPos(PointerEventArgs e)
    {
        var pos = e.GetPosition(VideoContainer);
        var r = GetVideoRect();
        if (!r.Contains(pos)) return null;
        return (Math.Clamp((pos.X - r.X) / r.Width, 0, 1), Math.Clamp((pos.Y - r.Y) / r.Height, 0, 1));
    }

    private void InputOverlay_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_remoteService?.IsConnected != true) return;
        if ((DateTime.UtcNow - _lastMouseMove).TotalMilliseconds < 16) return;
        _lastMouseMove = DateTime.UtcNow;
        var rel = GetRelPos(e);
        if (rel != null)
            _remoteService.SendCommand(new { action = "mouse_move", x = rel.Value.x, y = rel.Value.y });
    }

    private void InputOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_remoteService?.IsConnected != true) return;
        InputOverlay.Focus();
        var rel = GetRelPos(e);
        if (rel == null) return;

        var point = e.GetCurrentPoint(VideoContainer);
        string? button = point.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => "left",
            PointerUpdateKind.RightButtonPressed => "right",
            PointerUpdateKind.MiddleButtonPressed => "middle",
            PointerUpdateKind.XButton1Pressed => "x1",
            PointerUpdateKind.XButton2Pressed => "x2",
            _ => null
        };
        if (button == null) return;

        _remoteService.SendCommand(new { action = "mouse_move", x = rel.Value.x, y = rel.Value.y });
        _remoteService.SendCommand(new { action = "mouse_click", button, state = "down" });
    }

    private void InputOverlay_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_remoteService?.IsConnected != true) return;
        string? button = e.InitialPressMouseButton switch
        {
            MouseButton.Left => "left", MouseButton.Right => "right",
            MouseButton.Middle => "middle", MouseButton.XButton1 => "x1",
            MouseButton.XButton2 => "x2", _ => null
        };
        if (button != null)
            _remoteService.SendCommand(new { action = "mouse_click", button, state = "up" });
    }

    private void InputOverlay_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_remoteService?.IsConnected != true) return;
        int clicks = (int)(e.Delta.Y * 120);
        if (clicks != 0)
            _remoteService.SendCommand(new { action = "scroll", clicks });
    }

    // ===================================================================
    // Keyboard Input (Linux/macOS fallback — no LL hook available)
    // ===================================================================

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_remoteService?.IsConnected != true) return;

        // On Windows, the LL hook handles everything
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _keyboardHook is { IsInstalled: true })
        {
            e.Handled = true;
            return;
        }

        // Non-Windows fallback: send all mapped keys as scancodes
        int sc = KeyScancodeMap.GetScanCode(e.Key);
        if (sc != 0)
            _remoteService.SendCommand(new { action = "scancode", code = sc, state = "down" });
        e.Handled = true;
    }

    private void Window_KeyUp(object? sender, KeyEventArgs e)
    {
        if (_remoteService?.IsConnected != true) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _keyboardHook is { IsInstalled: true })
        {
            e.Handled = true;
            return;
        }

        int sc = KeyScancodeMap.GetScanCode(e.Key);
        if (sc != 0)
            _remoteService.SendCommand(new { action = "scancode", code = sc, state = "up" });
        e.Handled = true;
    }
}
