#region License

/*
RemoteViewing VNC Client/Server Library for .NET
Copyright (c) 2013 James F. Bellinger <http://software.seekye.com/remoteviewing>
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
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace RemoteViewing.Vnc;

/// <summary>
/// Connects to a remote VNC server and interacts with it.
/// </summary>
public partial class VncClient
{
    /// <summary>
    /// Occurs when a bell occurs on the remote server.
    /// </summary>
    public event EventHandler Bell;

    /// <summary>
    /// Occurs when the VNC client has successfully connected to the remote server.
    /// </summary>
    public event EventHandler Connected;

    /// <summary>
    /// Occurs when the VNC client has failed to connect to the server.
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
    public event EventHandler<FramebufferChangedEventArgs> FramebufferChanged;

    /// <summary>
    /// Occurs when the clipboard changes on the remote server.
    /// If you are implementing clipboard integration, use this to set the local clipboard.
    /// </summary>
    public event EventHandler<RemoteClipboardChangedEventArgs> RemoteClipboardChanged;

    private readonly VncStream _c = new();
    private readonly VncStatisticsHelper _stats = new();
    private int[] _colorMap;
    private VncClientConnectOptions _options;
    private VncPixelFormat _pixelFormat;
    private Version _serverVersion = new();
    private Thread _threadMain;
    private string _hostname;
    private int _port;
    private volatile bool _reconnecting; // Reconnection state
    private volatile bool _stopReconnecting;
    private readonly object _reconnectLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="VncClient"/> class.
    /// </summary>
    public VncClient()
    {
        MaxUpdateRate = 15;
    }

    /// <summary>
    /// Closes the connection with the remote server.
    /// </summary>
    public void Close()
    {
        StopReconnecting();
        var thread = _threadMain; _c.Close();
        thread?.Join();
    }

    /// <summary>
    /// Stops any ongoing reconnection attempts.
    /// </summary>
    public void StopReconnecting()
    {
        lock (_reconnectLock)
        {
            _stopReconnecting = true;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the client is currently attempting to reconnect.
    /// </summary>
    public bool IsReconnecting
    {
        get
        {
            lock (_reconnectLock)
            {
                return _reconnecting;
            }
        }
    }

    /// <summary>
    /// Connects to a VNC server with the specified hostname and port.
    /// </summary>
    /// <param name="hostname">The name of the host to connect to.</param>
    /// <param name="port">The port to connect on. 5900 is the usual for VNC.</param>
    /// <param name="options">Connection options, if any. You can specify a password here.</param>
    public void Connect(string hostname, int port = 5900, VncClientConnectOptions options = null)
    {
        Throw.If.Null(hostname, "hostname").Negative(port, "port");

        lock (_c.SyncRoot)
        {
            // Store connection parameters for reconnection
            _hostname = hostname;
            _port = port;
            _stopReconnecting = false;

            var client = new TcpClient();

            try
            {
                client.Connect(hostname, port);
            }
            catch (Exception)
            {
                OnConnectionFailed(); throw;
            }

            try
            {
                Connect(client.GetStream(), options);
            }
            catch (Exception)
            {
                client.Close(); throw;
            }
        }
    }

    /// <summary>
    /// Connects to a VNC server.
    /// </summary>
    /// <param name="stream">The stream containing the connection.</param>
    /// <param name="options">Connection options, if any. You can specify a password here.</param>
    public void Connect(Stream stream, VncClientConnectOptions options = null)
    {
        Throw.If.Null(stream, "stream");

        lock (_c.SyncRoot)
        {
            Close();

            _options = options ?? new VncClientConnectOptions();
            _c.Stream = stream;
            _stats.Reset();

            try
            {
                NegotiateVersion();
                NegotiateSecurity();
                NegotiateDesktop();
                NegotiateEncodings();
                NegotiatePixelFormat();
                InitFramebufferDecoder();
                SendFramebufferUpdateRequest(false);
            }
            catch (IOException e)
            {
                OnConnectionFailed();
                throw new VncException("IO error.", VncFailureReason.NetworkError, e);
            }
            catch (ObjectDisposedException e)
            {
                OnConnectionFailed();
                throw new VncException("Connection closed.", VncFailureReason.NetworkError, e);
            }
            catch (SocketException e)
            {
                OnConnectionFailed();
                throw new VncException("Connection failed.", VncFailureReason.NetworkError, e);
            }

            _threadMain = new Thread(ThreadMain)
            {
                Name = "RemoteViewing Client Connection",
                IsBackground = true,
            };
            _threadMain.Start();
        }
    }

    private void ThreadMain()
    {
        var requester = new Utility.PeriodicThread();
        IsConnected = true; OnConnected();

        bool shouldReconnect = false;

        try
        {
            requester.Start(() =>
            {
                SendFramebufferUpdateRequest(true);
                return true;
            }, () => MaxUpdateRate, true);

            while (true)
            {
                var command = _c.ReceiveByte();

                long ts0 = Stopwatch.GetTimestamp();

                switch (command)
                {
                    case 0:
                        HandleFramebufferUpdate();
                        _stats.Update(_c);
                        requester.Signal();
                        break;

                    case 1:
                        HandleSetColorMapEntries();
                        break;

                    case 2:
                        HandleBell();
                        break;

                    case 3:
                        HandleReceiveClipboardData();
                        break;

                    default:
                        VncStream.Require(false, "Unsupported command.",
                            VncFailureReason.UnrecognizedProtocolElement);
                        break;
                }

                long ts1 = Stopwatch.GetTimestamp();
                double cpuTime = (ts1 - ts0) / (double)Stopwatch.Frequency;
                _stats.AddCpuTime(cpuTime);
            }
        }
        catch (ObjectDisposedException)
        {
            // Check if auto-reconnect is enabled and this wasn't a user-initiated close
            shouldReconnect = ShouldAttemptReconnect();
        }
        catch (IOException)
        {
            // Network error - attempt reconnect if enabled
            shouldReconnect = ShouldAttemptReconnect();
        }
        catch (VncException)
        {
            // Protocol error - attempt reconnect if enabled
            shouldReconnect = ShouldAttemptReconnect();
        }

        requester.Stop();

        _c.Stream = null;
        IsConnected = false; OnClosed();

        // Attempt reconnection if enabled and not stopped
        if (shouldReconnect)
        {
            AttemptReconnect();
        }
    }

    private bool ShouldAttemptReconnect()
    {
        lock (_reconnectLock)
        {
            return _options?.AutoReconnect == true &&
                   !_stopReconnecting &&
                   !string.IsNullOrEmpty(_hostname);
        }
    }

    private void AttemptReconnect()
    {
        lock (_reconnectLock)
        {
            if (_stopReconnecting || _reconnecting)
            {
                return;
            }
            _reconnecting = true;
        }

        var options = _options;
        int maxAttempts = options?.MaxReconnectAttempts ?? VncClientConnectOptions.DefaultMaxReconnectAttempts;
        int delay = options?.ReconnectDelay ?? VncClientConnectOptions.DefaultReconnectDelay;
        int attempt = 0;

        try
        {
            while (true)
            {
                lock (_reconnectLock)
                {
                    if (_stopReconnecting)
                    {
                        return;
                    }
                }

                attempt++;

                // Check if we've exceeded the maximum attempts
                if (maxAttempts >= 0 && attempt > maxAttempts)
                {
                    OnReconnectFailed();
                    return;
                }

                // Raise the Reconnecting event
                var args = new ReconnectingEventArgs(attempt, maxAttempts);
                OnReconnecting(args);

                if (args.Cancel)
                {
                    OnReconnectFailed();
                    return;
                }

                // Wait before attempting to reconnect
                Thread.Sleep(delay);

                lock (_reconnectLock)
                {
                    if (_stopReconnecting)
                    {
                        return;
                    }
                }

                try
                {
                    // Attempt to reconnect using stored parameters
                    lock (_c.SyncRoot)
                    {
                        var client = new TcpClient();

                        try
                        {
                            client.Connect(_hostname, _port);
                        }
                        catch (Exception)
                        {
                            // Connection failed, continue to next attempt
                            continue;
                        }

                        try
                        {
                            // Reset stop flag since we're starting a new connection
                            lock (_reconnectLock)
                            {
                                _stopReconnecting = false;
                                _reconnecting = false;
                            }

                            Connect(client.GetStream(), options);
                            // Connection succeeded, exit the reconnection loop
                            return;
                        }
                        catch (Exception)
                        {
                            client.Close();
                            // Connection failed during negotiation, continue to next attempt
                            lock (_reconnectLock)
                            {
                                _reconnecting = true;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore exceptions during reconnection attempts
                }
            }
        }
        finally
        {
            lock (_reconnectLock)
            {
                _reconnecting = false;
            }
        }
    }

    private void NegotiateVersion()
    {
        _serverVersion = _c.ReceiveVersion();
        VncStream.Require(_serverVersion >= new Version(3, 7),
                              "RFB 3.7 not supported by server.",
                              VncFailureReason.UnsupportedProtocolVersion);

        // Reply with the highest protocol version that both sides support.
        // We currently implement RFB 3.7 and 3.8. Some servers (including our own) only accept 3.8,
        // so we should not hardcode 3.7 here, otherwise compatible servers will drop the connection.
        var clientVersion = new Version(3, 8);
        if (_serverVersion < clientVersion) { clientVersion = _serverVersion; }
        _c.SendVersion(clientVersion);
    }

    private void NegotiateSecurity()
    {
        int count = _c.ReceiveByte();
        if (count == 0)
        {
            string message = _c.ReceiveString().Trim('\0');
            VncStream.Require(false, message, VncFailureReason.ServerOfferedNoAuthenticationMethods);
        }

        var types = new List<AuthenticationMethod>();
        for (int i = 0; i < count; i++) { types.Add((AuthenticationMethod)_c.ReceiveByte()); }

        if (types.Contains(AuthenticationMethod.None))
        {
            _c.SendByte((byte)AuthenticationMethod.None);
        }
        else if (types.Contains(AuthenticationMethod.Password))
        {
            if (_options.Password == null)
            {
                var callback = _options.PasswordRequiredCallback;
                if (callback != null) { _options.Password = callback(this); }

                VncStream.Require(_options.Password != null,
                                      "Password required.",
                                      VncFailureReason.PasswordRequired);
            }

            _c.SendByte((byte)AuthenticationMethod.Password);

            var challenge = _c.Receive(16);
            var password = _options.Password;
            var response = new byte[16];

            using (new Utility.AutoClear(challenge))
            using (new Utility.AutoClear(response))
            {
                VncPasswordChallenge.GetChallengeResponse(challenge, _options.Password, response);
                _c.Send(response);
            }
        }
        else
        {
            VncStream.Require(false,
                                  "No supported authentication methods.",
                                  VncFailureReason.NoSupportedAuthenticationMethods);
        }

        uint status = _c.ReceiveUInt32BE();
        if (status != 0)
        {
            string message = _c.ReceiveString().Trim('\0');
            VncStream.Require(false, message, VncFailureReason.AuthenticationFailed);
        }
    }

    private void NegotiateDesktop()
    {
        _c.SendByte((byte)(_options.ShareDesktop ? 1 : 0));

        var width = _c.ReceiveUInt16BE(); VncStream.SanityCheck(width > 0 && width < 0x8000);
        var height = _c.ReceiveUInt16BE(); VncStream.SanityCheck(height > 0 && height < 0x8000);

        VncPixelFormat pixelFormat;
        try
        {
            pixelFormat = VncPixelFormat.Decode(_c.Receive(VncPixelFormat.Size), 0);
        }
        catch (ArgumentException e)
        {
            throw new VncException("Unsupported pixel format.",
                                           VncFailureReason.UnsupportedPixelFormat, e);
        }

        var name = _c.ReceiveString();
        Framebuffer = new VncFramebuffer(name, width, height);
        _colorMap = [];
        _pixelFormat = pixelFormat;
    }

    private void NegotiatePixelFormat()
    {
        if (_options.PixelFormat != null)
        {
            _colorMap = [];
            _pixelFormat = _options.PixelFormat;

            var pixelFormatBytes = new byte[VncPixelFormat.Size];
            _pixelFormat.Encode(pixelFormatBytes, 0);

            _c.Send([0, 0, 0, 0]);
            _c.Send(pixelFormatBytes);
        }
    }

    private void NegotiateEncodings()
    {
        var encodings = new VncEncoding[]
        {
            VncEncoding.Zlib,
            VncEncoding.Hextile,
            VncEncoding.CopyRect,
            VncEncoding.Raw,
            VncEncoding.PseudoDesktopSize
        };

        _c.Send([(byte)2, (byte)0]);
        _c.SendUInt16BE((ushort)encodings.Length);
        foreach (var encoding in encodings) { _c.SendUInt32BE((uint)encoding); }
    }

    private void SendFramebufferUpdateRequest(bool incremental)
    {
        var p = new byte[10];

        p[0] = (byte)3; p[1] = (byte)(incremental ? 1 : 0);
        VncUtility.EncodeUInt16BE(p, 2, (ushort)0);
        VncUtility.EncodeUInt16BE(p, 4, (ushort)0);
        VncUtility.EncodeUInt16BE(p, 6, (ushort)Framebuffer.Width);
        VncUtility.EncodeUInt16BE(p, 8, (ushort)Framebuffer.Height);

        _c.Send(p);
    }

    /// <summary>
    /// Notifies the server that the local clipboard has changed.
    /// If you are implementing clipboard integration, use this to set the remote clipboard.
    /// </summary>
    /// <param name="data">The contents of the local clipboard.</param>
    public void SendLocalClipboardChange(string data)
    {
        Throw.If.Null(data, "data");

        var bytes = VncStream.EncodeString(data);

        var p = new byte[8 + bytes.Length];

        p[0] = (byte)6;
        VncUtility.EncodeUInt32BE(p, 4, (uint)bytes.Length);
        Array.Copy(bytes, 0, p, 8, bytes.Length);

        if (IsConnected) { _c.Send(p); }
    }

    /// <summary>
    /// Sends a key event to the VNC server to indicate a key has been pressed or released.
    /// </summary>
    /// <param name="keysym">The X11 keysym of the key. For many keys this is the ASCII value.</param>
    /// <param name="pressed"><c>true</c> for a key press event, or <c>false</c> for a key release event.</param>
    public void SendKeyEvent(int keysym, bool pressed)
    {
        var p = new byte[8];

        p[0] = (byte)4; p[1] = (byte)(pressed ? 1 : 0);
        VncUtility.EncodeUInt32BE(p, 4, (uint)keysym);

        if (IsConnected) { _c.Send(p); }
    }

    /// <summary>
    /// Sends a pointer event to the VNC server to indicate mouse motion, a button click, etc.
    /// </summary>
    /// <param name="x">The X coordinate of the mouse.</param>
    /// <param name="y">The Y coordinate of the mouse.</param>
    /// <param name="pressedButtons">
    ///     A bit mask of pressed mouse buttons, in X11 convention: 1 is left, 2 is middle, and 4 is right.
    ///     Mouse wheel scrolling is treated as a button event: 8 for up and 16 for down.
    /// </param>
    public void SendPointerEvent(int x, int y, int pressedButtons)
    {
        var p = new byte[6];

        p[0] = (byte)5; p[1] = (byte)pressedButtons;
        VncUtility.EncodeUInt16BE(p, 2, (ushort)x);
        VncUtility.EncodeUInt16BE(p, 4, (ushort)y);

        if (IsConnected) { _c.Send(p); }
    }

    // Assumes we are already locked.
    unsafe void CopyToFramebuffer(int tx, int ty, int w, int h, byte[] pixels)
    {
        var fb = Framebuffer;
        var pixelFormat = _pixelFormat;

        fixed (byte* sourcePixels = pixels)
        fixed (int* targetPixels = fb.GetPixels())
        {
            VncPixelFormat.Copy(
                (IntPtr)sourcePixels, w * pixelFormat.BytesPerPixel, pixelFormat, new VncRectangle(0, 0, w, h),
                (IntPtr)targetPixels, fb.Width * 4, VncPixelFormat.Format32bpp, tx, ty, _colorMap);
        }
    }

    void HandleSetColorMapEntries()
    {
        _c.ReceiveByte(); // padding

        int firstColor = _c.ReceiveUInt16BE();
        int numColors = _c.ReceiveUInt16BE();

        if (firstColor + numColors > _colorMap.Length)
        {
            Array.Resize(ref _colorMap, firstColor + numColors);
        }

        var p = VncPixelFormat.Format32bpp;
        for (int i = 0; i < numColors; i++)
        {
            int r = _c.ReceiveUInt16BE();
            int g = _c.ReceiveUInt16BE();
            int b = _c.ReceiveUInt16BE();

            _colorMap[firstColor + i] =
                (r >> (16 - p.RedBits)) << p.RedShift |
                (g >> (16 - p.GreenBits)) << p.GreenShift |
                (b >> (16 - p.BlueBits)) << p.BlueShift;
        }
    }

    void HandleBell()
    {
        OnBell();
    }

    void HandleReceiveClipboardData()
    {
        _c.Receive(3); // padding

        var clipboard = _c.ReceiveString(0xffffff);

        OnRemoteClipboardChanged(new RemoteClipboardChangedEventArgs(clipboard));
    }

    protected virtual void OnBell()
    {
        RaiseBell();
    }

    protected void RaiseBell()
    {
        Bell?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnConnected()
    {
        RaiseConnected();
    }

    protected void RaiseConnected()
    {
        Connected?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnConnectionFailed()
    {
        RaiseConnectionFailed();
    }

    protected void RaiseConnectionFailed()
    {
        ConnectionFailed?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnClosed()
    {
        RaiseClosed();
    }

    protected void RaiseClosed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnReconnecting(ReconnectingEventArgs e)
    {
        RaiseReconnecting(e);
    }

    protected void RaiseReconnecting(ReconnectingEventArgs e)
    {
        Reconnecting?.Invoke(this, e);
    }

    protected virtual void OnReconnectFailed()
    {
        RaiseReconnectFailed();
    }

    protected void RaiseReconnectFailed()
    {
        ReconnectFailed?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnFramebufferChanged(FramebufferChangedEventArgs e)
    {
        RaiseFramebufferChanged(e);
    }

    protected void RaiseFramebufferChanged(FramebufferChangedEventArgs e)
    {
        FramebufferChanged?.Invoke(this, e);
    }

    protected virtual void OnRemoteClipboardChanged(RemoteClipboardChangedEventArgs e)
    {
        RaiseRemoteClipboardChanged(e);
    }

    protected void RaiseRemoteClipboardChanged(RemoteClipboardChangedEventArgs e)
    {
        RemoteClipboardChanged?.Invoke(this, e);
    }

    public VncClientStatistics GetStatistics()
    {
        var si = _stats;
        var so = new VncClientStatistics();

        lock (si.SyncRoot)
        {
            si.Update(_c);
            so.BytesReceived = si.BytesReceived;
            so.BytesReceivedPerSecond = si.BytesReceivedPerSecond;
            so.BytesSent = si.BytesSent;
            so.BytesSentPerSecond = si.BytesSentPerSecond;
            so.CpuUsage = si.CpuUsage;
        }

        return so;
    }

    /// <summary>
    /// The framebuffer for the VNC session.
    /// </summary>
    public VncFramebuffer Framebuffer { get; private set; }

    /// <summary>
    /// <c>true</c> if the client is connected to a server.
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// The max rate to request framebuffer updates at, in frames per second.
    ///
    /// The default is 15.
    /// </summary>
    public double MaxUpdateRate
    {
        get;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException("Max update rate must be positive.", (Exception)null);

            field = value;
        }
    }

    /// <summary>
    /// The protocol version of the server.
    /// </summary>
    public Version ServerVersion => _serverVersion;

    /// <summary>
    /// Store anything you want here.
    /// </summary>
    public object UserData { get; set; }
}
