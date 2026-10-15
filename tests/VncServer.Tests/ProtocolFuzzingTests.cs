using System.Buffers.Binary;
using System.Security.Cryptography;
using VncServer.Core.Protocol;
using Xunit;

namespace VncServer.Tests;

public class ProtocolFuzzingTests
{
    [Fact]
    public void TryReadHeader_WithInvalidMagic_ReturnsFalse()
    {
        byte[] header = new byte[VncProtocol.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 0xDEADBEEF); // Corrupt magic
        header[4] = 1; // Version
        header[5] = (byte)VncMessageType.Heartbeat;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(6, 4), 8);

        bool result = VncProtocol.TryReadHeader(header, out var type, out int length);

        Assert.False(result);
    }

    [Fact]
    public void TryReadHeader_WithUnsupportedVersion_ReturnsFalse()
    {
        byte[] header = new byte[VncProtocol.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), VncProtocol.Magic);
        header[4] = 99; // Future unsupported version
        header[5] = (byte)VncMessageType.Heartbeat;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(6, 4), 8);

        bool result = VncProtocol.TryReadHeader(header, out var type, out int length);

        Assert.False(result);
    }

    [Fact]
    public void TryReadHeader_WithNegativePayloadLength_ReturnsFalse()
    {
        byte[] header = new byte[VncProtocol.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), VncProtocol.Magic);
        header[4] = VncProtocol.CurrentVersion;
        header[5] = (byte)VncMessageType.ScreenFrame;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(6, 4), -1); // Malicious negative length

        bool result = VncProtocol.TryReadHeader(header, out var type, out int length);

        Assert.False(result);
    }

    [Fact]
    public void TryReadHeader_WithExcessivePayloadLength_ReturnsFalse()
    {
        byte[] header = new byte[VncProtocol.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), VncProtocol.Magic);
        header[4] = VncProtocol.CurrentVersion;
        header[5] = (byte)VncMessageType.ScreenFrame;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(6, 4), 32 * 1024 * 1024); // Exceeds MaxPayloadSize (16MB)

        bool result = VncProtocol.TryReadHeader(header, out var type, out int length);

        Assert.False(result);
    }

    [Fact]
    public void TryReadHeader_WithRandomFuzzedBytes_NeverThrowsUnhandledException()
    {
        byte[] fuzzed = new byte[VncProtocol.HeaderSize];
        for (int i = 0; i < 500; i++)
        {
            RandomNumberGenerator.Fill(fuzzed);
            bool success = VncProtocol.TryReadHeader(fuzzed, out var type, out int length);

            if (success)
            {
                Assert.InRange(length, 0, VncProtocol.MaxPayloadSize);
            }
        }
    }

    [Fact]
    public void DeserializeHandshakeRequest_WithTruncatedPayload_ThrowsExceptionSafely()
    {
        byte[] truncated = [1]; // Too short for client name length
        Assert.ThrowsAny<Exception>(() => VncProtocol.DeserializeHandshakeRequest(truncated));
    }

    [Fact]
    public void DeserializeMouseInput_WithTruncatedPayload_ThrowsExceptionSafely()
    {
        byte[] truncated = [0x01, 0x02]; // Mouse packet requires 9 bytes
        Assert.ThrowsAny<Exception>(() => VncProtocol.DeserializeMouseInput(truncated));
    }

    [Fact]
    public void DeserializeKeyboardInput_WithTruncatedPayload_ThrowsExceptionSafely()
    {
        byte[] truncated = [0x01]; // Keyboard requires 4 bytes
        Assert.ThrowsAny<Exception>(() => VncProtocol.DeserializeKeyboardInput(truncated));
    }

    [Fact]
    public void DeserializeClipboard_WithExcessiveLength_ThrowsInvalidDataException()
    {
        byte[] malicious = new byte[1024 * 1024 + 1]; // Exceeds 1MB limit

        Assert.Throws<InvalidDataException>(() => VncProtocol.DeserializeClipboard(malicious));
    }
}
