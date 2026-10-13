using System.Runtime.InteropServices;
using System.Text;

namespace VncServer.Core.Input;

public static class WindowsClipboard
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    public static bool SetText(string text)
    {
        if (text == null) return false;

        // Retry up to 5 times in case another app currently has clipboard locked
        for (int i = 0; i < 5; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                    IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                    if (hMem == IntPtr.Zero) return false;

                    IntPtr target = GlobalLock(hMem);
                    if (target == IntPtr.Zero)
                    {
                        GlobalFree(hMem);
                        return false;
                    }

                    Marshal.Copy(bytes, 0, target, bytes.Length);
                    GlobalUnlock(hMem);

                    SetClipboardData(CF_UNICODETEXT, hMem);
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(20);
        }

        return false;
    }

    public static string? GetText()
    {
        for (int i = 0; i < 5; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
                        return null;

                    IntPtr hMem = GetClipboardData(CF_UNICODETEXT);
                    if (hMem == IntPtr.Zero) return null;

                    IntPtr pText = GlobalLock(hMem);
                    if (pText == IntPtr.Zero) return null;

                    try
                    {
                        return Marshal.PtrToStringUni(pText);
                    }
                    finally
                    {
                        GlobalUnlock(hMem);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(20);
        }

        return null;
    }
}
