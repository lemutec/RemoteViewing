#region License

/*
RemoteViewing VNC Client/Server Library for .NET
Copyright (c) 2013, 2025 James F. Bellinger <http://software.seekye.com/remoteviewing>
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

#endregion

using RemoteViewing.Vnc;
using Windows.System;

namespace RemoteViewing.WinUI;

/// <summary>
/// Helps with WinUI keyboard interaction.
/// </summary>
public static class VncKeysym
{
    private const int VK_OEM_1 = 0xBA;
    private const int VK_OEM_PLUS = 0xBB;
    private const int VK_OEM_COMMA = 0xBC;
    private const int VK_OEM_MINUS = 0xBD;
    private const int VK_OEM_PERIOD = 0xBE;
    private const int VK_OEM_2 = 0xBF;
    private const int VK_OEM_3 = 0xC0;
    private const int VK_OEM_4 = 0xDB;
    private const int VK_OEM_5 = 0xDC;
    private const int VK_OEM_6 = 0xDD;
    private const int VK_OEM_7 = 0xDE;

    /// <summary>
    /// Converts a WinUI <see cref="VirtualKey"/> to an X11 keysym.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The keysym.</returns>
    public static KeySym FromKey(VirtualKey key)
    {
        switch (key)
        {
            case VirtualKey.Back: return KeySym.Backspace;
            case VirtualKey.Tab: return KeySym.Tab;
            case VirtualKey.Enter: return KeySym.Return;
            case VirtualKey.Escape: return KeySym.Escape;
            case VirtualKey.Insert: return KeySym.Insert;
            case VirtualKey.Delete: return KeySym.Delete;
            case VirtualKey.Home: return KeySym.Home;
            case VirtualKey.End: return KeySym.End;
            case VirtualKey.PageUp: return KeySym.PageUp;
            case VirtualKey.PageDown: return KeySym.PageDown;
            case VirtualKey.Left: return KeySym.Left;
            case VirtualKey.Up: return KeySym.Up;
            case VirtualKey.Right: return KeySym.Right;
            case VirtualKey.Down: return KeySym.Down;
            case VirtualKey.F1: return KeySym.F1;
            case VirtualKey.F2: return KeySym.F2;
            case VirtualKey.F3: return KeySym.F3;
            case VirtualKey.F4: return KeySym.F4;
            case VirtualKey.F5: return KeySym.F5;
            case VirtualKey.F6: return KeySym.F6;
            case VirtualKey.F7: return KeySym.F7;
            case VirtualKey.F8: return KeySym.F8;
            case VirtualKey.F9: return KeySym.F9;
            case VirtualKey.F10: return KeySym.F10;
            case VirtualKey.F11: return KeySym.F11;
            case VirtualKey.F12: return KeySym.F12;
            case VirtualKey.F13: return KeySym.F13;
            case VirtualKey.F14: return KeySym.F14;
            case VirtualKey.F15: return KeySym.F15;
            case VirtualKey.F16: return KeySym.F16;
            case VirtualKey.F17: return KeySym.F17;
            case VirtualKey.F18: return KeySym.F18;
            case VirtualKey.F19: return KeySym.F19;
            case VirtualKey.F20: return KeySym.F20;
            case VirtualKey.F21: return KeySym.F21;
            case VirtualKey.F22: return KeySym.F22;
            case VirtualKey.F23: return KeySym.F23;
            case VirtualKey.F24: return KeySym.F24;
            case VirtualKey.Shift:
            case VirtualKey.LeftShift:
            case VirtualKey.RightShift: return KeySym.ShiftRight;
            case VirtualKey.Control:
            case VirtualKey.LeftControl:
            case VirtualKey.RightControl: return KeySym.ControlRight;
            case VirtualKey.Menu:
            case VirtualKey.LeftMenu:
            case VirtualKey.RightMenu: return KeySym.AltRight;
            case VirtualKey.Pause: return KeySym.Pause;
            case VirtualKey.Scroll: return KeySym.ScrollLock;
            case VirtualKey.Snapshot: return KeySym.SysReq;
            case VirtualKey.NumberPad0: return KeySym.NumPad0;
            case VirtualKey.NumberPad1: return KeySym.NumPad1;
            case VirtualKey.NumberPad2: return KeySym.NumPad2;
            case VirtualKey.NumberPad3: return KeySym.NumPad3;
            case VirtualKey.NumberPad4: return KeySym.NumPad4;
            case VirtualKey.NumberPad5: return KeySym.NumPad5;
            case VirtualKey.NumberPad6: return KeySym.NumPad6;
            case VirtualKey.NumberPad7: return KeySym.NumPad7;
            case VirtualKey.NumberPad8: return KeySym.NumPad8;
            case VirtualKey.NumberPad9: return KeySym.NumPad9;
            case VirtualKey.Add: return KeySym.Plus;
            case VirtualKey.Subtract: return KeySym.Minus;
            case VirtualKey.Space: return KeySym.Space;
            default:
                int virtualKey = (int)key;
                if (virtualKey == VK_OEM_4) { return KeySym.BracketLeft; }
                if (virtualKey == VK_OEM_6) { return KeySym.Bracketright; }
                if (virtualKey == VK_OEM_5) { return KeySym.Backslash; }
                if (virtualKey == VK_OEM_COMMA) { return KeySym.Comma; }
                if (virtualKey == VK_OEM_PERIOD) { return KeySym.Period; }
                if (virtualKey == VK_OEM_1) { return KeySym.Semicolon; }
                if (virtualKey == VK_OEM_2) { return KeySym.Slash; }
                if (virtualKey == VK_OEM_MINUS) { return KeySym.Minus; }
                if (virtualKey == VK_OEM_PLUS) { return KeySym.Plus; }
                if (virtualKey == VK_OEM_7) { return KeySym.Apostrophe; }
                if (virtualKey == VK_OEM_3) { return KeySym.Grave; }

                if (key >= VirtualKey.A && key <= VirtualKey.Z)
                {
                    // X11 distinguishes between uppercase and lowercase keys.
                    // Shift is sent separately, so letters stay lowercase.
                    return (KeySym)(virtualKey - (int)VirtualKey.A + (int)KeySym.a);
                }

                if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
                {
                    return (KeySym)(virtualKey - (int)VirtualKey.Number0 + (int)KeySym.D0);
                }

                return (KeySym)virtualKey;
        }
    }
}
