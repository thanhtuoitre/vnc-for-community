using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace VncServer.Core.Protocol;

public static class VncProtocol
{
    public const uint Magic = 0x434E5654; // 'TVNC' in little-endian
    public const byte CurrentVersion = 1;
    public const int HeaderSize = 10;
    public const int MaxPayloadSize = 16 * 1024 * 1024; // 16 MB boundary limit

    public static void WriteHeader(Span<byte> destination, VncMessageType type, int payloadLength)
    {
        if (destination.Length < HeaderSize)
            throw new ArgumentException("Destination span too small for header", nameof(destination));
        if (payloadLength < 0 || payloadLength > MaxPayloadSize)
            throw new ArgumentOutOfRangeException(nameof(payloadLength), "Payload length exceeds maximum allowed");

        BinaryPrimitives.WriteUInt32LittleEndian(destination[..4], Magic);
        destination[4] = CurrentVersion;
        destination[5] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(destination[6..10], payloadLength);
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> source, out VncMessageType type, out int payloadLength)
    {
        type = default;
        payloadLength = 0;

        if (source.Length < HeaderSize)
            return false;

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(source[..4]);
        if (magic != Magic)
            return false;

        byte version = source[4];
        if (version != CurrentVersion)
            return false;

        type = (VncMessageType)source[5];
        payloadLength = BinaryPrimitives.ReadInt32LittleEndian(source[6..10]);
        return payloadLength >= 0 && payloadLength <= MaxPayloadSize;
    }

