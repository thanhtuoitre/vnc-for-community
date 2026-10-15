using System.Drawing;
using System.Drawing.Imaging;

namespace VncServer.Core.Codecs;

public sealed class JpegFrameEncoder : IFrameEncoder
{
    private readonly ImageCodecInfo _jpegCodec;
    private readonly MemoryStream _memoryStream = new(256 * 1024);
    private readonly object _syncLock = new();

    public string CodecName => "JPEG";

    public JpegFrameEncoder()
    {
        _jpegCodec = GetJpegCodec() ?? throw new InvalidOperationException("System JPEG encoder not found.");
    }

    public byte[] Encode(Bitmap bitmap, int quality)
    {
        int clampedQuality = Math.Clamp(quality, 10, 100);
        lock (_syncLock)
        {
            _memoryStream.SetLength(0);
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)clampedQuality);
            bitmap.Save(_memoryStream, _jpegCodec, encoderParams);
            return _memoryStream.ToArray();
        }
    }

    private static ImageCodecInfo? GetJpegCodec()
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == ImageFormat.Jpeg.Guid)
            {
                return codec;
            }
        }
        return null;
    }

    public void Dispose()
    {
        _memoryStream.Dispose();
    }
}
