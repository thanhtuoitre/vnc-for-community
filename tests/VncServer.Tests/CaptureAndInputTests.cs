using VncServer.Core.Capture;
using VncServer.Core.Input;
using VncServer.Core.Protocol;

namespace VncServer.Tests;

public class CaptureAndInputTests
{
    [Fact]
    public void ScreenCapture_DetectsValidResolution()
    {
        using var capture = new ScreenCapture(70);
        Assert.True(capture.Width > 0);
        Assert.True(capture.Height > 0);
    }

    [Fact]
    public void ScreenCapture_CapturesValidJpeg()
    {
        using var capture = new ScreenCapture(60);
        byte[]? jpeg = capture.CaptureJpeg(out uint frameIndex, force: true);

        Assert.NotNull(jpeg);
        Assert.True(jpeg.Length > 100);
        Assert.True(frameIndex > 0);

        // JPEG SOI marker is 0xFF, 0xD8
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);

        // JPEG EOI marker is 0xFF, 0xD9
        Assert.Equal(0xFF, jpeg[^2]);
        Assert.Equal(0xD9, jpeg[^1]);
    }

    [Fact]
    public void ScreenCapture_ReturnsNullOrValidFrameWhenUnchanged()
    {
        using var capture = new ScreenCapture(50);
        byte[]? frame1 = capture.CaptureJpeg(out _, force: true);
        Assert.NotNull(frame1);

        byte[]? frame2 = capture.CaptureJpeg(out _, force: false);
        // Either null (unchanged) or valid JPEG
        if (frame2 != null)
        {
            Assert.Equal(0xFF, frame2[0]);
            Assert.Equal(0xD8, frame2[1]);
        }
    }

    [Fact]
    public void InputSimulator_ExecutesWithoutExceptions()
    {
        var mouse = new MouseInput(MouseFlags.Move, 100, 100, 0);
        bool mouseRes = InputSimulator.SimulateMouse(mouse, 1920, 1080);
        Assert.True(mouseRes);

        var kb = new KeyboardInput(KeyFlags.KeyDown, 0x10, 0);
        bool kbRes = InputSimulator.SimulateKeyboard(kb);
        Assert.True(kbRes);

        var kbUp = new KeyboardInput(KeyFlags.KeyUp, 0x10, 0);
        bool kbUpRes = InputSimulator.SimulateKeyboard(kbUp);
        Assert.True(kbUpRes);
    }
}
