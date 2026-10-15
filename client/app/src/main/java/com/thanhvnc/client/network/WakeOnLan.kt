package com.thanhvnc.client.network

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

object WakeOnLan {

    /**
     * Builds a standard Wake-on-LAN magic packet:
     * 6 bytes of 0xFF followed by 16 repetitions of the 6-byte target MAC address.
     */
    fun buildMagicPacket(macAddress: String): ByteArray {
        val cleanMac = macAddress.replace(":", "").replace("-", "").trim()
        require(cleanMac.length == 12) { "Invalid MAC address format: $macAddress" }

        val macBytes = ByteArray(6)
        for (i in 0 until 6) {
            val hex = cleanMac.substring(i * 2, i * 2 + 2)
            macBytes[i] = hex.toInt(16).toByte()
        }

        val packet = ByteArray(6 + 16 * 6)
        for (i in 0 until 6) {
            packet[i] = 0xFF.toByte()
        }
        for (i in 0 until 16) {
            System.arraycopy(macBytes, 0, packet, 6 + i * 6, 6)
        }
        return packet
    }

    /**
     * Sends the magic packet over UDP broadcast (default port 9).
     */
    suspend fun sendMagicPacket(
        macAddress: String,
        broadcastIp: String = "255.255.255.255",
        port: Int = 9
    ): Boolean = withContext(Dispatchers.IO) {
        try {
            val packetData = buildMagicPacket(macAddress)
            val address = InetAddress.getByName(broadcastIp)
            val packet = DatagramPacket(packetData, packetData.size, address, port)
            DatagramSocket().use { socket ->
                socket.broadcast = true
                socket.send(packet)
            }
            true
        } catch (_: Exception) {
            false
        }
    }
}
