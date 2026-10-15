using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using VncServer.Core.Security;
using VncServer.Core.Transport;
using Xunit;

namespace VncServer.Tests;

public class SecurityTests
{
    [Fact]
    public void PasswordHasher_HashesAndVerifies_Pbkdf2()
    {
        string password = "StrongVncPassword#2026";
        string hash = PasswordHasher.HashPassword(password);

        Assert.StartsWith("pbkdf2$100000$", hash);
        Assert.True(PasswordHasher.VerifyPassword(password, hash));
        Assert.False(PasswordHasher.VerifyPassword("WrongPassword", hash));
    }

    [Fact]
    public void PasswordHasher_LegacyPlaintextFallback_Succeeds()
    {
        string plain = "legacysecret";
        Assert.True(PasswordHasher.VerifyPassword(plain, plain));
        Assert.False(PasswordHasher.VerifyPassword("wrong", plain));
    }

    [Fact]
    public void CertificateManager_GeneratesCertWithFingerprint()
    {
        var cert = CertificateManager.GenerateAndSaveSelfSignedCertificate();
        Assert.NotNull(cert);
        Assert.True(cert.HasPrivateKey);

        string fingerprint = CertificateManager.GetSha256Fingerprint(cert);
        Assert.NotEmpty(fingerprint);
        Assert.Contains(":", fingerprint);
    }

    [Fact]
    public void IpRateLimiter_ProgressiveLockout_BlocksAfterFailures()
    {
        var limiter = new IpRateLimiter(maxConnectionsPerWindow: 20, maxAuthFailures: 3);
        var ip = IPAddress.Parse("198.51.100.42");

        Assert.False(limiter.IsBlocked(ip));

        limiter.RecordAuthFailure(ip);
        limiter.RecordAuthFailure(ip);
        Assert.False(limiter.IsBlocked(ip));

        // 3rd failure triggers lockout
        limiter.RecordAuthFailure(ip);
        Assert.True(limiter.IsBlocked(ip));
    }

    [Fact]
    public async Task TlsVncTransport_LoopbackHandshake_Succeeds()
    {
        var cert = CertificateManager.GetOrCreateServerCertificate();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var serverTask = Task.Run(async () =>
            {
                var serverClient = await listener.AcceptTcpClientAsync();
                return await TlsVncTransport.AuthenticateAsServerAsync(serverClient, cert);
            });

            var clientTask = Task.Run(async () =>
            {
                var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var sslStream = new SslStream(client.GetStream(), false, (s, c, ch, err) => true);
                await sslStream.AuthenticateAsClientAsync("localhost");
                return sslStream;
            });

            await Task.WhenAll(serverTask, clientTask);
            var serverTransport = await serverTask;
            var clientSsl = await clientTask;

            Assert.True(serverTransport.IsConnected);
            Assert.True(serverTransport.SslProtocol >= SslProtocols.Tls12);

            // Test transmission over TLS
            byte[] testMsg = [1, 2, 3, 4, 5];
            await serverTransport.SendAsync(testMsg);

            byte[] recvBuffer = new byte[5];
            await clientSsl.ReadExactlyAsync(recvBuffer);
            Assert.Equal(testMsg, recvBuffer);

            await serverTransport.CloseAsync();
            clientSsl.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }
}
