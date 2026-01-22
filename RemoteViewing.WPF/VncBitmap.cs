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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RemoteViewing.Vnc;

namespace RemoteViewing.WPF;

/// <summary>
/// Helps with WPF bitmap conversion using <see cref="WriteableBitmap"/>.
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
    public unsafe static void CopyToFramebuffer(WriteableBitmap source, VncRectangle sourceRectangle,
                                                VncFramebuffer target, int targetX, int targetY)
    {
        Throw.If.Null(source, nameof(source)).Null(target, nameof(target));
        if (sourceRectangle.IsEmpty) { return; }

        source.Lock();
        try
        {
            var sourceStride = source.BackBufferStride;
            var sourceScan0 = source.BackBuffer;

            fixed (int* framebufferData = target.GetPixels())
            {
                var targetStride = target.Width * 4;

                for (int iy = 0; iy < sourceRectangle.Height; iy++)
                {
                    var sourceRow = (byte*)sourceScan0 + (sourceRectangle.Y + iy) * sourceStride + sourceRectangle.X * 4;
                    var targetRow = (byte*)framebufferData + (targetY + iy) * targetStride + targetX * 4;

                    // Copy row - both are 32bpp formats
                    Buffer.MemoryCopy(sourceRow, targetRow, sourceRectangle.Width * 4, sourceRectangle.Width * 4);
                }
            }
        }
        finally
        {
            source.Unlock();
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
    public unsafe static void CopyFromFramebuffer(VncFramebuffer source, VncRectangle sourceRectangle,
                                                  WriteableBitmap target, int targetX, int targetY)
    {
        Throw.If.Null(source, nameof(source)).Null(target, nameof(target));
        if (sourceRectangle.IsEmpty) { return; }

        target.Lock();
        try
        {
            var targetStride = target.BackBufferStride;
            var targetScan0 = target.BackBuffer;

            fixed (int* framebufferData = source.GetPixels())
            {
                var sourceStride = source.Width * 4;

                for (int iy = 0; iy < sourceRectangle.Height; iy++)
                {
                    var sourceRow = (byte*)framebufferData + (sourceRectangle.Y + iy) * sourceStride + sourceRectangle.X * 4;
                    var targetRow = (byte*)targetScan0 + (targetY + iy) * targetStride + targetX * 4;

                    // Copy row - both are 32bpp formats
                    Buffer.MemoryCopy(sourceRow, targetRow, sourceRectangle.Width * 4, sourceRectangle.Width * 4);
                }
            }

            // Mark the dirty region for the WriteableBitmap
            target.AddDirtyRect(new Int32Rect(targetX, targetY, sourceRectangle.Width, sourceRectangle.Height));
        }
        finally
        {
            target.Unlock();
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
        return new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
    }
}
