using System.Drawing;

namespace VncServer.Core.Codecs;

public interface IFrameEncoder : IDisposable
{
    string CodecName { get; }
    byte[] Encode(Bitmap bitmap, int quality);
}
