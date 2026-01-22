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
    /// <returns>The keysym, or -1 if the key is not recognized.</returns>
    public static int FromKey(Key key)
    {
        switch (key)
        {
            case Key.Back: return 0xff08;
            case Key.Tab: return 0xff09;
            case Key.Return: return 0xff0d;
            case Key.Escape: return 0xff1b;
            case Key.Insert: return 0xff63;
            case Key.Delete: return 0xffff;
            case Key.Home: return 0xff50;
            case Key.End: return 0xff57;
            case Key.PageUp: return 0xff55;
            case Key.PageDown: return 0xff56;
            case Key.Left: return 0xff51;
            case Key.Up: return 0xff52;
            case Key.Right: return 0xff53;
            case Key.Down: return 0xff54;
            case Key.F1: return 0xffbe;
            case Key.F2: return 0xffbf;
            case Key.F3: return 0xffc0;
            case Key.F4: return 0xffc1;
            case Key.F5: return 0xffc2;
            case Key.F6: return 0xffc3;
            case Key.F7: return 0xffc4;
            case Key.F8: return 0xffc5;
            case Key.F9: return 0xffc6;
            case Key.F10: return 0xffc7;
            case Key.F11: return 0xffc8;
            case Key.F12: return 0xffc9;
            case Key.F13: return 0xffca;
            case Key.F14: return 0xffcb;
            case Key.F15: return 0xffcc;
            case Key.F16: return 0xffcd;
            case Key.F17: return 0xffce;
            case Key.F18: return 0xffcf;
            case Key.F19: return 0xffd0;
            case Key.F20: return 0xffd1;
            case Key.F21: return 0xffd2;
            case Key.F22: return 0xffd3;
            case Key.F23: return 0xffd4;
            case Key.F24: return 0xffd5;
            case Key.LeftShift:
            case Key.RightShift: return 0xffe2;
            case Key.LeftCtrl:
            case Key.RightCtrl: return 0xffe4;
            case Key.LeftAlt:
            case Key.RightAlt: return 0xffea;
            case Key.OemOpenBrackets: return 0x005b;
            case Key.OemCloseBrackets: return 0x005d;
            case Key.OemPipe: return 0x005c;
            case Key.OemComma: return 0x002c;
            case Key.OemPeriod: return 0x002e;
            case Key.OemSemicolon: return 0x003b;
            case Key.OemQuestion: return 0x002f;
            case Key.OemMinus:
            case Key.Subtract: return 0x002d;
            case Key.OemPlus:
            case Key.Add: return 0x002b;
            case Key.OemQuotes: return 0x0027;
            case Key.OemTilde: return 0x0060;
            case Key.Pause: return 0xff13;
            case Key.Scroll: return 0xff14;
            case Key.PrintScreen: return 0xff15;
            case Key.NumPad0: return 0xffb0;
            case Key.NumPad1: return 0xffb1;
            case Key.NumPad2: return 0xffb2;
            case Key.NumPad3: return 0xffb3;
            case Key.NumPad4: return 0xffb4;
            case Key.NumPad5: return 0xffb5;
            case Key.NumPad6: return 0xffb6;
            case Key.NumPad7: return 0xffb7;
            case Key.NumPad8: return 0xffb8;
            case Key.NumPad9: return 0xffb9;
            case Key.Space: return 0x0020;
            default:
                if (key >= Key.A && key <= Key.Z)
                {
                    // X11 distinguishes between uppercase and lowercase keys.
                    // We want Shift to work properly, so let's not do that.
                    return (int)(key - Key.A + 0x0061);
                }
                else if (key >= Key.D0 && key <= Key.D9)
                {
                    return (int)(key - Key.D0 + 0x0030);
                }
                else
                {
                    // TODO: This is wildly incomplete.
                    return (int)key;
                }
        }
    }

    /// <summary>
    /// Converts <see cref="Keys"/> enum to X11 keysyms.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The keysym.</returns>
    public static int FromKeyCode(Keys key)
    {
        switch (key)
        {
            case Keys.Back: return 0xff08;
            case Keys.Tab: return 0xff09;
            case Keys.Enter: return 0xff0d;
            case Keys.Escape: return 0xff1b;
            case Keys.Insert: return 0xff63;
            case Keys.Delete: return 0xffff;
            case Keys.Home: return 0xff50;
            case Keys.End: return 0xff57;
            case Keys.PageUp: return 0xff55;
            case Keys.PageDown: return 0xff56;
            case Keys.Left: return 0xff51;
            case Keys.Up: return 0xff52;
            case Keys.Right: return 0xff53;
            case Keys.Down: return 0xff54;
            case Keys.F1: return 0xffbe;
            case Keys.F2: return 0xffbf;
            case Keys.F3: return 0xffc0;
            case Keys.F4: return 0xffc1;
            case Keys.F5: return 0xffc2;
            case Keys.F6: return 0xffc3;
            case Keys.F7: return 0xffc4;
            case Keys.F8: return 0xffc5;
            case Keys.F9: return 0xffc6;
            case Keys.F10: return 0xffc7;
            case Keys.F11: return 0xffc8;
            case Keys.F12: return 0xffc9;
            case Keys.F13: return 0xffca;
            case Keys.F14: return 0xffcb;
            case Keys.F15: return 0xffcc;
            case Keys.F16: return 0xffcd;
            case Keys.F17: return 0xffce;
            case Keys.F18: return 0xffcf;
            case Keys.F19: return 0xffd0;
            case Keys.F20: return 0xffd1;
            case Keys.F21: return 0xffd2;
            case Keys.F22: return 0xffd3;
            case Keys.F23: return 0xffd4;
            case Keys.F24: return 0xffd5;
            case Keys.ShiftKey: return 0xffe2;
            case Keys.ControlKey: return 0xffe4;
            case Keys.Menu: return 0xffea;
            case Keys.OemOpenBrackets: return 0x005b;
            case Keys.OemCloseBrackets: return 0x005d;
            case Keys.OemPipe: return 0x005c;
            case Keys.Oemcomma: return 0x002c;
            case Keys.OemPeriod: return 0x002e;
            case Keys.OemSemicolon: return 0x003b;
            case Keys.OemQuestion: return 0x002f;
            case Keys.OemMinus:
            case Keys.Subtract: return 0x002d;
            case Keys.Oemplus:
            case Keys.Add: return 0x002b;
            case Keys.OemQuotes: return 0x0027;
            case Keys.Oemtilde: return 0x0060;
            case Keys.Pause: return 0xff13;
            case Keys.Scroll: return 0xff14;
            case Keys.PrintScreen: return 0xff15;
            case Keys.NumPad0: return 0xffb0;
            case Keys.NumPad1: return 0xffb1;
            case Keys.NumPad2: return 0xffb2;
            case Keys.NumPad3: return 0xffb3;
            case Keys.NumPad4: return 0xffb4;
            case Keys.NumPad5: return 0xffb5;
            case Keys.NumPad6: return 0xffb6;
            case Keys.NumPad7: return 0xffb7;
            case Keys.NumPad8: return 0xffb8;
            case Keys.NumPad9: return 0xffb9;
            case Keys.Space: return 0x0020;
            default:
                if (key >= Keys.A && key <= Keys.Z)
                {
                    // X11 distinguishes between uppercase and lowercase keys.
                    // We want Shift to work properly, so let's not do that.
                    return (int)(key - Keys.A + 0x0061);
                }
                else
                {
                    // TODO: This is wildly incomplete.
                    return (int)key;
                }
        }
    }
}
