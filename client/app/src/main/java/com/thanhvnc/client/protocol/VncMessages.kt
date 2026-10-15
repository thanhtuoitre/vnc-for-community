package com.thanhvnc.client.protocol

enum class VncMessageType(val value: Byte) {
    HandshakeRequest(1),
    HandshakeResponse(2),
    AuthRequest(3),
    AuthResponse(4),
    ScreenFrame(5),
    MouseInput(6),
    KeyboardInput(7),
    Heartbeat(8),
    Disconnect(9),
    QualityControl(10),
    ClipboardText(11),
    AudioFrame(12),
    CursorPosition(13),
    CursorShape(14);

    companion object {
        private val map = entries.associateBy { it.value }
        fun fromByte(value: Byte): VncMessageType? = map[value]
    }
}

object MouseFlags {
    const val NONE: Short = 0
    const val MOVE: Short = (1 shl 0).toShort()
    const val LEFT_DOWN: Short = (1 shl 1).toShort()
    const val LEFT_UP: Short = (1 shl 2).toShort()
    const val RIGHT_DOWN: Short = (1 shl 3).toShort()
    const val RIGHT_UP: Short = (1 shl 4).toShort()
    const val MIDDLE_DOWN: Short = (1 shl 5).toShort()
    const val MIDDLE_UP: Short = (1 shl 6).toShort()
    const val WHEEL: Short = (1 shl 7).toShort()
}

object KeyFlags {
    const val NONE: Short = 0
    const val KEY_DOWN: Short = (1 shl 0).toShort()
    const val KEY_UP: Short = (1 shl 1).toShort()
    const val EXTENDED_KEY: Short = (1 shl 2).toShort()
    const val UNICODE: Short = (1 shl 3).toShort()
}

data class HandshakeRequest(val version: Byte, val clientName: String)

data class HandshakeResponse(
    val version: Byte,
    val challenge: ByteArray,
    val screenWidth: Int,
    val screenHeight: Int
)

data class AuthRequest(val hmacSignature: ByteArray, val resumeToken: ByteArray? = null)
 
object SessionPermissions {
    const val NONE: Byte = 0
    const val VIEW_ONLY: Byte = 1
    const val INPUT_CONTROL: Byte = 2
    const val CLIPBOARD_SYNC: Byte = 4
    const val FILE_TRANSFER: Byte = 8
    const val FULL_CONTROL: Byte = 15 // VIEW_ONLY | INPUT_CONTROL | CLIPBOARD_SYNC | FILE_TRANSFER
}

data class AuthResponse(
    val statusCode: Byte,
    val permissions: Byte = SessionPermissions.FULL_CONTROL,
    val sessionToken: ByteArray? = null
) // 0 = Success, 1 = InvalidAuth, 2 = ProtocolError

data class ScreenFrame(
    val frameIndex: Long,
    val timestampMs: Long,
    val x: Int,
    val y: Int,
    val width: Int,
    val height: Int,
    val format: Byte, // 1 = JPEG
    val data: ByteArray
)

data class MouseInput(
    val flags: Short,
    val x: Int,
    val y: Int,
    val wheelDelta: Short
)

data class KeyboardInput(
    val flags: Short,
    val virtualKey: Short,
    val scanCode: Short
)

data class Heartbeat(val timestampMs: Long)

data class DisconnectMessage(val reasonCode: Byte)

enum class StreamingPreset(val value: Byte) {
    BALANCED(0),
    LOW_LATENCY(1),
    HIGH_QUALITY(2)
}

data class QualityControl(val quality: Byte, val targetFps: Byte, val preset: Byte = 0)

data class ClipboardMessage(val text: String)

data class AudioFrame(
    val timestampMs: Long,
    val sampleRate: Int,
    val channels: Byte,
    val codec: Byte,
    val data: ByteArray
)

data class CursorPosition(
    val x: Int,
    val y: Int,
    val visible: Boolean
)

data class CursorShape(
    val width: Int,
    val height: Int,
    val hotspotX: Int,
    val hotspotY: Int,
    val format: Byte,
    val data: ByteArray
)
