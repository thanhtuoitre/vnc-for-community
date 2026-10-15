using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using VncServer.Core.Capture;
using VncServer.Core.Input;
using VncServer.Core.Logging;
using VncServer.Core.Protocol;
using VncServer.Core.Transport;

namespace VncServer.Core.Server;

public enum SessionState
{
    Connecting,
    Handshaking,
    Authenticating,
    Connected,
    Disconnecting,
    Closed
}

public sealed class VncSession : IAsyncDisposable
{
    private readonly IVncTransport _transport;
    private readonly IScreenCapture _capture;
    private readonly string _password;
    private readonly IVncLogger? _logger;
    private readonly Security.IpRateLimiter? _rateLimiter;
    private readonly Security.SessionResumeManager? _resumeManager;
    private byte[]? _currentSessionToken;
    private readonly CancellationTokenSource _sessionCts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _isDisposed;

    public string RemoteEndPoint => _transport.RemoteEndPoint;
    public SessionState State { get; private set; } = SessionState.Connecting;
    public SessionPermissions Permissions { get; set; } = SessionPermissions.FullControl;

    public VncSession(
        IVncTransport transport,
        IScreenCapture capture,
        string password,
        IVncLogger? logger = null,
        Security.IpRateLimiter? rateLimiter = null,
        Security.SessionResumeManager? resumeManager = null,
        SessionPermissions permissions = SessionPermissions.FullControl)
    {
        _transport = transport;
        _capture = capture;
        _password = password;
        _logger = logger;
        _rateLimiter = rateLimiter;
        _resumeManager = resumeManager;
        Permissions = permissions;
    }

    public VncSession(
        TcpClient client,
        ScreenCapture capture,
        string password,
        Action<string>? log = null,
        Security.IpRateLimiter? rateLimiter = null)
        : this(new TcpVncTransport(client), capture, password, log != null ? new ActionVncLogger(log) : null, rateLimiter, null)
    {
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token, cancellationToken);
        var ct = linkedCts.Token;

