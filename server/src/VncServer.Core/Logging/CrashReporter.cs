using System.Runtime.InteropServices;

namespace VncServer.Core.Logging;

public static class CrashReporter
{
    private static string _crashDumpDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThanhVNC",
        "crashes");

    private static IVncLogger? _logger;

    public static void Initialize(string? crashDumpDir = null, IVncLogger? logger = null)
    {
        if (!string.IsNullOrEmpty(crashDumpDir))
        {
            _crashDumpDir = crashDumpDir;
        }
        _logger = logger;

        try
        {
            if (!Directory.Exists(_crashDumpDir))
            {
                Directory.CreateDirectory(_crashDumpDir);
            }
        }
        catch
        {
            // Directory creation error fallback
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        WriteCrashDump("UnhandledException", ex, e.IsTerminating);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashDump("UnobservedTaskException", e.Exception, false);
    }

    public static void WriteCrashDump(string source, Exception? ex, bool isTerminating)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string filePath = Path.Combine(_crashDumpDir, $"crash_{timestamp}.txt");

        try
        {
            var content = $"""
                ================================================================================
                ThanhVNC Server Crash Report
                Timestamp: {DateTime.Now:O}
                Source: {source}
                IsTerminating: {isTerminating}
                OS: {Environment.OSVersion} ({RuntimeInformation.OSArchitecture})
                CLR: {Environment.Version}
                Working Set: {Environment.WorkingSet / (1024 * 1024)} MB
                ================================================================================
                Exception Type: {ex?.GetType().FullName}
                Message: {ex?.Message}
                StackTrace:
                {ex?.StackTrace}
                ================================================================================
                Inner Exception:
                {ex?.InnerException?.ToString()}
                ================================================================================
                """;

            File.WriteAllText(filePath, content);
            _logger?.Error($"CRASH REPORT GENERATED at: {filePath}", ex);
        }
        catch
        {
            // Fallback: write to stderr
            Console.Error.WriteLine($"[CRITICAL] Unhandled crash: {ex?.Message}");
        }
    }
}
