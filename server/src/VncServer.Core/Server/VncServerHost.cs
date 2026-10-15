using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VncServer.Core.Capture;
using VncServer.Core.Logging;

namespace VncServer.Core.Server;

public sealed class VncServerHost : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IScreenCapture _capture;
    private readonly string _password;
    private readonly IVncLogger? _logger;
    private readonly Security.IpRateLimiter _rateLimiter = new();
    private readonly ConcurrentDictionary<VncSession, byte> _sessions = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;

    public int Port { get; }
    public bool IsRunning { get; private set; }

    public VncServerHost(int port, string password, int captureQuality = 70, IVncLogger? logger = null, IScreenCapture? capture = null)
    {
        Port = port;
        _password = password;
        _logger = logger;
        _capture = capture ?? new HybridScreenCapture(captureQuality, logger);
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public VncServerHost(int port, string password, int captureQuality, Action<string>? log)
        : this(port, password, captureQuality, log != null ? new ActionVncLogger(log) : null)
    {
    }

    public void Start()
    {
        if (IsRunning) return;
        _listener.Start();
        IsRunning = true;
        _logger?.Info($"VNC Server listening on port {Port}...");
        _acceptTask = AcceptConnectionsAsync(_cts.Token);
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                if (tcpClient.Client.RemoteEndPoint is IPEndPoint ep)
                {
                    if (!_rateLimiter.IsConnectionAllowed(ep.Address))
                    {
                        _logger?.Warn($"Connection rejected from {ep.Address} (throttled / rate limited).");
                        tcpClient.Dispose();
                        continue;
                    }
                    _rateLimiter.RecordConnection(ep.Address);
                }

                var transport = new Transport.TcpVncTransport(tcpClient);
                var session = new VncSession(transport, _capture, _password, _logger, _rateLimiter);
                _sessions.TryAdd(session, 0);

                _logger?.Info($"Accepted connection from {session.RemoteEndPoint}");

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
                        _logger?.Info($"Closed session with {session.RemoteEndPoint}");
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
                    _logger?.Error($"Accept error: {ex.Message}", ex);
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
        _logger?.Info("VNC Server stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _capture.Dispose();
        _cts.Dispose();
    }
}
