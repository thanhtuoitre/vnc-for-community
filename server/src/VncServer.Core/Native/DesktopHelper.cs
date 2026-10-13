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
