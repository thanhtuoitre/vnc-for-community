using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using VncServer.Core.Logging;

namespace VncServer.Core.Service;

public static class WindowsServiceHelper
{
    private const string AutoStartKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ThanhVNCServer";

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static bool ConfigureAutoStart(bool enable, string? executablePath = null, string args = "")
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, writable: true);
            if (key == null) return false;

            if (enable)
            {
                string exe = executablePath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exe)) return false;

                string command = string.IsNullOrWhiteSpace(args) ? $"\"{exe}\"" : $"\"{exe}\" {args}";
                key.SetValue(AppName, command);
                return true;
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static uint GetActiveInteractiveSessionId()
    {
        return WTSGetActiveConsoleSessionId();
    }
}