        try
        {
            State = SessionState.Handshaking;
            if (!await PerformHandshakeAndAuthAsync(ct).ConfigureAwait(false))
            {
                State = SessionState.Closed;
                _logger?.Warn($"[{RemoteEndPoint}] Authentication failed. Connection terminated.");
                return;
            }

            State = SessionState.Connected;
            _logger?.Info($"[{RemoteEndPoint}] Authenticated successfully. Starting stream and input loop.");

            // Bounded channel with capacity 1 and DropOldest drops stale frames during network congestion
            var frameChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

            var readTask = ProcessIncomingPacketsAsync(ct);
            var captureTask = ProduceFramesAsync(frameChannel.Writer, ct);
            var sendTask = ConsumeFramesAsync(frameChannel.Reader, ct);
            var cursorTask = ProduceCursorUpdatesAsync(ct);

            await Task.WhenAny(readTask, captureTask, sendTask, cursorTask).ConfigureAwait(false);
            State = SessionState.Disconnecting;
            _sessionCts.Cancel();
            await Task.WhenAll(
                readTask.ContinueWith(_ => { }, TaskScheduler.Default),
                captureTask.ContinueWith(_ => { }, TaskScheduler.Default),
                sendTask.ContinueWith(_ => { }, TaskScheduler.Default),
                cursorTask.ContinueWith(_ => { }, TaskScheduler.Default)
            ).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on disconnect
        }
        catch (Exception ex)
        {
            _logger?.Error($"[{RemoteEndPoint}] Session error: {ex.Message}", ex);
        }
        finally
        {
            State = SessionState.Closed;
        }
    }

    private async Task<bool> PerformHandshakeAndAuthAsync(CancellationToken ct)
    {
        // 1. Read HandshakeRequest
        var (type, payload) = await ReadPacketAsync(ct).ConfigureAwait(false);
        if (type != VncMessageType.HandshakeRequest) return false;

        var handshakeReq = VncProtocol.DeserializeHandshakeRequest(payload);
        _logger?.Info($"[{RemoteEndPoint}] Handshake from '{handshakeReq.ClientName}' (v{handshakeReq.Version})");
        Security.SessionAuditLogger.RecordEvent("HANDSHAKE", RemoteEndPoint, $"Client '{handshakeReq.ClientName}' (v{handshakeReq.Version})");

        // 2. Send HandshakeResponse
        byte[] challenge = VncProtocol.GenerateChallenge();
        _capture.RefreshResolution();
        var handshakeRes = new HandshakeResponse(VncProtocol.CurrentVersion, challenge, _capture.Width, _capture.Height);
        await SendPacketAsync(VncMessageType.HandshakeResponse, VncProtocol.SerializeHandshakeResponse(handshakeRes), ct).ConfigureAwait(false);

        // 3. Read AuthRequest
        State = SessionState.Authenticating;
        var (authType, authPayload) = await ReadPacketAsync(ct).ConfigureAwait(false);
        if (authType != VncMessageType.AuthRequest) return false;

        var authReq = VncProtocol.DeserializeAuthRequest(authPayload);
        bool isValid = false;

        // Check for session resumption first
        if (authReq.ResumeToken != null && _resumeManager != null && _transport.RemoteAddress is IPAddress resumeAddr)
        {
            if (_resumeManager.TryResumeSession(authReq.ResumeToken, resumeAddr, out var resumedPerms))
            {
                isValid = true;
                Permissions = resumedPerms;
                _logger?.Info($"[{RemoteEndPoint}] Session successfully resumed via resumption token.");
                Security.SessionAuditLogger.RecordEvent("SESSION_RESUME", RemoteEndPoint, $"Permissions: {Permissions}");
            }
            else
            {
                _logger?.Warn($"[{RemoteEndPoint}] Session resumption token invalid or expired. Falling back to credential auth.");
            }
        }

        if (!isValid)
        {
            isValid = VncProtocol.VerifyHmac(challenge, authReq.HmacSignature, _password);
        }

        if (_transport.RemoteAddress is IPAddress address)
        {
            if (isValid)
            {
                _rateLimiter?.RecordAuthSuccess(address);
                if (authReq.ResumeToken == null)
                {
                    Security.SessionAuditLogger.RecordEvent("AUTH_SUCCESS", RemoteEndPoint, $"Permissions: {Permissions}");
                }
            }
            else
            {
                _rateLimiter?.RecordAuthFailure(address);
                Security.SessionAuditLogger.RecordEvent("AUTH_FAILURE", RemoteEndPoint, "Invalid credentials or challenge signature");
            }
        }

        // 4. Issue fresh session resumption token on success
        if (isValid && _resumeManager != null && _transport.RemoteAddress is IPAddress tokenAddr)
        {
            _currentSessionToken = _resumeManager.CreateSessionToken(tokenAddr, Permissions);
        }

        // 5. Send AuthResponse
        byte status = isValid ? (byte)0 : (byte)1;
        await SendPacketAsync(VncMessageType.AuthResponse, VncProtocol.SerializeAuthResponse(new AuthResponse(status, (byte)Permissions, _currentSessionToken)), ct).ConfigureAwait(false);

        return isValid;
    }

    private async Task ProcessIncomingPacketsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            (VncMessageType type, byte[] payload) packet;
            try
            {
                packet = await ReadPacketAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger?.Warn($"[{RemoteEndPoint}] Connection timed out (15s inactivity without heartbeat/input). Terminating dead connection.");
                throw new TimeoutException($"Client inactivity timeout on {RemoteEndPoint}");
            }

            var (type, payload) = packet;
            switch (type)
            {
                case VncMessageType.MouseInput:
                    if (Permissions.HasFlag(SessionPermissions.InputControl))
                    {
                        var mouse = VncProtocol.DeserializeMouseInput(payload);
                        InputSimulator.SimulateMouse(mouse, _capture.Width, _capture.Height);
                    }
                    break;

                case VncMessageType.KeyboardInput:
                    if (Permissions.HasFlag(SessionPermissions.InputControl))
                    {
                        var kb = VncProtocol.DeserializeKeyboardInput(payload);
                        InputSimulator.SimulateKeyboard(kb);
                    }
                    break;

                case VncMessageType.Heartbeat:
                    var hb = VncProtocol.DeserializeHeartbeat(payload);
                    await SendPacketAsync(VncMessageType.Heartbeat, VncProtocol.SerializeHeartbeat(hb), ct).ConfigureAwait(false);
                    break;

                case VncMessageType.QualityControl:
                    var qc = VncProtocol.DeserializeQualityControl(payload);
                    _capture.SetQuality(qc.Quality);
                    _targetFps = Math.Clamp((int)qc.TargetFps, 5, 60);
                    _logger?.Debug($"[{RemoteEndPoint}] Adaptive Quality: {qc.Quality}%, FPS: {_targetFps}, Preset: {(StreamingPreset)qc.Preset}");
                    break;

                case VncMessageType.ClipboardText:
                    if (Permissions.HasFlag(SessionPermissions.ClipboardSync))
                    {
                        var clip = VncProtocol.DeserializeClipboard(payload);
                        WindowsClipboard.SetText(clip.Text);
                        _logger?.Info($"[{RemoteEndPoint}] Remote clipboard updated ({clip.Text.Length} chars).");
                    }
                    break;

                case VncMessageType.SessionControl:
                    if (Permissions.HasFlag(SessionPermissions.InputControl))
                    {
                        var sessionCtrl = VncProtocol.DeserializeSessionControl(payload);
                        SystemControl.SessionController.Execute(sessionCtrl, _logger);
                        Security.SessionAuditLogger.RecordEvent("SESSION_CONTROL", RemoteEndPoint, $"Action: {sessionCtrl.Action}, Param: {sessionCtrl.Parameter}");
                    }
                    else
                    {
                        _logger?.Warn($"[{RemoteEndPoint}] SessionControl rejected: client lacks InputControl permission.");
                    }
                    break;

                case VncMessageType.Disconnect:
                    _logger?.Info($"[{RemoteEndPoint}] Client requested disconnect.");
                    if (_currentSessionToken != null && _resumeManager != null)
                    {
                        _resumeManager.InvalidateSession(_currentSessionToken);
                    }
                    return;

                default:
                    _logger?.Warn($"[{RemoteEndPoint}] Unexpected message type: {type}");
                    break;
            }
        }
    }

    private volatile int _targetFps = 30;

    private async Task ProduceFramesAsync(ChannelWriter<byte[]> writer, CancellationToken ct)
    {
        bool forceFirstFrame = true;

        while (!ct.IsCancellationRequested)
        {
            byte[]? jpeg = _capture.CaptureJpeg(out uint frameIndex, force: forceFirstFrame);
            forceFirstFrame = false;

            if (jpeg != null)
            {
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var frame = new ScreenFrame(frameIndex, timestamp, _capture.VirtualX, _capture.VirtualY, _capture.Width, _capture.Height, 1, jpeg);
                byte[] framePayload = VncProtocol.SerializeScreenFrame(frame);
                writer.TryWrite(framePayload);
            }

            int delayMs = Math.Max(16, 1000 / Math.Clamp(_targetFps, 5, 60));
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
    }

    private async Task ConsumeFramesAsync(ChannelReader<byte[]> reader, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (reader.TryRead(out byte[]? framePayload))
            {
                await SendPacketAsync(VncMessageType.ScreenFrame, framePayload, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<(VncMessageType Type, byte[] Payload)> ReadPacketAsync(CancellationToken ct)
    {
        byte[] headerBuffer = new byte[VncProtocol.HeaderSize];
        await _transport.ReadExactlyAsync(headerBuffer, ct).ConfigureAwait(false);

        if (!VncProtocol.TryReadHeader(headerBuffer, out var type, out int payloadLength))
            throw new InvalidDataException("Invalid packet header received");

        byte[] payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            await _transport.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        }

        return (type, payload);
    }

    private async Task ProduceCursorUpdatesAsync(CancellationToken ct)
    {
        IntPtr lastHCursor = (IntPtr)(-1);
        int lastFbX = -1;
        int lastFbY = -1;
        bool? lastVisible = null;

        while (!ct.IsCancellationRequested)
        {
            if (CursorCapture.GetCurrentCursor(out int screenX, out int screenY, out bool visible, out var hCursor))
            {
                int fbX = screenX - _capture.VirtualX;
                int fbY = screenY - _capture.VirtualY;

                // Send shape update if cursor handle changed or initial send
                if (hCursor != lastHCursor)
                {
                    lastHCursor = hCursor;
                    var shape = CursorCapture.CaptureShape(hCursor);
                    if (shape != null)
                    {
                        byte[] shapePayload = VncProtocol.SerializeCursorShape(shape);
                        await SendPacketAsync(VncMessageType.CursorShape, shapePayload, ct).ConfigureAwait(false);
                    }
                }

                // Send position update if coordinates or visibility changed
                if (fbX != lastFbX || fbY != lastFbY || visible != lastVisible)
                {
                    lastFbX = fbX;
                    lastFbY = fbY;
                    lastVisible = visible;
                    var posMsg = new CursorPosition(fbX, fbY, visible);
                    byte[] posPayload = VncProtocol.SerializeCursorPosition(posMsg);
                    await SendPacketAsync(VncMessageType.CursorPosition, posPayload, ct).ConfigureAwait(false);
                }
            }

            // ~60 Hz cursor tracking loop
            await Task.Delay(16, ct).ConfigureAwait(false);
        }
    }

    private async Task SendPacketAsync(VncMessageType type, byte[] payload, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte[] headerBuffer = new byte[VncProtocol.HeaderSize];
            VncProtocol.WriteHeader(headerBuffer, type, payload.Length);

            await _transport.SendAsync(headerBuffer, ct).ConfigureAwait(false);
            if (payload.Length > 0)
            {
                await _transport.SendAsync(payload, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendClipboardTextAsync(string text, CancellationToken ct = default)
    {
        var msg = new ClipboardMessage(text);
        byte[] payload = VncProtocol.SerializeClipboard(msg);
        await SendPacketAsync(VncMessageType.ClipboardText, payload, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            _sessionCts.Cancel();
        }
        catch (ObjectDisposedException) { }

        _sessionCts.Dispose();
        _sendLock.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
