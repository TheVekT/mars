using Avalonia.Input;
using System.Collections.Generic;

namespace Mars.UI.Services;

/// <summary>
/// Maps Avalonia <see cref="Key"/> values to Windows PS/2 Set 1 hardware scan codes.
/// These are the same codes that the Windows LL keyboard hook produces.
/// The Linux server translates them to evdev keycodes via its own mapping table.
/// </summary>
public static class KeyScancodeMap
{
    /// <summary>
    /// Returns the Windows hardware scan code for the given key. Returns 0 if unknown.
    /// </summary>
    public static int GetScanCode(Key key) => Map.TryGetValue(key, out var sc) ? sc : 0;

    private static readonly Dictionary<Key, int> Map = new()
    {
        // Row 0: Escape, Function Keys
        { Key.Escape,      0x01 },
        { Key.F1,          0x3B },
        { Key.F2,          0x3C },
        { Key.F3,          0x3D },
        { Key.F4,          0x3E },
        { Key.F5,          0x3F },
        { Key.F6,          0x40 },
        { Key.F7,          0x41 },
        { Key.F8,          0x42 },
        { Key.F9,          0x43 },
        { Key.F10,         0x44 },
        { Key.F11,         0x57 },
        { Key.F12,         0x58 },
        { Key.PrintScreen, 0xE037 },
        { Key.Scroll,      0x46 },
        { Key.Pause,       0x45 },

        // Row 1: Number row
        { Key.OemTilde,    0x29 }, // ` ~
        { Key.D1,          0x02 },
        { Key.D2,          0x03 },
        { Key.D3,          0x04 },
        { Key.D4,          0x05 },
        { Key.D5,          0x06 },
        { Key.D6,          0x07 },
        { Key.D7,          0x08 },
        { Key.D8,          0x09 },
        { Key.D9,          0x0A },
        { Key.D0,          0x0B },
        { Key.OemMinus,    0x0C }, // - _
        { Key.OemPlus,     0x0D }, // = +
        { Key.Back,        0x0E }, // Backspace

        // Row 2: QWERTY
        { Key.Tab,         0x0F },
        { Key.Q,           0x10 },
        { Key.W,           0x11 },
        { Key.E,           0x12 },
        { Key.R,           0x13 },
        { Key.T,           0x14 },
        { Key.Y,           0x15 },
        { Key.U,           0x16 },
        { Key.I,           0x17 },
        { Key.O,           0x18 },
        { Key.P,           0x19 },
        { Key.OemOpenBrackets, 0x1A }, // [ {
        { Key.OemCloseBrackets, 0x1B }, // ] }
        { Key.OemPipe,     0x2B }, // \ |

        // Row 3: ASDF
        { Key.CapsLock,    0x3A },
        { Key.A,           0x1E },
        { Key.S,           0x1F },
        { Key.D,           0x20 },
        { Key.F,           0x21 },
        { Key.G,           0x22 },
        { Key.H,           0x23 },
        { Key.J,           0x24 },
        { Key.K,           0x25 },
        { Key.L,           0x26 },
        { Key.OemSemicolon, 0x27 }, // ; :
        { Key.OemQuotes,   0x28 }, // ' "
        { Key.Enter,       0x1C },

        // Row 4: ZXCV
        { Key.LeftShift,   0x2A },
        { Key.Z,           0x2C },
        { Key.X,           0x2D },
        { Key.C,           0x2E },
        { Key.V,           0x2F },
        { Key.B,           0x30 },
        { Key.N,           0x31 },
        { Key.M,           0x32 },
        { Key.OemComma,    0x33 }, // , <
        { Key.OemPeriod,   0x34 }, // . >
        { Key.OemQuestion, 0x35 }, // / ?
        { Key.RightShift,  0x36 },

        // Row 5: Bottom row
        { Key.LeftCtrl,    0x1D },
        { Key.LWin,        0xE05B },
        { Key.LeftAlt,     0x38 },
        { Key.Space,       0x39 },
        { Key.RightAlt,    0xE038 },
        { Key.RWin,        0xE05C },
        { Key.Apps,        0xE05D },
        { Key.RightCtrl,   0xE01D },

        // Navigation cluster (extended)
        { Key.Insert,      0xE052 },
        { Key.Delete,      0xE053 },
        { Key.Home,        0xE047 },
        { Key.End,         0xE04F },
        { Key.PageUp,      0xE049 },
        { Key.PageDown,    0xE051 },

        // Arrow keys (extended)
        { Key.Up,          0xE048 },
        { Key.Down,        0xE050 },
        { Key.Left,        0xE04B },
        { Key.Right,       0xE04D },

        // Numpad
        { Key.NumLock,     0x45 },
        { Key.Divide,      0xE035 }, // Numpad /
        { Key.Multiply,    0x37 },   // Numpad *
        { Key.Subtract,    0x4A },   // Numpad -
        { Key.Add,         0x4E },   // Numpad +
        { Key.NumPad0,     0x52 },
        { Key.NumPad1,     0x4F },
        { Key.NumPad2,     0x50 },
        { Key.NumPad3,     0x51 },
        { Key.NumPad4,     0x4B },
        { Key.NumPad5,     0x4C },
        { Key.NumPad6,     0x4D },
        { Key.NumPad7,     0x47 },
        { Key.NumPad8,     0x48 },
        { Key.NumPad9,     0x49 },
        { Key.Decimal,     0x53 },   // Numpad .
    };
}
