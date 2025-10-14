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

#define PRINT_DEBUG_BANDWIDTH_STATS

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using RemoteViewing.Utility;

namespace RemoteViewing.Vnc.Server
{
    /// <summary>
    /// Serves a VNC client with framebuffer information and receives keyboard and mouse interactions.
    /// </summary>
    public class VncServerSession
    {
        /// <summary>
        /// Occurs when the VNC client provides a password.
        /// Respond to this event by accepting or rejecting the password.
        /// </summary>
        public event EventHandler<PasswordProvidedEventArgs> PasswordProvided;

        /// <summary>
        /// Occurs when the client requests access to the desktop.
        /// It may request exclusive or shared access -- this event will relay that information.
        /// </summary>
        public event EventHandler<CreatingDesktopEventArgs> CreatingDesktop;

        /// <summary>
        /// Occurs when the VNC client has successfully connected to the server.
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
        /// Occurs when the framebuffer needs to be captured.
        /// If you have not called <see cref="VncServerSession.SetFramebufferSource"/>, alter the framebuffer
        /// in response to this event.
        /// 
        /// <see cref="VncServerSession.FramebufferUpdateRequestLock"/> is held automatically while this event is raised.
        /// </summary>
        public event EventHandler FramebufferCapturing;

        /// <summary>
        /// Occurs when the framebuffer needs to be updated.
        /// If you do not set <see cref="HandledEventArgs.Handled"/> on <see cref="FramebufferUpdatingEventArgs"/>,
        /// <see cref="VncServerSession"/> will determine the updated regions itself.
        /// 
        /// <see cref="VncServerSession.FramebufferUpdateRequestLock"/> is held automatically while this event is raised.
        /// </summary>
        public event EventHandler<FramebufferUpdatingEventArgs> FramebufferUpdating;

        /// <summary>
        /// Occurs when the framebuffer has been updated.
        /// </summary>
        public event EventHandler FramebufferUpdated;

        /// <summary>
        /// Occurs when a key has been pressed or released.
        /// </summary>
        public event EventHandler<KeyChangedEventArgs> KeyChanged;

        /// <summary>
        /// Occurs on a mouse movement, button click, etc.
        /// </summary>
        public event EventHandler<PointerChangedEventArgs> PointerChanged;

        /// <summary>
        /// Occurs when the clipboard changes on the remote client.
        /// If you are implementing clipboard integration, use this to set the local clipboard.
        /// </summary>
        public event EventHandler<RemoteClipboardChangedEventArgs> RemoteClipboardChanged;

        long _debugRawPixelBytes;
        long _debugHexPixelBytesIn, _debugHexPixelBytesOut;
        long _debugZLibPixelBytesIn, _debugZLibPixelBytesOut;

        struct Rectangle
        {
            public VncRectangle Region;
            public VncEncoding Encoding;
            public byte[] Contents;
            public int ContentsOffset;
            public int ContentsSize;
        }
        VncStream _c = new VncStream();
        VncStatisticsHelper _stats = new VncStatisticsHelper();
        VncEncoding[] _clientEncoding = new VncEncoding[0];
        VncPixelFormat _clientPixelFormat;
        int _clientWidth, _clientHeight;
        Version _clientVersion = new Version();
        VncServerSessionOptions _options;
        VncFramebufferCache _fbuAutoCache;
        List<Rectangle> _fbuRectangles = new List<Rectangle>();
        object _fbuSync = new object();
        IVncFramebufferSource _fbSource;
        double _maxUpdateRate;
        Utility.PeriodicThread _requester;
        object _specialSync = new object();
        Thread _threadMain;
        MemoryStream _zlibMemoryStream;
        bool _zlibHeaderSent;
        unsafe ZLib.z_stream* _zlib;
        bool _zlibInit;

