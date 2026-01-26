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
using System.IO;
using System.IO.Compression;

namespace RemoteViewing.Vnc;

partial class VncClient
{
    private byte[] _framebufferScratch = [];
    private byte[] _zlibScratch = [];
    private MemoryStream _zlibMemoryStream;
    private DeflateStream _zlibInflater;

    void InitFramebufferDecoder()
    {
        _zlibMemoryStream = new MemoryStream();
        _zlibInflater = null; // Don't reuse the dictionary between sessions.
    }

    /// <summary>
    /// Compacts the zlib memory stream by removing already-consumed data from the beginning.
    /// This prevents unbounded memory growth while preserving unconsumed data.
    /// </summary>
    private void CompactZlibStream()
    {
        long currentPos = _zlibMemoryStream.Position;
        long length = _zlibMemoryStream.Length;

        // Only compact if we've consumed more than 1MB
        if (currentPos <= 1024 * 1024)
        {
            return;
        }

        int remaining = (int)(length - currentPos);
        byte[] unconsumedData = null;

        if (remaining > 0)
        {
            // Save the unconsumed data
            unconsumedData = new byte[remaining];
            _zlibMemoryStream.Read(unconsumedData, 0, remaining);
        }

        // Reset the stream
        _zlibMemoryStream.Position = 0;
        _zlibMemoryStream.SetLength(0);

        if (remaining > 0)
        {
            // Write back the unconsumed data
            _zlibMemoryStream.Write(unconsumedData, 0, remaining);
        }

        // Position at the beginning of the (preserved) unconsumed data
        _zlibMemoryStream.Position = 0;
    }

    byte[] AllocateFramebufferScratch(int bytes)
    {
        return VncUtility.AllocateScratch(bytes, ref _framebufferScratch);
    }

