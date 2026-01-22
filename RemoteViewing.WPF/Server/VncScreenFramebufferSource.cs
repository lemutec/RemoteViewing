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

using RemoteViewing.Vnc;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RemoteViewing.WPF.Server;

/// <summary>
/// Called to determine the screen region to send.
/// </summary>
/// <returns>The screen region.</returns>
public delegate Int32Rect VncScreenFramebufferSourceGetBoundsCallback();

/// <summary>
/// Provides a framebuffer with pixels copied from the screen.
/// </summary>
public class VncScreenFramebufferSource : IVncFramebufferSource
{
    private WriteableBitmap _bitmap;
    private VncFramebuffer _framebuffer;
    private string _name;
    private VncScreenFramebufferSourceGetBoundsCallback _getScreenBounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="VncScreenFramebufferSource"/> class.
    /// </summary>
    /// <param name="name">The framebuffer name. Many VNC clients set their titlebar to this name.</param>
    /// <param name="bounds">The bounds of the screen region.</param>
    public VncScreenFramebufferSource(string name, Int32Rect bounds)
    {
        Throw.If.Null(name, nameof(name));
        _name = name; _getScreenBounds = () => bounds;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VncScreenFramebufferSource"/> class.
    /// Screen region bounds are determined by a callback.
    /// </summary>
    /// <param name="name">The framebuffer name. Many VNC clients set their titlebar to this name.</param>
    /// <param name="getBoundsCallback">A callback supplying the bounds of the screen region to copy.</param>
    public VncScreenFramebufferSource(string name, VncScreenFramebufferSourceGetBoundsCallback getBoundsCallback)
    {
        Throw.If.Null(name, nameof(name)).Null(getBoundsCallback, nameof(getBoundsCallback));
        _name = name; _getScreenBounds = getBoundsCallback;
    }

    /// <summary>
    /// Creates a new instance using the primary screen bounds.
    /// </summary>
    /// <param name="name">The framebuffer name. Many VNC clients set their titlebar to this name.</param>
    /// <returns>A new <see cref="VncScreenFramebufferSource"/> for the primary screen.</returns>
    public static VncScreenFramebufferSource FromPrimaryScreen(string name)
    {
        Throw.If.Null(name, nameof(name));
        return new VncScreenFramebufferSource(name, () =>
        {
            return new Int32Rect(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
        });
    }

    /// <summary>
    /// Captures the screen.
    /// </summary>
    /// <returns>A framebuffer corresponding to the screen.</returns>
    public VncFramebuffer Capture()
    {
        var bounds = _getScreenBounds();
        int w = bounds.Width, h = bounds.Height;

        if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
        {
            _bitmap = VncBitmap.CreateBitmap(w, h);
            _framebuffer = new VncFramebuffer(_name, w, h);
        }

        // Capture screen using GDI+
        CaptureScreen(bounds.X, bounds.Y, w, h, _bitmap);

        lock (_framebuffer.SyncRoot)
        {
            VncBitmap.CopyToFramebuffer(_bitmap, new VncRectangle(0, 0, w, h),
                                        _framebuffer, 0, 0);
        }

        return _framebuffer;
    }

    private static void CaptureScreen(int x, int y, int width, int height, WriteableBitmap target)
    {
        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
        IntPtr hOld = SelectObject(hdcMem, hBitmap);

        BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, SRCCOPY);

        // Copy the bitmap data to the WriteableBitmap
        BITMAPINFO bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height; // Negative to indicate top-down DIB
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;

        target.Lock();
        try
        {
            GetDIBits(hdcMem, hBitmap, 0, (uint)height, target.BackBuffer, ref bmi, DIB_RGB_COLORS);
            target.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            target.Unlock();
        }

        SelectObject(hdcMem, hOld);
        DeleteObject(hBitmap);
        DeleteDC(hdcMem);
        ReleaseDC(IntPtr.Zero, hdcScreen);
    }

    #region Native Methods

    private const int SRCCOPY = 0x00CC0020;
    private const int BI_RGB = 0;
    private const int DIB_RGB_COLORS = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        IntPtr lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public int[] bmiColors;
    }

    #endregion
}
