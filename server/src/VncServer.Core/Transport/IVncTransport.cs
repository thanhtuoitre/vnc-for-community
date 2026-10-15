using System.Net;

namespace VncServer.Core.Transport;

public interface IVncTransport : IAsyncDisposable
{
    Stream Stream { get; }
    string RemoteEndPoint { get; }
    IPAddress? RemoteAddress { get; }
    bool IsConnected { get; }

    ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default);
    ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken ct = default);
    ValueTask CloseAsync();
}
