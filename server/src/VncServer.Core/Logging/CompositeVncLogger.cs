namespace VncServer.Core.Logging;

public sealed class CompositeVncLogger : IVncLogger
{
    private readonly List<IVncLogger> _loggers;

    public CompositeVncLogger(params IVncLogger[] loggers)
    {
        _loggers = [.. loggers];
    }

    public void AddLogger(IVncLogger logger)
    {
        lock (_loggers)
        {
            _loggers.Add(logger);
        }
    }

    public void Log(LogLevel level, string message, Exception? ex = null)
    {
        lock (_loggers)
        {
            foreach (var logger in _loggers)
            {
                try
                {
                    logger.Log(level, message, ex);
                }
                catch
                {
                    // Ignore logger errors
                }
            }
        }
    }

    public void Trace(string message) => Log(LogLevel.Trace, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warn(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
}
