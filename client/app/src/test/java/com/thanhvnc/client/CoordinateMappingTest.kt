package com.thanhvnc.client

import org.junit.Assert.assertEquals
import org.junit.Test

class CoordinateMappingTest {

    private fun mapTouchToRemote(
        touchX: Float, touchY: Float,
        dstLeft: Float, dstTop: Float, dstWidth: Float, dstHeight: Float,
        remoteW: Int, remoteH: Int
    ): Pair<Int, Int>? {
        if (dstWidth <= 0f || dstHeight <= 0f) return null
        if (touchX < dstLeft || touchX > dstLeft + dstWidth || touchY < dstTop || touchY > dstTop + dstHeight) {
            return null
        }

        val normX = (touchX - dstLeft) / dstWidth
        val normY = (touchY - dstTop) / dstHeight

        val rx = (normX * remoteW).toInt().coerceIn(0, remoteW - 1)
        val ry = (normY * remoteH).toInt().coerceIn(0, remoteH - 1)
        return Pair(rx, ry)
    }

    @Test
    fun testCenterTouchMapsToCenterRemote() {
        val result = mapTouchToRemote(
            touchX = 540f, touchY = 960f,
            dstLeft = 0f, dstTop = 0f, dstWidth = 1080f, dstHeight = 1920f,
            remoteW = 1920, remoteH = 1080
        )
        assertEquals(Pair(960, 540), result)
    }

    @Test
    fun testLetterboxTouchOutsideReturnsNull() {
        // Pillarbox with letterbox left/right margins: dst starts at x = 100
        val result = mapTouchToRemote(
            touchX = 50f, touchY = 500f,
            dstLeft = 100f, dstTop = 0f, dstWidth = 800f, dstHeight = 1000f,
            remoteW = 1920, remoteH = 1080
        )
        assertEquals(null, result)
    }

    @Test
    fun testBoundaryTouchClampedCorrectly() {
        val result = mapTouchToRemote(
            touchX = 900f, touchY = 1000f,
            dstLeft = 100f, dstTop = 0f, dstWidth = 800f, dstHeight = 1000f,
            remoteW = 1920, remoteH = 1080
        )
        assertEquals(Pair(1919, 1079), result)
    }
}
