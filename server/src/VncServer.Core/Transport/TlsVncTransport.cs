using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace VncServer.Core.Transport;

public sealed class TlsVncTransport : IVncTransport
{
    private readonly TcpClient _tcpClient;
    private readonly SslStream _sslStream;
    private bool _isDisposed;

    public Stream Stream => _sslStream;
    public string RemoteEndPoint => _tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
    public IPAddress? RemoteAddress => (_tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address;
    public bool IsConnected => !_isDisposed && _tcpClient.Connected;
    public SslProtocols SslProtocol => _sslStream.SslProtocol;

    public TlsVncTransport(TcpClient tcpClient, SslStream sslStream)
    {
        _tcpClient = tcpClient;
        _sslStream = sslStream;
    }

    public static async Task<TlsVncTransport> AuthenticateAsServerAsync(
        TcpClient tcpClient,
        X509Certificate2 serverCert,
        CancellationToken ct = default)
    {
        tcpClient.NoDelay = true;
        var networkStream = tcpClient.GetStream();
        var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);

        var sslOptions = new SslServerAuthenticationOptions
        {
            ServerCertificate = serverCert,
            ClientCertificateRequired = false,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };

        await sslStream.AuthenticateAsServerAsync(sslOptions, ct).ConfigureAwait(false);
        return new TlsVncTransport(tcpClient, sslStream);
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _sslStream.WriteAsync(buffer, ct).ConfigureAwait(false);
        await _sslStream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _sslStream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
    }

    public async ValueTask CloseAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            try
            {
                await _sslStream.DisposeAsync().ConfigureAwait(false);
                _tcpClient.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }

    public ValueTask DisposeAsync() => CloseAsync();
}
