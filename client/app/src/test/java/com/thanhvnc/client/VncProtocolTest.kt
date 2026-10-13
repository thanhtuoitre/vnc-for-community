package com.thanhvnc.client

import com.thanhvnc.client.protocol.*
import org.junit.Assert.*
import org.junit.Test

class VncProtocolTest {

    @Test
    fun testHeaderRoundtrip() {
        val header = ByteArray(VncProtocol.HEADER_SIZE)
        VncProtocol.writeHeader(header, VncMessageType.MouseInput, 12)

        val parsed = VncProtocol.parseHeader(header)
        assertNotNull(parsed)
        assertEquals(VncMessageType.MouseInput, parsed?.first)
        assertEquals(12, parsed?.second)
    }

    @Test
    fun testHeaderInvalidMagic() {
        val header = ByteArray(VncProtocol.HEADER_SIZE)
        header[0] = 0x00 // corrupt magic
        val parsed = VncProtocol.parseHeader(header)
        assertNull(parsed)
    }

    @Test
    fun testHandshakeRequestRoundtrip() {
        val original = HandshakeRequest(1, "AndroidClientTest")
        val serialized = VncProtocol.serializeHandshakeRequest(original)
        // HandshakeRequest is only serialized by client, but we verify byte structure:
        // version: 1 byte, len: 2 bytes, name: UTF-8
        assertEquals(1.toByte(), serialized[0])
        val name = String(serialized, 3, serialized.size - 3, Charsets.UTF_8)
        assertEquals("AndroidClientTest", name)
    }

    @Test
    fun testHandshakeResponseDeserialization() {
        val challenge = byteArrayOf(1, 2, 3, 4, 5, 6, 7, 8)
        val buf = java.nio.ByteBuffer.allocate(11 + challenge.size).order(java.nio.ByteOrder.LITTLE_ENDIAN)
        buf.put(1.toByte())
        buf.putInt(1920)
        buf.putInt(1080)
        buf.putShort(challenge.size.toShort())
        buf.put(challenge)

        val res = VncProtocol.deserializeHandshakeResponse(buf.array())
        assertEquals(1.toByte(), res.version)
        assertEquals(1920, res.screenWidth)
        assertEquals(1080, res.screenHeight)
        assertArrayEquals(challenge, res.challenge)
    }

    @Test
    fun testAuthRoundtrip() {
        val sig = ByteArray(32) { it.toByte() }
        val req = AuthRequest(sig)
        val serialized = VncProtocol.serializeAuthRequest(req)
        assertTrue(serialized.size >= 34)

        val res = VncProtocol.deserializeAuthResponse(byteArrayOf(0))
        assertEquals(0.toByte(), res.statusCode)
    }

    @Test
    fun testScreenFrameDeserialization() {
        val dummyData = byteArrayOf(0xFF.toByte(), 0xD8.toByte(), 0xFF.toByte(), 0xD9.toByte())
        val metaLen = 29
        val buf = java.nio.ByteBuffer.allocate(metaLen + dummyData.size).order(java.nio.ByteOrder.LITTLE_ENDIAN)
        buf.putInt(99) // frame index
        buf.putLong(123456789L) // timestamp
        buf.putInt(0) // x
        buf.putInt(0) // y
        buf.putInt(1920) // w
        buf.putInt(1080) // h
        buf.put(1.toByte()) // format = JPEG
        buf.put(dummyData)

        val frame = VncProtocol.deserializeScreenFrame(buf.array())
        assertEquals(99L, frame.frameIndex)
        assertEquals(123456789L, frame.timestampMs)
        assertEquals(1920, frame.width)
        assertEquals(1080, frame.height)
        assertEquals(1.toByte(), frame.format)
        assertArrayEquals(dummyData, frame.data)
    }

    @Test
    fun testMouseInputSerialization() {
        val expectedFlags = (MouseFlags.MOVE.toInt() or MouseFlags.LEFT_DOWN.toInt()).toShort()
        val mouse = MouseInput(expectedFlags, 350, 450, 120)
        val bytes = VncProtocol.serializeMouseInput(mouse)
        assertEquals(12, bytes.size)

        val buf = java.nio.ByteBuffer.wrap(bytes).order(java.nio.ByteOrder.LITTLE_ENDIAN)
        val flags = buf.short
        val x = buf.int
        val y = buf.int
        val wheel = buf.short

        assertEquals(expectedFlags, flags)
        assertEquals(350, x)
        assertEquals(450, y)
        assertEquals(120.toShort(), wheel)
    }

    @Test
    fun testKeyboardInputSerialization() {
        val kb = KeyboardInput(KeyFlags.KEY_DOWN, 0x41.toShort(), 0x1E.toShort())
        val bytes = VncProtocol.serializeKeyboardInput(kb)
        assertEquals(6, bytes.size)

        val buf = java.nio.ByteBuffer.wrap(bytes).order(java.nio.ByteOrder.LITTLE_ENDIAN)
        assertEquals(KeyFlags.KEY_DOWN, buf.short)
        assertEquals(0x41.toShort(), buf.short)
        assertEquals(0x1E.toShort(), buf.short)
    }

    @Test
    fun testHmacAuthentication() {
        val challenge = byteArrayOf(10, 20, 30, 40)
        val password = "SecretVncPassword"

        val sig1 = HmacAuth.computeHmac(challenge, password)
        val sig2 = HmacAuth.computeHmac(challenge, password)
        val sigWrong = HmacAuth.computeHmac(challenge, "WrongPassword")

        assertArrayEquals(sig1, sig2)
        assertFalse(sig1.contentEquals(sigWrong))
    }
}
