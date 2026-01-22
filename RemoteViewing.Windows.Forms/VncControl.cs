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

using RemoteViewing.Vnc;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoteViewing.Windows.Forms;

/// <summary>
/// Displays the framebuffer sent from a VNC server, and allows input to be sent back.
/// </summary>
[Category("Network")]
[Description("Displays the framebuffer sent from a VNC server, and allows input to be sent back.")]
public partial class VncControl : UserControl
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

    private enum TransformDirection
    {
        FromDevice,
        ToDevice,
    }

    private const int WM_CLIPBOARDUPDATE = 0x31d;

    private int _buttons;
    private Point _mouseLocation;

    private Cursor _dotCursor;
    private Bitmap _bitmap;
    private VncClient _client;
    private string _expectedClipboard = string.Empty;
    private HashSet<int> _keysyms = [];
    private float _scaleFactor = 1.0f;

    /// <summary>
    /// Initializes a new instance of the <see cref="VncControl"/>.
    /// </summary>
    public VncControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        using (var bmp = new Bitmap(5, 5))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Black, 1, 1, 3, 3);
            _dotCursor = new Cursor(bmp.GetHicon());
        }

        AllowInput = true;
        AllowRemoteCursor = true;
        Client = new VncClient();
        SizeMode = VncControlSizeMode.AutoSize;

        InitializeComponent();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (!DesignMode)
        {
            try { AddClipboardFormatListener(Handle); }
            catch { }
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (!DesignMode)
        {
            try { RemoveClipboardFormatListener(Handle); }
            catch { }
        }

        if (Client != null)
        {
            try { Client.Close(); }
            catch { }

            try { Client = null; }
            catch { }
        }

        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (!DesignMode)
        {
            if (AllowClipboardSharingToServer && m.Msg == WM_CLIPBOARDUPDATE)
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

        base.WndProc(ref m);
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

        if (_bitmap == null || _bitmap.Width != w || _bitmap.Height != h || force)
        {
            _bitmap = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            VncBitmap.CopyFromFramebuffer(framebuffer, new VncRectangle(0, 0, w, h), _bitmap, 0, 0);
            ScaleFactor = GetScaleFactor(framebuffer);
            if (SizeMode == VncControlSizeMode.AutoSize) { ClientSize = new Size(w, h); }
            Invalidate();
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
        // Avoid calling when the handle is not created
        if (!IsHandleCreated) return;

        _ = BeginInvoke(() =>
        {
            _expectedClipboard = string.Empty;
            ClearInputState();
            Connected?.Invoke(this, EventArgs.Empty);
        });
    }

    private void HandleConnectionFailed(object sender, EventArgs e)
    {
        // Avoid calling when the handle is not created
        if (!IsHandleCreated) return;

        _ = BeginInvoke(() =>
        {
            ClearInputState();
            ConnectionFailed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void HandleClosed(object sender, EventArgs e)
    {
        // Avoid calling when the handle is not created
        if (!IsHandleCreated) return;

        _ = BeginInvoke(() =>
        {
            ClearInputState();
            Closed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void HandleFramebufferChanged(object sender, FramebufferChangedEventArgs e)
    {
        // Avoid calling when the handle is not created
        if (!IsHandleCreated) return;

        _ = BeginInvoke(() =>
        {
            if (DesignMode) { return; }

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

            if (SizeMode == VncControlSizeMode.AutoSize)
            {
                // AutoResize mode: use transformed rectangles
                for (int i = 0; i < e.RectangleCount; i++)
                {
                    var rect = e.GetRectangle(i);
                    var transformedRect = Transform(
                        new Rectangle(rect.X, rect.Y, rect.Width, rect.Height),
                        TransformDirection.FromDevice);
                    Invalidate(transformedRect);
                }
            }
            else
            {
                // Original mode: use destination bounds scaling
                if (TryComputeDestinationBounds(out Rectangle dst))
                {
                    var scaleX = (double)dst.Width / _bitmap.Width;
                    var scaleY = (double)dst.Height / _bitmap.Height;
                    for (int i = 0; i < e.RectangleCount; i++)
                    {
                        var srcRect = e.GetRectangle(i);
                        double dstX0 = dst.X + srcRect.X * scaleX; double dstX1 = dstX0 + srcRect.Width * scaleX;
                        double dstY0 = dst.Y + srcRect.Y * scaleY; double dstY1 = dstY0 + srcRect.Height * scaleY;
                        int dstX = (int)Math.Floor(dstX0), dstW = (int)Math.Ceiling(dstX1) - dstX;
                        int dstY = (int)Math.Floor(dstY0), dstH = (int)Math.Ceiling(dstY1) - dstY;
                        Invalidate(new Rectangle(dstX, dstY, dstW, dstH));
                    }
                }
            }

            RaiseFramebufferChanged();
        });
    }

    private void RaiseFramebufferChanged()
    {
        var ev = FramebufferChanged;
        if (ev != null)
        {
            BeginInvoke(new Action(() =>
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
            if (InvokeRequired)
            {
                _ = BeginInvoke(() => HandleRemoteClipboardChanged(sender, e));
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

    private static int GetMouseMask(MouseButtons button)
    {
        return button switch
        {
            MouseButtons.Left => 1 << 0,
            MouseButtons.Middle => 1 << 1,
            MouseButtons.Right => 1 << 2,
            _ => 0,
        };
    }

    private void SendKeyUpdate(int keysym, bool pressed)
    {
        if (_client != null && AllowInput) { _client.SendKeyEvent(keysym, pressed); }
    }

    private bool TryScaleMouseLocation(Point mouseLocation, out Point scaledLocation)
    {
        if (TryComputeDestinationBounds(out Rectangle destination) && !destination.IsEmpty)
        {
            int w = _bitmap.Width, h = _bitmap.Height;
#pragma warning disable IDE0059 // Unnecessary assignment of a value
            Rectangle src = new(0, 0, _bitmap.Width, _bitmap.Height);
#pragma warning restore IDE0059 // Unnecessary assignment of a value

            int x = (int)Math.Round((double)(mouseLocation.X - destination.Left) * w / destination.Width);
            int y = (int)Math.Round((double)(mouseLocation.Y - destination.Top) * h / destination.Height);
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
            if (SizeMode == VncControlSizeMode.AutoSize)
            {
                // AutoResize mode: use transformed coordinates
                var devicePoint = TransformPoint(_mouseLocation.X, _mouseLocation.Y, TransformDirection.ToDevice);
                _client.SendPointerEvent(devicePoint.X, devicePoint.Y, _buttons);
            }
            else
            {
                // Original mode: use scaled coordinates
                if (TryScaleMouseLocation(_mouseLocation, out Point scaledLocation))
                {
                    _client.SendPointerEvent(scaledLocation.X, scaledLocation.Y, _buttons);
                }
            }
        }
    }

    private void VncControl_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
    {
        if (!DesignMode)
        {
            if (AllowInput) { e.IsInputKey = true; }
        }
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);

        if (!DesignMode)
        {
            ClearInputState();
        }
    }

    private void VncControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (!DesignMode)
        {
            int keysym = (int)VncKeysym.FromKeyCode(e.KeyCode);
            SendKeyUpdate(keysym, true); _keysyms.Add(keysym);
        }
    }

    private void VncControl_KeyUp(object sender, KeyEventArgs e)
    {
        if (!DesignMode)
        {
            int keysym = (int)VncKeysym.FromKeyCode(e.KeyCode);
            SendKeyUpdate(keysym, false); _keysyms.Remove(keysym);
        }
    }

    private void VncControl_MouseEnter(object sender, EventArgs e)
    {
        if (!DesignMode)
        {
            if (AllowRemoteCursor) { Cursor = _dotCursor; }
        }
    }

    private void VncControl_MouseLeave(object sender, EventArgs e)
    {
        if (!DesignMode)
        {
            if (AllowRemoteCursor) { Cursor = Cursors.Default; }
        }
    }

    private void VncControl_MouseDown(object sender, MouseEventArgs e)
    {
        if (!DesignMode)
        {
            _mouseLocation = e.Location;
            _buttons |= GetMouseMask(e.Button);
            SendMouseUpdate();
        }
    }

    private void VncControl_MouseUp(object sender, MouseEventArgs e)
    {
        if (!DesignMode)
        {
            _mouseLocation = e.Location;
            _buttons &= ~GetMouseMask(e.Button);
            SendMouseUpdate();
        }
    }

    private void VncControl_MouseMove(object sender, MouseEventArgs e)
    {
        if (!DesignMode)
        {
            _mouseLocation = e.Location;
            SendMouseUpdate();
        }
    }

    private void SendMouseScroll(bool down)
    {
        int mask = down ? (1 << 4) : (1 << 3);
        _buttons |= mask; SendMouseUpdate();
        _buttons &= ~mask; SendMouseUpdate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!DesignMode)
        {
            _mouseLocation = e.Location;
            if (e.Delta < 0) { SendMouseScroll(false); }
            else if (e.Delta > 0) { SendMouseScroll(true); }
        }
    }

    private bool TryComputeDestinationBounds(out Rectangle destination)
    {
        destination = default;

        if (_bitmap == null) { return false; }
        int bw = _bitmap.Width, bh = _bitmap.Height, cw = ClientSize.Width, ch = ClientSize.Height;
        if (bw < 1 || bh < 1 || cw < 1 || ch < 1) { return false; }

        switch (SizeMode)
        {
            case VncControlSizeMode.Center:
                destination = new Rectangle((cw - bw) / 2, (ch - bh) / 2, bw, bh);
                break;

            case VncControlSizeMode.Stretch:
                destination = new Rectangle(0, 0, cw, ch);
                break;

            case VncControlSizeMode.Zoom:
                double ba = (double)bw / bh;
                double ca = (double)cw / ch;
                int ow, oh;

                if (ba > ca)
                {
                    // The bitmap is wider than the screen. Limit on width.
                    ow = cw; oh = (int)Math.Max(1, cw / ba);
                }
                else
                {
                    // The bitmap is taller than the screen. Limit on height.
                    ow = (int)Math.Max(1, ch * ba); oh = ch;
                }

                destination = new Rectangle((cw - ow) / 2, (ch - oh) / 2, ow, oh);
                break;

            case VncControlSizeMode.AutoSize:
            case VncControlSizeMode.Clip:
            default:
                destination = new Rectangle(0, 0, bw, bh);
                break;
        }

        return true;
    }

    private float GetScaleFactor(VncFramebuffer framebuffer)
    {
        if (framebuffer == null)
        {
            return 1.0f;
        }

        return GetScaleFactor(framebuffer.Width, framebuffer.Height, ClientSize.Width, ClientSize.Height);
    }

    private float GetScaleFactor(int remoteWidth, int remoteHeight, int controlWidth, int controlHeight)
    {
        if (remoteWidth <= 0 || remoteHeight <= 0 || controlWidth <= 0 || controlHeight <= 0)
        {
            return 1.0f;
        }

        var widthScaleFactor = (float)controlWidth / remoteWidth;
        var heightScaleFactor = (float)controlHeight / remoteHeight;
        var scaleFactor = Math.Min(widthScaleFactor, heightScaleFactor);
        return scaleFactor > 1.0f ? 1.0f : scaleFactor;
    }

    private Rectangle Transform(Rectangle rectangle, TransformDirection direction)
    {
        var upperLeft = TransformPoint(rectangle.Left, rectangle.Top, direction);
        var bottomRight = TransformPoint(rectangle.Right, rectangle.Bottom, direction);
        return Rectangle.FromLTRB(upperLeft.X, upperLeft.Y, bottomRight.X, bottomRight.Y);
    }

    private Point TransformPoint(int x, int y, TransformDirection direction)
    {
        var scaleFactor = ScaleFactor;
        if (scaleFactor >= 1.0f)
        {
            return new Point(x, y);
        }

        scaleFactor = direction == TransformDirection.ToDevice ? 1.0f / scaleFactor : scaleFactor;

        return new Point((int)(x * scaleFactor), (int)(y * scaleFactor));
    }

    private void VncControl_Paint(object sender, PaintEventArgs e)
    {
        if (!DesignMode)
        {
            if (_bitmap == null)
            {
                return;
            }

            if (SizeMode == VncControlSizeMode.AutoSize)
            {
                // AutoResize rendering mode
                var scaleFactor = ScaleFactor;
                if (scaleFactor < 1.0f)
                {
                    e.Graphics.ScaleTransform(scaleFactor, scaleFactor);
                }

                e.Graphics.DrawImageUnscaled(_bitmap, 0, 0);
            }
            else
            {
                // Original rendering mode with scaling and interpolation
                if (TryComputeDestinationBounds(out Rectangle dst))
                {
                    var src = new Rectangle(0, 0, _bitmap.Width, _bitmap.Height);
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    e.Graphics.DrawImage(_bitmap, dst, src, GraphicsUnit.Pixel);
                }
            }
        }
    }

    [DllImport("user32", EntryPoint = "AddClipboardFormatListener", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr handle);

    [DllImport("user32", EntryPoint = "RemoveClipboardFormatListener", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr handle);

    /// <summary>
    /// The <see cref="VncClient"/> being interacted with.
    ///
    /// By default, this is a new instance.
    /// Call <see cref="VncClient.Connect(string, int, VncClientConnectOptions)"/>
    /// on it to get things up and running quickly.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
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
    [DefaultValue(true)]
    public bool AllowInput { get; set; }

    /// <summary>
    /// Whether the local cursor is allowed to be hidden.
    ///
    /// By default, this is <c>true</c>.
    /// </summary>
    [DefaultValue(true)]
    public bool AllowRemoteCursor { get; set; }

    /// <summary>
    /// If enabled, clipboard changes on the remote VNC server will alter the local clipboard.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClipboardSharingFromServer { get; set; }

    /// <summary>
    /// If enabled, local clipboard changes will be sent to the remote VNC server.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClipboardSharingToServer { get; set; }

    /// <summary>
    /// Specifies how the screen is positioned and sized.
    ///
    /// By default, this is <see cref="VncControlSizeMode.AutoSize"/>.
    /// </summary>
    [DefaultValue(VncControlSizeMode.AutoSize)]
    public VncControlSizeMode SizeMode { get; set; } = VncControlSizeMode.AutoSize;

    private float ScaleFactor
    {
        get => _scaleFactor;
        set => _scaleFactor = value;
    }

    private void VncControl_Resize(object sender, EventArgs e)
    {
        if (!DesignMode)
        {
            ScaleFactor = GetScaleFactor(_client?.Framebuffer);
            Invalidate();
        }
    }
}
