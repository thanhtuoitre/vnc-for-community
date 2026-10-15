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
    private readonly Security.SessionResumeManager _resumeManager = new();
    private readonly ConcurrentDictionary<VncSession, byte> _sessions = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;
    private Network.LanDiscoveryService? _discoveryService;
    private Network.UpnpPortForwarder? _upnpForwarder;

    public int Port { get; }
    public bool IsRunning { get; private set; }
    public System.Security.Cryptography.X509Certificates.X509Certificate2? ServerCertificate { get; }
    public bool UseTls { get; }
    public bool EnableDiscovery { get; }
    public bool EnableUpnp { get; }
    public string? ServerName { get; }

    public VncServerHost(
        int port,
        string password,
        int captureQuality = 70,
        IVncLogger? logger = null,
        IScreenCapture? capture = null,
        System.Security.Cryptography.X509Certificates.X509Certificate2? serverCertificate = null,
        bool useTls = false,
        bool enableDiscovery = true,
        bool enableUpnp = false,
        string? serverName = null)
    {
        Port = port;
        _password = password;
        _logger = logger;
        _capture = capture ?? new HybridScreenCapture(captureQuality, logger);
        _listener = new TcpListener(IPAddress.Any, port);
        ServerCertificate = serverCertificate;
        UseTls = useTls || serverCertificate != null;
        EnableDiscovery = enableDiscovery;
        EnableUpnp = enableUpnp;
        ServerName = serverName;
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
        _logger?.Info($"VNC Server listening on port {Port} (TLS: {UseTls})...");
        _acceptTask = AcceptConnectionsAsync(_cts.Token);

        if (EnableDiscovery)
        {
            string? fp = ServerCertificate != null ? Convert.ToHexString(ServerCertificate.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256)) : null;
            _discoveryService = new Network.LanDiscoveryService(Port, UseTls, fp, ServerName, _logger);
            _discoveryService.Start();
        }

        if (EnableUpnp)
        {
            _upnpForwarder = new Network.UpnpPortForwarder(_logger);
            _ = Task.Run(async () => await _upnpForwarder.ForwardPortAsync(Port).ConfigureAwait(false));
        }
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

                Transport.IVncTransport transport;
                if (UseTls && ServerCertificate != null)
                {
                    transport = await Transport.TlsVncTransport.AuthenticateAsServerAsync(tcpClient, ServerCertificate, ct).ConfigureAwait(false);
                }
                else
                {
                    transport = new Transport.TcpVncTransport(tcpClient);
                }
                var session = new VncSession(transport, _capture, _password, _logger, _rateLimiter, _resumeManager);
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

        if (_discoveryService != null)
        {
            await _discoveryService.DisposeAsync().ConfigureAwait(false);
            _discoveryService = null;
        }

        if (_upnpForwarder != null)
        {
            await _upnpForwarder.DeletePortMappingAsync(Port).ConfigureAwait(false);
            _upnpForwarder = null;
        }

        _logger?.Info("VNC Server stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _capture.Dispose();
        _cts.Dispose();
    }
}
