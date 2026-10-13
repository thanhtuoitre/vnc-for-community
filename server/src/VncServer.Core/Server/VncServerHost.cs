using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VncServer.Core.Capture;

namespace VncServer.Core.Server;

public sealed class VncServerHost : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly ScreenCapture _capture;
    private readonly string _password;
    private readonly Action<string>? _log;
    private readonly ConcurrentDictionary<VncSession, byte> _sessions = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;

    public int Port { get; }
    public bool IsRunning { get; private set; }

    public VncServerHost(int port, string password, int captureQuality = 70, Action<string>? log = null)
    {
        Port = port;
        _password = password;
        _log = log;
        _capture = new ScreenCapture(captureQuality);
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start()
    {
        if (IsRunning) return;
        _listener.Start();
        IsRunning = true;
        _log?.Invoke($"VNC Server listening on port {Port}...");
        _acceptTask = AcceptConnectionsAsync(_cts.Token);
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                var session = new VncSession(tcpClient, _capture, _password, _log);
                _sessions.TryAdd(session, 0);

                _log?.Invoke($"Accepted connection from {session.RemoteEndPoint}");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await session.RunAsync(ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        _sessions.TryRemove(session, out _);
                        await session.DisposeAsync().ConfigureAwait(false);
                        _log?.Invoke($"Closed session with {session.RemoteEndPoint}");
                    }
                }, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    _log?.Invoke($"Accept error: {ex.Message}");
                }
            }
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts.Cancel();
        _listener.Stop();

        if (_acceptTask != null)
        {
            try { await _acceptTask.ConfigureAwait(false); } catch { }
        }

        foreach (var session in _sessions.Keys)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        _sessions.Clear();
        _log?.Invoke("VNC Server stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _capture.Dispose();
        _cts.Dispose();
    }
}
