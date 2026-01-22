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

using System.Windows.Input;
using RemoteViewing.Vnc;

namespace RemoteViewing.WPF;

/// <summary>
/// Helps with WPF keyboard interaction.
/// </summary>
public static class VncKeysym
{
    // See http://www.realvnc.com/docs/rfbproto.pdf for common keys.
    /// <summary>
    /// Converts WPF <see cref="Key"/> to X11 keysyms.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The keysym.</returns>
    public static KeySym FromKey(Key key)
    {
        switch (key)
        {
            case Key.Back: return KeySym.Backspace;
            case Key.Tab: return KeySym.Tab;
            case Key.Return: return KeySym.Return;
            case Key.Escape: return KeySym.Escape;
            case Key.Insert: return KeySym.Insert;
            case Key.Delete: return KeySym.Delete;
            case Key.Home: return KeySym.Home;
            case Key.End: return KeySym.End;
            case Key.PageUp: return KeySym.PageUp;
            case Key.PageDown: return KeySym.PageDown;
            case Key.Left: return KeySym.Left;
            case Key.Up: return KeySym.Up;
            case Key.Right: return KeySym.Right;
            case Key.Down: return KeySym.Down;
            case Key.F1: return KeySym.F1;
            case Key.F2: return KeySym.F2;
            case Key.F3: return KeySym.F3;
            case Key.F4: return KeySym.F4;
            case Key.F5: return KeySym.F5;
            case Key.F6: return KeySym.F6;
            case Key.F7: return KeySym.F7;
            case Key.F8: return KeySym.F8;
            case Key.F9: return KeySym.F9;
            case Key.F10: return KeySym.F10;
            case Key.F11: return KeySym.F11;
            case Key.F12: return KeySym.F12;
            case Key.F13: return KeySym.F13;
            case Key.F14: return KeySym.F14;
            case Key.F15: return KeySym.F15;
            case Key.F16: return KeySym.F16;
            case Key.F17: return KeySym.F17;
            case Key.F18: return KeySym.F18;
            case Key.F19: return KeySym.F19;
            case Key.F20: return KeySym.F20;
            case Key.F21: return KeySym.F21;
            case Key.F22: return KeySym.F22;
            case Key.F23: return KeySym.F23;
            case Key.F24: return KeySym.F24;
            case Key.LeftShift:
            case Key.RightShift: return KeySym.ShiftRight;
            case Key.LeftCtrl:
            case Key.RightCtrl: return KeySym.ControlRight;
            case Key.LeftAlt:
            case Key.RightAlt: return KeySym.AltRight;
            case Key.OemOpenBrackets: return KeySym.BracketLeft;
            case Key.OemCloseBrackets: return KeySym.Bracketright;
            case Key.OemPipe: return KeySym.Backslash;
            case Key.OemComma: return KeySym.Comma;
            case Key.OemPeriod: return KeySym.Period;
            case Key.OemSemicolon: return KeySym.Semicolon;
            case Key.OemQuestion: return KeySym.Slash;
            case Key.OemMinus:
            case Key.Subtract: return KeySym.Minus;
            case Key.OemPlus:
            case Key.Add: return KeySym.Plus;
            case Key.OemQuotes: return KeySym.Apostrophe;
            case Key.OemTilde: return KeySym.Grave;
            case Key.Pause: return KeySym.Pause;
            case Key.Scroll: return KeySym.ScrollLock;
            case Key.PrintScreen: return KeySym.SysReq;
            case Key.NumPad0: return KeySym.NumPad0;
            case Key.NumPad1: return KeySym.NumPad1;
            case Key.NumPad2: return KeySym.NumPad2;
            case Key.NumPad3: return KeySym.NumPad3;
            case Key.NumPad4: return KeySym.NumPad4;
            case Key.NumPad5: return KeySym.NumPad5;
            case Key.NumPad6: return KeySym.NumPad6;
            case Key.NumPad7: return KeySym.NumPad7;
            case Key.NumPad8: return KeySym.NumPad8;
            case Key.NumPad9: return KeySym.NumPad9;
            case Key.Space: return KeySym.Space;
            default:
                if (key >= Key.A && key <= Key.Z)
                {
                    // X11 distinguishes between uppercase and lowercase keys.
                    // We want Shift to work properly, so let's not do that.
                    return (KeySym)(key - Key.A + (int)KeySym.a);
                }
                else if (key >= Key.D0 && key <= Key.D9)
                {
                    return (KeySym)(key - Key.D0 + (int)KeySym.D0);
                }
                else
                {
                    // TODO: This is wildly incomplete.
                    return (KeySym)key;
                }
        }
    }

