using System.Net;
using System.Net.Sockets;

namespace VncServer.Core.Transport;

public sealed class TcpVncTransport : IVncTransport
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private bool _isDisposed;

    public Stream Stream => _stream;
    public string RemoteEndPoint => _tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
    public IPAddress? RemoteAddress => (_tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address;
    public bool IsConnected => !_isDisposed && _tcpClient.Connected;

    public TcpVncTransport(TcpClient tcpClient)
    {
        _tcpClient = tcpClient;
        _tcpClient.NoDelay = true;
        _stream = tcpClient.GetStream();
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _stream.WriteAsync(buffer, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
    }

    public ValueTask CloseAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            try
            {
                _stream.Dispose();
                _tcpClient.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => CloseAsync();
}
