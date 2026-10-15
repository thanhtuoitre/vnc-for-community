using System.Diagnostics;
using System.Runtime.InteropServices;
using VncServer.Core.Input;
using VncServer.Core.Logging;
using VncServer.Core.Protocol;

namespace VncServer.Core.SystemControl;

public static class SessionController
{
    private const uint EwxLogOff = 0x00000000;
    private const uint EwxForceIfHung = 0x00000010;

    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12; // Alt
    private const ushort VkEscape = 0x1B;
    private const ushort VkTab = 0x09;
    private const ushort VkLWin = 0x5B;
    private const ushort VkD = 0x44;
    private const ushort VkX = 0x58;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    public static bool Execute(SessionControl control, IVncLogger? logger = null)
    {
        try
        {
            switch (control.Action)
            {
                case SessionAction.Lock:
                    logger?.Info("SessionController: Locking workstation.");
                    return LockWorkStation();

                case SessionAction.LogOff:
                    logger?.Info("SessionController: Logging off current user.");
                    return ExitWindowsEx(EwxLogOff | EwxForceIfHung, 0);

                case SessionAction.Reboot:
                    logger?.Info("SessionController: Triggering system reboot.");
                    return RunProcess("shutdown.exe", "/r /t 0");

                case SessionAction.Shutdown:
                    logger?.Info("SessionController: Triggering system shutdown.");
                    return RunProcess("shutdown.exe", "/s /t 0");

                case SessionAction.KeyCombo:
                    var combo = (KeyCombo)control.Parameter;
                    logger?.Info($"SessionController: Executing key combo: {combo}");
                    return ExecuteKeyCombo(combo);

                case SessionAction.SwitchMonitor:
                    logger?.Info($"SessionController: Switch monitor requested (index: {control.Parameter})");
                    return true;

                default:
                    logger?.Warn($"SessionController: Unknown action {(byte)control.Action}");
                    return false;
            }
        }
        catch (Exception ex)
        {
            logger?.Error($"SessionController error executing {control.Action}: {ex.Message}", ex);
            return false;
        }
    }

    private static bool ExecuteKeyCombo(KeyCombo combo)
    {
        switch (combo)
        {
            case KeyCombo.WinD:
                // Win down, D down, D up, Win up
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkLWin, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkD, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkD, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkLWin, 0));
                return true;

            case KeyCombo.WinX:
                // Win down, X down, X up, Win up
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkLWin, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkX, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkX, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkLWin, 0));
                return true;

            case KeyCombo.AltTab:
                // Alt down, Tab down, Tab up, Alt up
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkMenu, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkTab, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkTab, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkMenu, 0));
                return true;

            case KeyCombo.CtrlEsc:
                // Ctrl down, Esc down, Esc up, Ctrl up
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkControl, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkEscape, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkEscape, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkControl, 0));
                return true;

            case KeyCombo.CtrlShiftEsc:
                // Task Manager shortcut
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkControl, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkShift, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyDown, VkEscape, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkEscape, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkShift, 0));
                InputSimulator.SimulateKeyboard(new KeyboardInput(KeyFlags.KeyUp, VkControl, 0));
                return true;

            case KeyCombo.CtrlAltDel:
                // Windows isolates Ctrl+Alt+Del for security (SAS). Launch Task Manager as user-friendly fallback.
                return RunProcess("taskmgr.exe", "");

            default:
                return false;
        }
    }

    private static bool RunProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            return proc != null;
        }
        catch
        {
            return false;
        }
    }
}
