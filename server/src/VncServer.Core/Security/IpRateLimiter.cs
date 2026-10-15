using System.Collections.Concurrent;
using System.Net;

namespace VncServer.Core.Security;

public sealed class IpRateLimiter
{
    private sealed class IpTracker
    {
        private readonly Queue<long> _connectionTimestamps = new();
        private int _authFailureCount;
        private long _blockedUntilTicks;
        private readonly object _lock = new();

        public bool IsAllowed(long nowTicks, int maxPerWindow, long windowTicks)
        {
            lock (_lock)
            {
                if (nowTicks < _blockedUntilTicks)
                    return false;

                while (_connectionTimestamps.Count > 0 && nowTicks - _connectionTimestamps.Peek() > windowTicks)
                {
                    _connectionTimestamps.Dequeue();
                }

                return _connectionTimestamps.Count < maxPerWindow;
            }
        }

        public void RecordConnection(long nowTicks)
        {
            lock (_lock)
            {
                _connectionTimestamps.Enqueue(nowTicks);
            }
        }

        public void RecordAuthFailure(long nowTicks, int maxFailures, long blockTicks)
        {
            lock (_lock)
            {
                _authFailureCount++;
                long durationTicks;
                if (_authFailureCount >= 10)
                {
                    durationTicks = TimeSpan.FromHours(1).Ticks;
                }
                else if (_authFailureCount >= 5)
                {
                    durationTicks = TimeSpan.FromMinutes(5).Ticks;
                }
                else if (_authFailureCount >= maxFailures)
                {
                    durationTicks = blockTicks;
                }
                else if (_authFailureCount >= 3)
                {
                    durationTicks = TimeSpan.FromSeconds(30).Ticks;
                }
                else
                {
                    return;
                }

                _blockedUntilTicks = Math.Max(_blockedUntilTicks, nowTicks + durationTicks);
            }
        }

        public void RecordAuthSuccess()
        {
            lock (_lock)
            {
                _authFailureCount = 0;
            }
        }

        public bool IsBlocked(long nowTicks)
        {
            lock (_lock)
            {
                return nowTicks < _blockedUntilTicks;
            }
        }
    }

    private readonly ConcurrentDictionary<IPAddress, IpTracker> _trackers = new();
    private readonly int _maxConnectionsPerWindow;
    private readonly TimeSpan _window;
    private readonly int _maxAuthFailures;
    private readonly TimeSpan _blockDuration;

    public IpRateLimiter(
        int maxConnectionsPerWindow = 15,
        TimeSpan? window = null,
        int maxAuthFailures = 5,
        TimeSpan? blockDuration = null)
    {
        _maxConnectionsPerWindow = maxConnectionsPerWindow;
        _window = window ?? TimeSpan.FromMinutes(1);
        _maxAuthFailures = maxAuthFailures;
        _blockDuration = blockDuration ?? TimeSpan.FromMinutes(5);
    }

    public bool IsConnectionAllowed(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true; // Whitelist local loopback

        long now = DateTime.UtcNow.Ticks;
        var tracker = _trackers.GetOrAdd(ip, _ => new IpTracker());
        return tracker.IsAllowed(now, _maxConnectionsPerWindow, _window.Ticks);
    }

    public void RecordConnection(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return;
        long now = DateTime.UtcNow.Ticks;
        var tracker = _trackers.GetOrAdd(ip, _ => new IpTracker());
        tracker.RecordConnection(now);
    }

    public void RecordAuthFailure(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return;
        long now = DateTime.UtcNow.Ticks;
        var tracker = _trackers.GetOrAdd(ip, _ => new IpTracker());
        tracker.RecordAuthFailure(now, _maxAuthFailures, _blockDuration.Ticks);
    }

    public void RecordAuthSuccess(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return;
        if (_trackers.TryGetValue(ip, out var tracker))
        {
            tracker.RecordAuthSuccess();
        }
    }

    public bool IsBlocked(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;
        long now = DateTime.UtcNow.Ticks;
        return _trackers.TryGetValue(ip, out var tracker) && tracker.IsBlocked(now);
    }

    public void Reset()
    {
        _trackers.Clear();
    }
}
