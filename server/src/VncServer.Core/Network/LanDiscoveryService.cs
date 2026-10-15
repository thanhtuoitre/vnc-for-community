using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VncServer.Core.Logging;

namespace VncServer.Core.Network;

public sealed record ServerBeaconPayload(
    string Name,
    int Port,
    bool Tls,
    string? Fingerprint,
    int Version = 1);

/// <summary>
/// Background zero-configuration LAN discovery service.
/// Broadcasts server presence and responds to discovery probes over UDP port 5902.
/// </summary>
public sealed class LanDiscoveryService : IAsyncDisposable, IDisposable
{
    public const int DefaultDiscoveryPort = 5902;
    public const string ProbeMagic = "THVNC_PROBE";
    public const string BeaconPrefix = "THVNC_BEACON:";

    private readonly int _serverPort;
    private readonly bool _useTls;
    private readonly string? _fingerprint;
    private readonly string _serverName;
    private readonly IVncLogger? _logger;
    private readonly CancellationTokenSource _cts = new();

    private UdpClient? _udpClient;
    private Task? _listenTask;
    private Task? _broadcastTask;

    public bool IsRunning { get; private set; }

    public LanDiscoveryService(
        int serverPort,
        bool useTls,
        string? fingerprint = null,
        string? serverName = null,
        IVncLogger? logger = null)
    {
        _serverPort = serverPort;
        _useTls = useTls;
        _fingerprint = fingerprint;
        _serverName = string.IsNullOrWhiteSpace(serverName) ? Environment.MachineName : serverName;
        _logger = logger;
    }

    public void Start()
    {
        if (IsRunning) return;

        try
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DefaultDiscoveryPort));
            _udpClient.EnableBroadcast = true;

            IsRunning = true;
            _listenTask = ListenForProbesAsync(_cts.Token);
            _broadcastTask = PeriodicBroadcastAsync(_cts.Token);

            _logger?.Info($"LAN Discovery service active on UDP port {DefaultDiscoveryPort}.");
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Could not bind LAN discovery service on UDP port {DefaultDiscoveryPort}: {ex.Message}");
        }
    }

    public string BuildBeaconJson()
    {
        var payload = new ServerBeaconPayload(
            Name: _serverName,
            Port: _serverPort,
            Tls: _useTls,
            Fingerprint: _fingerprint,
            Version: 1);

        return JsonSerializer.Serialize(payload);
    }

    public byte[] BuildBeaconPacket()
    {
        string json = BuildBeaconJson();
        return Encoding.UTF8.GetBytes(BeaconPrefix + json);
    }

    private async Task ListenForProbesAsync(CancellationToken ct)
    {
        var beaconBytes = BuildBeaconPacket();

        while (!ct.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct).ConfigureAwait(false);
                string text = Encoding.UTF8.GetString(result.Buffer);

                if (text.StartsWith(ProbeMagic, StringComparison.OrdinalIgnoreCase))
                {
                    // Respond directly to the sender
                    await _udpClient.SendAsync(beaconBytes, beaconBytes.Length, result.RemoteEndPoint).ConfigureAwait(false);
                    _logger?.Debug($"Responded to LAN discovery probe from {result.RemoteEndPoint}");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    _logger?.Debug($"Discovery probe receive error: {ex.Message}");
                }
            }
        }
    }

    private async Task PeriodicBroadcastAsync(CancellationToken ct)
    {
        var beaconBytes = BuildBeaconPacket();
        var broadcastEp = new IPEndPoint(IPAddress.Broadcast, DefaultDiscoveryPort);

        while (!ct.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                // Send to 255.255.255.255
                await _udpClient.SendAsync(beaconBytes, beaconBytes.Length, broadcastEp).ConfigureAwait(false);

                // Also send directly on all active IPv4 broadcast addresses
                foreach (var broadcastAddr in GetSubnetBroadcastAddresses())
                {
                    try
                    {
                        var ep = new IPEndPoint(broadcastAddr, DefaultDiscoveryPort);
                        await _udpClient.SendAsync(beaconBytes, beaconBytes.Length, ep).ConfigureAwait(false);
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    _logger?.Debug($"Discovery broadcast warning: {ex.Message}");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static IEnumerable<IPAddress> GetSubnetBroadcastAddresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up ||
                    iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var unicast in iface.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask != null)
                    {
                        byte[] ipBytes = unicast.Address.GetAddressBytes();
                        byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();
                        byte[] broadcastBytes = new byte[4];
                        for (int i = 0; i < 4; i++)
                        {
                            broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                        }
                        list.Add(new IPAddress(broadcastBytes));
                    }
                }
            }
        }
        catch { }
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _cts.Cancel();
        _udpClient?.Dispose();

        if (_listenTask != null)
        {
            try { await _listenTask.ConfigureAwait(false); } catch { }
        }
        if (_broadcastTask != null)
        {
            try { await _broadcastTask.ConfigureAwait(false); } catch { }
        }

        _cts.Dispose();
        _logger?.Info("LAN Discovery service stopped.");
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
