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

using System;

namespace RemoteViewing.Vnc.Server;

sealed class VncFramebufferCache
{
    private const int TileSize = 16;

    private int[] _oldPixels;

    public VncFramebufferCache(VncFramebuffer framebuffer)
    {
        Throw.If.Null(framebuffer, "framebuffer");
        Framebuffer = framebuffer;
    }

    public unsafe bool RespondToUpdateRequest(VncServerSession session)
    {
        var fb = Framebuffer; var fbr = session.FramebufferUpdateRequest;
        if (fb == null || fbr == null) { return false; }

        var incremental = fbr.Incremental; var region = fbr.Region;
        session.FramebufferManualBeginUpdate();

        var buffer = fb.GetPixels();

        if (_oldPixels == null || _oldPixels.Length != buffer.Length)
        {
            lock (fb.SyncRoot)
            {
                _oldPixels = (int[])buffer.Clone();
            }

            incremental = false;
        }

        if (incremental)
        {
            lock (fb.SyncRoot)
            {
                fixed (int* newPixels0 = buffer)
                fixed (int* oldPixels0 = _oldPixels)
                {
                    int ymax = Math.Min(region.Y + region.Height, fb.Height);
                    int xmax = Math.Min(region.X + region.Width, fb.Width);

                    for (int y = region.Y; y < ymax; y += TileSize)
                    {
                        for (int x = region.X; x < xmax; x += TileSize)
                        {
                            int w = Math.Min(TileSize, xmax - x);
                            int h = Math.Min(TileSize, ymax - y);

                            var subregion = new VncRectangle(x, y, w, h);

                            int stride = fb.Width - w, offset = y * fb.Width + x;
                            int* newPixels = newPixels0 + offset;
                            int* oldPixels = oldPixels0 + offset;
                            bool changed = false;

                            // fast path for 64-bit programs
                            if (IntPtr.Size == 8 && (stride & 1) == 0 && (w & 1) == 0 && ((ulong)newPixels & 7) == 0 && ((ulong)oldPixels & 7) == 0)
                            {
                                int w64 = w >> 1, stride64 = stride >> 1;

                                long* newPixels64 = (long*)newPixels;
                                long* oldPixels64 = (long*)oldPixels;

                                for (int iy = 0; iy < h; iy++)
                                {
                                    for (int ix = 0; ix < w64; ix++)
                                    {
                                        if (*oldPixels64 != *newPixels64)
                                        {
                                            *oldPixels64 = *newPixels64;
                                            changed = true;
                                        }
                                        oldPixels64++; newPixels64++;
                                    }
                                    oldPixels64 += stride64; newPixels64 += stride64;
                                }
                            }
                            else
                            {
                                for (int iy = 0; iy < h; iy++)
                                {
                                    for (int ix = 0; ix < w; ix++)
                                    {
                                        if (*oldPixels != *newPixels)
                                        {
                                            *oldPixels = *newPixels;
                                            changed = true;
                                        }
                                        oldPixels++; newPixels++;
                                    }
                                    oldPixels += stride; newPixels += stride;
                                }
                            }

                            if (changed)
                            {
                                session.FramebufferManualInvalidate(subregion);
                            }
                        }
                    }
                }
            }
        }
        else
        {
            session.FramebufferManualInvalidate(region);
        }

        return session.FramebufferManualEndUpdate();
    }

    public VncFramebuffer Framebuffer { get; private set; }
}
