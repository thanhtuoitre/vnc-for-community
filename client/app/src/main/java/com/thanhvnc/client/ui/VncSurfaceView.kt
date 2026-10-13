package com.thanhvnc.client.ui

import android.content.Context
import android.graphics.*
import android.util.AttributeSet
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.SurfaceHolder
import android.view.SurfaceView
import com.thanhvnc.client.protocol.MouseFlags
import com.thanhvnc.client.protocol.MouseInput
import com.thanhvnc.client.protocol.ScreenFrame
import java.util.concurrent.atomic.AtomicReference
import kotlin.math.abs

class VncSurfaceView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null
) : SurfaceView(context, attrs), SurfaceHolder.Callback {

    // ponytail: VncSurfaceView uses Canvas + BitmapFactory; upgrade to SurfaceView + MediaCodec when H.264/HEVC is introduced.

    private var renderThread: Thread? = null
    @Volatile private var isRunning = false

    private val latestFrame = AtomicReference<ScreenFrame?>()
    private val paint = Paint(Paint.FILTER_BITMAP_FLAG)
    private val bgPaint = Paint().apply { color = Color.BLACK }

    var remoteWidth: Int = 1920
    var remoteHeight: Int = 1080
    var onMouseInput: ((MouseInput) -> Unit)? = null

    private var videoDstRect = RectF()
    private var lastTwoFingerY = 0f

    private val gestureDetector = GestureDetector(context, object : GestureDetector.SimpleOnGestureListener() {
        override fun onDown(e: MotionEvent): Boolean = true

        override fun onSingleTapConfirmed(e: MotionEvent): Boolean {
            val (rx, ry) = mapToRemote(e.x, e.y) ?: return false
            onMouseInput?.invoke(MouseInput(MouseFlags.LEFT_DOWN, rx, ry, 0))
            onMouseInput?.invoke(MouseInput(MouseFlags.LEFT_UP, rx, ry, 0))
            return true
        }

        override fun onDoubleTap(e: MotionEvent): Boolean {
            val (rx, ry) = mapToRemote(e.x, e.y) ?: return false
            // Send rapid double click sequence
            onMouseInput?.invoke(MouseInput(MouseFlags.LEFT_DOWN, rx, ry, 0))
            onMouseInput?.invoke(MouseInput(MouseFlags.LEFT_UP, rx, ry, 0))
            onMouseInput?.invoke(MouseInput(MouseFlags.LEFT_DOWN, rx, ry, 0))
            onMouseInput?.invoke(MouseInput(MouseFlags.LEFT_UP, rx, ry, 0))
            return true
        }

        override fun onLongPress(e: MotionEvent) {
            val (rx, ry) = mapToRemote(e.x, e.y) ?: return
            onMouseInput?.invoke(MouseInput(MouseFlags.RIGHT_DOWN, rx, ry, 0))
            onMouseInput?.invoke(MouseInput(MouseFlags.RIGHT_UP, rx, ry, 0))
        }

        override fun onScroll(e1: MotionEvent?, e2: MotionEvent, distanceX: Float, distanceY: Float): Boolean {
            if (e2.pointerCount == 1) {
                val (rx, ry) = mapToRemote(e2.x, e2.y) ?: return false
                onMouseInput?.invoke(MouseInput(MouseFlags.MOVE, rx, ry, 0))
                return true
            }
            return false
        }
    })

    init {
        holder.addCallback(this)
        isFocusable = true
    }

    fun submitFrame(frame: ScreenFrame) {
        remoteWidth = frame.width
        remoteHeight = frame.height
        latestFrame.set(frame)
    }

    fun mapToRemote(touchX: Float, touchY: Float): Pair<Int, Int>? {
        val rect = videoDstRect
        if (rect.width() <= 0f || rect.height() <= 0f) return null
        if (!rect.contains(touchX, touchY)) return null

        val normX = (touchX - rect.left) / rect.width()
        val normY = (touchY - rect.top) / rect.height()

        val rx = (normX * remoteWidth).toInt().coerceIn(0, remoteWidth - 1)
        val ry = (normY * remoteHeight).toInt().coerceIn(0, remoteHeight - 1)
        return Pair(rx, ry)
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (event.pointerCount == 2) {
            // Two-finger scroll handling
            when (event.actionMasked) {
                MotionEvent.ACTION_POINTER_DOWN -> {
                    lastTwoFingerY = (event.getY(0) + event.getY(1)) / 2f
                }
                MotionEvent.ACTION_MOVE -> {
                    val currentY = (event.getY(0) + event.getY(1)) / 2f
                    val deltaY = currentY - lastTwoFingerY
                    if (abs(deltaY) > 10f) {
                        val (rx, ry) = mapToRemote(event.getX(0), event.getY(0)) ?: Pair(remoteWidth / 2, remoteHeight / 2)
                        val wheelDelta = if (deltaY > 0) 120.toShort() else (-120).toShort()
                        onMouseInput?.invoke(MouseInput(MouseFlags.WHEEL, rx, ry, wheelDelta))
                        lastTwoFingerY = currentY
                    }
                }
            }
            return true
        }

        return gestureDetector.onTouchEvent(event) || super.onTouchEvent(event)
    }

    override fun surfaceCreated(holder: SurfaceHolder) {
        isRunning = true
        renderThread = Thread({ renderLoop() }, "VncRenderThread").apply { start() }
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
        computeDstRect(width, height)
    }

    override fun surfaceDestroyed(holder: SurfaceHolder) {
        isRunning = false
        renderThread?.interrupt()
        try {
            renderThread?.join(500)
        } catch (_: InterruptedException) {}
        renderThread = null
    }

    private fun computeDstRect(viewW: Int, viewH: Int) {
        if (remoteWidth <= 0 || remoteHeight <= 0 || viewW <= 0 || viewH <= 0) return

        val viewAspect = viewW.toFloat() / viewH
        val remoteAspect = remoteWidth.toFloat() / remoteHeight

        val dstW: Float
        val dstH: Float
        if (viewAspect > remoteAspect) {
            dstH = viewH.toFloat()
            dstW = dstH * remoteAspect
        } else {
            dstW = viewW.toFloat()
            dstH = dstW / remoteAspect
        }

        val left = (viewW - dstW) / 2f
        val top = (viewH - dstH) / 2f
        videoDstRect = RectF(left, top, left + dstW, top + dstH)
    }

    private fun renderLoop() {
        var lastRenderedIndex = -1L

        while (isRunning) {
            val frame = latestFrame.get()
            if (frame != null && frame.frameIndex != lastRenderedIndex) {
                lastRenderedIndex = frame.frameIndex

                val bitmap = BitmapFactory.decodeByteArray(frame.data, 0, frame.data.size)
                if (bitmap != null) {
                    var canvas: Canvas? = null
                    try {
                        canvas = if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
                            holder.lockHardwareCanvas()
                        } else {
                            holder.lockCanvas()
                        }

                        if (canvas != null) {
                            computeDstRect(canvas.width, canvas.height)
                            canvas.drawRect(0f, 0f, canvas.width.toFloat(), canvas.height.toFloat(), bgPaint)
                            canvas.drawBitmap(bitmap, null, videoDstRect, paint)
                        }
                    } finally {
                        if (canvas != null) {
                            holder.unlockCanvasAndPost(canvas)
                        }
                        bitmap.recycle()
                    }
                }
            } else {
                try {
                    Thread.sleep(8) // ~120 Hz poll
                } catch (_: InterruptedException) {
                    break
                }
            }
        }
    }
}
