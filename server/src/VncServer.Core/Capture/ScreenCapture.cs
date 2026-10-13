using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VncServer.Core.Capture;

public sealed class ScreenCapture : IDisposable
{
    // ponytail: ScreenCapture uses GDI CopyFromScreen + GDI+ JPEG; upgrade to DXGI Desktop Duplication + NVENC when 120fps/multi-monitor HDR is required.
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private readonly ImageCodecInfo _jpegCodec;
    private readonly EncoderParameters _encoderParameters;
    private readonly MemoryStream _memoryStream = new(128 * 1024);
    private readonly object _syncLock = new();

    private long _lastFrameHash = -1;
    private uint _frameIndex = 0;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Quality { get; private set; }

    public ScreenCapture(int quality = 70)
    {
        Quality = Math.Clamp(quality, 10, 100);
        _jpegCodec = GetJpegCodec() ?? throw new InvalidOperationException("JPEG encoder not found in system");

        _encoderParameters = new EncoderParameters(1);
        _encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)Quality);

        RefreshResolution();
    }

    public void SetQuality(int quality)
    {
        lock (_syncLock)
        {
            Quality = Math.Clamp(quality, 10, 100);
            _encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)Quality);
        }
    }

    public void RefreshResolution()
    {
        int w = GetSystemMetrics(SmCxScreen);
        int h = GetSystemMetrics(SmCyScreen);

        if (w <= 0) w = 1920;
        if (h <= 0) h = 1080;

        Width = w;
        Height = h;
    }

    public byte[]? CaptureJpeg(out uint frameIndex, bool force = false)
    {
        lock (_syncLock)
        {
            Native.DesktopHelper.AttachToInputDesktop();
            RefreshResolution();
            int w = Width;
            int h = Height;

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(0, 0, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            }

            long currentHash = ComputeFastSampleHash(bmp);
            if (!force && currentHash == _lastFrameHash)
            {
                frameIndex = _frameIndex;
                return null; // Screen unchanged, skip redundant frame
            }

            _lastFrameHash = currentHash;
            frameIndex = ++_frameIndex;

            _memoryStream.SetLength(0);
            bmp.Save(_memoryStream, _jpegCodec, _encoderParameters);
            return _memoryStream.ToArray();
        }
    }

    private static unsafe long ComputeFastSampleHash(Bitmap bmp)
    {
        // Sample a 16x16 grid across the screen to quickly detect changes with negligible CPU cost
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            int stepX = Math.Max(1, bmp.Width / 16);
            int stepY = Math.Max(1, bmp.Height / 16);
            int* ptr = (int*)data.Scan0.ToPointer();
            int stride = data.Stride / 4;

            long hash = 17;
            for (int y = 0; y < bmp.Height; y += stepY)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < bmp.Width; x += stepX)
                {
                    hash = (hash * 31) ^ ptr[rowOffset + x];
                }
            }
            return hash;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static ImageCodecInfo? GetJpegCodec()
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == ImageFormat.Jpeg.Guid)
                return codec;
        }
        return null;
    }

    public void Dispose()
    {
        _encoderParameters.Dispose();
        _memoryStream.Dispose();
    }
}
