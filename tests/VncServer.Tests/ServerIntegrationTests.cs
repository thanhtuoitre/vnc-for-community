using System.Net.Sockets;
using VncServer.Core.Protocol;
using VncServer.Core.Server;

namespace VncServer.Tests;

public class ServerIntegrationTests
{
    private static async Task<(VncMessageType Type, byte[] Payload)> ReadPacketAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] headerBuffer = new byte[VncProtocol.HeaderSize];
        await stream.ReadExactlyAsync(headerBuffer, ct);

        if (!VncProtocol.TryReadHeader(headerBuffer, out var type, out int payloadLength))
            throw new InvalidDataException("Invalid packet header");

        byte[] payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            await stream.ReadExactlyAsync(payload, ct);
        }

        return (type, payload);
    }

    private static async Task SendPacketAsync(NetworkStream stream, VncMessageType type, byte[] payload, CancellationToken ct)
    {
        byte[] headerBuffer = new byte[VncProtocol.HeaderSize];
        VncProtocol.WriteHeader(headerBuffer, type, payload.Length);
        await stream.WriteAsync(headerBuffer, ct);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, ct);
        }
        await stream.FlushAsync(ct);
    }

    [Fact]
    public async Task EndToEnd_ClientConnect_Handshake_Auth_FrameReceive_Disconnect()
    {
        const int port = 5919;
        const string password = "TestIntegrationPassword456";

        await using var server = new VncServerHost(port, password, 50);
        server.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = cts.Token;

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port, ct);
        var stream = client.GetStream();

        // 1. Send HandshakeRequest
        var handshakeReq = new HandshakeRequest(VncProtocol.CurrentVersion, "IntegrationTestClient");
        await SendPacketAsync(stream, VncMessageType.HandshakeRequest, VncProtocol.SerializeHandshakeRequest(handshakeReq), ct);

        // 2. Receive HandshakeResponse
        var (respType, respPayload) = await ReadPacketAsync(stream, ct);
        Assert.Equal(VncMessageType.HandshakeResponse, respType);
        var handshakeResp = VncProtocol.DeserializeHandshakeResponse(respPayload);
        Assert.Equal(VncProtocol.CurrentVersion, handshakeResp.Version);
        Assert.True(handshakeResp.ScreenWidth > 0);
        Assert.True(handshakeResp.ScreenHeight > 0);
        Assert.NotEmpty(handshakeResp.Challenge);

        // 3. Compute HMAC and send AuthRequest
        byte[] signature = VncProtocol.ComputeHmac(handshakeResp.Challenge, password);
        var authReq = new AuthRequest(signature);
        await SendPacketAsync(stream, VncMessageType.AuthRequest, VncProtocol.SerializeAuthRequest(authReq), ct);

        // 4. Receive AuthResponse
        var (authRespType, authRespPayload) = await ReadPacketAsync(stream, ct);
        Assert.Equal(VncMessageType.AuthResponse, authRespType);
        var authResp = VncProtocol.DeserializeAuthResponse(authRespPayload);
        Assert.Equal(0, authResp.StatusCode); // 0 = Success

        // 5. Send MouseInput
        var mouse = new MouseInput(MouseFlags.Move, 100, 100, 0);
        await SendPacketAsync(stream, VncMessageType.MouseInput, VncProtocol.SerializeMouseInput(mouse), ct);

        // 6. Receive at least one ScreenFrame
        var (frameType, framePayload) = await ReadPacketAsync(stream, ct);
        Assert.Equal(VncMessageType.ScreenFrame, frameType);
        var frame = VncProtocol.DeserializeScreenFrame(framePayload);
        Assert.True(frame.FrameIndex > 0);
        Assert.Equal(1, frame.Format); // 1 = JPEG
        Assert.True(frame.Data.Length > 100);
        Assert.Equal(0xFF, frame.Data[0]); // JPEG SOI marker
        Assert.Equal(0xD8, frame.Data[1]);

        // 7. Send Disconnect
        await SendPacketAsync(stream, VncMessageType.Disconnect, VncProtocol.SerializeDisconnect(new DisconnectMessage(0)), ct);

        await Task.Delay(200, CancellationToken.None);
        await server.StopAsync();
    }

    [Fact]
    public async Task EndToEnd_ClientConnect_InvalidAuth_Rejected()
    {
        const int port = 5920;
        const string correctPassword = "CorrectPassword";
        const string wrongPassword = "WrongPassword";

        await using var server = new VncServerHost(port, correctPassword, 50);
        server.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port, ct);
        var stream = client.GetStream();

        // 1. Send Handshake
        var handshakeReq = new HandshakeRequest(VncProtocol.CurrentVersion, "BadAuthClient");
        await SendPacketAsync(stream, VncMessageType.HandshakeRequest, VncProtocol.SerializeHandshakeRequest(handshakeReq), ct);

        // 2. Receive HandshakeResponse
        var (_, respPayload) = await ReadPacketAsync(stream, ct);
        var handshakeResp = VncProtocol.DeserializeHandshakeResponse(respPayload);

        // 3. Send Auth with WRONG password
        byte[] invalidSignature = VncProtocol.ComputeHmac(handshakeResp.Challenge, wrongPassword);
        var authReq = new AuthRequest(invalidSignature);
        await SendPacketAsync(stream, VncMessageType.AuthRequest, VncProtocol.SerializeAuthRequest(authReq), ct);

        // 4. Receive AuthResponse with non-zero failure code
        var (authRespType, authRespPayload) = await ReadPacketAsync(stream, ct);
        Assert.Equal(VncMessageType.AuthResponse, authRespType);
        var authResp = VncProtocol.DeserializeAuthResponse(authRespPayload);
        Assert.Equal(1, authResp.StatusCode); // 1 = InvalidAuth

        await server.StopAsync();
    }
}