    unsafe void HandleFramebufferUpdate()
    {
        _c.ReceiveByte(); // padding

        var numRects = _c.ReceiveUInt16BE();
        var rects = new List<VncRectangle>();

        for (int i = 0; i < numRects; i++)
        {
            var r = _c.ReceiveRectangle();
            int x = r.X, y = r.Y, w = r.Width, h = r.Height;
            VncStream.SanityCheck(w > 0 && w < 0x8000);
            VncStream.SanityCheck(h > 0 && h < 0x8000);

            int fbW = Framebuffer.Width, fbH = Framebuffer.Height, bpp = _pixelFormat.BytesPerPixel;
            var inRange = w <= fbW && h <= fbH && x <= fbW - w && y <= fbH - h; byte[] pixels;

            var encoding = (VncEncoding)_c.ReceiveUInt32BE();
            switch (encoding)
            {
                case VncEncoding.Hextile: // KVM seems to avoid this now that I support Zlib.
                    var background = new byte[bpp];
                    var foreground = new byte[bpp];

                    for (int ty = 0; ty < h; ty += 16)
                    {
                        int th = Math.Min(16, h - ty);
                        for (int tx = 0; tx < w; tx += 16)
                        {
                            int tw = Math.Min(16, w - tx);

                            var subencoding = _c.ReceiveByte();
                            pixels = AllocateFramebufferScratch(tw * th * bpp);

                            if (0 != (subencoding & 1)) // raw
                            {
                                _c.Receive(pixels, 0, tw * th * bpp);

                                if (inRange)
                                {
                                    lock (Framebuffer.SyncRoot) { CopyToFramebuffer(x + tx, y + ty, tw, th, pixels); }
                                }
                            }
                            else
                            {
                                pixels = AllocateFramebufferScratch(tw * th * bpp);
                                if (0 != (subencoding & 2)) { background = _c.Receive(bpp); }
                                if (0 != (subencoding & 4)) { foreground = _c.Receive(bpp); }

                                int ptr = 0;
                                for (int pp = 0; pp < tw * th; pp++)
                                {
                                    for (int pe = 0; pe < bpp; pe++) { pixels[ptr++] = background[pe]; }
                                }

                                int nsubrects = 0 != (subencoding & 8) ? _c.ReceiveByte() : 0;
                                if (nsubrects > 0)
                                {
                                    var subrectsColored = 0 != (subencoding & 16);
                                    for (int subrect = 0; subrect < nsubrects; subrect++)
                                    {
                                        var color = subrectsColored ? _c.Receive(bpp) : foreground;
                                        var srxy = _c.ReceiveByte(); var srwh = _c.ReceiveByte();
                                        int srx = (srxy >> 4) & 0xf, srw = ((srwh >> 4) & 0xf) + 1;
                                        int sry = (srxy >> 0) & 0xf, srh = ((srwh >> 0) & 0xf) + 1;
                                        if (srx + srw > tw || sry + srh > th) { continue; }

                                        for (int py = 0; py < srh; py++)
                                        {
                                            for (int px = 0; px < srw; px++)
                                            {
                                                int off = bpp * ((py + sry) * tw + (px + srx));
                                                for (int pe = 0; pe < bpp; pe++)
                                                {
                                                    pixels[off + pe] = color[pe];
                                                }
                                            }
                                        }
                                    }
                                }

                                lock (Framebuffer.SyncRoot) { CopyToFramebuffer(x + tx, y + ty, tw, th, pixels); }
                            }
                        }
                    }
                    break;

                case VncEncoding.CopyRect:
                    var srcx = (int)_c.ReceiveUInt16BE();
                    var srcy = (int)_c.ReceiveUInt16BE();

                    if (srcx + w > fbW) { w = fbW - srcx; }
                    if (srcy + h > fbH) { h = fbH - srcy; }
                    if (w < 1 || h < 1 || !inRange) { continue; }

                    var fbFormat = VncPixelFormat.Format32bpp;
                    int fbBpp = fbFormat.BytesPerPixel;

                    pixels = AllocateFramebufferScratch(w * h * fbBpp);
                    lock (Framebuffer.SyncRoot)
                    {
                        fixed (byte* tempPixels = pixels)
                        fixed (int* fbPixels = Framebuffer.GetPixels())
                        {
                            VncPixelFormat.Copy(
                                (IntPtr)fbPixels, fbW * fbBpp, fbFormat, new VncRectangle(srcx, srcy, w, h),
                                (IntPtr)tempPixels, w * fbBpp, fbFormat);

                            VncPixelFormat.Copy(
                                (IntPtr)tempPixels, w * fbBpp, fbFormat, new VncRectangle(0, 0, w, h),
                                (IntPtr)fbPixels, fbW * fbBpp, fbFormat, x, y);
                        }
                    }
                    break;

                case VncEncoding.Raw:
                    pixels = AllocateFramebufferScratch(w * h * bpp);
                    _c.Receive(pixels, 0, w * h * bpp);

                    if (inRange)
                    {
                        lock (Framebuffer.SyncRoot) { CopyToFramebuffer(x, y, w, h, pixels); }
                    }
                    break;

                case VncEncoding.Zlib:
                    int bytesDesired = w * h * bpp;

                    int size = (int)_c.ReceiveUInt32BE(); VncStream.SanityCheck(size >= 0 && size < 0x10000000);
                    VncUtility.AllocateScratch(size, ref _zlibScratch);
                    _c.Receive(_zlibScratch, 0, size);

                    if (_zlibInflater == null) // Zlib has a two-byte header.
                    {
                        VncStream.SanityCheck(size >= 2);
                        // First Zlib block: skip the 2-byte zlib header
                        _zlibMemoryStream.Position = 0;
                        _zlibMemoryStream.SetLength(0);
                        _zlibMemoryStream.Write(_zlibScratch, 2, size - 2);
                        _zlibMemoryStream.Position = 0;
                        _zlibInflater = new DeflateStream(_zlibMemoryStream, CompressionMode.Decompress, true);
                    }
                    else
                    {
                        // Subsequent Zlib blocks: append data to the stream
                        // This preserves any data that DeflateStream may have buffered internally
                        CompactZlibStream();

                        // Append new data at the end of the stream
                        long currentReadPos = _zlibMemoryStream.Position;
                        _zlibMemoryStream.Seek(0, SeekOrigin.End);
                        _zlibMemoryStream.Write(_zlibScratch, 0, size);
                        _zlibMemoryStream.Position = currentReadPos;
                    }

                    pixels = AllocateFramebufferScratch(bytesDesired);
                    for (int j = 0; j < bytesDesired;)
                    {
                        int count = 0;

                        try
                        {
                            count = _zlibInflater.Read(pixels, j, bytesDesired - j);
                        }
                        catch (InvalidDataException)
                        {
                            VncStream.Require(false,
                                                  "Bad data compressed.",
                                                  VncFailureReason.UnrecognizedProtocolElement);
                        }

                        VncStream.Require(count > 0,
                                              "No data compressed.",
                                              VncFailureReason.UnrecognizedProtocolElement);
                        j += count;
                    }

                    if (inRange)
                    {
                        lock (Framebuffer.SyncRoot) { CopyToFramebuffer(x, y, w, h, pixels); }
                    }
                    break;

                case VncEncoding.PseudoDesktopSize:
                    Framebuffer = new VncFramebuffer(Framebuffer.Name, w, h);
                    continue; // Don't call OnFramebufferChanged for this one.

                default:
                    VncStream.Require(false,
                                          "Unsupported encoding.",
                                          VncFailureReason.UnrecognizedProtocolElement);
                    break;
            }

            rects.Add(new VncRectangle(x, y, w, h));
        }

        if (rects.Count > 0)
        {
            OnFramebufferChanged(new FramebufferChangedEventArgs(rects));
        }
    }
}
