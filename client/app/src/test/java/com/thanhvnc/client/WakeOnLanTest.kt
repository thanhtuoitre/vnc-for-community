package com.thanhvnc.client

import com.thanhvnc.client.network.WakeOnLan
import org.junit.Assert.assertEquals
import org.junit.Test

class WakeOnLanTest {

    @Test
    fun testBuildMagicPacketLengthAndHeader() {
        val mac = "AA:BB:CC:DD:EE:FF"
        val packet = WakeOnLan.buildMagicPacket(mac)

        assertEquals(102, packet.size)

        // First 6 bytes must be 0xFF
        for (i in 0 until 6) {
            assertEquals(0xFF.toByte(), packet[i])
        }

        // Next 16 * 6 bytes must be AA:BB:CC:DD:EE:FF repeated 16 times
        val expectedMac = byteArrayOf(
            0xAA.toByte(), 0xBB.toByte(), 0xCC.toByte(),
            0xDD.toByte(), 0xEE.toByte(), 0xFF.toByte()
        )
        for (rep in 0 until 16) {
            val offset = 6 + rep * 6
            for (byteIdx in 0 until 6) {
                assertEquals(expectedMac[byteIdx], packet[offset + byteIdx])
            }
        }
    }

    @Test
    fun testBuildMagicPacketHyphenSeparators() {
        val mac = "00-11-22-33-44-55"
        val packet = WakeOnLan.buildMagicPacket(mac)

        assertEquals(102, packet.size)
        val expectedMac = byteArrayOf(0x00, 0x11, 0x22, 0x33, 0x44, 0x55)
        for (rep in 0 until 16) {
            val offset = 6 + rep * 6
            for (byteIdx in 0 until 6) {
                assertEquals(expectedMac[byteIdx], packet[offset + byteIdx])
            }
        }
    }

    @Test(expected = IllegalArgumentException::class)
    fun testBuildMagicPacketInvalidMacThrows() {
        WakeOnLan.buildMagicPacket("INVALID_MAC")
    }
}
