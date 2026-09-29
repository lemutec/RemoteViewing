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
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RemoteViewing.Vnc;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace RemoteViewing.WinUI;

/// <summary>
/// Displays the framebuffer sent from a VNC server, and allows input to be sent back.
/// This is the WinUI 3 counterpart of the WPF <c>VncControl</c>.
/// </summary>
[Description("Displays the framebuffer sent from a VNC server, and allows input to be sent back.")]
public class VncControl : UserControl
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

    /// <summary>
    /// Identifies the <see cref="AllowInput"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty AllowInputProperty =
        DependencyProperty.Register(nameof(AllowInput), typeof(bool), typeof(VncControl), new PropertyMetadata(true));

    /// <summary>
    /// Identifies the <see cref="AllowRemoteCursor"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty AllowRemoteCursorProperty =
        DependencyProperty.Register(nameof(AllowRemoteCursor), typeof(bool), typeof(VncControl), new PropertyMetadata(true));

    /// <summary>
    /// Identifies the <see cref="AllowClipboardSharingFromServer"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty AllowClipboardSharingFromServerProperty =
        DependencyProperty.Register(nameof(AllowClipboardSharingFromServer), typeof(bool), typeof(VncControl), new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="AllowClipboardSharingToServer"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty AllowClipboardSharingToServerProperty =
        DependencyProperty.Register(nameof(AllowClipboardSharingToServer), typeof(bool), typeof(VncControl), new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="SizeMode"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SizeModeProperty =
        DependencyProperty.Register(nameof(SizeMode), typeof(VncControlSizeMode), typeof(VncControl),
            new PropertyMetadata(VncControlSizeMode.Zoom, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="ShowFps"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ShowFpsProperty =
        DependencyProperty.Register(nameof(ShowFps), typeof(bool), typeof(VncControl),
            new PropertyMetadata(false, OnShowFpsChanged));

    private readonly Canvas _canvas;
    private readonly Image _image;
    private readonly Border _fpsHost;
    private readonly TextBlock _fpsText;

    private int _buttons;
    private Point _mouseLocation;
    private WriteableBitmap _bitmap;
    private VncClient _client;
    private string _expectedClipboard = string.Empty;
    private readonly HashSet<int> _keysyms = new();
    private InputCursor _dotCursor;
    private InputCursor _arrowCursor;
    private bool _clipboardHooked;

    private int _frameCount;
    private DateTime _lastFpsUpdate = DateTime.UtcNow;
    private double _currentFps;
    private readonly object _fpsLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="VncControl"/>.
    /// </summary>
    public VncControl()
    {
        _image = new Image
        {
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };

        _canvas = new Canvas
        {
            IsHitTestVisible = false,
        };
        _canvas.Children.Add(_image);

        _fpsText = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            IsHitTestVisible = false,
        };

        _fpsHost = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 0, 0, 0)),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _fpsText,
        };

        var root = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
        };
        root.Children.Add(_canvas);
        root.Children.Add(_fpsHost);
        Content = root;

        IsTabStop = true;
        UseSystemFocusVisuals = false;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Black);

        Client = new VncClient();

        Loaded += VncControl_Loaded;
        Unloaded += VncControl_Unloaded;
    }

    /// <summary>
    /// The <see cref="VncClient"/> being interacted with.
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
            RunOnUi(UpdateFramebuffer);
            RaiseFramebufferChanged();
        }
    }

    /// <summary>
    /// Whether the control should send input to the server, or act only as a viewer.
    /// By default, this is <c>true</c>.
    /// </summary>
    public bool AllowInput
    {
        get => (bool)GetValue(AllowInputProperty);
        set => SetValue(AllowInputProperty, value);
    }

    /// <summary>
    /// Whether the local cursor is allowed to be hidden.
    /// By default, this is <c>true</c>.
    /// </summary>
    public bool AllowRemoteCursor
    {
        get => (bool)GetValue(AllowRemoteCursorProperty);
        set => SetValue(AllowRemoteCursorProperty, value);
    }

    /// <summary>
    /// If enabled, clipboard changes on the remote VNC server will alter the local clipboard.
    /// </summary>
    public bool AllowClipboardSharingFromServer
    {
        get => (bool)GetValue(AllowClipboardSharingFromServerProperty);
        set => SetValue(AllowClipboardSharingFromServerProperty, value);
    }

    /// <summary>
    /// If enabled, local clipboard changes will be sent to the remote VNC server.
    /// </summary>
    public bool AllowClipboardSharingToServer
    {
        get => (bool)GetValue(AllowClipboardSharingToServerProperty);
        set => SetValue(AllowClipboardSharingToServerProperty, value);
    }

    /// <summary>
    /// Specifies how the screen is positioned and sized.
    /// By default, this is <see cref="VncControlSizeMode.Zoom"/>.
    /// </summary>
    public VncControlSizeMode SizeMode
    {
        get => (VncControlSizeMode)GetValue(SizeModeProperty);
        set => SetValue(SizeModeProperty, value);
    }

    /// <summary>
    /// Whether to display the current frames per second (FPS) in the top-left corner.
    /// By default, this is <c>false</c>.
    /// </summary>
    public bool ShowFps
    {
        get => (bool)GetValue(ShowFpsProperty);
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

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);

        if (SizeMode == VncControlSizeMode.AutoSize && _bitmap != null)
        {
            return new Size(_bitmap.PixelWidth, _bitmap.PixelHeight);
        }

        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size result = base.ArrangeOverride(finalSize);
        Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, finalSize.Width, finalSize.Height),
        };
        LayoutImage(finalSize);
        return result;
    }

    /// <inheritdoc />
    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (!IsDesignMode)
        {
            ClearInputState();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsDesignMode && AllowInput)
        {
            int keysym = (int)VncKeysym.FromKey(e.Key);
            SendKeyUpdate(keysym, true);
            _keysyms.Add(keysym);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        base.OnKeyUp(e);

        if (!IsDesignMode && AllowInput)
        {
            int keysym = (int)VncKeysym.FromKey(e.Key);
            SendKeyUpdate(keysym, false);
            _keysyms.Remove(keysym);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!IsDesignMode && AllowRemoteCursor)
        {
            EnsureCursors();
            ProtectedCursor = _dotCursor;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);
        if (!IsDesignMode && AllowRemoteCursor)
        {
            EnsureCursors();
            ProtectedCursor = _arrowCursor;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsDesignMode) { return; }

        Focus(FocusState.Programmatic);
        CapturePointer(e.Pointer);
        UpdatePointer(e);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsDesignMode) { return; }

        UpdatePointer(e);
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (IsDesignMode) { return; }

        UpdatePointer(e);
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (IsDesignMode) { return; }

        _mouseLocation = e.GetCurrentPoint(this).Position;
        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta < 0) { SendMouseScroll(false); }
        else if (delta > 0) { SendMouseScroll(true); }
        e.Handled = true;
    }

    private static bool IsDesignMode => Windows.ApplicationModel.DesignMode.DesignModeEnabled;

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (VncControl)d;
        control.InvalidateMeasure();
        control.InvalidateArrange();
    }

    private static void OnShowFpsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (VncControl)d;
        control.UpdateFpsOverlay();
    }

    private void VncControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (IsDesignMode || _clipboardHooked) { return; }

        Clipboard.ContentChanged += Clipboard_ContentChanged;
        _clipboardHooked = true;
    }

    private void VncControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (IsDesignMode) { return; }

        if (_clipboardHooked)
        {
            Clipboard.ContentChanged -= Clipboard_ContentChanged;
            _clipboardHooked = false;
        }

        if (Client != null)
        {
            try { Client.Close(); }
            catch { }

            try { Client = null; }
            catch { }
        }
    }

    private async void Clipboard_ContentChanged(object sender, object e)
    {
        if (!AllowClipboardSharingToServer || _client == null)
        {
            return;
        }

        string text;
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return;
            }

            text = await content.GetTextAsync();
        }
        catch
        {
            return;
        }

        if (!string.IsNullOrEmpty(text) && _client != null && text != _expectedClipboard)
        {
            _expectedClipboard = text;
            try { _client.SendLocalClipboardChange(text); }
            catch { }
        }
    }

    private void EnsureCursors()
    {
        _dotCursor ??= InputSystemCursor.Create(InputSystemCursorShape.Cross);
        _arrowCursor ??= InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    private void ClearInputState()
    {
        _buttons = 0;
        foreach (int keysym in _keysyms)
        {
            SendKeyUpdate(keysym, false);
        }

        _keysyms.Clear();
    }

    private void UpdateFramebuffer(bool force, VncFramebuffer framebuffer)
    {
        if (framebuffer == null) { return; }
        int w = framebuffer.Width, h = framebuffer.Height;
        if (w < 1 || h < 1) { return; }

        if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h || force)
        {
            _bitmap = VncBitmap.CreateBitmap(w, h);
            VncBitmap.CopyFromFramebuffer(framebuffer, new VncRectangle(0, 0, w, h), _bitmap, 0, 0);
            _image.Source = _bitmap;
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    private void UpdateFramebuffer()
    {
        if (_client == null) { return; }
        UpdateFramebuffer(true, _client.Framebuffer);
    }

    private void HandleBell(object sender, EventArgs e)
    {
        try { MessageBeep(0); }
        catch { }
    }

    private void HandleConnected(object sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            _expectedClipboard = string.Empty;
            ClearInputState();
            Connected?.Invoke(this, EventArgs.Empty);
        });
    }

    private void HandleConnectionFailed(object sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            ClearInputState();
            ConnectionFailed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void HandleClosed(object sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            ClearInputState();
            Closed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void HandleReconnecting(object sender, ReconnectingEventArgs e)
    {
        RunOnUi(() => Reconnecting?.Invoke(this, e));
    }

    private void HandleReconnectFailed(object sender, EventArgs e)
    {
        RunOnUi(() => ReconnectFailed?.Invoke(this, EventArgs.Empty));
    }

    private void HandleFramebufferChanged(object sender, FramebufferChangedEventArgs e)
    {
        UpdateFpsCounter();

        RunOnUi(() =>
        {
            if (IsDesignMode || _client == null) { return; }

            VncFramebuffer framebuffer = _client.Framebuffer;
            if (framebuffer == null) { return; }

            lock (framebuffer.SyncRoot)
            {
                UpdateFramebuffer(false, framebuffer);

                if (_bitmap != null)
                {
                    for (int i = 0; i < e.RectangleCount; i++)
                    {
                        VncRectangle rect = e.GetRectangle(i);
                        VncBitmap.CopyFromFramebuffer(framebuffer, rect, _bitmap, rect.X, rect.Y);
                    }
                }
            }

            UpdateFpsOverlay();
            FramebufferChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void UpdateFpsCounter()
    {
        lock (_fpsLock)
        {
            _frameCount++;
            DateTime now = DateTime.UtcNow;
            double elapsed = (now - _lastFpsUpdate).TotalSeconds;
            if (elapsed >= 1.0)
            {
                _currentFps = _frameCount / elapsed;
                _frameCount = 0;
                _lastFpsUpdate = now;
            }
        }
    }

    private void UpdateFpsOverlay()
    {
        if (!ShowFps)
        {
            _fpsHost.Visibility = Visibility.Collapsed;
            return;
        }

        double fps;
        lock (_fpsLock)
        {
            fps = _currentFps;
        }

        _fpsText.Text = $"FPS:{fps:F0}";
        _fpsHost.Visibility = Visibility.Visible;
    }

    private void RaiseFramebufferChanged()
    {
        if (FramebufferChanged == null) { return; }
        RunOnUi(() => FramebufferChanged?.Invoke(this, EventArgs.Empty));
    }

    private void HandleRemoteClipboardChanged(object sender, RemoteClipboardChangedEventArgs e)
    {
        if (!AllowClipboardSharingFromServer) { return; }

        RunOnUi(() =>
        {
            if (e.Contents.Length == 0 || _expectedClipboard == e.Contents)
            {
                return;
            }

            try
            {
                _expectedClipboard = e.Contents;
                var package = new DataPackage();
                package.SetText(e.Contents);
                Clipboard.SetContent(package);
                Clipboard.Flush();
            }
            catch
            {
            }
        });
    }

    private void SendKeyUpdate(int keysym, bool pressed)
    {
        if (_client != null && AllowInput)
        {
            _client.SendKeyEvent(keysym, pressed);
        }
    }

    private void UpdatePointer(PointerRoutedEventArgs e)
    {
        _mouseLocation = e.GetCurrentPoint(this).Position;
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        int buttons = 0;
        if (props.IsLeftButtonPressed) { buttons |= 1 << 0; }
        if (props.IsMiddleButtonPressed) { buttons |= 1 << 1; }
        if (props.IsRightButtonPressed) { buttons |= 1 << 2; }
        _buttons = buttons;
        SendMouseUpdate();
    }

    private bool TryScaleMouseLocation(Point mouseLocation, out Point scaledLocation)
    {
        if (_bitmap != null && TryComputeDestinationBounds(new Size(ActualWidth, ActualHeight), out Rect destination)
            && destination.Width > 0 && destination.Height > 0)
        {
            int w = _bitmap.PixelWidth;
            int h = _bitmap.PixelHeight;
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
        if (_client != null && AllowInput && TryScaleMouseLocation(_mouseLocation, out Point scaledLocation))
        {
            _client.SendPointerEvent((int)scaledLocation.X, (int)scaledLocation.Y, _buttons);
        }
    }

    private void SendMouseScroll(bool down)
    {
        int mask = down ? (1 << 4) : (1 << 3);
        _buttons |= mask;
        SendMouseUpdate();
        _buttons &= ~mask;
        SendMouseUpdate();
    }

    private void LayoutImage(Size finalSize)
    {
        if (_bitmap == null || !TryComputeDestinationBounds(finalSize, out Rect destination))
        {
            _image.Visibility = Visibility.Collapsed;
            return;
        }

        _image.Visibility = Visibility.Visible;
        Canvas.SetLeft(_image, destination.X);
        Canvas.SetTop(_image, destination.Y);
        _image.Width = Math.Max(1, destination.Width);
        _image.Height = Math.Max(1, destination.Height);
    }

    private bool TryComputeDestinationBounds(Size controlSize, out Rect destination)
    {
        destination = default;
        if (_bitmap == null) { return false; }

        int bw = _bitmap.PixelWidth;
        int bh = _bitmap.PixelHeight;
        double cw = controlSize.Width;
        double ch = controlSize.Height;
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
                double bitmapAspect = (double)bw / bh;
                double controlAspect = cw / ch;
                double ow;
                double oh;
                if (bitmapAspect > controlAspect)
                {
                    ow = cw;
                    oh = Math.Max(1, cw / bitmapAspect);
                }
                else
                {
                    ow = Math.Max(1, ch * bitmapAspect);
                    oh = ch;
                }

                destination = new Rect((cw - ow) / 2, (ch - oh) / 2, ow, oh);
                break;

            case VncControlSizeMode.AutoSize:
                float scale = GetScaleFactor(bw, bh, cw, ch);
                if (scale < 1f)
                {
                    destination = new Rect(0, 0, bw * scale, bh * scale);
                }
                else
                {
                    destination = new Rect(0, 0, bw, bh);
                }

                break;

            case VncControlSizeMode.Clip:
            default:
                destination = new Rect(0, 0, bw, bh);
                break;
        }

        return true;
    }

    private static float GetScaleFactor(int remoteWidth, int remoteHeight, double controlWidth, double controlHeight)
    {
        if (remoteWidth <= 0 || remoteHeight <= 0 || controlWidth <= 0 || controlHeight <= 0)
        {
            return 1f;
        }

        float widthScale = (float)(controlWidth / remoteWidth);
        float heightScale = (float)(controlHeight / remoteHeight);
        return Math.Min(widthScale, heightScale);
    }

    private void RunOnUi(Action action)
    {
        if (action == null) { return; }

        var queue = DispatcherQueue;
        if (queue == null)
        {
            return;
        }

        if (queue.HasThreadAccess)
        {
            action();
            return;
        }

        queue.TryEnqueue(() => action());
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool MessageBeep(uint type);
}
