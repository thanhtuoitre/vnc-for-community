using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using VncServer.Core.Logging;

namespace VncServer.Core.Network;

public sealed record UpnpMappingResult(
    bool Success,
    string? ExternalIp = null,
    int ExternalPort = 0,
    string? ErrorMessage = null);

/// <summary>
/// UPnP IGD (Internet Gateway Device) automatic port-mapping helper for WAN access.
/// Uses SSDP discovery and standard SOAP WANIPConnection:1 / WANPPPConnection:1.
/// </summary>
public sealed class UpnpPortForwarder
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly IVncLogger? _logger;
    private string? _serviceType;
    private string? _controlUrl;

    public UpnpPortForwarder(IVncLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to discover UPnP router gateway and map an external port to local machine.
    /// </summary>
    public async Task<UpnpMappingResult> ForwardPortAsync(int port, string description = "ThanhVNC Remote Desktop", CancellationToken ct = default)
    {
        try
        {
            _logger?.Info($"[UPnP] Searching for Internet Gateway Device (IGD) on LAN...");
            var deviceLocation = await DiscoverIgdLocationAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(deviceLocation))
            {
                _logger?.Info("[UPnP] No UPnP IGD router responded on LAN (UPnP may be disabled on router).");
                return new UpnpMappingResult(false, ErrorMessage: "UPnP IGD not found");
            }

            _logger?.Debug($"[UPnP] Found IGD description at {deviceLocation}");
            bool foundService = await ParseDeviceDescriptionAsync(deviceLocation, ct).ConfigureAwait(false);
            if (!foundService || _controlUrl == null || _serviceType == null)
            {
                _logger?.Warn("[UPnP] Could not locate WANIPConnection or WANPPPConnection service in IGD description.");
                return new UpnpMappingResult(false, ErrorMessage: "WAN IP/PPP service not found");
            }

            string? externalIp = await GetExternalIpAsync(_controlUrl, _serviceType, ct).ConfigureAwait(false);
            string localIp = GetLocalIpAddress();

            bool mapped = await AddPortMappingAsync(_controlUrl, _serviceType, port, port, localIp, description, ct).ConfigureAwait(false);
            if (mapped)
            {
                _logger?.Info($"[UPnP] Successfully mapped external port {port} -> {localIp}:{port} on WAN router! (Public IP: {externalIp})");
                return new UpnpMappingResult(true, externalIp, port);
            }
            else
            {
                _logger?.Warn("[UPnP] AddPortMapping SOAP request was rejected by the router.");
                return new UpnpMappingResult(false, externalIp, port, "AddPortMapping rejected");
            }
        }
        catch (Exception ex)
        {
            _logger?.Debug($"[UPnP] Port forwarding error: {ex.Message}");
            return new UpnpMappingResult(false, ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Deletes the port mapping when server shuts down.
    /// </summary>
    public async Task DeletePortMappingAsync(int port, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_controlUrl) || string.IsNullOrEmpty(_serviceType)) return;

        try
        {
            string soap = $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
  <s:Body>
    <u:DeletePortMapping xmlns:u=""{_serviceType}"">
      <NewRemoteHost></NewRemoteHost>
      <NewExternalPort>{port}</NewExternalPort>
      <NewProtocol>TCP</NewProtocol>
    </u:DeletePortMapping>
  </s:Body>
</s:Envelope>";

            using var content = new StringContent(soap, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", $"\"{_serviceType}#DeletePortMapping\"");
            await _http.PostAsync(_controlUrl, content, ct).ConfigureAwait(false);
            _logger?.Info($"[UPnP] Deleted port mapping for port {port}.");
        }
        catch { }
    }

    private static async Task<string?> DiscoverIgdLocationAsync(CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = 2500;
        udp.EnableBroadcast = true;

        string query = "M-SEARCH * HTTP/1.1\r\n" +
                       "HOST: 239.255.255.250:1900\r\n" +
                       "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n" +
                       "MAN: \"ssdp:discover\"\r\n" +
                       "MX: 2\r\n\r\n";

        byte[] queryBytes = Encoding.ASCII.GetBytes(query);
        var targetEp = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

        await udp.SendAsync(queryBytes, queryBytes.Length, targetEp).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(2500);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                string response = Encoding.ASCII.GetString(result.Buffer);

                using var reader = new StringReader(response);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
                    {
                        return line["LOCATION:".Length..].Trim();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }

        return null;
    }

    private async Task<bool> ParseDeviceDescriptionAsync(string locationUrl, CancellationToken ct)
    {
        try
        {
            string xml = await _http.GetStringAsync(locationUrl, ct).ConfigureAwait(false);
            var doc = XDocument.Parse(xml);

            var baseUri = new Uri(locationUrl);
            var services = doc.Descendants().Where(d => d.Name.LocalName == "service");

            foreach (var svc in services)
            {
                string? svcType = svc.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value;
                string? ctrlUrl = svc.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value;

                if (svcType != null && ctrlUrl != null)
                {
                    if (svcType.Contains("WANIPConnection:1") || svcType.Contains("WANPPPConnection:1") ||
                        svcType.Contains("WANIPConnection:2"))
                    {
                        _serviceType = svcType;
                        _controlUrl = new Uri(baseUri, ctrlUrl).ToString();
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    private static async Task<string?> GetExternalIpAsync(string controlUrl, string serviceType, CancellationToken ct)
    {
        try
        {
            string soap = $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
  <s:Body>
    <u:GetExternalIPAddress xmlns:u=""{serviceType}"">
    </u:GetExternalIPAddress>
  </s:Body>
</s:Envelope>";

            using var content = new StringContent(soap, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", $"\"{serviceType}#GetExternalIPAddress\"");

            var response = await _http.PostAsync(controlUrl, content, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                string resXml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var doc = XDocument.Parse(resXml);
                var elem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress");
                return elem?.Value;
            }
        }
        catch { }

        return null;
    }

    private static async Task<bool> AddPortMappingAsync(
        string controlUrl,
        string serviceType,
        int externalPort,
        int internalPort,
        string internalClientIp,
        string description,
        CancellationToken ct)
    {
        try
        {
            string soap = $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
  <s:Body>
    <u:AddPortMapping xmlns:u=""{serviceType}"">
      <NewRemoteHost></NewRemoteHost>
      <NewExternalPort>{externalPort}</NewExternalPort>
      <NewProtocol>TCP</NewProtocol>
      <NewInternalPort>{internalPort}</NewInternalPort>
      <NewInternalClient>{internalClientIp}</NewInternalClient>
      <NewEnabled>1</NewEnabled>
      <NewPortMappingDescription>{description}</NewPortMappingDescription>
      <NewLeaseDuration>0</NewLeaseDuration>
    </u:AddPortMapping>
  </s:Body>
</s:Envelope>";

            using var content = new StringContent(soap, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", $"\"{serviceType}#AddPortMapping\"");

            var response = await _http.PostAsync(controlUrl, content, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep)
            {
                return ep.Address.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
    }
}
