namespace VncServer.Core.Logging;

public sealed class ActionVncLogger : IVncLogger
{
    private readonly Action<string> _action;

    public ActionVncLogger(Action<string> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Log(LogLevel level, string message, Exception? ex = null)
    {
        string text = ex != null ? $"{message} (Exception: {ex.Message})" : message;
        _action(text);
    }

    public void Trace(string message) => Log(LogLevel.Trace, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warn(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
}
