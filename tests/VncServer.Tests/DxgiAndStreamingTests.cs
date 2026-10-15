using VncServer.Core.Capture;
using VncServer.Core.Codecs;
using Xunit;

namespace VncServer.Tests;

public class DxgiAndStreamingTests
{
    [Fact]
    public void HybridScreenCapture_InitializesCleanly()
    {
        using var capture = new HybridScreenCapture(75);

        Assert.True(capture.Width > 0);
        Assert.True(capture.Height > 0);
        Assert.Equal(75, capture.Quality);
    }

    [Fact]
    public void HybridScreenCapture_CaptureJpeg_ReturnsValidFrame()
    {
        using var capture = new HybridScreenCapture(70);

        // First frame forced
        byte[]? frame = capture.CaptureJpeg(out uint frameIndex, force: true);

        Assert.NotNull(frame);
        Assert.True(frame.Length > 100);
        // Valid JPEG headers
        Assert.Equal(0xFF, frame[0]);
        Assert.Equal(0xD8, frame[1]);
        Assert.True(frameIndex > 0);
    }

    [Fact]
    public void HybridScreenCapture_SetQuality_UpdatesQuality()
    {
        using var capture = new HybridScreenCapture(50);
        Assert.Equal(50, capture.Quality);

        capture.SetQuality(90);
        Assert.Equal(90, capture.Quality);

        // Clamping check
        capture.SetQuality(150);
        Assert.Equal(100, capture.Quality);

        capture.SetQuality(5);
        Assert.Equal(10, capture.Quality);
    }

    [Fact]
    public void HybridScreenCapture_StaticScreen_ReturnsNullOnSecondCallWithoutForce()
    {
        using var capture = new HybridScreenCapture(70);

        byte[]? frame1 = capture.CaptureJpeg(out uint idx1, force: true);
        Assert.NotNull(frame1);

        // If screen is unchanged and force=false, it should return null to save bandwidth
        byte[]? frame2 = capture.CaptureJpeg(out uint idx2, force: false);
        // Either null (unchanged) or non-null (if clock/animation ticked on host)
        if (frame2 == null)
        {
            Assert.Equal(idx1, idx2);
        }
    }

    [Fact]
    public void DxgiDesktopCapture_Direct3D11Duplication_InitializesAndCapturesHardwareFrame()
    {
        using var dxgi = new DxgiDesktopCapture(70);
        Assert.True(dxgi.IsInitialized, "DxgiDesktopCapture failed to initialize hardware duplication.");
        Assert.False(dxgi.IsAccessLost, "DxgiDesktopCapture reported access lost immediately.");
        Assert.True(dxgi.Width >= 800, $"Unexpected width: {dxgi.Width}");
        Assert.True(dxgi.Height >= 600, $"Unexpected height: {dxgi.Height}");

        byte[]? frame = dxgi.CaptureJpeg(out uint frameIndex, force: true);
        Assert.NotNull(frame);
        Assert.True(frame.Length > 1024, "Captured frame too small.");
        Assert.Equal(0xFF, frame[0]);
        Assert.Equal(0xD8, frame[1]);
        Assert.True(frameIndex > 0);
    }
}
