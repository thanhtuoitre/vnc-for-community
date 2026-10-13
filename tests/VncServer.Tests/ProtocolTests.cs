using System.Text;
using VncServer.Core.Protocol;

namespace VncServer.Tests;

public class ProtocolTests
{
    [Fact]
    public void Header_WriteAndRead_Succeeds()
    {
        Span<byte> buffer = stackalloc byte[VncProtocol.HeaderSize];
        VncProtocol.WriteHeader(buffer, VncMessageType.MouseInput, 12);

        bool success = VncProtocol.TryReadHeader(buffer, out var type, out int length);

        Assert.True(success);
        Assert.Equal(VncMessageType.MouseInput, type);
        Assert.Equal(12, length);
    }

    [Fact]
    public void Header_InvalidMagic_Fails()
    {
        byte[] buffer = new byte[VncProtocol.HeaderSize];
        buffer[0] = 0x00; // corrupt magic

        bool success = VncProtocol.TryReadHeader(buffer, out _, out _);
        Assert.False(success);
    }

    [Fact]
    public void Header_InvalidPayloadLength_ThrowsOrFails()
    {
        byte[] buffer = new byte[VncProtocol.HeaderSize];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VncProtocol.WriteHeader(buffer, VncMessageType.ScreenFrame, VncProtocol.MaxPayloadSize + 1));
    }

    [Fact]
    public void HandshakeRequest_Roundtrip_Succeeds()
    {
        var original = new HandshakeRequest(1, "TestAndroidClient");
        byte[] serialized = VncProtocol.SerializeHandshakeRequest(original);
        var deserialized = VncProtocol.DeserializeHandshakeRequest(serialized);

        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.ClientName, deserialized.ClientName);
    }

    [Fact]
    public void HandshakeResponse_Roundtrip_Succeeds()
    {
        byte[] challenge = [1, 2, 3, 4, 5, 6, 7, 8];
        var original = new HandshakeResponse(1, challenge, 1920, 1080);
        byte[] serialized = VncProtocol.SerializeHandshakeResponse(original);
        var deserialized = VncProtocol.DeserializeHandshakeResponse(serialized);

        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.ScreenWidth, deserialized.ScreenWidth);
        Assert.Equal(original.ScreenHeight, deserialized.ScreenHeight);
        Assert.Equal(original.Challenge, deserialized.Challenge);
    }

    [Fact]
    public void AuthRequest_Roundtrip_Succeeds()
    {
        byte[] sig = new byte[32];
        Random.Shared.NextBytes(sig);
        var original = new AuthRequest(sig);
        byte[] serialized = VncProtocol.SerializeAuthRequest(original);
        var deserialized = VncProtocol.DeserializeAuthRequest(serialized);

        Assert.Equal(original.HmacSignature, deserialized.HmacSignature);
    }

    [Fact]
    public void AuthResponse_Roundtrip_Succeeds()
    {
        var original = new AuthResponse(0);
        byte[] serialized = VncProtocol.SerializeAuthResponse(original);
        var deserialized = VncProtocol.DeserializeAuthResponse(serialized);

        Assert.Equal(original.StatusCode, deserialized.StatusCode);
    }

    [Fact]
    public void ScreenFrame_Roundtrip_Succeeds()
    {
        byte[] dummyJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
        var original = new ScreenFrame(42, 1234567890L, 0, 0, 1920, 1080, 1, dummyJpeg);
        byte[] serialized = VncProtocol.SerializeScreenFrame(original);
        var deserialized = VncProtocol.DeserializeScreenFrame(serialized);

        Assert.Equal(original.FrameIndex, deserialized.FrameIndex);
        Assert.Equal(original.TimestampMs, deserialized.TimestampMs);
        Assert.Equal(original.X, deserialized.X);
        Assert.Equal(original.Y, deserialized.Y);
        Assert.Equal(original.Width, deserialized.Width);
        Assert.Equal(original.Height, deserialized.Height);
        Assert.Equal(original.Format, deserialized.Format);
        Assert.Equal(original.Data, deserialized.Data);
    }

    [Fact]
    public void MouseInput_Roundtrip_Succeeds()
    {
        var original = new MouseInput(MouseFlags.Move | MouseFlags.LeftDown, 500, 300, 120);
        byte[] serialized = VncProtocol.SerializeMouseInput(original);
        var deserialized = VncProtocol.DeserializeMouseInput(serialized);

        Assert.Equal(original.Flags, deserialized.Flags);
        Assert.Equal(original.X, deserialized.X);
        Assert.Equal(original.Y, deserialized.Y);
        Assert.Equal(original.WheelDelta, deserialized.WheelDelta);
    }

    [Fact]
    public void KeyboardInput_Roundtrip_Succeeds()
    {
        var original = new KeyboardInput(KeyFlags.KeyDown | KeyFlags.ExtendedKey, 0x41, 0x1E);
        byte[] serialized = VncProtocol.SerializeKeyboardInput(original);
        var deserialized = VncProtocol.DeserializeKeyboardInput(serialized);

        Assert.Equal(original.Flags, deserialized.Flags);
        Assert.Equal(original.VirtualKey, deserialized.VirtualKey);
        Assert.Equal(original.ScanCode, deserialized.ScanCode);
    }

    [Fact]
    public void Heartbeat_Roundtrip_Succeeds()
    {
        var original = new Heartbeat(9876543210L);
        byte[] serialized = VncProtocol.SerializeHeartbeat(original);
        var deserialized = VncProtocol.DeserializeHeartbeat(serialized);

        Assert.Equal(original.TimestampMs, deserialized.TimestampMs);
    }

    [Fact]
    public void Disconnect_Roundtrip_Succeeds()
    {
        var original = new DisconnectMessage(2);
        byte[] serialized = VncProtocol.SerializeDisconnect(original);
        var deserialized = VncProtocol.DeserializeDisconnect(serialized);

        Assert.Equal(original.ReasonCode, deserialized.ReasonCode);
    }

    [Fact]
    public void QualityControl_Roundtrip_Succeeds()
    {
        var original = new QualityControl(85, 45);
        byte[] serialized = VncProtocol.SerializeQualityControl(original);
        var deserialized = VncProtocol.DeserializeQualityControl(serialized);

        Assert.Equal(original.Quality, deserialized.Quality);
        Assert.Equal(original.TargetFps, deserialized.TargetFps);
    }

    [Fact]
    public void HmacAuthentication_VerificationLogic_WorksCorrectly()
    {
        byte[] challenge = VncProtocol.GenerateChallenge(32);
        const string correctPassword = "SuperSecretPassword123";
        const string wrongPassword = "WrongPassword";

        byte[] validSignature = VncProtocol.ComputeHmac(challenge, correctPassword);

        Assert.True(VncProtocol.VerifyHmac(challenge, validSignature, correctPassword));
        Assert.False(VncProtocol.VerifyHmac(challenge, validSignature, wrongPassword));
    }

    [Fact]
    public void Clipboard_Roundtrip_Succeeds()
    {
        const string text = "Hello from Android to Windows remote clipboard! 🚀";
        var original = new ClipboardMessage(text);
        byte[] serialized = VncProtocol.SerializeClipboard(original);
        var deserialized = VncProtocol.DeserializeClipboard(serialized);

        Assert.Equal(text, deserialized.Text);
    }

    [Fact]
    public void AudioFrame_Roundtrip_Succeeds()
    {
        byte[] pcmData = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];
        var original = new AudioFrame(54321, 48000, 2, 1, pcmData);
        byte[] serialized = VncProtocol.SerializeAudioFrame(original);
        var deserialized = VncProtocol.DeserializeAudioFrame(serialized);

        Assert.Equal(original.TimestampMs, deserialized.TimestampMs);
        Assert.Equal(original.SampleRate, deserialized.SampleRate);
        Assert.Equal(original.Channels, deserialized.Channels);
        Assert.Equal(original.Codec, deserialized.Codec);
        Assert.Equal(original.Data, deserialized.Data);
    }

    [Fact]
    public void CursorPosition_Roundtrip_Succeeds()
    {
        var original = new CursorPosition(1280, 720, true);
        byte[] serialized = VncProtocol.SerializeCursorPosition(original);
        var deserialized = VncProtocol.DeserializeCursorPosition(serialized);

        Assert.Equal(original.X, deserialized.X);
        Assert.Equal(original.Y, deserialized.Y);
        Assert.Equal(original.Visible, deserialized.Visible);
    }

    [Fact]
    public void CursorShape_Roundtrip_Succeeds()
    {
        byte[] pngData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0x03];
        var original = new CursorShape(32, 32, 5, 8, 1, pngData);
        byte[] serialized = VncProtocol.SerializeCursorShape(original);
        var deserialized = VncProtocol.DeserializeCursorShape(serialized);

        Assert.Equal(original.Width, deserialized.Width);
        Assert.Equal(original.Height, deserialized.Height);
        Assert.Equal(original.HotspotX, deserialized.HotspotX);
        Assert.Equal(original.HotspotY, deserialized.HotspotY);
        Assert.Equal(original.Format, deserialized.Format);
        Assert.Equal(original.Data, deserialized.Data);
    }
}

