namespace VncServer.Core.Logging;

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

public interface IVncLogger
{
    void Log(LogLevel level, string message, Exception? ex = null);
    void Trace(string message);
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
