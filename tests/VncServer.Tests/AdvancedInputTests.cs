using VncServer.Core.Input;
using VncServer.Core.Protocol;

namespace VncServer.Tests;

public class AdvancedInputTests
{
    [Theory]
    [InlineData(MouseFlags.LeftDown)]
    [InlineData(MouseFlags.LeftUp)]
    [InlineData(MouseFlags.RightDown)]
    [InlineData(MouseFlags.RightUp)]
    [InlineData(MouseFlags.MiddleDown)]
    [InlineData(MouseFlags.MiddleUp)]
    [InlineData(MouseFlags.Wheel)]
    public void SimulateMouse_ClickAndWheelEvents_ExecuteCleanly(MouseFlags flag)
    {
        var input = new MouseInput(flag, 500, 500, flag == MouseFlags.Wheel ? (short)120 : (short)0);
        bool res = InputSimulator.SimulateMouse(input, 1920, 1080);
        Assert.True(res);
    }

    [Theory]
    [InlineData(0x08, 0x0E)] // Backspace
    [InlineData(0x09, 0x0F)] // Tab
    [InlineData(0x0D, 0x1C)] // Enter
    [InlineData(0x1B, 0x01)] // Escape
    [InlineData(0x25, 0x4B)] // Left arrow
    [InlineData(0x26, 0x48)] // Up arrow
    [InlineData(0x27, 0x4D)] // Right arrow
    [InlineData(0x28, 0x50)] // Down arrow
    public void SimulateKeyboard_SpecialKeys_ExecuteCleanly(ushort vk, ushort sc)
    {
        var down = new KeyboardInput(KeyFlags.KeyDown, vk, sc);
        bool downRes = InputSimulator.SimulateKeyboard(down);
        Assert.True(downRes);

        var up = new KeyboardInput(KeyFlags.KeyUp, vk, sc);
        bool upRes = InputSimulator.SimulateKeyboard(up);
        Assert.True(upRes);
    }

    [Theory]
    [InlineData('A')]
    [InlineData('z')]
    [InlineData('9')]
    [InlineData('đ')]
    [InlineData('ê')]
    [InlineData('€')]
    [InlineData('@')]
    public void SimulateKeyboard_UnicodeCharacters_ExecuteCleanly(char c)
    {
        var down = new KeyboardInput(KeyFlags.KeyDown | KeyFlags.Unicode, 0, (ushort)c);
        bool downRes = InputSimulator.SimulateKeyboard(down);
        Assert.True(downRes);

        var up = new KeyboardInput(KeyFlags.KeyUp | KeyFlags.Unicode, 0, (ushort)c);
        bool upRes = InputSimulator.SimulateKeyboard(up);
        Assert.True(upRes);
    }

    [Fact]
    public void SimulateMouse_ClickWithCoordinate_ExecutesPositionedClick()
    {
        var click = new MouseInput(MouseFlags.LeftDown, 640, 480, 0);
        bool res = InputSimulator.SimulateMouse(click, 1920, 1080);
        Assert.True(res);

        var release = new MouseInput(MouseFlags.LeftUp, 640, 480, 0);
        bool resUp = InputSimulator.SimulateMouse(release, 1920, 1080);
        Assert.True(resUp);
    }
}
