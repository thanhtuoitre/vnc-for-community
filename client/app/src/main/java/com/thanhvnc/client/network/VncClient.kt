package com.thanhvnc.client.network

import com.thanhvnc.client.protocol.*
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.net.InetSocketAddress
import java.net.Socket

sealed class VncConnectionState {
    data object Disconnected : VncConnectionState()
    data object Connecting : VncConnectionState()
    data object Handshaking : VncConnectionState()
    data object Authenticating : VncConnectionState()
    data class Connected(val screenWidth: Int, val screenHeight: Int) : VncConnectionState()
    data class Reconnecting(val attempt: Int, val nextDelayMs: Long) : VncConnectionState()
    data class Error(val message: String) : VncConnectionState()
}

class VncClient(private val clientScope: CoroutineScope = CoroutineScope(Dispatchers.IO + SupervisorJob())) {

    private val _state = MutableStateFlow<VncConnectionState>(VncConnectionState.Disconnected)
    val state: StateFlow<VncConnectionState> = _state.asStateFlow()

    private val _frames = MutableSharedFlow<ScreenFrame>(extraBufferCapacity = 2)
    val frames: SharedFlow<ScreenFrame> = _frames.asSharedFlow()

    private val _rttMs = MutableStateFlow(0L)
    val rttMs: StateFlow<Long> = _rttMs.asStateFlow()

    private val _clipboardEvents = MutableSharedFlow<String>(extraBufferCapacity = 5)
    val clipboardEvents: SharedFlow<String> = _clipboardEvents.asSharedFlow()

    private val _cursorPositions = MutableSharedFlow<CursorPosition>(extraBufferCapacity = 5)
    val cursorPositions: SharedFlow<CursorPosition> = _cursorPositions.asSharedFlow()

    private val _cursorShapes = MutableSharedFlow<CursorShape>(extraBufferCapacity = 5)
    val cursorShapes: SharedFlow<CursorShape> = _cursorShapes.asSharedFlow()

    private var currentQuality: Byte = 70
    private var currentFps: Byte = 30

    private var socket: Socket? = null
    private var job: Job? = null
    private var isManualDisconnect = false

    // Channel for immediate priority transmission of input/control packets (bypassing frame reads)
    private val outgoingPackets = Channel<Pair<VncMessageType, ByteArray>>(Channel.BUFFERED)

    var screenWidth: Int = 1920
        private set
    var screenHeight: Int = 1080
        private set

    fun connect(host: String, port: Int, password: String, autoReconnect: Boolean = true) {
        disconnect()
        isManualDisconnect = false

        job = clientScope.launch {
            var attempt = 1
            var backoffMs = 1000L

            while (isActive && !isManualDisconnect) {
                try {
                    _state.value = if (attempt == 1) VncConnectionState.Connecting else VncConnectionState.Reconnecting(attempt, 0)
                    runSession(host, port, password)
                    attempt = 1
                    backoffMs = 1000L
                } catch (e: CancellationException) {
                    break
                } catch (e: Exception) {
                    if (isManualDisconnect) break

                    if (autoReconnect) {
                        _state.value = VncConnectionState.Reconnecting(attempt, backoffMs)
                        delay(backoffMs)
                        attempt++
                        backoffMs = (backoffMs * 2).coerceAtMost(10000L)
                    } else {
                        _state.value = VncConnectionState.Error(e.message ?: "Connection error")
                        break
                    }
                } finally {
                    cleanup()
                }
            }

            if (_state.value !is VncConnectionState.Error) {
                _state.value = VncConnectionState.Disconnected
            }
        }
    }

