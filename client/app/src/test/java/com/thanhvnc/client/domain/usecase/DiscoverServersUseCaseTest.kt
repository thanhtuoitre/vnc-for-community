package com.thanhvnc.client.domain.usecase

import com.thanhvnc.client.domain.model.DiscoveredServer
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class DiscoverServersUseCaseTest {

    @Test
    fun testBeaconJsonParsing_ValidFields() {
        val jsonStr = """
            {
                "Name": "DEV-WORKSTATION",
                "Port": 5901,
                "Tls": true,
                "Fingerprint": "AA:BB:CC:DD:EE:FF",
                "Version": 1
            }
        """.trimIndent()

        val json = JSONObject(jsonStr)
        val name = if (json.has("Name")) json.getString("Name") else json.optString("name", "VNC Server")
        val port = if (json.has("Port")) json.getInt("Port") else json.optInt("port", 5900)
        val tls = if (json.has("Tls")) json.getBoolean("Tls") else json.optBoolean("tls", false)
        val fp = if (json.has("Fingerprint") && !json.isNull("Fingerprint")) json.getString("Fingerprint") else null

        val server = DiscoveredServer(
            name = name,
            host = "192.168.1.100",
            port = port,
            useTls = tls,
            fingerprint = fp
        )

        assertEquals("DEV-WORKSTATION", server.name)
        assertEquals("192.168.1.100", server.host)
        assertEquals(5901, server.port)
        assertTrue(server.useTls)
        assertEquals("AA:BB:CC:DD:EE:FF", server.fingerprint)
    }

    @Test
    fun testBeaconJsonParsing_FallbackLowercase() {
        val jsonStr = """
            {
                "name": "HomePC",
                "port": 5900,
                "tls": false
            }
        """.trimIndent()

        val json = JSONObject(jsonStr)
        val name = if (json.has("Name")) json.getString("Name") else json.optString("name", "VNC Server")
        val port = if (json.has("Port")) json.getInt("Port") else json.optInt("port", 5900)
        val tls = if (json.has("Tls")) json.getBoolean("Tls") else json.optBoolean("tls", false)
        val fp = if (json.has("Fingerprint") && !json.isNull("Fingerprint")) json.getString("Fingerprint")
                 else if (json.has("fingerprint") && !json.isNull("fingerprint")) json.getString("fingerprint")
                 else null

        val server = DiscoveredServer(name = name, host = "10.0.0.2", port = port, useTls = tls, fingerprint = fp)

        assertEquals("HomePC", server.name)
        assertEquals("10.0.0.2", server.host)
        assertEquals(5900, server.port)
        assertFalse(server.useTls)
        assertNull(server.fingerprint)
    }

    @Test
    fun testConstants() {
        assertEquals(5902, DiscoverServersUseCase.DISCOVERY_PORT)
        assertEquals("THVNC_PROBE", DiscoverServersUseCase.PROBE_MAGIC)
        assertEquals("THVNC_BEACON:", DiscoverServersUseCase.BEACON_PREFIX)
        assertTrue(DiscoverServersUseCase.STALE_TIMEOUT_MS >= 5000L)
    }
}
