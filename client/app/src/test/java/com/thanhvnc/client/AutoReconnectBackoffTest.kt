package com.thanhvnc.client

import org.junit.Assert.assertEquals
import org.junit.Test

class AutoReconnectBackoffTest {

    private fun calculateNextBackoff(currentBackoff: Long): Long {
        return (currentBackoff * 2).coerceAtMost(10000L)
    }

    @Test
    fun testExponentialBackoffProgression() {
        var delay = 1000L

        delay = calculateNextBackoff(delay)
        assertEquals(2000L, delay)

        delay = calculateNextBackoff(delay)
        assertEquals(4000L, delay)

        delay = calculateNextBackoff(delay)
        assertEquals(8000L, delay)

        delay = calculateNextBackoff(delay)
        assertEquals(10000L, delay) // Capped at 10s max

        delay = calculateNextBackoff(delay)
        assertEquals(10000L, delay) // Remains capped
    }
}