    private suspend fun runSession(host: String, port: Int, password: String) = coroutineScope {
        val s = Socket()
        s.tcpNoDelay = true
        s.connect(InetSocketAddress(host, port), 5000)
        socket = s

        val inputStream = BufferedInputStream(s.getInputStream())
        val outputStream = BufferedOutputStream(s.getOutputStream())

        // 1. Handshake
        _state.value = VncConnectionState.Handshaking
        val handshakeReq = HandshakeRequest(VncProtocol.CURRENT_VERSION, "AndroidClient")
        sendPacketDirect(outputStream, VncMessageType.HandshakeRequest, VncProtocol.serializeHandshakeRequest(handshakeReq))

        val (resType, resPayload) = readPacket(inputStream)
        if (resType != VncMessageType.HandshakeResponse) {
            throw IllegalStateException("Expected HandshakeResponse, got $resType")
        }
        val handshakeRes = VncProtocol.deserializeHandshakeResponse(resPayload)
        screenWidth = handshakeRes.screenWidth
        screenHeight = handshakeRes.screenHeight

        // 2. Authentication
        _state.value = VncConnectionState.Authenticating
        val signature = HmacAuth.computeHmac(handshakeRes.challenge, password)
        val authReq = AuthRequest(signature)
        sendPacketDirect(outputStream, VncMessageType.AuthRequest, VncProtocol.serializeAuthRequest(authReq))

        val (authType, authPayload) = readPacket(inputStream)
        if (authType != VncMessageType.AuthResponse) {
            throw IllegalStateException("Expected AuthResponse, got $authType")
        }
        val authRes = VncProtocol.deserializeAuthResponse(authPayload)
        if (authRes.statusCode.toInt() != 0) {
            throw SecurityException("Authentication rejected by server (code ${authRes.statusCode})")
        }

        _state.value = VncConnectionState.Connected(screenWidth, screenHeight)

        // 3. Sender Pipeline: sends mouse, keyboard, quality control, heartbeats
        val sendJob = launch {
            for ((type, payload) in outgoingPackets) {
                sendPacketDirect(outputStream, type, payload)
            }
        }

        // 4. Heartbeat Keepalive: ping every 5 seconds
        val heartbeatJob = launch {
            while (isActive) {
                delay(5000)
                val hb = Heartbeat(System.currentTimeMillis())
                outgoingPackets.trySend(Pair(VncMessageType.Heartbeat, VncProtocol.serializeHeartbeat(hb)))
            }
        }

        try {
            while (isActive) {
                val (type, payload) = readPacket(inputStream)
                when (type) {
                    VncMessageType.ScreenFrame -> {
                        val frame = VncProtocol.deserializeScreenFrame(payload)
                        _frames.tryEmit(frame)
                    }
                    VncMessageType.Heartbeat -> {
                        val hb = VncProtocol.deserializeHeartbeat(payload)
                        val rtt = System.currentTimeMillis() - hb.timestampMs
                        if (rtt >= 0) {
                            _rttMs.value = rtt
                            if (rtt > 200 && currentQuality > 40) {
                                setQuality(40.toByte(), 20.toByte())
                            } else if (rtt < 60 && currentQuality < 70) {
                                setQuality(70.toByte(), 30.toByte())
                            }
                        }
                    }
                    VncMessageType.ClipboardText -> {
                        val clip = VncProtocol.deserializeClipboard(payload)
                        _clipboardEvents.tryEmit(clip.text)
                    }
                    VncMessageType.CursorPosition -> {
                        val pos = VncProtocol.deserializeCursorPosition(payload)
                        _cursorPositions.tryEmit(pos)
                    }
                    VncMessageType.CursorShape -> {
                        val shape = VncProtocol.deserializeCursorShape(payload)
                        _cursorShapes.tryEmit(shape)
                    }
                    VncMessageType.Disconnect -> {
                        break
                    }
                    else -> Unit
                }
            }
        } finally {
            heartbeatJob.cancel()
            sendJob.cancel()
        }
    }

    fun sendMouseInput(input: MouseInput) {
        outgoingPackets.trySend(Pair(VncMessageType.MouseInput, VncProtocol.serializeMouseInput(input)))
    }

    fun sendKeyboardInput(input: KeyboardInput) {
        outgoingPackets.trySend(Pair(VncMessageType.KeyboardInput, VncProtocol.serializeKeyboardInput(input)))
    }

    fun sendClipboardText(text: String) {
        val clip = ClipboardMessage(text)
        outgoingPackets.trySend(Pair(VncMessageType.ClipboardText, VncProtocol.serializeClipboard(clip)))
    }

    fun setQuality(quality: Byte, targetFps: Byte, preset: StreamingPreset = StreamingPreset.BALANCED) {
        currentQuality = quality
        currentFps = targetFps
        val qc = QualityControl(quality, targetFps, preset.value)
        outgoingPackets.trySend(Pair(VncMessageType.QualityControl, VncProtocol.serializeQualityControl(qc)))
    }

    fun disconnect() {
        isManualDisconnect = true
        job?.cancel()
        job = null
        cleanup()
        _state.value = VncConnectionState.Disconnected
    }

    private fun cleanup() {
        try {
            socket?.close()
        } catch (_: Exception) {}
        socket = null
    }

    private fun readPacket(stream: BufferedInputStream): Pair<VncMessageType, ByteArray> {
        val header = ByteArray(VncProtocol.HEADER_SIZE)
        VncProtocol.readExactly(stream, header, header.size)

        val parsed = VncProtocol.parseHeader(header)
            ?: throw IllegalStateException("Invalid packet header from server")

        val (type, payloadLength) = parsed
        val payload = ByteArray(payloadLength)
        if (payloadLength > 0) {
            VncProtocol.readExactly(stream, payload, payloadLength)
        }
        return Pair(type, payload)
    }

    private fun sendPacketDirect(stream: BufferedOutputStream, type: VncMessageType, payload: ByteArray) {
        val header = ByteArray(VncProtocol.HEADER_SIZE)
        VncProtocol.writeHeader(header, type, payload.size)
        stream.write(header)
        if (payload.isNotEmpty()) {
            stream.write(payload)
        }
        stream.flush()
    }
}
