using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mars.UI.Services;

/// <summary>
/// Windows Low-Level Keyboard Hook (WH_KEYBOARD_LL).
/// Intercepts ALL keyboard input at the OS level, including Win key, Alt+Tab, Ctrl+Esc.
/// On non-Windows platforms, this class is a no-op.
/// </summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    /// <summary>
    /// Called for every intercepted key. Args: (scanCode, vkCode, isKeyDown).
    /// Return true to suppress the key locally.
    /// </summary>
    public Func<int, int, bool, bool>? OnKeyIntercepted { get; set; }

    private IntPtr _hookId = IntPtr.Zero;
    private HookProc? _hookProcDelegate;

    public bool IsInstalled => _hookId != IntPtr.Zero;

    public void Install()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (_hookId != IntPtr.Zero) return;

        _hookProcDelegate = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProcDelegate,
            GetModuleHandle(curModule.ModuleName!), 0);
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _hookProcDelegate = null;
    }

    public void Dispose() => Uninstall();

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && OnKeyIntercepted != null)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int scanCode = Marshal.ReadInt32(lParam, 4);
            int flags = Marshal.ReadInt32(lParam, 8);

            bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool isKeyUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                bool isExtended = (flags & 0x01) != 0;
                int fullScanCode = isExtended ? (0xE000 | scanCode) : scanCode;

                bool suppress = OnKeyIntercepted.Invoke(fullScanCode, vkCode, isKeyDown);
                if (suppress)
                    return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // --- P/Invoke ---
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
}