    public static byte[] SerializeHandshakeRequest(HandshakeRequest req)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(req.ClientName);
        byte[] buffer = new byte[1 + 2 + nameBytes.Length];
        buffer[0] = req.Version;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(1, 2), (ushort)nameBytes.Length);
        nameBytes.CopyTo(buffer.AsSpan(3));
        return buffer;
    }

    public static HandshakeRequest DeserializeHandshakeRequest(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 3)
            throw new FormatException("Handshake request payload too short");

        byte version = payload[0];
        ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2));
        if (payload.Length < 3 + nameLen)
            throw new FormatException("Handshake client name payload truncated");

        string name = Encoding.UTF8.GetString(payload.Slice(3, nameLen));
        return new HandshakeRequest(version, name);
    }

    public static byte[] SerializeHandshakeResponse(HandshakeResponse res)
    {
        byte[] buffer = new byte[1 + 4 + 4 + 2 + res.Challenge.Length];
        buffer[0] = res.Version;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1, 4), res.ScreenWidth);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(5, 4), res.ScreenHeight);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(9, 2), (ushort)res.Challenge.Length);
        res.Challenge.CopyTo(buffer.AsSpan(11));
        return buffer;
    }

    public static HandshakeResponse DeserializeHandshakeResponse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 11)
            throw new FormatException("Handshake response payload too short");

        byte version = payload[0];
        int width = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(1, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(5, 4));
        ushort challengeLen = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(9, 2));

        if (payload.Length < 11 + challengeLen)
            throw new FormatException("Handshake challenge payload truncated");

        byte[] challenge = payload.Slice(11, challengeLen).ToArray();
        return new HandshakeResponse(version, challenge, width, height);
    }

    public static byte[] SerializeAuthRequest(AuthRequest req)
    {
        byte[] buffer = new byte[2 + req.HmacSignature.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), (ushort)req.HmacSignature.Length);
        req.HmacSignature.CopyTo(buffer.AsSpan(2));
        return buffer;
    }

    public static AuthRequest DeserializeAuthRequest(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
            throw new FormatException("Auth request payload too short");

        ushort sigLen = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
        if (payload.Length < 2 + sigLen)
            throw new FormatException("Auth signature truncated");

        byte[] sig = payload.Slice(2, sigLen).ToArray();
        return new AuthRequest(sig);
    }

    public static byte[] SerializeAuthResponse(AuthResponse res) => [res.StatusCode];

    public static AuthResponse DeserializeAuthResponse(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            throw new FormatException("Auth response payload empty");
        return new AuthResponse(payload[0]);
    }

    public static byte[] SerializeScreenFrame(ScreenFrame frame)
    {
        const int metaLen = 4 + 8 + 4 + 4 + 4 + 4 + 1;
        byte[] buffer = new byte[metaLen + frame.Data.Length];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], frame.FrameIndex);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(4, 8), frame.TimestampMs);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12, 4), frame.X);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16, 4), frame.Y);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(20, 4), frame.Width);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24, 4), frame.Height);
        span[28] = frame.Format;
        frame.Data.CopyTo(span[29..]);

        return buffer;
    }

    public static ScreenFrame DeserializeScreenFrame(ReadOnlySpan<byte> payload)
    {
        const int metaLen = 29;
        if (payload.Length < metaLen)
            throw new FormatException("Screen frame payload too short");

        uint frameIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload[..4]);
        long timestampMs = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(4, 8));
        int x = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12, 4));
        int y = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(16, 4));
        int width = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(20, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(24, 4));
        byte format = payload[28];
        byte[] data = payload[metaLen..].ToArray();

        return new ScreenFrame(frameIndex, timestampMs, x, y, width, height, format, data);
    }

    public static byte[] SerializeMouseInput(MouseInput input)
    {
        byte[] buffer = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), (ushort)input.Flags);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(2, 4), input.X);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(6, 4), input.Y);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(10, 2), input.WheelDelta);
        return buffer;
    }

    public static MouseInput DeserializeMouseInput(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12)
            throw new FormatException("Mouse input payload too short");

        var flags = (MouseFlags)BinaryPrimitives.ReadUInt16LittleEndian(payload[..2]);
        int x = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(2, 4));
        int y = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(6, 4));
        short wheel = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(10, 2));

        return new MouseInput(flags, x, y, wheel);
    }

    public static byte[] SerializeKeyboardInput(KeyboardInput input)
    {
        byte[] buffer = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), (ushort)input.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), input.VirtualKey);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), input.ScanCode);
        return buffer;
    }

    public static KeyboardInput DeserializeKeyboardInput(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6)
            throw new FormatException("Keyboard input payload too short");

        var flags = (KeyFlags)BinaryPrimitives.ReadUInt16LittleEndian(payload[..2]);
        ushort vk = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));
        ushort sc = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));

        return new KeyboardInput(flags, vk, sc);
    }

    public static byte[] SerializeHeartbeat(Heartbeat heartbeat)
    {
        byte[] buffer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, heartbeat.TimestampMs);
        return buffer;
    }

    public static Heartbeat DeserializeHeartbeat(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
            throw new FormatException("Heartbeat payload too short");
        return new Heartbeat(BinaryPrimitives.ReadInt64LittleEndian(payload[..8]));
    }

    public static byte[] SerializeDisconnect(DisconnectMessage msg) => [msg.ReasonCode];

    public static DisconnectMessage DeserializeDisconnect(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            throw new FormatException("Disconnect payload empty");
        return new DisconnectMessage(payload[0]);
    }

    public static byte[] SerializeQualityControl(QualityControl qc) => [qc.Quality, qc.TargetFps];

    public static QualityControl DeserializeQualityControl(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
            throw new FormatException("QualityControl payload too short");
        return new QualityControl(payload[0], payload[1]);
    }

    public static byte[] SerializeClipboard(ClipboardMessage msg)
    {
        return Encoding.UTF8.GetBytes(msg.Text);
    }

    public static ClipboardMessage DeserializeClipboard(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 1024 * 1024)
            throw new FormatException("Clipboard payload exceeds 1 MB limit");
        return new ClipboardMessage(Encoding.UTF8.GetString(payload));
    }

    public static byte[] GenerateChallenge(int length = 32)
    {
        byte[] challenge = new byte[length];
        RandomNumberGenerator.Fill(challenge);
        return challenge;
    }

    public static byte[] ComputeHmac(ReadOnlySpan<byte> challenge, string password)
    {
        byte[] key = Encoding.UTF8.GetBytes(password);
        return HMACSHA256.HashData(key, challenge);
    }

    public static bool VerifyHmac(ReadOnlySpan<byte> challenge, ReadOnlySpan<byte> signature, string password)
    {
        byte[] expected = ComputeHmac(challenge, password);
        return CryptographicOperations.FixedTimeEquals(expected, signature);
    }
}
