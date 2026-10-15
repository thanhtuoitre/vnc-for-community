using System.Net;
using System.Net.Sockets;
using VncServer.Core.Logging;
using VncServer.Core.Server;

// Initialize structured logging and crash reporting
string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThanhVNC", "logs");
string logFile = Path.Combine(logDir, "vnc-server.log");
var consoleLogger = new ConsoleVncLogger();
var fileLogger = new FileVncLogger(logFile);
var logger = new CompositeVncLogger(consoleLogger, fileLogger);
CrashReporter.Initialize(Path.Combine(logDir, "..", "crashes"), logger);

Console.WriteLine("========================================");
Console.WriteLine("        VNC Remote Desktop Server       ");
Console.WriteLine("========================================");

int port = 5901;
string password = "vncsecret";

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int p))
    {
        port = p;
    }
    else if (args[i] == "--password" && i + 1 < args.Length)
    {
        password = args[i + 1];
    }
}

logger.Info($"Server Port: {port}");
logger.Info($"Log File:    {logFile}");
logger.Info("Local IPv4 Addresses:");

try
{
    var host = Dns.GetHostEntry(Dns.GetHostName());
    foreach (var ip in host.AddressList)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            logger.Info($"  - {ip}:{port}");
        }
    }
}
catch
{
    logger.Info($"  - 127.0.0.1:{port}");
}

Console.WriteLine();
Console.WriteLine("Press Ctrl+C to shut down.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await using var server = new VncServerHost(port, password, 70, logger);
server.Start();

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nShutting down server...");
}

await server.StopAsync();
Console.WriteLine("Server exited.");
