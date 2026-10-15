namespace VncServer.Core.Security;

public static class SessionAuditLogger
{
    private static readonly string AuditDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThanhVNC",
        "logs");

    private static readonly string AuditFilePath = Path.Combine(AuditDir, "audit.log");
    private static readonly object SyncLock = new();

    public static void RecordEvent(string eventType, string remoteEndPoint, string details)
    {
        lock (SyncLock)
        {
            try
            {
                if (!Directory.Exists(AuditDir))
                {
                    Directory.CreateDirectory(AuditDir);
                }

                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                string line = $"[{timestamp}] [AUDIT] [{eventType,-14}] [{remoteEndPoint}] {details}";

                File.AppendAllText(AuditFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Never crash server on audit logger failure
            }
        }
    }
}
