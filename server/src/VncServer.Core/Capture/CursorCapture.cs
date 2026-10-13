using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VncServer.Core.Protocol;

namespace VncServer.Core.Capture;

public static class CursorCapture
{
    // ponytail: CursorCapture uses Win32 GetCursorInfo + GetIconInfo + GDI DIB extraction; upgrade to DXGI cursor pointer shape when DXGI pipeline is introduced.

    private const int CursorShowing = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hObject, int nCount, out BITMAP lpObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbmp,
        uint uStartScan,
        uint cScanLines,
        [Out] byte[]? lpvBits,
        ref BITMAPINFOHEADER lpbi,
        uint uUsage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static bool GetCurrentCursor(out int screenX, out int screenY, out bool visible, out IntPtr hCursor)
    {
        Native.DesktopHelper.AttachToInputDesktop();
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (GetCursorInfo(ref ci))
        {
            screenX = ci.ptScreenPos.X;
            screenY = ci.ptScreenPos.Y;
            visible = (ci.flags & CursorShowing) != 0;
            hCursor = ci.hCursor;
            return true;
        }

        screenX = 0;
        screenY = 0;
        visible = false;
        hCursor = IntPtr.Zero;
        return false;
    }

    public static CursorShape? CaptureShape(IntPtr hCursor)
    {
        if (hCursor == IntPtr.Zero)
            return CreateFallbackArrow();

        try
        {
            if (!GetIconInfo(hCursor, out var iconInfo))
                return CreateFallbackArrow();

            IntPtr hbmColor = iconInfo.hbmColor;
            IntPtr hbmMask = iconInfo.hbmMask;

            try
            {
                int width;
                int height;
                int hotspotX = iconInfo.xHotspot;
                int hotspotY = iconInfo.yHotspot;

                if (hbmColor != IntPtr.Zero)
                {
                    GetObject(hbmColor, Marshal.SizeOf<BITMAP>(), out var bmColor);
                    width = bmColor.bmWidth;
                    height = bmColor.bmHeight;
                }
                else if (hbmMask != IntPtr.Zero)
                {
                    GetObject(hbmMask, Marshal.SizeOf<BITMAP>(), out var bmMask);
                    width = bmMask.bmWidth;
                    height = bmMask.bmHeight / 2; // Monochrome cursor mask height includes both AND and XOR masks
                }
                else
                {
                    return CreateFallbackArrow();
                }

                if (width <= 0 || height <= 0 || width > 256 || height > 256)
                    return CreateFallbackArrow();

                hotspotX = Math.Clamp(hotspotX, 0, width - 1);
                hotspotY = Math.Clamp(hotspotY, 0, height - 1);

                byte[] bgraPixels = new byte[width * height * 4];
                IntPtr hdc = GetDC(IntPtr.Zero);

                try
                {
                    if (hbmColor != IntPtr.Zero)
                    {
                        var bih = new BITMAPINFOHEADER
                        {
                            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                            biWidth = width,
                            biHeight = -height, // Top-down
                            biPlanes = 1,
                            biBitCount = 32,
                            biCompression = 0
                        };

                        GetDIBits(hdc, hbmColor, 0, (uint)height, bgraPixels, ref bih, 0);

                        // Check if cursor has real alpha channel
                        bool hasAlpha = false;
                        for (int i = 3; i < bgraPixels.Length; i += 4)
                        {
                            if (bgraPixels[i] > 0)
                            {
                                hasAlpha = true;
                                break;
                            }
                        }

                        // If not a 32-bit alpha cursor, use hbmMask as 1-bit transparency mask
                        if (!hasAlpha && hbmMask != IntPtr.Zero)
                        {
                            byte[] maskPixels = new byte[width * height * 4];
                            var maskBih = new BITMAPINFOHEADER
                            {
                                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                                biWidth = width,
                                biHeight = -height,
                                biPlanes = 1,
                                biBitCount = 32,
                                biCompression = 0
                            };
                            GetDIBits(hdc, hbmMask, 0, (uint)height, maskPixels, ref maskBih, 0);

                            for (int i = 0; i < width * height; i++)
                            {
                                int idx = i * 4;
                                bool isTransparent = (maskPixels[idx] > 128);
                                bgraPixels[idx + 3] = isTransparent ? (byte)0 : (byte)255;
                            }
                        }
                    }
                    else if (hbmMask != IntPtr.Zero)
                    {
                        // Monochrome cursor: top half AND mask, bottom half XOR mask
                        int doubleHeight = height * 2;
                        byte[] maskPixels = new byte[width * doubleHeight * 4];
                        var maskBih = new BITMAPINFOHEADER
                        {
                            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                            biWidth = width,
                            biHeight = -doubleHeight,
                            biPlanes = 1,
                            biBitCount = 32,
                            biCompression = 0
                        };
                        GetDIBits(hdc, hbmMask, 0, (uint)doubleHeight, maskPixels, ref maskBih, 0);

                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int andIdx = (y * width + x) * 4;
                                int xorIdx = ((y + height) * width + x) * 4;
                                int outIdx = (y * width + x) * 4;

                                bool andBit = maskPixels[andIdx] > 128;
                                bool xorBit = maskPixels[xorIdx] > 128;

                                if (!andBit && !xorBit)
                                {
                                    // Black
                                    bgraPixels[outIdx] = 0;
                                    bgraPixels[outIdx + 1] = 0;
                                    bgraPixels[outIdx + 2] = 0;
                                    bgraPixels[outIdx + 3] = 255;
                                }
                                else if (!andBit && xorBit)
                                {
                                    // White
                                    bgraPixels[outIdx] = 255;
                                    bgraPixels[outIdx + 1] = 255;
                                    bgraPixels[outIdx + 2] = 255;
                                    bgraPixels[outIdx + 3] = 255;
                                }
                                else if (andBit && !xorBit)
                                {
                                    // Transparent
                                    bgraPixels[outIdx] = 0;
                                    bgraPixels[outIdx + 1] = 0;
                                    bgraPixels[outIdx + 2] = 0;
                                    bgraPixels[outIdx + 3] = 0;
                                }
                                else
                                {
                                    // Inverted / high-contrast pixel (e.g. I-Beam cursor)
                                    bgraPixels[outIdx] = 0;
                                    bgraPixels[outIdx + 1] = 0;
                                    bgraPixels[outIdx + 2] = 0;
                                    bgraPixels[outIdx + 3] = 255;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, hdc);
                }

                // Encode BGRA pixels into PNG
                using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var bmpData = bmp.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                Marshal.Copy(bgraPixels, 0, bmpData.Scan0, bgraPixels.Length);
                bmp.UnlockBits(bmpData);

                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                byte[] pngBytes = ms.ToArray();

                return new CursorShape(width, height, hotspotX, hotspotY, 1, pngBytes);
            }
            finally
            {
                if (hbmColor != IntPtr.Zero) DeleteObject(hbmColor);
                if (hbmMask != IntPtr.Zero) DeleteObject(hbmMask);
            }
        }
        catch
        {
            return CreateFallbackArrow();
        }
    }

    public static CursorShape CreateFallbackArrow()
    {
        const int w = 32;
        const int h = 32;
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var fillBrush = new SolidBrush(Color.White);
            using var borderPen = new Pen(Color.Black, 1.5f);

            Point[] pts =
            [
                new(0, 0),
                new(0, 20),
                new(5, 15),
                new(10, 22),
                new(13, 20),
                new(8, 13),
                new(15, 13)
            ];

            g.FillPolygon(fillBrush, pts);
            g.DrawPolygon(borderPen, pts);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return new CursorShape(w, h, 0, 0, 1, ms.ToArray());
    }
}
