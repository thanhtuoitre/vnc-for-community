using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using VncServer.Core.Protocol;

namespace VncServer.Core.Security;

/// <summary>
/// Manages cryptographically secure session resumption tokens, allowing clients
/// to rapidly resume dropped connections within a short TTL without re-entering credentials.
/// </summary>
public sealed class SessionResumeManager
{
    private sealed record SessionResumeEntry(
        byte[] Token,
        IPAddress ClientAddress,
        DateTime ExpiryUtc,
        SessionPermissions Permissions);

    private readonly ConcurrentDictionary<string, SessionResumeEntry> _resumableSessions = new();
    private readonly TimeSpan _defaultTtl;

    public SessionResumeManager(TimeSpan? defaultTtl = null)
    {
        _defaultTtl = defaultTtl ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Issues a new 16-byte cryptographically secure session resumption token.
    /// </summary>
    public byte[] CreateSessionToken(IPAddress clientAddress, SessionPermissions permissions, TimeSpan? ttl = null)
    {
        CleanupExpired();

        byte[] token = RandomNumberGenerator.GetBytes(16);
        string key = Convert.ToHexString(token);
        var entry = new SessionResumeEntry(
            Token: token,
            ClientAddress: clientAddress,
            ExpiryUtc: DateTime.UtcNow.Add(ttl ?? _defaultTtl),
            Permissions: permissions);

        _resumableSessions[key] = entry;
        return token;
    }

    /// <summary>
    /// Validates a session resumption token from a reconnecting client.
    /// If valid, the old token is consumed (preventing replay attacks) and true is returned.
    /// </summary>
    public bool TryResumeSession(ReadOnlySpan<byte> token, IPAddress clientAddress, out SessionPermissions permissions)
    {
        permissions = SessionPermissions.None;
        if (token.Length != 16) return false;

        string key = Convert.ToHexString(token);
        if (!_resumableSessions.TryRemove(key, out var entry))
        {
            return false;
        }

        if (DateTime.UtcNow > entry.ExpiryUtc)
        {
            return false; // Expired
        }

        // Allow loopback match or subnet/IP match
        if (!IsAddressCompatible(entry.ClientAddress, clientAddress))
        {
            return false;
        }

        permissions = entry.Permissions;
        return true;
    }

    /// <summary>
    /// Invalidate any active session token when a client explicitly disconnects.
    /// </summary>
    public void InvalidateSession(ReadOnlySpan<byte> token)
    {
        if (token.Length != 16) return;
        string key = Convert.ToHexString(token);
        _resumableSessions.TryRemove(key, out _);
    }

    public int ActiveSessionCount => _resumableSessions.Count;

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (k, v) in _resumableSessions)
        {
            if (now > v.ExpiryUtc)
            {
                _resumableSessions.TryRemove(k, out _);
            }
        }
    }

    private static bool IsAddressCompatible(IPAddress registered, IPAddress candidate)
    {
        if (registered.Equals(candidate)) return true;
        if (IPAddress.IsLoopback(registered) && IPAddress.IsLoopback(candidate)) return true;
        // In dual-stack IPv4/IPv6 mapped scenarios
        if (registered.IsIPv4MappedToIPv6 && registered.MapToIPv4().Equals(candidate)) return true;
        if (candidate.IsIPv4MappedToIPv6 && candidate.MapToIPv4().Equals(registered)) return true;
        return false;
    }
}
