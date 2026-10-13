using System.Buffers.Binary;

namespace VncServer.Core.Protocol;

public enum VncMessageType : byte
{
    HandshakeRequest = 1,
    HandshakeResponse = 2,
    AuthRequest = 3,
    AuthResponse = 4,
    ScreenFrame = 5,
    MouseInput = 6,
    KeyboardInput = 7,
    Heartbeat = 8,
    Disconnect = 9,
    QualityControl = 10,
    ClipboardText = 11,
    AudioFrame = 12
}

[Flags]
public enum MouseFlags : ushort
{
    None = 0,
    Move = 1 << 0,
    LeftDown = 1 << 1,
    LeftUp = 1 << 2,
    RightDown = 1 << 3,
    RightUp = 1 << 4,
    MiddleDown = 1 << 5,
    MiddleUp = 1 << 6,
    Wheel = 1 << 7
}

[Flags]
public enum KeyFlags : ushort
{
    None = 0,
    KeyDown = 1 << 0,
    KeyUp = 1 << 1,
    ExtendedKey = 1 << 2,
    Unicode = 1 << 3
}

public sealed record HandshakeRequest(byte Version, string ClientName);

public sealed record HandshakeResponse(byte Version, byte[] Challenge, int ScreenWidth, int ScreenHeight);

public sealed record AuthRequest(byte[] HmacSignature);

public sealed record AuthResponse(byte StatusCode); // 0 = Success, 1 = InvalidAuth, 2 = ProtocolError

public sealed record ScreenFrame(
    uint FrameIndex,
    long TimestampMs,
    int X,
    int Y,
    int Width,
    int Height,
    byte Format, // 1 = JPEG
    byte[] Data);

public sealed record MouseInput(MouseFlags Flags, int X, int Y, short WheelDelta);

public sealed record KeyboardInput(KeyFlags Flags, ushort VirtualKey, ushort ScanCode);

public sealed record Heartbeat(long TimestampMs);

public sealed record DisconnectMessage(byte ReasonCode);

public sealed record QualityControl(byte Quality, byte TargetFps);

public sealed record ClipboardMessage(string Text);

public sealed record AudioFrame(uint TimestampMs, ushort SampleRate, byte Channels, byte Codec, byte[] Data);
