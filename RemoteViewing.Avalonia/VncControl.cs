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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RemoteViewing.Vnc;
using AvaControl = Avalonia.Controls.Control;

namespace RemoteViewing.Avalonia;

/// <summary>
/// Displays the framebuffer sent from a VNC server, and allows input to be sent back.
///
/// This is the Avalonia (cross-platform) counterpart of the WPF <c>VncControl</c>.
/// </summary>
[Description("Displays the framebuffer sent from a VNC server, and allows input to be sent back.")]
public class VncControl : AvaControl
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
    /// Occurs when the VNC client is attempting to reconnect.
    /// </summary>
    public event EventHandler<ReconnectingEventArgs> Reconnecting;

    /// <summary>
    /// Occurs when the VNC client has given up reconnecting after reaching the maximum attempts.
    /// </summary>
    public event EventHandler ReconnectFailed;

    /// <summary>
    /// Occurs when the framebuffer changes.
    /// </summary>
    public event EventHandler FramebufferChanged;

    private enum TransformDirection
    {
        FromDevice,
        ToDevice,
    }

    private int _buttons;
    private Point _mouseLocation;

    private readonly Cursor _dotCursor;
    private WriteableBitmap _bitmap;
    private VncClient _client;
    private string _expectedClipboard = string.Empty;
    private readonly HashSet<int> _keysyms = new();
    private float _scaleFactor = 1f;

    private DispatcherTimer _clipboardTimer;

    // FPS tracking
    private int _frameCount;
    private DateTime _lastFpsUpdate = DateTime.UtcNow;
    private double _currentFps;
    private readonly object _fpsLock = new();

    #region Styled properties

    public static readonly StyledProperty<bool> AllowInputProperty =
        AvaloniaProperty.Register<VncControl, bool>(nameof(AllowInput), true);

    public static readonly StyledProperty<bool> AllowRemoteCursorProperty =
        AvaloniaProperty.Register<VncControl, bool>(nameof(AllowRemoteCursor), true);

    public static readonly StyledProperty<bool> AllowClipboardSharingFromServerProperty =
        AvaloniaProperty.Register<VncControl, bool>(nameof(AllowClipboardSharingFromServer));

    public static readonly StyledProperty<bool> AllowClipboardSharingToServerProperty =
        AvaloniaProperty.Register<VncControl, bool>(nameof(AllowClipboardSharingToServer));

    public static readonly StyledProperty<VncControlSizeMode> SizeModeProperty =
        AvaloniaProperty.Register<VncControl, VncControlSizeMode>(nameof(SizeMode), VncControlSizeMode.Zoom);

    public static readonly StyledProperty<bool> ShowFpsProperty =
        AvaloniaProperty.Register<VncControl, bool>(nameof(ShowFps));

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="VncControl"/>.
    /// </summary>
    public VncControl()
    {
        _dotCursor = CreateDotCursor();

        Client = new VncClient();

        Focusable = true;
        ClipToBounds = true;
        Background = Brushes.Black;
    }

    /// <summary>
    /// Background brush used when no framebuffer is rendered.
    /// </summary>
    public static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<VncControl, IBrush>(nameof(Background), Brushes.Black);

    /// <summary>
    /// Gets or sets the background brush.
    /// </summary>
    public IBrush Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Creates a small dot cursor (5x5 pixels, white background with 3x3 black center).
    /// </summary>
    private static Cursor CreateDotCursor()
    {
        try
        {
            const int size = 5;
            WriteableBitmap bitmap = new(
                new PixelSize(size, size),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            using (ILockedFramebuffer buf = bitmap.Lock())
            {
                unsafe
                {
                    byte* scan0 = (byte*)buf.Address;
                    int stride = buf.RowBytes;
                    for (int y = 0; y < size; y++)
                    {
                        byte* row = scan0 + y * stride;
                        for (int x = 0; x < size; x++)
                        {
                            bool isCenter = x >= 1 && x <= 3 && y >= 1 && y <= 3;
                            byte* px = row + x * 4;
                            if (isCenter)
                            {
                                px[0] = 0x00;
                                px[1] = 0x00;
                                px[2] = 0x00;
                                px[3] = 0xFF;
                            }
                            else
                            {
                                px[0] = 0xFF;
                                px[1] = 0xFF;
                                px[2] = 0xFF;
                                px[3] = 0xFF;
                            }
                        }
                    }
                }
            }

            return new Cursor(bitmap, new PixelPoint(2, 2));
        }
        catch
        {
            return new Cursor(StandardCursorType.Arrow);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (!Design.IsDesignMode)
        {
            _clipboardTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500),
            };
            _clipboardTimer.Tick += ClipboardTimer_Tick;
            _clipboardTimer.Start();

            SizeChanged += VncControl_SizeChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (!Design.IsDesignMode)
        {
            SizeChanged -= VncControl_SizeChanged;

            if (_clipboardTimer != null)
            {
                _clipboardTimer.Stop();
                _clipboardTimer.Tick -= ClipboardTimer_Tick;
                _clipboardTimer = null;
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

    private void VncControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!Design.IsDesignMode && _client?.Framebuffer != null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            ScaleFactor = GetScaleFactor(_client.Framebuffer);
            InvalidateVisual();
        }
    }

    private async void ClipboardTimer_Tick(object sender, EventArgs e)
    {
        if (!AllowClipboardSharingToServer)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        var clipboard = topLevel?.Clipboard;
        if (clipboard == null)
        {
            return;
        }

        string text = string.Empty;
        try
        {
            text = await clipboard.GetTextAsync() ?? string.Empty;
        }
        catch
        {
            return;
        }

        if (!string.IsNullOrEmpty(text))
        {
            if (_client != null && text != _expectedClipboard)
            {
                _expectedClipboard = text;
                try { _client.SendLocalClipboardChange(text); }
                catch { }
            }
        }
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

        if (_bitmap == null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h || force)
        {
            _bitmap = VncBitmap.CreateBitmap(w, h);
            VncBitmap.CopyFromFramebuffer(framebuffer, new VncRectangle(0, 0, w, h), _bitmap, 0, 0);
            ScaleFactor = GetScaleFactor(framebuffer);
            InvalidateVisual();
            InvalidateMeasure();
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
        // Avalonia has no built-in system beep. Apps can subscribe to Client.Bell for custom behavior.
    }

    private void HandleConnected(object sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _expectedClipboard = string.Empty;
            ClearInputState();
            Connected?.Invoke(this, EventArgs.Empty);
        });
    }

    private void HandleConnectionFailed(object sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ClearInputState();
            ConnectionFailed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void HandleClosed(object sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ClearInputState();
            Closed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void HandleReconnecting(object sender, ReconnectingEventArgs e)
    {
        Dispatcher.UIThread.Post(() => Reconnecting?.Invoke(this, e));
    }

    private void HandleReconnectFailed(object sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => ReconnectFailed?.Invoke(this, EventArgs.Empty));
    }

    private void HandleFramebufferChanged(object sender, FramebufferChangedEventArgs e)
    {
        UpdateFpsCounter();

        Dispatcher.UIThread.Post(() =>
        {
            if (Design.IsDesignMode) { return; }

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
        });
    }

    private void UpdateFpsCounter()
    {
        lock (_fpsLock)
        {
            _frameCount++;
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;

            if (elapsed >= 1.0)
            {
                _currentFps = _frameCount / elapsed;
                _frameCount = 0;
                _lastFpsUpdate = now;
            }
        }
    }

    private void RaiseFramebufferChanged()
    {
        if (FramebufferChanged != null)
        {
            Dispatcher.UIThread.Post(() => FramebufferChanged(this, EventArgs.Empty));
        }
    }

    private async void HandleRemoteClipboardChanged(object sender, RemoteClipboardChangedEventArgs e)
    {
        if (!AllowClipboardSharingFromServer)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => HandleRemoteClipboardChanged(sender, e));
            return;
        }

        if (e.Contents.Length != 0 && _expectedClipboard != e.Contents)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null)
            {
                return;
            }

            try
            {
                await clipboard.SetTextAsync(e.Contents);
                _expectedClipboard = e.Contents;
            }
            catch
            {
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
        if (TryComputeDestinationBounds(out Rect destination) && destination.Width > 0 && destination.Height > 0)
        {
            int w = _bitmap.PixelSize.Width, h = _bitmap.PixelSize.Height;

            int x = (int)Math.Round((mouseLocation.X - destination.X) * w / destination.Width);
            int y = (int)Math.Round((mouseLocation.Y - destination.Y) * h / destination.Height);
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
                var devicePoint = TransformPoint((int)_mouseLocation.X, (int)_mouseLocation.Y, TransformDirection.ToDevice);
                _client.SendPointerEvent((int)devicePoint.X, (int)devicePoint.Y, _buttons);
            }
            else
            {
                if (TryScaleMouseLocation(_mouseLocation, out Point scaledLocation))
                {
                    _client.SendPointerEvent((int)scaledLocation.X, (int)scaledLocation.Y, _buttons);
                }
            }
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);

        if (!Design.IsDesignMode)
        {
            ClearInputState();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!Design.IsDesignMode && AllowInput)
        {
            int keysym = (int)VncKeysym.FromKey(e.Key);
            SendKeyUpdate(keysym, true); _keysyms.Add(keysym);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (!Design.IsDesignMode && AllowInput)
        {
            int keysym = (int)VncKeysym.FromKey(e.Key);
            SendKeyUpdate(keysym, false); _keysyms.Remove(keysym);
            e.Handled = true;
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);

        if (!Design.IsDesignMode && AllowRemoteCursor)
        {
            Cursor = _dotCursor;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (!Design.IsDesignMode && AllowRemoteCursor)
        {
            Cursor = new Cursor(StandardCursorType.Arrow);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!Design.IsDesignMode)
        {
            Focus();
            _mouseLocation = e.GetPosition(this);

            var props = e.GetCurrentPoint(this).Properties;
            UpdateButtonsFromProps(props);
            SendMouseUpdate();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!Design.IsDesignMode)
        {
            _mouseLocation = e.GetPosition(this);

            var props = e.GetCurrentPoint(this).Properties;
            UpdateButtonsFromProps(props);
            SendMouseUpdate();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!Design.IsDesignMode)
        {
            _mouseLocation = e.GetPosition(this);

            var props = e.GetCurrentPoint(this).Properties;
            UpdateButtonsFromProps(props);
            SendMouseUpdate();
        }
    }

    private void UpdateButtonsFromProps(PointerPointProperties props)
    {
        int b = 0;
        if (props.IsLeftButtonPressed) { b |= 1 << 0; }
        if (props.IsMiddleButtonPressed) { b |= 1 << 1; }
        if (props.IsRightButtonPressed) { b |= 1 << 2; }
        _buttons = b;
    }

    private void SendMouseScroll(bool down)
    {
        int mask = down ? (1 << 4) : (1 << 3);
        _buttons |= mask; SendMouseUpdate();
        _buttons &= ~mask; SendMouseUpdate();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (!Design.IsDesignMode)
        {
            _mouseLocation = e.GetPosition(this);
            if (e.Delta.Y < 0) { SendMouseScroll(false); }
            else if (e.Delta.Y > 0) { SendMouseScroll(true); }
            e.Handled = true;
        }
    }

    private bool TryComputeDestinationBounds(out Rect destination)
    {
        destination = default;

        if (_bitmap == null) { return false; }
        int bw = _bitmap.PixelSize.Width, bh = _bitmap.PixelSize.Height;
        double cw = Bounds.Width, ch = Bounds.Height;
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
                    ow = cw; oh = Math.Max(1, cw / ba);
                }
                else
                {
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

    private float GetScaleFactor(VncFramebuffer framebuffer)
    {
        if (framebuffer == null)
        {
            return 1.0f;
        }

        return GetScaleFactor(framebuffer.Width, framebuffer.Height, Bounds.Width, Bounds.Height);
    }

    private float GetScaleFactor(int remoteWidth, int remoteHeight, double controlWidth, double controlHeight)
    {
        if (remoteWidth <= 0 || remoteHeight <= 0 || controlWidth <= 0 || controlHeight <= 0)
        {
            return 1.0f;
        }

        var widthScaleFactor = (float)(controlWidth / remoteWidth);
        var heightScaleFactor = (float)(controlHeight / remoteHeight);
        var scaleFactor = Math.Min(widthScaleFactor, heightScaleFactor);
        return scaleFactor;
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

    protected override Size MeasureOverride(Size availableSize)
    {
        if (SizeMode == VncControlSizeMode.AutoSize && _bitmap != null)
        {
            return new Size(_bitmap.PixelSize.Width, _bitmap.PixelSize.Height);
        }

        return base.MeasureOverride(availableSize);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!Design.IsDesignMode && _client?.Framebuffer != null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            var newScaleFactor = GetScaleFactor(_client.Framebuffer);
            if (Math.Abs(newScaleFactor - ScaleFactor) > 0.001f)
            {
                ScaleFactor = newScaleFactor;
            }
        }

        // Draw background
        if (Background != null)
        {
            context.FillRectangle(Background, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }

        if (!Design.IsDesignMode)
        {
            if (_bitmap == null)
            {
                return;
            }

            if (SizeMode == VncControlSizeMode.AutoSize)
            {
                var scaleFactor = ScaleFactor;
                Rect srcRect = new(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height);
                if (scaleFactor < 1.0f)
                {
                    Rect dstRect = new(0, 0, _bitmap.PixelSize.Width * scaleFactor, _bitmap.PixelSize.Height * scaleFactor);
                    context.DrawImage(_bitmap, srcRect, dstRect);
                }
                else
                {
                    context.DrawImage(_bitmap, srcRect, srcRect);
                }
            }
            else
            {
                if (TryComputeDestinationBounds(out Rect dst))
                {
                    Rect srcRect = new(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height);
                    RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
                    context.DrawImage(_bitmap, srcRect, dst);
                }
            }

            if (ShowFps)
            {
                DrawFpsOverlay(context);
            }
        }
    }

    private void DrawFpsOverlay(DrawingContext drawingContext)
    {
        double fps;
        lock (_fpsLock)
        {
            fps = _currentFps;
        }

        string fpsText = $"FPS:{fps:F0}";

        var typeface = new Typeface(new FontFamily("Consolas, Courier New, monospace"), FontStyle.Normal, FontWeight.Normal);
        var formattedText = new FormattedText(
            fpsText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            14,
            Brushes.White);

        const double padding = 4d;
        Rect bgRect = new(
            padding,
            padding,
            formattedText.Width + padding * 2d,
            formattedText.Height + padding);

        drawingContext.FillRectangle(
            new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
            bgRect);

        drawingContext.DrawText(formattedText, new Point(padding * 2d, padding + 2d));
    }

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
                _client.Reconnecting -= HandleReconnecting;
                _client.ReconnectFailed -= HandleReconnectFailed;
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
                _client.Reconnecting += HandleReconnecting;
                _client.ReconnectFailed += HandleReconnectFailed;
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
    public bool AllowInput
    {
        get => GetValue(AllowInputProperty);
        set => SetValue(AllowInputProperty, value);
    }

    /// <summary>
    /// Whether the local cursor is allowed to be hidden.
    ///
    /// By default, this is <c>true</c>.
    /// </summary>
    public bool AllowRemoteCursor
    {
        get => GetValue(AllowRemoteCursorProperty);
        set => SetValue(AllowRemoteCursorProperty, value);
    }

    /// <summary>
    /// If enabled, clipboard changes on the remote VNC server will alter the local clipboard.
    /// </summary>
    public bool AllowClipboardSharingFromServer
    {
        get => GetValue(AllowClipboardSharingFromServerProperty);
        set => SetValue(AllowClipboardSharingFromServerProperty, value);
    }

    /// <summary>
    /// If enabled, local clipboard changes will be sent to the remote VNC server.
    /// </summary>
    public bool AllowClipboardSharingToServer
    {
        get => GetValue(AllowClipboardSharingToServerProperty);
        set => SetValue(AllowClipboardSharingToServerProperty, value);
    }

    /// <summary>
    /// Specifies how the screen is positioned and sized.
    ///
    /// By default, this is <see cref="VncControlSizeMode.Zoom"/>.
    /// </summary>
    public VncControlSizeMode SizeMode
    {
        get => GetValue(SizeModeProperty);
        set => SetValue(SizeModeProperty, value);
    }

    /// <summary>
    /// Whether to display the current frames per second (FPS) in the top-left corner.
    ///
    /// By default, this is <c>false</c>.
    /// </summary>
    public bool ShowFps
    {
        get => GetValue(ShowFpsProperty);
        set => SetValue(ShowFpsProperty, value);
    }

    /// <summary>
    /// Gets the current frames per second (FPS) value.
    /// This value is updated approximately once per second.
    /// </summary>
    public double CurrentFps
    {
        get
        {
            lock (_fpsLock)
            {
                return _currentFps;
            }
        }
    }

    private float ScaleFactor
    {
        get => _scaleFactor;
        set => _scaleFactor = value;
    }
}
