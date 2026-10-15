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

    @Test
    fun testFocalZoomAndPanMapping() {
        val zoomScale = 2.0f
        val panOffsetX = -200f
        val panOffsetY = -100f
        val baseW = 1000f
        val baseH = 500f
        val zoomedW = baseW * zoomScale
        val zoomedH = baseH * zoomScale
        val dLeft = panOffsetX
        val dTop = panOffsetY

        val touchX = 300f
        val touchY = 400f
        val normX = ((touchX - dLeft) / zoomedW).coerceIn(0f, 1f)
        val normY = ((touchY - dTop) / zoomedH).coerceIn(0f, 1f)
        val rx = (normX * 1919).toInt()
        val ry = (normY * 1079).toInt()

        assertEquals(479, rx)
        assertEquals(539, ry)
    }

    @Test
    fun testTrackpadDeltaClamping() {
        var cursorX = 960f
        var cursorY = 540f
        val remoteW = 1920
        val remoteH = 1080

        // Move by delta +50, +30
        cursorX = (cursorX + 50f).coerceIn(0f, (remoteW - 1).toFloat())
        cursorY = (cursorY + 30f).coerceIn(0f, (remoteH - 1).toFloat())
        assertEquals(1010f, cursorX)
        assertEquals(570f, cursorY)

        // Large move beyond bounds clamps to max
        cursorX = (cursorX + 2000f).coerceIn(0f, (remoteW - 1).toFloat())
        cursorY = (cursorY + 2000f).coerceIn(0f, (remoteH - 1).toFloat())
        assertEquals(1919f, cursorX)
        assertEquals(1079f, cursorY)
    }

    @Test
    fun testCursorHotspotAlignment() {
        val canvasMouseX = 500f
        val canvasMouseY = 300f
        val hotspotX = 10
        val hotspotY = 12

        val drawLeft = canvasMouseX - hotspotX.toFloat()
        val drawTop = canvasMouseY - hotspotY.toFloat()

        assertEquals(490f, drawLeft)
        assertEquals(288f, drawTop)
    }
}
