namespace VncServer.Core.Capture;

public interface IScreenCapture : IDisposable
{
    int VirtualX { get; }
    int VirtualY { get; }
    int Width { get; }
    int Height { get; }
    int Quality { get; }

    void SetQuality(int quality);
    void RefreshResolution();
    byte[]? CaptureJpeg(out uint frameIndex, bool force = false);
}
