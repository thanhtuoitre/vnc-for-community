using System.Runtime.InteropServices;
using VncServer.Core.Protocol;

namespace VncServer.Core.Input;

public sealed class InputSimulator
{
    // ponytail: InputSimulator calls user32 SendInput; upgrade to Windows Synthetic Input or driver injection when UAC/secure desktop injection is needed.
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;
    private const uint MouseEventAbsolute = 0x8000;
    private const uint MouseEventVirtualDesk = 0x4000;

    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public static bool SimulateMouse(MouseInput input, int screenWidth, int screenHeight)
    {
        Native.DesktopHelper.AttachToInputDesktop();
        uint flags = 0;
        int normX = 0;
        int normY = 0;

        if (input.Flags.HasFlag(MouseFlags.Move))
        {
            flags |= MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk;
            int maxW = Math.Max(1, screenWidth - 1);
            int maxH = Math.Max(1, screenHeight - 1);
            normX = (int)Math.Round((Math.Clamp(input.X, 0, maxW) * 65535.0) / maxW);
            normY = (int)Math.Round((Math.Clamp(input.Y, 0, maxH) * 65535.0) / maxH);
        }

        if (input.Flags.HasFlag(MouseFlags.LeftDown)) flags |= MouseEventLeftDown;
        if (input.Flags.HasFlag(MouseFlags.LeftUp)) flags |= MouseEventLeftUp;
        if (input.Flags.HasFlag(MouseFlags.RightDown)) flags |= MouseEventRightDown;
        if (input.Flags.HasFlag(MouseFlags.RightUp)) flags |= MouseEventRightUp;
        if (input.Flags.HasFlag(MouseFlags.MiddleDown)) flags |= MouseEventMiddleDown;
        if (input.Flags.HasFlag(MouseFlags.MiddleUp)) flags |= MouseEventMiddleUp;
        if (input.Flags.HasFlag(MouseFlags.Wheel)) flags |= MouseEventWheel;

        if (flags == 0) return true;

        var nativeInput = new INPUT
        {
            type = InputMouse,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = normX,
                    dy = normY,
                    mouseData = (uint)(int)input.WheelDelta,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        return SendInput(1, [nativeInput], Marshal.SizeOf<INPUT>()) > 0;
    }

    public static bool SimulateKeyboard(KeyboardInput input)
    {
        Native.DesktopHelper.AttachToInputDesktop();
        uint flags = 0;
        if (input.Flags.HasFlag(KeyFlags.KeyUp)) flags |= KeyEventKeyUp;
        if (input.Flags.HasFlag(KeyFlags.ExtendedKey)) flags |= KeyEventExtendedKey;

        var nativeInput = new INPUT
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = input.VirtualKey,
                    wScan = input.ScanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        return SendInput(1, [nativeInput], Marshal.SizeOf<INPUT>()) > 0;
    }
}
