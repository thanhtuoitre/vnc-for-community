using System.Net;
using VncServer.Core.Security;
using Xunit;

namespace VncServer.Tests;

public class RateLimiterTests
{
    [Fact]
    public void LoopbackIp_IsAlwaysAllowed()
    {
        var limiter = new IpRateLimiter(maxConnectionsPerWindow: 2, window: TimeSpan.FromMinutes(1));
        var loopback = IPAddress.Loopback;

        for (int i = 0; i < 10; i++)
        {
            limiter.RecordConnection(loopback);
            Assert.True(limiter.IsConnectionAllowed(loopback));
        }
    }

    [Fact]
    public void RemoteIp_ExceedingConnectionLimit_IsThrottled()
    {
        var limiter = new IpRateLimiter(maxConnectionsPerWindow: 3, window: TimeSpan.FromMinutes(1));
        var remoteIp = IPAddress.Parse("203.0.113.42");

        for (int i = 0; i < 3; i++)
        {
            Assert.True(limiter.IsConnectionAllowed(remoteIp));
            limiter.RecordConnection(remoteIp);
        }

        // 4th connection in the same window must be rejected
        Assert.False(limiter.IsConnectionAllowed(remoteIp));
    }

    [Fact]
    public void RemoteIp_RepeatedAuthFailures_BlocksIpTemporarily()
    {
        var limiter = new IpRateLimiter(
            maxConnectionsPerWindow: 50,
            maxAuthFailures: 3,
            blockDuration: TimeSpan.FromMinutes(2));

        var remoteIp = IPAddress.Parse("198.51.100.25");

        Assert.True(limiter.IsConnectionAllowed(remoteIp));

        limiter.RecordAuthFailure(remoteIp);
        limiter.RecordAuthFailure(remoteIp);
        Assert.True(limiter.IsConnectionAllowed(remoteIp));

        // 3rd failure triggers block
        limiter.RecordAuthFailure(remoteIp);
        Assert.True(limiter.IsBlocked(remoteIp));
        Assert.False(limiter.IsConnectionAllowed(remoteIp));
    }

    [Fact]
    public void RemoteIp_SuccessfulAuth_ClearsFailureCount()
    {
        var limiter = new IpRateLimiter(maxAuthFailures: 3);
        var remoteIp = IPAddress.Parse("198.51.100.99");

        limiter.RecordAuthFailure(remoteIp);
        limiter.RecordAuthFailure(remoteIp);
        limiter.RecordAuthSuccess(remoteIp);

        // One more failure should not trigger block because counter was reset
        limiter.RecordAuthFailure(remoteIp);
        Assert.False(limiter.IsBlocked(remoteIp));
    }
}
