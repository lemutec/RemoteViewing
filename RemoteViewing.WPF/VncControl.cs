#region License

/*
RemoteViewing VNC Client/Server Library for .NET
Copyright (c) 2013, 2016, 2025 James F. Bellinger <http://software.seekye.com/remoteviewing>
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

using Microsoft.Win32.SafeHandles;
using RemoteViewing.Vnc;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RemoteViewing.WPF;

/// <summary>
/// Displays the framebuffer sent from a VNC server, and allows input to be sent back.
/// </summary>
[Description("Displays the framebuffer sent from a VNC server, and allows input to be sent back.")]
public class VncControl : FrameworkElement
{
    /// <summary>
    /// Occurs when the VNC client has successfully connected to the remote server.
    /// </summary>
    public event EventHandler Connected;

    /// <summary>
    /// Occurs when the VNC client has failed to connect to the remote server.
    /// </summary>
    public event EventHandler ConnectionFailed;

    /// <summary>
    /// Occurs when the VNC client is disconnected.
    /// </summary>
    public event EventHandler Closed;

    /// <summary>
    /// Occurs when the framebuffer changes.
    /// </summary>
    public event EventHandler FramebufferChanged;

    private const int WM_CLIPBOARDUPDATE = 0x31d;

    private int _buttons;
    private Point _mouseLocation;

    private Cursor _dotCursor;
    private WriteableBitmap _bitmap;
    private VncClient _client;
    private string _expectedClipboard = string.Empty;
    private HashSet<int> _keysyms = [];

    private HwndSource _hwndSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="VncControl"/>.
    /// </summary>
    public VncControl()
    {
        // Create a small dot cursor (5x5, white background with 3x3 black center)
        _dotCursor = CreateDotCursor();

        AllowInput = true;
        AllowRemoteCursor = true;
        Client = new VncClient();
        SizeMode = VncControlSizeMode.AutoSize;

        Focusable = true;
        ClipToBounds = true;

        Loaded += VncControl_Loaded;
        Unloaded += VncControl_Unloaded;
    }

    /// <summary>
    /// Creates a small dot cursor (5x5 pixels, white background with 3x3 black center).
    /// </summary>
    private static Cursor CreateDotCursor()
    {
        try
        {
            // Create a 5x5 pixel cursor with white background and black 3x3 center
            const int size = 5;
            const int hotspotX = 2;
            const int hotspotY = 2;

            // Create the color (XOR) mask - BGRA format
            // White = 0xFFFFFFFF, Black = 0xFF000000
            var colorData = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = (y * size + x) * 4;
                    bool isCenter = x >= 1 && x <= 3 && y >= 1 && y <= 3;

                    if (isCenter)
                    {
                        // Black pixel (BGRA)
                        colorData[index + 0] = 0x00; // B
                        colorData[index + 1] = 0x00; // G
                        colorData[index + 2] = 0x00; // R
                        colorData[index + 3] = 0xFF; // A
                    }
                    else
                    {
                        // White pixel (BGRA)
                        colorData[index + 0] = 0xFF; // B
                        colorData[index + 1] = 0xFF; // G
                        colorData[index + 2] = 0xFF; // R
                        colorData[index + 3] = 0xFF; // A
                    }
                }
            }

            // Create the AND mask (all 0s for fully opaque cursor)
            // AND mask is 1 bit per pixel, padded to WORD boundary
            int andMaskStride = ((size + 15) / 16) * 2;
            var andMask = new byte[andMaskStride * size];
            // All zeros = fully opaque

            // Create the bitmap info header
            var bmi = new BITMAPV5HEADER
            {
                bV5Size = Marshal.SizeOf(typeof(BITMAPV5HEADER)),
                bV5Width = size,
                bV5Height = -size, // Negative for top-down
                bV5Planes = 1,
                bV5BitCount = 32,
                bV5Compression = BI_BITFIELDS,
                bV5RedMask = 0x00FF0000,
                bV5GreenMask = 0x0000FF00,
                bV5BlueMask = 0x000000FF,
                bV5AlphaMask = 0xFF000000,
            };

            IntPtr hdc = GetDC(IntPtr.Zero);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr ppvBits = IntPtr.Zero;

            try
            {
                hBitmap = CreateDIBSection(hdc, ref bmi, DIB_RGB_COLORS, out ppvBits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || ppvBits == IntPtr.Zero)
                {
                    return Cursors.Cross;
                }

                Marshal.Copy(colorData, 0, ppvBits, colorData.Length);

                // Create monochrome AND mask bitmap
                IntPtr hMonoBitmap = CreateBitmap(size, size, 1, 1, andMask);
                if (hMonoBitmap == IntPtr.Zero)
                {
                    DeleteObject(hBitmap);
                    return Cursors.Cross;
                }

                try
                {
                    var iconInfo = new ICONINFO
                    {
                        fIcon = false, // Cursor, not icon
                        xHotspot = hotspotX,
                        yHotspot = hotspotY,
                        hbmMask = hMonoBitmap,
                        hbmColor = hBitmap,
                    };

                    IntPtr hIcon = CreateIconIndirect(ref iconInfo);
                    if (hIcon != IntPtr.Zero)
                    {
                        return CursorInteropHelper.Create(new SafeIconHandle(hIcon));
                    }
                }
                finally
                {
                    DeleteObject(hMonoBitmap);
                }
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        catch
        {
            // Fallback to Cross cursor if anything fails
        }

        return Cursors.Arrow;
    }

    private void VncControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(WndProc);
                try { AddClipboardFormatListener(_hwndSource.Handle); }
                catch { }
            }
        }
    }

    private void VncControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            if (_hwndSource != null)
            {
                try { RemoveClipboardFormatListener(_hwndSource.Handle); }
                catch { }
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            if (Client != null)
            {
                try { Client.Close(); }
                catch { }

                try { Client = null; }
                catch { }
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            if (AllowClipboardSharingToServer && msg == WM_CLIPBOARDUPDATE)
            {
                string clipboard = string.Empty;
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        clipboard = Clipboard.GetText();
                    }
                }
                catch (ExternalException)
                {
                }

                if (clipboard.Length != 0)
                {
                    if (_client != null && clipboard != _expectedClipboard)
                    {
                        _expectedClipboard = clipboard;
                        _client.SendLocalClipboardChange(clipboard);
                    }
                }
            }
        }

        return IntPtr.Zero;
    }

    private void ClearInputState()
    {
        _buttons = 0;
        foreach (var keysym in _keysyms) { SendKeyUpdate(keysym, false); }
        _keysyms.Clear();
    }

    private void UpdateFramebuffer(bool force, VncFramebuffer framebuffer)
    {
        if (framebuffer == null) { return; }
        int w = framebuffer.Width, h = framebuffer.Height;

        if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h || force)
        {
            _bitmap = VncBitmap.CreateBitmap(w, h);
            VncBitmap.CopyFromFramebuffer(framebuffer, new VncRectangle(0, 0, w, h), _bitmap, 0, 0);
            if (SizeMode == VncControlSizeMode.AutoSize)
            {
                Width = w;
                Height = h;
            }
            InvalidateVisual();
        }
    }

    private void UpdateFramebuffer()
    {
        if (_client == null) { return; }

        var framebuffer = _client.Framebuffer;
        UpdateFramebuffer(true, framebuffer);
    }

    private void HandleBell(object sender, EventArgs e)
    {
        SystemSounds.Beep.Play();
    }

    private void HandleConnected(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _expectedClipboard = string.Empty;
            ClearInputState();
            Connected?.Invoke(this, EventArgs.Empty);
        }));
    }

    private void HandleConnectionFailed(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ClearInputState();
            ConnectionFailed?.Invoke(this, EventArgs.Empty);
        }));
    }

    private void HandleClosed(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ClearInputState();
            Closed?.Invoke(this, EventArgs.Empty);
        }));
    }

    private void HandleFramebufferChanged(object sender, FramebufferChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DesignerProperties.GetIsInDesignMode(this)) { return; }

            if (_client == null) { return; }

            var framebuffer = _client.Framebuffer;
            if (framebuffer == null) { return; }

            lock (framebuffer.SyncRoot)
            {
                UpdateFramebuffer(false, framebuffer);

                if (_bitmap != null)
                {
                    for (int i = 0; i < e.RectangleCount; i++)
                    {
                        var rect = e.GetRectangle(i);
                        VncBitmap.CopyFromFramebuffer(framebuffer, rect, _bitmap, rect.X, rect.Y);
                    }
                }
            }

            InvalidateVisual();
            RaiseFramebufferChanged();
        }));
    }

    private void RaiseFramebufferChanged()
    {
        var ev = FramebufferChanged;
        if (ev != null)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ev(this, EventArgs.Empty);
            }));
        }
    }

    private void HandleRemoteClipboardChanged(object sender, RemoteClipboardChangedEventArgs e)
    {
        if (AllowClipboardSharingFromServer)
        {
            // Ensure clipboard operation is performed in UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => HandleRemoteClipboardChanged(sender, e));
                return;
            }

            if (e.Contents.Length != 0 && _expectedClipboard != e.Contents)
            {
                try
                {
                    Clipboard.SetText(e.Contents);
                    _expectedClipboard = e.Contents;
                }
                catch (ExternalException)
                {
                }
            }
        }
    }

    private static int GetMouseMask(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => 1 << 0,
            MouseButton.Middle => 1 << 1,
            MouseButton.Right => 1 << 2,
            _ => 0,
        };
    }

    private void SendKeyUpdate(int keysym, bool pressed)
    {
        if (_client != null && AllowInput) { _client.SendKeyEvent(keysym, pressed); }
    }

    private bool TryScaleMouseLocation(Point mouseLocation, out Point scaledLocation)
    {
        if (TryComputeDestinationBounds(out Rect destination) && !destination.IsEmpty)
        {
            int w = _bitmap.PixelWidth, h = _bitmap.PixelHeight;

            int x = (int)Math.Round((mouseLocation.X - destination.Left) * w / destination.Width);
            int y = (int)Math.Round((mouseLocation.Y - destination.Top) * h / destination.Height);
            x = Math.Max(0, Math.Min(w - 1, x));
            y = Math.Max(0, Math.Min(h - 1, y));

            scaledLocation = new Point(x, y);
            return true;
        }

        scaledLocation = default;
        return false;
    }

    private void SendMouseUpdate()
    {
        if (_client != null && AllowInput)
        {
            if (TryScaleMouseLocation(_mouseLocation, out Point scaledLocation))
            {
                _client.SendPointerEvent((int)scaledLocation.X, (int)scaledLocation.Y, _buttons);
            }
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            ClearInputState();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            if (AllowInput)
            {
                int keysym = VncKeysym.FromKey(e.Key); if (keysym < 0) { return; }
                SendKeyUpdate(keysym, true); _keysyms.Add(keysym);
                e.Handled = true;
            }
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            if (AllowInput)
            {
                int keysym = VncKeysym.FromKey(e.Key); if (keysym < 0) { return; }
                SendKeyUpdate(keysym, false); _keysyms.Remove(keysym);
                e.Handled = true;
            }
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            if (AllowRemoteCursor) { Cursor = _dotCursor; }
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            if (AllowRemoteCursor) { Cursor = Cursors.Arrow; }
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            Focus();
            _mouseLocation = e.GetPosition(this);
            _buttons |= GetMouseMask(e.ChangedButton);
            SendMouseUpdate();
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            _mouseLocation = e.GetPosition(this);
            _buttons &= ~GetMouseMask(e.ChangedButton);
            SendMouseUpdate();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            _mouseLocation = e.GetPosition(this);
            SendMouseUpdate();
        }
    }

    void SendMouseScroll(bool down)
    {
        int mask = down ? (1 << 4) : (1 << 3);
        _buttons |= mask; SendMouseUpdate();
        _buttons &= ~mask; SendMouseUpdate();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            _mouseLocation = e.GetPosition(this);
            if (e.Delta < 0) { SendMouseScroll(false); }
            else if (e.Delta > 0) { SendMouseScroll(true); }
            e.Handled = true;
        }
    }

    bool TryComputeDestinationBounds(out Rect destination)
    {
        destination = default;

        if (_bitmap == null) { return false; }
        int bw = _bitmap.PixelWidth, bh = _bitmap.PixelHeight;
        double cw = ActualWidth, ch = ActualHeight;
        if (bw < 1 || bh < 1 || cw < 1 || ch < 1) { return false; }

        switch (SizeMode)
        {
            case VncControlSizeMode.Center:
                destination = new Rect((cw - bw) / 2, (ch - bh) / 2, bw, bh);
                break;

            case VncControlSizeMode.Stretch:
                destination = new Rect(0, 0, cw, ch);
                break;

            case VncControlSizeMode.Zoom:
                double ba = (double)bw / bh;
                double ca = cw / ch;
                double ow, oh;

                if (ba > ca)
                {
                    // The bitmap is wider than the screen. Limit on width.
                    ow = cw; oh = Math.Max(1, cw / ba);
                }
                else
                {
                    // The bitmap is taller than the screen. Limit on height.
                    ow = Math.Max(1, ch * ba); oh = ch;
                }

                destination = new Rect((cw - ow) / 2, (ch - oh) / 2, ow, oh);
                break;

            case VncControlSizeMode.AutoSize:
            case VncControlSizeMode.Clip:
            default:
                destination = new Rect(0, 0, bw, bh);
                break;
        }

        return true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        // Draw background
        drawingContext.DrawRectangle(Brushes.Black, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            if (TryComputeDestinationBounds(out Rect dst))
            {
                // Use high quality scaling
                RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
                drawingContext.DrawImage(_bitmap, dst);
            }
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            InvalidateVisual();
        }
    }

    #region Native Methods

    [DllImport("user32", EntryPoint = "AddClipboardFormatListener", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr handle);

    [DllImport("user32", EntryPoint = "RemoveClipboardFormatListener", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPV5HEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, byte[] lpBits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint BI_BITFIELDS = 3;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPV5HEADER
    {
        public int bV5Size;
        public int bV5Width;
        public int bV5Height;
        public short bV5Planes;
        public short bV5BitCount;
        public uint bV5Compression;
        public int bV5SizeImage;
        public int bV5XPelsPerMeter;
        public int bV5YPelsPerMeter;
        public int bV5ClrUsed;
        public int bV5ClrImportant;
        public uint bV5RedMask;
        public uint bV5GreenMask;
        public uint bV5BlueMask;
        public uint bV5AlphaMask;
        public int bV5CSType;
        public int bV5Endpoints_ciexyzRed_x;
        public int bV5Endpoints_ciexyzRed_y;
        public int bV5Endpoints_ciexyzRed_z;
        public int bV5Endpoints_ciexyzGreen_x;
        public int bV5Endpoints_ciexyzGreen_y;
        public int bV5Endpoints_ciexyzGreen_z;
        public int bV5Endpoints_ciexyzBlue_x;
        public int bV5Endpoints_ciexyzBlue_y;
        public int bV5Endpoints_ciexyzBlue_z;
        public uint bV5GammaRed;
        public uint bV5GammaGreen;
        public uint bV5GammaBlue;
        public int bV5Intent;
        public int bV5ProfileData;
        public int bV5ProfileSize;
        public int bV5Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    /// <summary>
    /// Safe handle for HICON that calls DestroyIcon on disposal.
    /// </summary>
    private class SafeIconHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeIconHandle(IntPtr hIcon) : base(true)
        {
            SetHandle(hIcon);
        }

        protected override bool ReleaseHandle()
        {
            return DestroyIcon(handle);
        }
    }

    #endregion

    /// <summary>
    /// The <see cref="VncClient"/> being interacted with.
    ///
    /// By default, this is a new instance.
    /// Call <see cref="VncClient.Connect(string, int, VncClientConnectOptions)"/>
    /// on it to get things up and running quickly.
    /// </summary>
    public VncClient Client
    {
        get => _client;
        set
        {
            if (_client == value) { return; }

            if (_client != null)
            {
                _client.Bell -= HandleBell;
                _client.Connected -= HandleConnected;
                _client.ConnectionFailed -= HandleConnectionFailed;
                _client.Closed -= HandleClosed;
                _client.FramebufferChanged -= HandleFramebufferChanged;
                _client.RemoteClipboardChanged -= HandleRemoteClipboardChanged;
            }

            _client = value;

            if (_client != null)
            {
                _client.Bell += HandleBell;
                _client.Connected += HandleConnected;
                _client.ConnectionFailed += HandleConnectionFailed;
                _client.Closed += HandleClosed;
                _client.FramebufferChanged += HandleFramebufferChanged;
                _client.RemoteClipboardChanged += HandleRemoteClipboardChanged;
            }

            ClearInputState();
            UpdateFramebuffer();
            RaiseFramebufferChanged();
        }
    }

    /// <summary>
    /// Whether the control should send input to the server, or act only as a viewer.
    ///
    /// By default, this is <c>true</c>.
    /// </summary>
    public bool AllowInput { get; set; }

    /// <summary>
    /// Whether the local cursor is allowed to be hidden.
    ///
    /// By default, this is <c>true</c>.
    /// </summary>
    public bool AllowRemoteCursor { get; set; }

    /// <summary>
    /// If enabled, clipboard changes on the remote VNC server will alter the local clipboard.
    /// </summary>
    public bool AllowClipboardSharingFromServer { get; set; }

    /// <summary>
    /// If enabled, local clipboard changes will be sent to the remote VNC server.
    /// </summary>
    public bool AllowClipboardSharingToServer { get; set; }

    /// <summary>
    /// Specifies how the screen is positioned and sized.
    ///
    /// By default, this is <see cref="VncControlSizeMode.AutoSize"/>.
    /// </summary>
    public VncControlSizeMode SizeMode { get; set; }
}
