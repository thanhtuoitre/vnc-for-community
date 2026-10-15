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
    fun testKeyboardInputUnicodeSerialization() {
        val unicodeFlag = (KeyFlags.KEY_DOWN.toInt() or KeyFlags.UNICODE.toInt()).toShort()
        val kb = KeyboardInput(unicodeFlag, 0.toShort(), 0x0111.toShort()) // 'đ'
        val bytes = VncProtocol.serializeKeyboardInput(kb)
        assertEquals(6, bytes.size)

        val buf = java.nio.ByteBuffer.wrap(bytes).order(java.nio.ByteOrder.LITTLE_ENDIAN)
        assertEquals(unicodeFlag, buf.short)
        assertEquals(0.toShort(), buf.short)
        assertEquals(0x0111.toShort(), buf.short)
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

    @Test
    fun testQualityControlRoundtrip() {
        val original = QualityControl(85.toByte(), 45.toByte())
        val serialized = VncProtocol.serializeQualityControl(original)
        val deserialized = VncProtocol.deserializeQualityControl(serialized)

        assertEquals(original.quality, deserialized.quality)
        assertEquals(original.targetFps, deserialized.targetFps)
    }

    @Test
    fun testHeartbeatRoundtripAndRtt() {
        val sendTime = 1700000000000L
        val original = Heartbeat(sendTime)
        val serialized = VncProtocol.serializeHeartbeat(original)
        assertEquals(8, serialized.size)

        val deserialized = VncProtocol.deserializeHeartbeat(serialized)
        assertEquals(sendTime, deserialized.timestampMs)

        val receiveTime = sendTime + 42L
        val rtt = receiveTime - deserialized.timestampMs
        assertEquals(42L, rtt)
    }

    @Test
    fun testClipboardRoundtrip() {
        val testText = "Hello from Android VNC client! 📱 -> 💻"
        val msg = ClipboardMessage(testText)
        val serialized = VncProtocol.serializeClipboard(msg)
        val deserialized = VncProtocol.deserializeClipboard(serialized)

        assertEquals(testText, deserialized.text)
    }

    @Test
    fun testAudioFrameRoundtrip() {
        val dummyPcm = byteArrayOf(0x01, 0x02, 0x03, 0x04, 0x05, 0x06)
        val original = AudioFrame(998877L, 48000, 2.toByte(), 1.toByte(), dummyPcm)
        val serialized = VncProtocol.serializeAudioFrame(original)
        val deserialized = VncProtocol.deserializeAudioFrame(serialized)

        assertEquals(original.timestampMs, deserialized.timestampMs)
        assertEquals(original.sampleRate, deserialized.sampleRate)
        assertEquals(original.channels, deserialized.channels)
        assertEquals(original.codec, deserialized.codec)
        assertArrayEquals(original.data, deserialized.data)
    }

    @Test
    fun testCursorPositionRoundtrip() {
        val original = CursorPosition(1024, 768, true)
        val serialized = VncProtocol.serializeCursorPosition(original)
        assertEquals(9, serialized.size)

        val deserialized = VncProtocol.deserializeCursorPosition(serialized)
        assertEquals(original.x, deserialized.x)
        assertEquals(original.y, deserialized.y)
        assertEquals(original.visible, deserialized.visible)
    }

    @Test
    fun testCursorShapeRoundtrip() {
        val dummyPng = byteArrayOf(0x89.toByte(), 0x50.toByte(), 0x4E.toByte(), 0x47.toByte(), 0x01, 0x02)
        val original = CursorShape(32, 32, 4, 10, 1.toByte(), dummyPng)
        val serialized = VncProtocol.serializeCursorShape(original)
        assertEquals(17 + dummyPng.size, serialized.size)

        val deserialized = VncProtocol.deserializeCursorShape(serialized)
        assertEquals(original.width, deserialized.width)
        assertEquals(original.height, deserialized.height)
        assertEquals(original.hotspotX, deserialized.hotspotX)
        assertEquals(original.hotspotY, deserialized.hotspotY)
        assertEquals(original.format, deserialized.format)
        assertArrayEquals(original.data, deserialized.data)
    }

    @Test
    fun testParseHeaderUnsupportedVersion() {
        val header = ByteArray(VncProtocol.HEADER_SIZE)
        val buf = java.nio.ByteBuffer.wrap(header).order(java.nio.ByteOrder.LITTLE_ENDIAN)
        buf.putInt(VncProtocol.MAGIC.toInt())
        buf.put(99.toByte()) // Unsupported version
        buf.put(VncMessageType.Heartbeat.value)
        buf.putInt(8)

        val parsed = VncProtocol.parseHeader(header)
        assertNull(parsed)
    }

    @Test
    fun testParseHeaderNegativePayloadLength() {
        val header = ByteArray(VncProtocol.HEADER_SIZE)
        val buf = java.nio.ByteBuffer.wrap(header).order(java.nio.ByteOrder.LITTLE_ENDIAN)
        buf.putInt(VncProtocol.MAGIC.toInt())
        buf.put(VncProtocol.CURRENT_VERSION)
        buf.put(VncMessageType.ScreenFrame.value)
        buf.putInt(-1) // Negative payload length

        val parsed = VncProtocol.parseHeader(header)
        assertNull(parsed)
    }

    @Test
    fun testParseHeaderExcessivePayloadLength() {
        val header = ByteArray(VncProtocol.HEADER_SIZE)
        val buf = java.nio.ByteBuffer.wrap(header).order(java.nio.ByteOrder.LITTLE_ENDIAN)
        buf.putInt(VncProtocol.MAGIC.toInt())
        buf.put(VncProtocol.CURRENT_VERSION)
        buf.put(VncMessageType.ScreenFrame.value)
        buf.putInt(32 * 1024 * 1024) // 32MB exceeds 16MB limit

        val parsed = VncProtocol.parseHeader(header)
        assertNull(parsed)
    }

    @Test
    fun testParseHeaderFuzzedBytesRobustness() {
        val random = java.util.Random(42)
        val fuzzed = ByteArray(VncProtocol.HEADER_SIZE)
        for (i in 0 until 500) {
            random.nextBytes(fuzzed)
            val parsed = VncProtocol.parseHeader(fuzzed)
            if (parsed != null) {
                assertTrue(parsed.second in 0..VncProtocol.MAX_PAYLOAD_SIZE)
            }
        }
    }
}

