using System.Net;
using System.Net.Sockets;
using VncServer.Core.Server;

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

Console.WriteLine($"Server Port: {port}");
Console.WriteLine($"Password:    {password}");
Console.WriteLine("Local IPv4 Addresses:");

try
{
    var host = Dns.GetHostEntry(Dns.GetHostName());
    foreach (var ip in host.AddressList)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            Console.WriteLine($"  - {ip}:{port}");
        }
    }
}
catch
{
    Console.WriteLine($"  - 127.0.0.1:{port}");
}

Console.WriteLine();
Console.WriteLine("Press Ctrl+C to shut down.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await using var server = new VncServerHost(port, password, 70, Console.WriteLine);
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
