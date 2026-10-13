package com.thanhvnc.client.protocol

import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

object VncProtocol {
    const val MAGIC: Int = 0x434E5654 // 'TVNC' in little-endian
    const val CURRENT_VERSION: Byte = 1
    const val HEADER_SIZE: Int = 10
    const val MAX_PAYLOAD_SIZE: Int = 16 * 1024 * 1024

    fun writeHeader(dest: ByteArray, type: VncMessageType, payloadLength: Int) {
        require(dest.size >= HEADER_SIZE) { "Destination buffer too small" }
        require(payloadLength in 0..MAX_PAYLOAD_SIZE) { "Payload length out of bounds" }

        val buf = ByteBuffer.wrap(dest).order(ByteOrder.LITTLE_ENDIAN)
        buf.putInt(MAGIC)
        buf.put(CURRENT_VERSION)
        buf.put(type.value)
        buf.putInt(payloadLength)
    }

    fun parseHeader(src: ByteArray): Pair<VncMessageType, Int>? {
        if (src.size < HEADER_SIZE) return null
        val buf = ByteBuffer.wrap(src).order(ByteOrder.LITTLE_ENDIAN)
        val magic = buf.int
        if (magic != MAGIC) return null

        val version = buf.get()
        if (version != CURRENT_VERSION) return null

        val typeByte = buf.get()
        val type = VncMessageType.fromByte(typeByte) ?: return null

        val payloadLength = buf.int
        if (payloadLength < 0 || payloadLength > MAX_PAYLOAD_SIZE) return null

        return Pair(type, payloadLength)
    }

    fun serializeHandshakeRequest(req: HandshakeRequest): ByteArray {
        val nameBytes = req.clientName.toByteArray(Charsets.UTF_8)
        val buf = ByteBuffer.allocate(1 + 2 + nameBytes.size).order(ByteOrder.LITTLE_ENDIAN)
        buf.put(req.version)
        buf.putShort(nameBytes.size.toShort())
        buf.put(nameBytes)
        return buf.array()
    }

    fun deserializeHandshakeResponse(payload: ByteArray): HandshakeResponse {
        require(payload.size >= 11) { "Handshake response payload too short" }
        val buf = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)
        val version = buf.get()
        val width = buf.int
        val height = buf.int
        val challengeLen = buf.short.toInt() and 0xFFFF
        require(payload.size >= 11 + challengeLen) { "Challenge truncated" }
        val challenge = ByteArray(challengeLen)
        buf.get(challenge)
        return HandshakeResponse(version, challenge, width, height)
    }

    fun serializeAuthRequest(req: AuthRequest): ByteArray {
        val buf = ByteBuffer.allocate(2 + req.hmacSignature.size).order(ByteOrder.LITTLE_ENDIAN)
        buf.putShort(req.hmacSignature.size.toShort())
        buf.put(req.hmacSignature)
        return buf.array()
    }

    fun deserializeAuthResponse(payload: ByteArray): AuthResponse {
        require(payload.isNotEmpty()) { "Auth response empty" }
        return AuthResponse(payload[0])
    }

    fun deserializeScreenFrame(payload: ByteArray): ScreenFrame {
        val metaLen = 4 + 8 + 4 + 4 + 4 + 4 + 1
        require(payload.size >= metaLen) { "Screen frame payload too short" }

        val buf = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)
        val frameIndex = buf.int.toLong() and 0xFFFFFFFFL
        val timestampMs = buf.long
        val x = buf.int
        val y = buf.int
        val width = buf.int
        val height = buf.int
        val format = buf.get()

        val dataSize = payload.size - metaLen
        val data = ByteArray(dataSize)
        buf.get(data)

        return ScreenFrame(frameIndex, timestampMs, x, y, width, height, format, data)
    }

    fun serializeMouseInput(input: MouseInput): ByteArray {
        val buf = ByteBuffer.allocate(12).order(ByteOrder.LITTLE_ENDIAN)
        buf.putShort(input.flags)
        buf.putInt(input.x)
        buf.putInt(input.y)
        buf.putShort(input.wheelDelta)
        return buf.array()
    }

    fun serializeKeyboardInput(input: KeyboardInput): ByteArray {
        val buf = ByteBuffer.allocate(6).order(ByteOrder.LITTLE_ENDIAN)
        buf.putShort(input.flags)
        buf.putShort(input.virtualKey)
        buf.putShort(input.scanCode)
        return buf.array()
    }

    fun serializeHeartbeat(hb: Heartbeat): ByteArray {
        val buf = ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN)
        buf.putLong(hb.timestampMs)
        return buf.array()
    }

    fun deserializeHeartbeat(payload: ByteArray): Heartbeat {
        require(payload.size >= 8) { "Heartbeat payload too short" }
        val buf = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)
        return Heartbeat(buf.long)
    }

    fun serializeDisconnect(msg: DisconnectMessage): ByteArray = byteArrayOf(msg.reasonCode)

    fun serializeQualityControl(qc: QualityControl): ByteArray = byteArrayOf(qc.quality, qc.targetFps)

    fun deserializeQualityControl(payload: ByteArray): QualityControl {
        require(payload.size >= 2) { "QualityControl payload too short" }
        return QualityControl(payload[0], payload[1])
    }

    fun serializeClipboard(msg: ClipboardMessage): ByteArray = msg.text.toByteArray(Charsets.UTF_8)

    fun deserializeClipboard(payload: ByteArray): ClipboardMessage {
        require(payload.size <= 1024 * 1024) { "Clipboard payload exceeds 1 MB limit" }
        return ClipboardMessage(String(payload, Charsets.UTF_8))
    }

    fun serializeAudioFrame(frame: AudioFrame): ByteArray {
        val metaLen = 8
        val buf = ByteBuffer.allocate(metaLen + frame.data.size).order(ByteOrder.LITTLE_ENDIAN)
        buf.putInt((frame.timestampMs and 0xFFFFFFFFL).toInt())
        buf.putShort(frame.sampleRate.toShort())
        buf.put(frame.channels)
        buf.put(frame.codec)
        buf.put(frame.data)
        return buf.array()
    }

    fun deserializeAudioFrame(payload: ByteArray): AudioFrame {
        val metaLen = 8
        require(payload.size >= metaLen) { "Audio frame payload too short" }
        val buf = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)
        val timestampMs = buf.int.toLong() and 0xFFFFFFFFL
        val sampleRate = buf.short.toInt() and 0xFFFF
        val channels = buf.get()
        val codec = buf.get()
        val data = ByteArray(payload.size - metaLen)
        buf.get(data)
        return AudioFrame(timestampMs, sampleRate, channels, codec, data)
    }

    fun readExactly(stream: InputStream, buffer: ByteArray, length: Int) {
        var offset = 0
        while (offset < length) {
            val read = stream.read(buffer, offset, length - offset)
            if (read == -1) throw java.io.EOFException("Unexpected end of stream")
            offset += read
        }
    }
}
