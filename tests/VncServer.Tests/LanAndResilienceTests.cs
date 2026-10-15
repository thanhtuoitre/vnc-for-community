using System.Net;
using System.Text.Json;
using VncServer.Core.Network;
using VncServer.Core.Protocol;
using VncServer.Core.Security;
using Xunit;

namespace VncServer.Tests;

public class LanAndResilienceTests
{
    [Fact]
    public void SessionResumeManager_CreateAndResume_Succeeds()
    {
        var manager = new SessionResumeManager(TimeSpan.FromSeconds(60));
        var clientIp = IPAddress.Parse("192.168.1.50");

        byte[] token = manager.CreateSessionToken(clientIp, SessionPermissions.FullControl);
        Assert.NotNull(token);
        Assert.Equal(16, token.Length);
        Assert.Equal(1, manager.ActiveSessionCount);

        // Resume with matching token and IP
        bool resumed = manager.TryResumeSession(token, clientIp, out var perms);
        Assert.True(resumed);
        Assert.Equal(SessionPermissions.FullControl, perms);

        // Replay attempt with same token must fail (token was consumed)
        bool replayed = manager.TryResumeSession(token, clientIp, out _);
        Assert.False(replayed);
    }

    [Fact]
    public void SessionResumeManager_ExpiredToken_Fails()
    {
        // TTL of -1 second means immediately expired
        var manager = new SessionResumeManager(TimeSpan.FromSeconds(-1));
        var clientIp = IPAddress.Parse("10.0.0.99");

        byte[] token = manager.CreateSessionToken(clientIp, SessionPermissions.ViewOnly);
        bool resumed = manager.TryResumeSession(token, clientIp, out _);

        Assert.False(resumed);
    }

    [Fact]
    public void SessionResumeManager_InvalidToken_Fails()
    {
        var manager = new SessionResumeManager(TimeSpan.FromSeconds(60));
        var clientIp = IPAddress.Parse("127.0.0.1");

        byte[] randomToken = new byte[16];
        randomToken[0] = 0xDE;
        randomToken[1] = 0xAD;

        bool resumed = manager.TryResumeSession(randomToken, clientIp, out _);
        Assert.False(resumed);
    }

    [Fact]
    public void SessionResumeManager_Invalidate_Succeeds()
    {
        var manager = new SessionResumeManager(TimeSpan.FromSeconds(60));
        var clientIp = IPAddress.Parse("192.168.1.100");

        byte[] token = manager.CreateSessionToken(clientIp, SessionPermissions.InputControl);
        Assert.Equal(1, manager.ActiveSessionCount);

        manager.InvalidateSession(token);
        Assert.Equal(0, manager.ActiveSessionCount);

        bool resumed = manager.TryResumeSession(token, clientIp, out _);
        Assert.False(resumed);
    }

    [Fact]
    public void LanDiscoveryService_BuildBeacon_ValidJsonAndFormat()
    {
        using var service = new LanDiscoveryService(
            serverPort: 5900,
            useTls: true,
            fingerprint: "11:22:33:44:55:66:77:88",
            serverName: "TestDesktop");

        string json = service.BuildBeaconJson();
        Assert.NotEmpty(json);

        var payload = JsonSerializer.Deserialize<ServerBeaconPayload>(json);
        Assert.NotNull(payload);
        Assert.Equal("TestDesktop", payload.Name);
        Assert.Equal(5900, payload.Port);
        Assert.True(payload.Tls);
        Assert.Equal("11:22:33:44:55:66:77:88", payload.Fingerprint);

        byte[] packet = service.BuildBeaconPacket();
        string packetStr = System.Text.Encoding.UTF8.GetString(packet);
        Assert.StartsWith(LanDiscoveryService.BeaconPrefix, packetStr);
    }

    [Fact]
    public void AuthRequest_WithResumeToken_Roundtrip_Succeeds()
    {
        byte[] hmac = new byte[32];
        hmac[0] = 0xAA;
        byte[] resumeToken = new byte[16];
        resumeToken[0] = 0xBB;
        resumeToken[15] = 0xCC;

        var req = new AuthRequest(hmac, resumeToken);
        byte[] bytes = VncProtocol.SerializeAuthRequest(req);

        var deserialized = VncProtocol.DeserializeAuthRequest(bytes);
        Assert.Equal(hmac, deserialized.HmacSignature);
        Assert.NotNull(deserialized.ResumeToken);
        Assert.Equal(resumeToken, deserialized.ResumeToken);
    }

    [Fact]
    public void AuthResponse_WithSessionToken_Roundtrip_Succeeds()
    {
        byte[] sessionToken = new byte[16];
        sessionToken[0] = 0x42;
        sessionToken[15] = 0x99;

        var res = new AuthResponse(0, (byte)SessionPermissions.ClipboardSync, sessionToken);
        byte[] bytes = VncProtocol.SerializeAuthResponse(res);

        var deserialized = VncProtocol.DeserializeAuthResponse(bytes);
        Assert.Equal(0, deserialized.StatusCode);
        Assert.Equal((byte)SessionPermissions.ClipboardSync, deserialized.Permissions);
        Assert.NotNull(deserialized.SessionToken);
        Assert.Equal(sessionToken, deserialized.SessionToken);
    }

    [Fact]
    public void UpnpMappingResult_Properties_Match()
    {
        var result = new UpnpMappingResult(true, "203.0.113.1", 5900);
        Assert.True(result.Success);
        Assert.Equal("203.0.113.1", result.ExternalIp);
        Assert.Equal(5900, result.ExternalPort);
        Assert.Null(result.ErrorMessage);

        var failed = new UpnpMappingResult(false, ErrorMessage: "Router timeout");
        Assert.False(failed.Success);
        Assert.Equal("Router timeout", failed.ErrorMessage);
    }
}
