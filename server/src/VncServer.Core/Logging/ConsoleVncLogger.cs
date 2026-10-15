namespace VncServer.Core.Logging;

public sealed class ConsoleVncLogger : IVncLogger
{
    private static readonly object SyncLock = new();

    public void Log(LogLevel level, string message, Exception? ex = null)
    {
        lock (SyncLock)
        {
            var origColor = Console.ForegroundColor;
            Console.ForegroundColor = level switch
            {
                LogLevel.Trace => ConsoleColor.DarkGray,
                LogLevel.Debug => ConsoleColor.Gray,
                LogLevel.Info => ConsoleColor.Cyan,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
                _ => ConsoleColor.White
            };

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string formatted = $"[{timestamp}] [{level.ToString().ToUpperInvariant(),-5}] {message}";
            Console.WriteLine(formatted);

            if (ex != null)
            {
                Console.WriteLine($"  -> Exception: {ex.GetType().Name}: {ex.Message}");
                if (!string.IsNullOrEmpty(ex.StackTrace))
                {
                    Console.WriteLine($"  {ex.StackTrace}");
                }
            }

            Console.ForegroundColor = origColor;
        }
    }

    public void Trace(string message) => Log(LogLevel.Trace, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warn(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
}
