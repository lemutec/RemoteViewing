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
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using RemoteViewing.Vnc;

namespace RemoteViewing.WinUI;

/// <summary>
/// Helps with WinUI bitmap conversion using <see cref="WriteableBitmap"/>.
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

        int sourceStride = source.PixelWidth * 4;
        int rowBytes = sourceRectangle.Width * 4;
        byte[] row = ArrayPool<byte>.Shared.Rent(rowBytes);
        try
        {
            using var stream = source.PixelBuffer.AsStream();
            fixed (int* framebufferData = target.GetPixels())
            fixed (byte* rowPtr = row)
            {
                int targetStride = target.Width * 4;
                for (int iy = 0; iy < sourceRectangle.Height; iy++)
                {
                    stream.Position = ((long)sourceRectangle.Y + iy) * sourceStride + sourceRectangle.X * 4;
                    ReadExactly(stream, row, 0, rowBytes);

                    byte* targetRow = (byte*)framebufferData + (targetY + iy) * targetStride + targetX * 4;
                    Buffer.MemoryCopy(rowPtr, targetRow, rowBytes, rowBytes);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(row);
        }
    }

    /// <summary>
    /// Copies a region of the framebuffer into a <see cref="WriteableBitmap"/>.
    /// WinUI bitmaps are premultiplied BGRA, so the unused VNC alpha byte is forced opaque.
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

        int targetStride = target.PixelWidth * 4;
        int sourceStride = source.Width * 4;
        int rowBytes = sourceRectangle.Width * 4;
        byte[] row = ArrayPool<byte>.Shared.Rent(rowBytes);
        try
        {
            using var stream = target.PixelBuffer.AsStream();
            fixed (int* framebufferData = source.GetPixels())
            fixed (byte* rowPtr = row)
            {
                for (int iy = 0; iy < sourceRectangle.Height; iy++)
                {
                    byte* sourceRow = (byte*)framebufferData + (sourceRectangle.Y + iy) * sourceStride + sourceRectangle.X * 4;
                    uint* src = (uint*)sourceRow;
                    uint* dst = (uint*)rowPtr;
                    int count = sourceRectangle.Width;
                    for (int x = 0; x < count; x++)
                    {
                        dst[x] = src[x] | 0xFF000000u;
                    }

                    stream.Position = ((long)targetY + iy) * targetStride + targetX * 4;
                    stream.Write(row, 0, rowBytes);
                }
            }

            target.Invalidate();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(row);
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
        return new WriteableBitmap(width, height);
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
#if NET7_0_OR_GREATER
        stream.ReadExactly(buffer, offset, count);
#else
        while (count > 0)
        {
            int read = stream.Read(buffer, offset, count);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
            count -= read;
        }
#endif
    }
}
