package com.thanhvnc.client.ui

import android.content.Context
import android.graphics.*
import android.util.AttributeSet
import android.view.View

class CursorOverlayView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    // ponytail: CursorOverlayView uses hardware-accelerated Canvas with exact hotspot mapping; upgrade to RenderNode/AGSL shader when custom cursor glow or shadows are needed.

    private var remoteX: Float = 960f
    private var remoteY: Float = 540f
    private var isCursorVisible: Boolean = true

    private var cursorBitmap: Bitmap? = null
    private var hotspotX: Int = 0
    private var hotspotY: Int = 0

    var remoteWidth: Int = 1920
        private set
    var remoteHeight: Int = 1080
        private set
    private val displayRect = RectF()

    private val bitmapPaint = Paint(Paint.FILTER_BITMAP_FLAG or Paint.ANTI_ALIAS_FLAG)
    private val fallbackFillPaint = Paint().apply {
        color = Color.WHITE
        style = Paint.Style.FILL
        isAntiAlias = true
    }
    private val fallbackBorderPaint = Paint().apply {
        color = Color.BLACK
        style = Paint.Style.STROKE
        strokeWidth = 2.5f
        isAntiAlias = true
    }
    private val fallbackPath = Path()

    init {
        isClickable = false
        isFocusable = false
    }

    fun updateDisplayRect(dRect: RectF, rWidth: Int, rHeight: Int) {
        displayRect.set(dRect)
        if (rWidth > 0) remoteWidth = rWidth
        if (rHeight > 0) remoteHeight = rHeight
        postInvalidate()
    }

    fun updateRemotePosition(x: Float, y: Float, visible: Boolean) {
        remoteX = x
        remoteY = y
        isCursorVisible = visible
        postInvalidate()
    }

    fun updateCursorShape(bitmap: Bitmap, hX: Int, hY: Int) {
        val old = cursorBitmap
        cursorBitmap = bitmap
        hotspotX = hX.coerceIn(0, (bitmap.width - 1).coerceAtLeast(0))
        hotspotY = hY.coerceIn(0, (bitmap.height - 1).coerceAtLeast(0))
        if (old != null && old != bitmap && !old.isRecycled) {
            old.recycle()
        }
        postInvalidate()
    }

    fun getRemotePosition(): Pair<Float, Float> = Pair(remoteX, remoteY)

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)

        if (!isCursorVisible || remoteWidth <= 0 || remoteHeight <= 0 ||
            displayRect.width() <= 0f || displayRect.height() <= 0f) {
            return
        }

        // Map remote coordinate through aspect ratio, letterboxing, zoom, and pan
        val normX = (remoteX / (remoteWidth - 1).coerceAtLeast(1).toFloat()).coerceIn(0f, 1f)
        val normY = (remoteY / (remoteHeight - 1).coerceAtLeast(1).toFloat()).coerceIn(0f, 1f)
        val canvasMouseX = displayRect.left + normX * displayRect.width()
        val canvasMouseY = displayRect.top + normY * displayRect.height()

        val bmp = cursorBitmap
        if (bmp != null && !bmp.isRecycled) {
            // Real Windows cursor: align bitmap hotspot pixel exactly to canvasMouseX, canvasMouseY
            val drawLeft = canvasMouseX - hotspotX.toFloat()
            val drawTop = canvasMouseY - hotspotY.toFloat()
            canvas.drawBitmap(bmp, drawLeft, drawTop, bitmapPaint)
        } else {
            // Fallback crisp vector cursor with hotspot at (0, 0)
            drawFallbackCursor(canvas, canvasMouseX, canvasMouseY)
        }
    }

    private fun drawFallbackCursor(canvas: Canvas, cx: Float, cy: Float) {
        fallbackPath.reset()
        fallbackPath.moveTo(cx, cy)
        fallbackPath.lineTo(cx, cy + 24f)
        fallbackPath.lineTo(cx + 6f, cy + 18f)
        fallbackPath.lineTo(cx + 12f, cy + 26f)
        fallbackPath.lineTo(cx + 15f, cy + 23f)
        fallbackPath.lineTo(cx + 10f, cy + 16f)
        fallbackPath.lineTo(cx + 18f, cy + 16f)
        fallbackPath.close()

        canvas.drawPath(fallbackPath, fallbackBorderPaint)
        canvas.drawPath(fallbackPath, fallbackFillPaint)
    }

    override fun onDetachedFromWindow() {
        super.onDetachedFromWindow()
        val bmp = cursorBitmap
        if (bmp != null && !bmp.isRecycled) {
            bmp.recycle()
        }
        cursorBitmap = null
    }
}
