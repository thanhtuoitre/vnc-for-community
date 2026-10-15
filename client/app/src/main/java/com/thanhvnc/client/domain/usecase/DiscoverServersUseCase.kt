package com.thanhvnc.client.domain.usecase

import com.thanhvnc.client.domain.model.DiscoveredServer
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.util.concurrent.ConcurrentHashMap

/**
 * UseCase that discovers active Windows VNC servers on the local LAN
 * via zero-configuration UDP broadcast probes and beacons on port 5902.
 */
class DiscoverServersUseCase(
    private val scope: CoroutineScope = CoroutineScope(Dispatchers.IO + SupervisorJob())
) {
    companion object {
        const val DISCOVERY_PORT = 5902
        const val PROBE_MAGIC = "THVNC_PROBE"
        const val BEACON_PREFIX = "THVNC_BEACON:"
        const val STALE_TIMEOUT_MS = 10000L
    }

    private val _servers = MutableStateFlow<List<DiscoveredServer>>(emptyList())
    val servers: StateFlow<List<DiscoveredServer>> = _servers.asStateFlow()

    private val discoveredMap = ConcurrentHashMap<String, DiscoveredServer>()
    private var job: Job? = null
    private var socket: DatagramSocket? = null

    fun startDiscovery() {
        if (job != null && job?.isActive == true) return

        job = scope.launch {
            try {
                val s = DatagramSocket()
                s.broadcast = true
                s.soTimeout = 2000
                socket = s

                // 1. Probe & Broadcast Loop
                val probeJob = launch {
                    val probeBytes = PROBE_MAGIC.toByteArray(Charsets.UTF_8)
                    val broadcastAddr = InetAddress.getByName("255.255.255.255")
                    val probePacket = DatagramPacket(probeBytes, probeBytes.size, broadcastAddr, DISCOVERY_PORT)

                    while (isActive) {
                        try {
                            s.send(probePacket)
                        } catch (_: Exception) {}

                        // Clean up stale servers
                        val now = System.currentTimeMillis()
                        val iterator = discoveredMap.entries.iterator()
                        var changed = false
                        while (iterator.hasNext()) {
                            val entry = iterator.next()
                            if (now - entry.value.lastSeenMs > STALE_TIMEOUT_MS) {
                                iterator.remove()
                                changed = true
                            }
                        }
                        if (changed) {
                            _servers.value = discoveredMap.values.toList().sortedBy { it.name }
                        }

                        delay(3000)
                    }
                }

                // 2. Receive Listen Loop
                val rxBuffer = ByteArray(2048)
                while (isActive) {
                    val rxPacket = DatagramPacket(rxBuffer, rxBuffer.size)
                    try {
                        s.receive(rxPacket)
                        val text = String(rxPacket.data, 0, rxPacket.length, Charsets.UTF_8)
                        if (text.startsWith(BEACON_PREFIX)) {
                            val jsonStr = text.substring(BEACON_PREFIX.length).trim()
                            val json = JSONObject(jsonStr)

                            val name = if (json.has("Name")) json.getString("Name") else json.optString("name", "VNC Server")
                            val port = if (json.has("Port")) json.getInt("Port") else json.optInt("port", 5900)
                            val tls = if (json.has("Tls")) json.getBoolean("Tls") else json.optBoolean("tls", false)
                            val fp = if (json.has("Fingerprint") && !json.isNull("Fingerprint")) json.getString("Fingerprint")
                                     else if (json.has("fingerprint") && !json.isNull("fingerprint")) json.getString("fingerprint")
                                     else null
                            val host = rxPacket.address.hostAddress ?: ""

                            if (host.isNotBlank() && port > 0) {
                                val server = DiscoveredServer(
                                    name = name,
                                    host = host,
                                    port = port,
                                    useTls = tls,
                                    fingerprint = if (fp == "null" || fp.isNullOrBlank()) null else fp,
                                    lastSeenMs = System.currentTimeMillis()
                                )
                                val key = "$host:$port"
                                discoveredMap[key] = server
                                _servers.value = discoveredMap.values.toList().sortedBy { it.name }
                            }
                        }
                    } catch (_: java.net.SocketTimeoutException) {
                        // Expected on timeout, loop again
                    } catch (e: Exception) {
                        if (!isActive) break
                    }
                }
                probeJob.cancel()
            } catch (_: Exception) {
            } finally {
                socket?.close()
                socket = null
            }
        }
    }

    fun stopDiscovery() {
        job?.cancel()
        job = null
        try {
            socket?.close()
        } catch (_: Exception) {}
        socket = null
    }
}
