using System.Runtime.InteropServices;
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
    public void ScreenCapture_DynamicQualityChange()
    {
        using var capture = new ScreenCapture(20);
        Assert.Equal(20, capture.Quality);

        byte[]? lowQ = capture.CaptureJpeg(out _, force: true);
        Assert.NotNull(lowQ);

        capture.SetQuality(90);
        Assert.Equal(90, capture.Quality);

        byte[]? highQ = capture.CaptureJpeg(out _, force: true);
        Assert.NotNull(highQ);

        // Quality 90 JPEG should be larger than Quality 20 JPEG
        Assert.True(highQ.Length > lowQ.Length);
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

    [Fact]
    public void CursorCapture_GetCurrentCursor_ReturnsValidData()
    {
        bool success = CursorCapture.GetCurrentCursor(out int x, out int y, out bool visible, out var hCursor);
        Assert.True(success);
        // On a running Windows session, coordinates should be within reasonable screen bounds
        Assert.True(x >= -10000 && x <= 20000);
        Assert.True(y >= -10000 && y <= 20000);
    }

    [Fact]
    public void CursorCapture_FallbackArrow_ProducesValidPng()
    {
        var shape = CursorCapture.CreateFallbackArrow();
        Assert.NotNull(shape);
        Assert.Equal(32, shape.Width);
        Assert.Equal(32, shape.Height);
        Assert.Equal(0, shape.HotspotX);
        Assert.Equal(0, shape.HotspotY);
        Assert.Equal(1, shape.Format);
        Assert.True(shape.Data.Length > 50);

        // Verify PNG header: 0x89, 'P', 'N', 'G'
        Assert.Equal(0x89, shape.Data[0]);
        Assert.Equal((byte)'P', shape.Data[1]);
        Assert.Equal((byte)'N', shape.Data[2]);
        Assert.Equal((byte)'G', shape.Data[3]);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [Theory]
    [InlineData(32512)] // IDC_ARROW
    [InlineData(32513)] // IDC_IBEAM
    [InlineData(32649)] // IDC_HAND
    [InlineData(32644)] // IDC_SIZEWE
    [InlineData(32645)] // IDC_SIZENS
    public void CursorCapture_StandardWindowsCursors_CaptureValidShapeAndHotspot(int cursorId)
    {
        IntPtr hCursor = LoadCursor(IntPtr.Zero, cursorId);
        if (hCursor == IntPtr.Zero) return;

        var shape = CursorCapture.CaptureShape(hCursor);
        Assert.NotNull(shape);
        Assert.True(shape.Width > 0 && shape.Width <= 128);
        Assert.True(shape.Height > 0 && shape.Height <= 128);
        Assert.True(shape.HotspotX >= 0 && shape.HotspotX < shape.Width);
        Assert.True(shape.HotspotY >= 0 && shape.HotspotY < shape.Height);
        Assert.Equal(1, shape.Format);
        Assert.True(shape.Data.Length > 50);

        // Verify PNG magic bytes
        Assert.Equal(0x89, shape.Data[0]);
        Assert.Equal((byte)'P', shape.Data[1]);
        Assert.Equal((byte)'N', shape.Data[2]);
        Assert.Equal((byte)'G', shape.Data[3]);
    }
}

