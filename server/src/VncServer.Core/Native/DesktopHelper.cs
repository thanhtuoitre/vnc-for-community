using System.Runtime.InteropServices;

namespace VncServer.Core.Native;

public static class DesktopHelper
{
    // Attaches the current thread to the active interactive input desktop (Default, Winlogon, etc.)
    private const uint DesktopAll = 0x01FF;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDPIAware();

    public static void EnableDpiAwareness()
    {
        try
        {
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
            if (!SetProcessDpiAwarenessContext(new IntPtr(-4)))
            {
                SetProcessDPIAware();
            }
        }
        catch
        {
            try { SetProcessDPIAware(); } catch { }
        }
    }

    public static void AttachToInputDesktop()
    {
        IntPtr desk = OpenInputDesktop(0, false, DesktopAll);
        if (desk != IntPtr.Zero)
        {
            SetThreadDesktop(desk);
            CloseDesktop(desk);
        }
    }
}
