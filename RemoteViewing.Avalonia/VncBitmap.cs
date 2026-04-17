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
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RemoteViewing.Vnc;

namespace RemoteViewing.Avalonia;

/// <summary>
/// Helps with Avalonia bitmap conversion using <see cref="WriteableBitmap"/>.
/// </summary>
public static class VncBitmap
{
    /// <summary>
    /// Copies a region of a <see cref="WriteableBitmap"/> into the framebuffer.
    /// </summary>
    /// <param name="source">The bitmap to read.</param>
    /// <param name="sourceRectangle">The bitmap region to copy.</param>
    /// <param name="target">The framebuffer to copy into.</param>
    /// <param name="targetX">The leftmost X coordinate of the framebuffer to draw to.</param>
    /// <param name="targetY">The topmost Y coordinate of the framebuffer to draw to.</param>
    public static unsafe void CopyToFramebuffer(WriteableBitmap source, VncRectangle sourceRectangle,
                                                VncFramebuffer target, int targetX, int targetY)
    {
        if (source == null) { throw new ArgumentNullException(nameof(source)); }
        if (target == null) { throw new ArgumentNullException(nameof(target)); }
        if (sourceRectangle.IsEmpty) { return; }

        using ILockedFramebuffer buf = source.Lock();
        int sourceStride = buf.RowBytes;
        IntPtr sourceScan0 = buf.Address;

        fixed (int* framebufferData = target.GetPixels())
        {
            int targetStride = target.Width * 4;

            for (int iy = 0; iy < sourceRectangle.Height; iy++)
            {
                byte* sourceRow = (byte*)sourceScan0 + (sourceRectangle.Y + iy) * sourceStride + sourceRectangle.X * 4;
                byte* targetRow = (byte*)framebufferData + (targetY + iy) * targetStride + targetX * 4;

                Buffer.MemoryCopy(sourceRow, targetRow, sourceRectangle.Width * 4, sourceRectangle.Width * 4);
            }
        }
    }

    /// <summary>
    /// Copies a region of the framebuffer into a <see cref="WriteableBitmap"/>.
    /// </summary>
    /// <param name="source">The framebuffer to read.</param>
    /// <param name="sourceRectangle">The framebuffer region to copy.</param>
    /// <param name="target">The bitmap to copy into.</param>
    /// <param name="targetX">The leftmost X coordinate of the bitmap to draw to.</param>
    /// <param name="targetY">The topmost Y coordinate of the bitmap to draw to.</param>
    public static unsafe void CopyFromFramebuffer(VncFramebuffer source, VncRectangle sourceRectangle,
                                                  WriteableBitmap target, int targetX, int targetY)
    {
        if (source == null) { throw new ArgumentNullException(nameof(source)); }
        if (target == null) { throw new ArgumentNullException(nameof(target)); }
        if (sourceRectangle.IsEmpty) { return; }

        using ILockedFramebuffer buf = target.Lock();
        int targetStride = buf.RowBytes;
        IntPtr targetScan0 = buf.Address;

        fixed (int* framebufferData = source.GetPixels())
        {
            int sourceStride = source.Width * 4;

            for (int iy = 0; iy < sourceRectangle.Height; iy++)
            {
                byte* sourceRow = (byte*)framebufferData + (sourceRectangle.Y + iy) * sourceStride + sourceRectangle.X * 4;
                byte* targetRow = (byte*)targetScan0 + (targetY + iy) * targetStride + targetX * 4;

                Buffer.MemoryCopy(sourceRow, targetRow, sourceRectangle.Width * 4, sourceRectangle.Width * 4);
            }
        }
    }

    /// <summary>
    /// Creates a new <see cref="WriteableBitmap"/> with the specified dimensions.
    /// </summary>
    /// <param name="width">The width of the bitmap.</param>
    /// <param name="height">The height of the bitmap.</param>
    /// <returns>A new <see cref="WriteableBitmap"/>.</returns>
    public static WriteableBitmap CreateBitmap(int width, int height)
    {
        return new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
    }
}