    /// <summary>
    /// Converts <see cref="Keys"/> enum to X11 keysyms.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The keysym.</returns>
    public static KeySym FromKeyCode(Keys key)
    {
        switch (key)
        {
            case Keys.Back: return KeySym.Backspace;
            case Keys.Tab: return KeySym.Tab;
            case Keys.Enter: return KeySym.Return;
            case Keys.Escape: return KeySym.Escape;
            case Keys.Insert: return KeySym.Insert;
            case Keys.Delete: return KeySym.Delete;
            case Keys.Home: return KeySym.Home;
            case Keys.End: return KeySym.End;
            case Keys.PageUp: return KeySym.PageUp;
            case Keys.PageDown: return KeySym.PageDown;
            case Keys.Left: return KeySym.Left;
            case Keys.Up: return KeySym.Up;
            case Keys.Right: return KeySym.Right;
            case Keys.Down: return KeySym.Down;
            case Keys.F1: return KeySym.F1;
            case Keys.F2: return KeySym.F2;
            case Keys.F3: return KeySym.F3;
            case Keys.F4: return KeySym.F4;
            case Keys.F5: return KeySym.F5;
            case Keys.F6: return KeySym.F6;
            case Keys.F7: return KeySym.F7;
            case Keys.F8: return KeySym.F8;
            case Keys.F9: return KeySym.F9;
            case Keys.F10: return KeySym.F10;
            case Keys.F11: return KeySym.F11;
            case Keys.F12: return KeySym.F12;
            case Keys.F13: return KeySym.F13;
            case Keys.F14: return KeySym.F14;
            case Keys.F15: return KeySym.F15;
            case Keys.F16: return KeySym.F16;
            case Keys.F17: return KeySym.F17;
            case Keys.F18: return KeySym.F18;
            case Keys.F19: return KeySym.F19;
            case Keys.F20: return KeySym.F20;
            case Keys.F21: return KeySym.F21;
            case Keys.F22: return KeySym.F22;
            case Keys.F23: return KeySym.F23;
            case Keys.F24: return KeySym.F24;
            case Keys.ShiftKey: return KeySym.ShiftRight;
            case Keys.ControlKey: return KeySym.ControlRight;
            case Keys.Menu: return KeySym.AltRight;
            case Keys.OemOpenBrackets: return KeySym.BracketLeft;
            case Keys.OemCloseBrackets: return KeySym.Bracketright;
            case Keys.OemPipe: return KeySym.Backslash;
            case Keys.Oemcomma: return KeySym.Comma;
            case Keys.OemPeriod: return KeySym.Period;
            case Keys.OemSemicolon: return KeySym.Semicolon;
            case Keys.OemQuestion: return KeySym.Slash;
            case Keys.OemMinus:
            case Keys.Subtract: return KeySym.Minus;
            case Keys.Oemplus:
            case Keys.Add: return KeySym.Plus;
            case Keys.OemQuotes: return KeySym.Apostrophe;
            case Keys.Oemtilde: return KeySym.Grave;
            case Keys.Pause: return KeySym.Pause;
            case Keys.Scroll: return KeySym.ScrollLock;
            case Keys.PrintScreen: return KeySym.SysReq;
            case Keys.NumPad0: return KeySym.NumPad0;
            case Keys.NumPad1: return KeySym.NumPad1;
            case Keys.NumPad2: return KeySym.NumPad2;
            case Keys.NumPad3: return KeySym.NumPad3;
            case Keys.NumPad4: return KeySym.NumPad4;
            case Keys.NumPad5: return KeySym.NumPad5;
            case Keys.NumPad6: return KeySym.NumPad6;
            case Keys.NumPad7: return KeySym.NumPad7;
            case Keys.NumPad8: return KeySym.NumPad8;
            case Keys.NumPad9: return KeySym.NumPad9;
            case Keys.Space: return KeySym.Space;
            default:
                if (key >= Keys.A && key <= Keys.Z)
                {
                    // X11 distinguishes between uppercase and lowercase keys.
                    // We want Shift to work properly, so let's not do that.
                    return (KeySym)(key - Keys.A + (int)KeySym.a);
                }
                else
                {
                    // TODO: This is wildly incomplete.
                    return (KeySym)key;
                }
        }
    }
}