        /// <summary>
        /// Initializes a new instance of the <see cref="VncServerSession"/> class.
        /// </summary>
        public unsafe VncServerSession()
        {
            MaxUpdateRate = 15;

            try
            {
                _zlib = (ZLib.z_stream*)ZLib.calloc(1, sizeof(ZLib.z_stream));

                int zret = ZLib.deflateInit_(_zlib, ZLib.Z_BEST_SPEED);
                if (zret == ZLib.Z_OK)
                {
                    _zlibInit = true;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                _zlib = null;
            }
        }

        ~VncServerSession()
        {
            DisposeOfZLib();
        }

        unsafe void DisposeOfZLib()
        {
            try
            {
                if (_zlib != null)
                {
                    if (_zlibInit)
                    {
                        int zret = ZLib.deflateEnd(_zlib);
                      //Debug.Assert(zret == ZLib.Z_OK);
                        _zlibInit = false;
                    }

                    ZLib.free(_zlib);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
            finally
            {
                _zlib = null;
            }
        }

        /// <summary>
        /// Closes the connection with the remote client.
        /// </summary>
        public void Close()
        {
            var thread = _threadMain; _c.Close();
            if (thread != null) { thread.Join(); }
        }

        /// <summary>
        /// Starts a session with a VNC client.
        /// </summary>
        /// <param name="stream">The stream containing the connection.</param>
        /// <param name="options">Session options, if any.</param>
        public void Connect(Stream stream, VncServerSessionOptions options = null)
        {
            Throw.If.Null(stream, "stream");

            lock (_c.SyncRoot)
            {
                Close();

                _options = options ?? new VncServerSessionOptions();
                _c.Stream = stream;

                _threadMain = new Thread(ThreadMain);
                _threadMain.Name = "RemoteViewing Server Connected User";
                _threadMain.IsBackground = true;
                _threadMain.Start();
            }
        }

        void ThreadMain()
        {
            _requester = new Utility.PeriodicThread();

            try
            {
                InitFramebufferEncoder();

                AuthenticationMethod[] methods;
                NegotiateVersion(out methods);
                NegotiateSecurity(methods);
                NegotiateDesktop();
                NegotiateEncodings();

                _requester.Start(FramebufferSendChanges, () => MaxUpdateRate, false);

                IsConnected = true; OnConnected();

                while (true)
                {
                    var command = _c.ReceiveByte();

                    switch (command)
                    {
                        case 0:
                            HandleSetPixelFormat();
                            break;

                        case 2:
                            HandleSetEncodings();
                            break;

                        case 3:
                            HandleFramebufferUpdateRequest();
                            break;

                        case 4:
                            HandleKeyEvent();
                            break;

                        case 5:
                            HandlePointerEvent();
                            break;

                        case 6:
                            HandleReceiveClipboardData();
                            break;

                        default:
                            VncStream.Require(false, "Unsupported command.",
                                              VncFailureReason.UnrecognizedProtocolElement);
                            break;
                    }
                }
            }
            catch (ObjectDisposedException)
            {

            }
            catch (IOException e)
            {

            }
            catch (VncException e)
            {
                Debug.WriteLine(e);
            }

            _requester.Stop();

            var stream = _c.Stream;
            if (stream != null)
            {
                try { stream.Flush(); }
                catch (IOException) { }

                try { stream.Close(); }
                catch (IOException) { }

                _c.Stream = null;
            }

            if (IsConnected)
            {
                IsConnected = false; OnClosed();
            }
            else
            {
                OnConnectionFailed();
            }
        }

        void NegotiateVersion(out AuthenticationMethod[] methods)
        {
            _c.SendVersion(new Version(3, 8));

            _clientVersion = _c.ReceiveVersion();
            if (_clientVersion == new Version(3, 8))
            {
                methods = new[]
                {
                    _options.AuthenticationMethod == AuthenticationMethod.Password
                        ? AuthenticationMethod.Password : AuthenticationMethod.None
                };
            }
            else
            {
                methods = new AuthenticationMethod[0];
            }
        }

        void NegotiateSecurity(AuthenticationMethod[] methods)
        {
            _c.SendByte((byte)methods.Length);
            VncStream.Require(methods.Length > 0,
                                  "Client is not allowed in.",
                                  VncFailureReason.NoSupportedAuthenticationMethods);
            foreach (var method in methods) { _c.SendByte((byte)method); }

            var selectedMethod = (AuthenticationMethod)_c.ReceiveByte();
            VncStream.Require(methods.Contains(selectedMethod),
                              "Invalid authentication method.",
                              VncFailureReason.UnrecognizedProtocolElement);

            bool success = true; string authenticationFailedMessage = null;
            if (selectedMethod == AuthenticationMethod.Password)
            {
                var challenge = VncPasswordChallenge.GenerateChallenge();
                using (new Utility.AutoClear(challenge))
                {
                    _c.Send(challenge);

                    var response = _c.Receive(16);
                    using (new Utility.AutoClear(response))
                    {
                        var e = new PasswordProvidedEventArgs(challenge, response);
                        OnPasswordProvided(e);
                        success = e.IsAuthenticated;
                        authenticationFailedMessage = e.AuthenticationFailedMessage;
                    }
                }
            }

            if (success)
            {
                _c.SendUInt32BE(0);
            }
            else
            {
                if (authenticationFailedMessage == null) { authenticationFailedMessage = "Failed to authenticate."; }

                _c.SendUInt32BE((uint)1);
                _c.SendString(authenticationFailedMessage, true);
                VncStream.Require(false,
                                  authenticationFailedMessage,
                                  VncFailureReason.AuthenticationFailed);
            }
        }

        void NegotiateDesktop()
        {
            byte shareDesktopSetting = _c.ReceiveByte();
            bool shareDesktop = shareDesktopSetting != 0;

            var e = new CreatingDesktopEventArgs(shareDesktop);
            OnCreatingDesktop(e);

            var fbSource = _fbSource;
            Framebuffer = fbSource != null ? fbSource.Capture() : null;
            VncStream.Require(Framebuffer != null,
                              "No framebuffer. Make sure you've called SetFramebufferSource. It can be set to a VncFramebuffer.",
                              VncFailureReason.SanityCheckFailed);
            _clientPixelFormat = VncPixelFormat.Format32bpp;
            _clientWidth = Framebuffer.Width; _clientHeight = Framebuffer.Height;
            _fbuAutoCache = null;
            
            _c.SendUInt16BE((ushort)Framebuffer.Width);
            _c.SendUInt16BE((ushort)Framebuffer.Height);

            var pixelFormat = new byte[VncPixelFormat.Size];
            _clientPixelFormat.Encode(pixelFormat, 0);
            _c.Send(pixelFormat);
            _c.SendString(Framebuffer.Name, true);
        }

        void NegotiateEncodings()
        {
            _clientEncoding = new VncEncoding[0]; // Default to no encodings.
        }

        void HandleSetPixelFormat()
        {
            _c.Receive(3);

            var pixelFormat = _c.Receive(VncPixelFormat.Size);
            _clientPixelFormat = VncPixelFormat.Decode(pixelFormat, 0);

            if (_clientPixelFormat.IsPalettized)
            {
                // Fine. We'll support this with 2-3-3.
                lock (_c.SyncRoot)
                {
                    if (!IsConnected) { return; }
                    _c.SendByte(1);
                    _c.SendByte(0);
                    _c.SendUInt16BE(0);
                    _c.SendUInt16BE(256);
                    for (int color = 0; color < 256; color++)
                    {
                        ushort red = (ushort)(((color >> 6) & 0x3) * 21845);
                        ushort green = (ushort)(((color >> 3) & 0x7) * 9362);
                        ushort blue = (ushort)(((color >> 0) & 0x7) * 9362);
                        _c.SendUInt16BE(red);
                        _c.SendUInt16BE(green);
                        _c.SendUInt16BE(blue);
                    }
                }
            }
        }

        void HandleSetEncodings()
        {
            _c.Receive(1);

            int encodingCount = _c.ReceiveUInt16BE(); VncStream.SanityCheck(encodingCount <= 0x1ff);
            var clientEncoding = new VncEncoding[encodingCount];
            for (int i = 0; i < clientEncoding.Length; i++)
            {
                uint encoding = _c.ReceiveUInt32BE();
                clientEncoding[i] = (VncEncoding)encoding;
            }
            _clientEncoding = clientEncoding;
        }

        void HandleFramebufferUpdateRequest()
        {
            var incremental = _c.ReceiveByte() != 0;
            var region = _c.ReceiveRectangle();

            lock (FramebufferUpdateRequestLock)
            {
                FramebufferUpdateRequest = new FramebufferUpdateRequest(incremental, region);
                FramebufferChanged();
            }
        }

        void HandleKeyEvent()
        {
            var pressed = _c.ReceiveByte() != 0; _c.Receive(2);
            var keysym = (int)_c.ReceiveUInt32BE();

            OnKeyChanged(new KeyChangedEventArgs(keysym, pressed));
        }

        void HandlePointerEvent()
        {
            int pressedButtons = _c.ReceiveByte();
            int x = _c.ReceiveUInt16BE();
            int y = _c.ReceiveUInt16BE();

            OnPointerChanged(new PointerChangedEventArgs(x, y, pressedButtons));
        }

        void HandleReceiveClipboardData()
        {
            _c.Receive(3); // padding

            var clipboard = _c.ReceiveString(0xffffff);

            OnRemoteClipboardChanged(new RemoteClipboardChangedEventArgs(clipboard));
        }

        /// <summary>
        /// Tells the client to play a bell sound.
        /// </summary>
        public void Bell()
        {
            lock (_c.SyncRoot)
            {
                if (!IsConnected) { return; }
                _c.SendByte((byte)2);
            }
        }

        /// <summary>
        /// Notifies the client that the local clipboard has changed.
        /// If you are implementing clipboard integration, use this to set the remote clipboard.
        /// </summary>
        /// <param name="data">The contents of the local clipboard.</param>
        public void SendLocalClipboardChange(string data)
        {
            Throw.If.Null(data, "data");

            lock (_c.SyncRoot)
            {
                if (!IsConnected) { return; }
                _c.SendByte((byte)3);
                _c.Send(new byte[3]);
                _c.SendString(data, true);
            }
        }

        /// <summary>
        /// Sets the framebuffer source.
        /// </summary>
        /// <param name="source">The framebuffer source, or <c>null</c> if you intend to handle the framebuffer manually.</param>
        public void SetFramebufferSource(IVncFramebufferSource source)
        {
            _fbSource = source;
        }

        /// <summary>
        /// Notifies the framebuffer update thread to check for recent changes.
        /// </summary>
        public void FramebufferChanged()
        {
            _requester.Signal();
        }

        bool FramebufferSendChanges()
        {
            var e = new FramebufferUpdatingEventArgs();

            long ts0 = Stopwatch.GetTimestamp();

            lock (FramebufferUpdateRequestLock)
            {
                if (FramebufferUpdateRequest != null)
                {
                    var fbSource = _fbSource;
                    if (fbSource != null)
                    {
                        var newFramebuffer = fbSource.Capture();
                        if (newFramebuffer != null && newFramebuffer != Framebuffer)
                        {
                            Framebuffer = newFramebuffer;
                        }
                    }

                    OnFramebufferCapturing();
                    OnFramebufferUpdating(e);

                    if (!e.Handled)
                    {
                        if (_fbuAutoCache == null || _fbuAutoCache.Framebuffer != Framebuffer)
                        {
                            _fbuAutoCache = new VncFramebufferCache(Framebuffer);
                        }

                        e.Handled = true;
                        e.SentChanges = _fbuAutoCache.RespondToUpdateRequest(this);
                    }
                }
            }

            long ts1 = Stopwatch.GetTimestamp();
            double cpuTime = (double)(ts1 - ts0) / (double)Stopwatch.Frequency;
            _stats.AddCpuTime(cpuTime);

            _stats.Update(_c);

            if (e.SentChanges) { OnFramebufferUpdated(EventArgs.Empty); }

            return e.SentChanges;
        }

        /// <summary>
        /// Begins a manual framebuffer update.
        /// 
        /// Do not call this method without holding <see cref="VncServerSession.FramebufferUpdateRequestLock"/>.
        /// </summary>
        public void FramebufferManualBeginUpdate()
        {
            _debugRawPixelBytes = 0;
            _debugHexPixelBytesIn = 0; _debugHexPixelBytesOut = 0;
            _debugZLibPixelBytesIn = 0; _debugZLibPixelBytesOut = 0;

            _fbuUpdateBytesPtr = 0;

            _fbuRectangles.Clear();
        }

        void FlushUpdate()
        {
            FramebufferManualEndUpdate(); FramebufferManualBeginUpdate();
        }

        void AddRegion(VncRectangle region, VncEncoding encoding, byte[] contents)
        {
            AddRegion(region, encoding, contents, 0, contents.Length);
        }
        
        void AddRegion(VncRectangle region, VncEncoding encoding, byte[] contents, int contentsOffset, int contentsSize)
        {
            _fbuRectangles.Add(new Rectangle()
            {
                Region = region,
                Encoding = encoding,
                Contents = contents,
                ContentsOffset = contentsOffset,
                ContentsSize = contentsSize
            });

            // Avoid the overflow of updated rectangle count.
            // NOTE: EndUpdate may implicitly add one for desktop resizing.
            if (_fbuRectangles.Count >= ushort.MaxValue - 1)
            {
                FlushUpdate();
            }
        }

        /// <summary>
        /// Queues an update corresponding to one region of the framebuffer being copied to another.
        /// 
        /// Do not call this method without holding <see cref="VncServerSession.FramebufferUpdateRequestLock"/>.
        /// </summary>
        public void FramebufferManualCopyRegion(VncRectangle target, int sourceX, int sourceY)
        {
            if (!_clientEncoding.Contains(VncEncoding.CopyRect))
            {
                var source = new VncRectangle(sourceX, sourceY, target.Width, target.Height);
                var region = VncRectangle.Union(source, target);

                if (region.Area > source.Area + target.Area) { FramebufferManualInvalidate(new[] { source, target }); }
                else                                         { FramebufferManualInvalidate(region); }
                return;
            }

            var contents = new byte[4];
            VncUtility.EncodeUInt16BE(contents, 0, (ushort)sourceX);
            VncUtility.EncodeUInt16BE(contents, 2, (ushort)sourceY);
            AddRegion(target, VncEncoding.CopyRect, contents);
        }

        void InitFramebufferEncoder()
        {
            _zlibMemoryStream = new MemoryStream();
            _zlibHeaderSent = false;
        }

        /// <summary>
        /// Queues an update for the entire framebuffer.
        /// 
        /// Do not call this method without holding <see cref="VncServerSession.FramebufferUpdateRequestLock"/>.
        /// </summary>
        public void FramebufferManualInvalidateAll()
        {
            FramebufferManualInvalidate(new VncRectangle(0, 0, Framebuffer.Width, Framebuffer.Height));
        }

        byte[] _fbuUpdateBytes;
        int _fbuUpdateBytesPtr;

        int AllocUpdateBytes(int count)
        {
            if (_fbuUpdateBytesPtr + count > _fbuUpdateBytes.Length)
            {
                FlushUpdate();
            }

            int curPtr = _fbuUpdateBytesPtr;
            int newPtr = curPtr + count;
            if (newPtr > _fbuUpdateBytes.Length)
            {
                throw new Exception("Internal error. This should never happen. It indicates that we drastically undersized the update buffer.");
            }

            _fbuUpdateBytesPtr = newPtr;
            return curPtr;
        }

        static int GetZLibUpdateMaxBytes(int rawSize)
        {
            return 4 + (int)ZLib.compressBound((ulong)rawSize);
        }

        /// <summary>
        /// Queues an update for the specified region.
        /// 
        /// Do not call this method without holding <see cref="VncServerSession.FramebufferUpdateRequestLock"/>.
        /// </summary>
        /// <param name="region">The region to invalidate.</param>
        public unsafe void FramebufferManualInvalidate(VncRectangle region)
        {
            bool zlib = _clientEncoding.Contains(VncEncoding.Zlib) && _zlibInit;

            var fb = Framebuffer; var cpf = _clientPixelFormat;
            int cw = _clientWidth, ch = _clientHeight, bpp = cpf.BytesPerPixel;
            region = VncRectangle.Intersect(region, new VncRectangle(0, 0, cw, ch));
            if (region.IsEmpty) { return; }

            int updateBytesNeeded = cw * ch * bpp; // Technically we could allocate a bit more for protocol overhead, but in practice, "if it overflows, just send it all" works and we don't encounter this often.
            if (zlib) { updateBytesNeeded = GetZLibUpdateMaxBytes(updateBytesNeeded); }
            if (_fbuUpdateBytes == null || _fbuUpdateBytes.Length != updateBytesNeeded)
            {
                _fbuUpdateBytes = new byte[updateBytesNeeded];
                _fbuUpdateBytesPtr = 0;
            }

            int x = region.X, y = region.Y, w = region.Width, h = region.Height;

            // Previously this reallocated contents every send. Let's not do that.
            fixed (int* sourcePixels = fb.GetPixels())
            {
                // Are ALL the pixels the SAME, and is w = 16 and h = 16? If so, we'll use "Hextile".
                // This is a greatly restricted case, but it'll cover a lot of normal desktop use,
                // since that is our scan size right now.
                if (w == 16 && h == 16)
                {
                    if (_clientEncoding.Contains(VncEncoding.Hextile))
                    {
                        uint* pixel = (uint*)(sourcePixels + y * cw + x); int stride = cw - 16;
                        uint pixel0 = *pixel;

                        // fast path for 64-bit programs
                        if (IntPtr.Size == 8 && (stride & 1) == 0 && (w & 1) == 0 && ((ulong)pixel & 7) == 0)
                        {
                            ulong* pixel64 = (ulong*)pixel;
                            ulong pixel640 = (ulong)pixel0 << 32 | pixel0;
                            int stride64 = stride >> 1;

                            for (int iy = 0; iy < 16; iy++)
                            {
                                for (int ix = 0; ix < 8; ix++)
                                {
                                    if (*pixel64 != pixel640) { goto notHexTile; }
                                    pixel64++;
                                }
                                pixel64 += stride64;
                            }
                        }
                        else
                        {
                            for (int iy = 0; iy < 16; iy++)
                            {
                                for (int ix = 0; ix < 16; ix++)
                                {
                                    if (*pixel != pixel0) { goto notHexTile; }
                                    pixel++;
                                }
                                pixel += stride;
                            }
                        }

                        _debugHexPixelBytesIn += w * h * bpp;
                        _debugHexPixelBytesOut += 1 + bpp;

                        int size = 1 + bpp;
                        int offset = AllocUpdateBytes(size);
                        var hexContents = _fbuUpdateBytes;
                        hexContents[offset] = 2; // subencoding 'Background Specified = 2'

                        fixed (byte* targetPixel = hexContents)
                        {
                            VncPixelFormat.Copy((IntPtr)sourcePixels, fb.Width * 4, VncPixelFormat.Format32bpp, new VncRectangle(x, y, 1, 1),
                                                (IntPtr)(targetPixel + offset + 1), bpp, cpf);
                        }

                        AddRegion(region, VncEncoding.Hextile, hexContents, offset, size);
                        return;
                    }

                notHexTile: ;
                }

                {
                    int rawSize = w * h * bpp;
                    int allocSize = zlib ? GetZLibUpdateMaxBytes(rawSize) : rawSize;
                    int rawOffset = AllocUpdateBytes(allocSize);
                    var rawContents = _fbuUpdateBytes;

                    fixed (byte* targetPixels = rawContents)
                    {
                        VncPixelFormat.Copy((IntPtr)sourcePixels, fb.Width * 4, VncPixelFormat.Format32bpp, region,
                                            (IntPtr)(targetPixels + rawOffset), w * bpp, cpf);
                    }

                    if (zlib)
                    {
                        int zsize;

                        if (allocSize >= 65536)
                        {
                            // This only happens when we are invalidating the whole screen (at connect, for example).
                            // Let's not waste RAM most of the time for this case.
                            // (To be fair, I *could* make DoCompress use a buffer and do compression in-place. That would avoid this. If I get time...)
                            byte[] zlibOut = new byte[allocSize];

                            fixed (byte* rawIn = rawContents)
                            fixed (byte* zlibOutPtr = zlibOut)
                            {
                                zsize = DoCompress(rawIn + rawOffset, rawSize, zlibOutPtr, allocSize);
                            }

                            Array.Copy(zlibOut, 0, rawContents, rawOffset + 4, zsize);
                        }
                        else
                        {
                            byte* zlibOut = stackalloc byte[allocSize];

                            fixed (byte* rawIn = rawContents)
                            {
                                zsize = DoCompress(rawIn + rawOffset, rawSize, zlibOut, allocSize);
                            }

                            Marshal.Copy((IntPtr)zlibOut, rawContents, rawOffset + 4, zsize);
                        }

                        VncUtility.EncodeUInt32BE(rawContents, rawOffset, (uint)zsize);

                        _debugZLibPixelBytesIn += w * h * bpp;
                        _debugZLibPixelBytesOut += zsize + 4;
                        AddRegion(region, VncEncoding.Zlib, rawContents, rawOffset, zsize + 4);
                    }
                    else
                    {
                        _debugRawPixelBytes += rawSize;
                        AddRegion(region, VncEncoding.Raw, rawContents, rawOffset, rawSize);
                    }
                }
            }
        }

        unsafe int DoCompress(byte* inData, int inLength, byte* outData, int outLength)
        {
            _zlib->next_in = inData;
            _zlib->avail_in = (uint)inLength;
            _zlib->next_out = outData;
            _zlib->avail_out = (uint)outLength;
            int zerr = ZLib.deflate(_zlib, ZLib.Z_SYNC_FLUSH);
            VncStream.Require(zerr == ZLib.Z_OK, "ZLib failed.", VncFailureReason.Unknown);
            VncStream.Require(_zlib->avail_in == 0, "ZLib did not compress all data.", VncFailureReason.Unknown);

            int received = outLength - (int)_zlib->avail_out;
            VncStream.Require((uint)received <= outLength, "ZLib somehow had extra data.", VncFailureReason.Unknown);
            return received;
        }

        /// <summary>
        /// Queues an update for each of the specified regions.
        /// 
        /// Do not call this method without holding <see cref="VncServerSession.FramebufferUpdateRequestLock"/>.
        /// </summary>
        /// <param name="regions">The regions to invalidate.</param>
        public void FramebufferManualInvalidate(VncRectangle[] regions)
        {
            Throw.If.Null(regions, "regions");
            foreach (var region in regions) { FramebufferManualInvalidate(region); }
        }

        /// <summary>
        /// Completes a manual framebuffer update.
        /// 
        /// Do not call this method without holding <see cref="VncServerSession.FramebufferUpdateRequestLock"/>.
        /// </summary>
        public bool FramebufferManualEndUpdate()
        {
#if PRINT_DEBUG_BANDWIDTH_STATS
            if (_debugRawPixelBytes > 0 || _debugHexPixelBytesIn > 0 || _debugHexPixelBytesOut > 0)
            {
                Console.WriteLine(string.Format("Raw: {0}  Hex: {1} -> {2}  ZLib: {3} -> {4}", _debugRawPixelBytes, _debugHexPixelBytesIn, _debugHexPixelBytesOut, _debugZLibPixelBytesIn, _debugZLibPixelBytesOut));
            }
#endif

            var fb = Framebuffer;
            if (_clientWidth != fb.Width || _clientHeight != fb.Height)
            {
                if (_clientEncoding.Contains(VncEncoding.PseudoDesktopSize))
                {
                    var region = new VncRectangle(0, 0, fb.Width, fb.Height);
                    AddRegion(region, VncEncoding.PseudoDesktopSize, new byte[0]);
                    _clientWidth = Framebuffer.Width; _clientHeight = Framebuffer.Height;
                }
            }

            if (_fbuRectangles.Count == 0) { return false; }
            FramebufferUpdateRequest = null;

            lock (_c.SyncRoot)
            {
                _c.Send(new byte[2] { 0, 0 });
                _c.SendUInt16BE((ushort)_fbuRectangles.Count);

                foreach (var rectangle in _fbuRectangles)
                {
                    _c.SendRectangle(rectangle.Region);
                    _c.SendUInt32BE((uint)rectangle.Encoding);
                    _c.Send(rectangle.Contents, rectangle.ContentsOffset, rectangle.ContentsSize);
                }

                _fbuRectangles.Clear(); return true;
            }
        }

        protected virtual void OnPasswordProvided(PasswordProvidedEventArgs e)
        {
            RaisePasswordProvided(e);
        }

        protected void RaisePasswordProvided(PasswordProvidedEventArgs e)
        {
            var ev = PasswordProvided;
            if (ev != null) { ev(this, e); }
        }

        protected virtual void OnCreatingDesktop(CreatingDesktopEventArgs e)
        {
            RaiseCreatingDesktop(e);
        }

        protected void RaiseCreatingDesktop(CreatingDesktopEventArgs e)
        {
            var ev = CreatingDesktop;
            if (ev != null) { ev(this, e); }
        }

        protected virtual void OnConnected()
        {
            RaiseConnected();
        }

        protected void RaiseConnected()
        {
            var ev = Connected;
            if (ev != null) { ev(this, EventArgs.Empty); }
        }

        protected virtual void OnConnectionFailed()
        {
            RaiseConnectionFailed();
        }

        protected void RaiseConnectionFailed()
        {
            var ev = ConnectionFailed;
            if (ev != null) { ev(this, EventArgs.Empty); }
        }

        protected virtual void OnClosed()
        {
            RaiseClosed();
        }

        protected void RaiseClosed()
        {
            var ev = Closed;
            if (ev != null) { ev(this, EventArgs.Empty); }
        }

        protected virtual void OnFramebufferCapturing()
        {
            RaiseFramebufferCapturing();
        }

        protected void RaiseFramebufferCapturing()
        {
            var ev = FramebufferCapturing;
            if (ev != null) { ev(this, EventArgs.Empty); }
        }

        protected virtual void OnFramebufferUpdating(FramebufferUpdatingEventArgs e)
        {
            RaiseFramebufferUpdating(e);
        }

        protected void RaiseFramebufferUpdating(FramebufferUpdatingEventArgs e)
        {
            var ev = FramebufferUpdating;
            if (ev != null) { ev(this, e); }
        }

        protected virtual void OnFramebufferUpdated(EventArgs e)
        {
            RaiseFramebufferUpdated(e);
        }

        protected void RaiseFramebufferUpdated(EventArgs e)
        {
            var ev = FramebufferUpdated;
            if (ev != null) { ev(this, e); }
        }

        protected void OnKeyChanged(KeyChangedEventArgs e)
        {
            RaiseKeyChanged(e);
        }

        protected void RaiseKeyChanged(KeyChangedEventArgs e)
        {
            var ev = KeyChanged;
            if (ev != null) { ev(this, e); }
        }

        protected void OnPointerChanged(PointerChangedEventArgs e)
        {
            RaisePointerChanged(e);
        }

        protected void RaisePointerChanged(PointerChangedEventArgs e)
        {
            var ev = PointerChanged;
            if (ev != null) { ev(this, e); }
        }

        protected virtual void OnRemoteClipboardChanged(RemoteClipboardChangedEventArgs e)
        {
            RaiseRemoteClipboardChanged(e);
        }

        protected void RaiseRemoteClipboardChanged(RemoteClipboardChangedEventArgs e)
        {
            var ev = RemoteClipboardChanged;
            if (ev != null) { ev(this, e); }
        }

        public VncServerSessionStatistics GetStatistics()
        {
            var si = _stats;
            var so = new VncServerSessionStatistics();

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
        /// The protocol version of the client.
        /// </summary>
        public Version ClientVersion
        {
            get { return _clientVersion; }
        }

        /// <summary>
        /// The framebuffer for the VNC session.
        /// </summary>
        public VncFramebuffer Framebuffer
        {
            get;
            private set;
        }

        /// <summary>
        /// Information about the client's most recent framebuffer update request.
        /// This may be <c>null</c> if the client has no framebuffer request queued.
        /// </summary>
        public FramebufferUpdateRequest FramebufferUpdateRequest
        {
            get;
            private set;
        }

        /// <summary>
        /// Lock this before performing any framebuffer updates.
        /// </summary>
        public object FramebufferUpdateRequestLock
        {
            get { return _fbuSync; }
        }

        /// <summary>
        /// <c>true</c> if the server is connected to a client.
        /// </summary>
        public bool IsConnected
        {
            get;
            private set;
        }

        /// <summary>
        /// The max rate to send framebuffer updates at, in frames per second.
        /// 
        /// The default is 15.
        /// </summary>
        public double MaxUpdateRate
        {
            get { return _maxUpdateRate; }
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException("Max update rate must be positive.",
                                                          (Exception)null);
                }

                _maxUpdateRate = value;
            }
        }

        /// <summary>
        /// Store anything you want here.
        /// </summary>
        public object UserData
        {
            get;
            set;
        }
    }
}
