namespace VncServer.Core.Logging;

public sealed class FileVncLogger : IVncLogger
{
    private readonly string _logFilePath;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxArchivedFiles;
    private readonly object _syncLock = new();

    public FileVncLogger(string logFilePath, long maxFileSizeBytes = 5 * 1024 * 1024, int maxArchivedFiles = 5)
    {
        _logFilePath = Path.GetFullPath(logFilePath);
        _maxFileSizeBytes = maxFileSizeBytes;
        _maxArchivedFiles = maxArchivedFiles;

        string? dir = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public void Log(LogLevel level, string message, Exception? ex = null)
    {
        lock (_syncLock)
        {
            try
            {
                RotateLogsIfNeeded();

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string line = $"[{timestamp}] [{level.ToString().ToUpperInvariant(),-5}] {message}";

                using (var sw = File.AppendText(_logFilePath))
                {
                    sw.WriteLine(line);
                    if (ex != null)
                    {
                        sw.WriteLine($"  -> Exception: {ex.GetType().Name}: {ex.Message}");
                        if (!string.IsNullOrEmpty(ex.StackTrace))
                        {
                            sw.WriteLine($"  {ex.StackTrace}");
                        }
                    }
                }
            }
            catch
            {
                // Never crash server on file logger failure
            }
        }
    }

    private void RotateLogsIfNeeded()
    {
        if (!File.Exists(_logFilePath)) return;

        var fi = new FileInfo(_logFilePath);
        if (fi.Length < _maxFileSizeBytes) return;

        string dir = Path.GetDirectoryName(_logFilePath)!;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(_logFilePath);
        string ext = Path.GetExtension(_logFilePath);

        // Delete the oldest archive file if it exists
        string oldest = Path.Combine(dir, $"{nameWithoutExt}.{_maxArchivedFiles}{ext}");
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        // Shift existing archives: .4 -> .5, .3 -> .4, etc.
        for (int i = _maxArchivedFiles - 1; i >= 1; i--)
        {
            string current = Path.Combine(dir, $"{nameWithoutExt}.{i}{ext}");
            string next = Path.Combine(dir, $"{nameWithoutExt}.{i + 1}{ext}");
            if (File.Exists(current))
            {
                File.Move(current, next, overwrite: true);
            }
        }

        // Move current log to .1
        string first = Path.Combine(dir, $"{nameWithoutExt}.1{ext}");
        File.Move(_logFilePath, first, overwrite: true);
    }

    public void Trace(string message) => Log(LogLevel.Trace, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warn(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
}
