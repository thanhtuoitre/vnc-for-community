package com.thanhvnc.client.ui

import android.content.Context
import android.graphics.*
import android.os.SystemClock
import android.text.InputType
import android.util.AttributeSet
import android.view.*
import android.view.inputmethod.BaseInputConnection
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputConnection
import com.thanhvnc.client.protocol.KeyFlags
import com.thanhvnc.client.protocol.KeyboardInput
import com.thanhvnc.client.protocol.MouseFlags
import com.thanhvnc.client.protocol.MouseInput
import com.thanhvnc.client.protocol.ScreenFrame
import java.util.concurrent.atomic.AtomicReference
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

class VncSurfaceView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null
) : SurfaceView(context, attrs), SurfaceHolder.Callback {

    // ponytail: VncSurfaceView uses SurfaceView + lockHardwareCanvas; upgrade to MediaCodec Surface when video stream is introduced.

    private var renderThread: Thread? = null
    @Volatile private var isRunning = false

    private val latestFrame = AtomicReference<ScreenFrame?>()
    private val paint = Paint(Paint.FILTER_BITMAP_FLAG)
    private val bgPaint = Paint().apply { color = Color.BLACK }

    var remoteWidth: Int = 1920
    var remoteHeight: Int = 1080
    var onMouseInput: ((MouseInput) -> Unit)? = null
    var onKeyboardInput: ((KeyboardInput) -> Unit)? = null

    // Mouse Mode: Trackpad vs Direct Touch
    var isTrackpadMode: Boolean = false
    private var cursorX: Float = 960f
    private var cursorY: Float = 540f

    // Viewport scaling and Zoom & Pan
    var zoomScale: Float = 1.0f
        private set
    private var panOffsetX: Float = 0f
    private var panOffsetY: Float = 0f
    private var baseDstRect = RectF()

    // Cursor rendering paints
    private val cursorFillPaint = Paint().apply {
        color = Color.WHITE
        style = Paint.Style.FILL
        isAntiAlias = true
    }
    private val cursorBorderPaint = Paint().apply {
        color = Color.BLACK
        style = Paint.Style.STROKE
        strokeWidth = 2.5f
        isAntiAlias = true
    }
    private val cursorPath = Path()

    // Input Coalescing
    private var lastSentMoveMs = 0L
    private var lastSentRx = -1
    private var lastSentRy = -1

    // Drag tracking state
    private var isDragging = false
    private var isPinching = false
    private var lastTouchX = 0f
    private var lastTouchY = 0f
    private var twoFingerStartDistance = 0f
    private var twoFingerLastMidY = 0f
    private var twoFingerIsScroll = false

    private val scaleDetector: ScaleGestureDetector
    private val gestureDetector: GestureDetector

    init {
        holder.addCallback(this)
        isFocusable = true
        isFocusableInTouchMode = true

        scaleDetector = ScaleGestureDetector(context, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
            override fun onScaleBegin(detector: ScaleGestureDetector): Boolean {
                isPinching = true
                return true
            }

            override fun onScale(detector: ScaleGestureDetector): Boolean {
                val prevScale = zoomScale
                zoomScale = (zoomScale * detector.scaleFactor).coerceIn(1.0f, 5.0f)
                val scaleRatio = zoomScale / prevScale

                val focusX = detector.focusX
                val focusY = detector.focusY
                panOffsetX = focusX - (focusX - panOffsetX) * scaleRatio
                panOffsetY = focusY - (focusY - panOffsetY) * scaleRatio
                clampPan()
                return true
            }

            override fun onScaleEnd(detector: ScaleGestureDetector) {
                isPinching = false
            }
        })

        gestureDetector = GestureDetector(context, object : GestureDetector.SimpleOnGestureListener() {
            override fun onDown(e: MotionEvent): Boolean {
                return true
            }

            override fun onSingleTapConfirmed(e: MotionEvent): Boolean {
                if (isPinching) return false
                val (rx, ry) = if (isTrackpadMode) {
                    Pair(cursorX.toInt(), cursorY.toInt())
                } else {
                    mapToRemote(e.x, e.y) ?: return false
                }
                dispatchClick(rx, ry, isRight = false)
                return true
            }

            override fun onDoubleTap(e: MotionEvent): Boolean {
                if (isPinching) return false
                val (rx, ry) = if (isTrackpadMode) {
                    Pair(cursorX.toInt(), cursorY.toInt())
                } else {
                    mapToRemote(e.x, e.y) ?: return false
                }
                dispatchClick(rx, ry, isRight = false)
                dispatchClick(rx, ry, isRight = false)
                return true
            }

            override fun onLongPress(e: MotionEvent) {
                if (isPinching) return
                performHapticFeedback(HapticFeedbackConstants.LONG_PRESS)
                val (rx, ry) = if (isTrackpadMode) {
                    Pair(cursorX.toInt(), cursorY.toInt())
                } else {
                    mapToRemote(e.x, e.y) ?: return
                }
                dispatchClick(rx, ry, isRight = true)
            }

            override fun onScroll(e1: MotionEvent?, e2: MotionEvent, distanceX: Float, distanceY: Float): Boolean {
                if (isPinching) return false

                if (e2.pointerCount == 1) {
                    if (isTrackpadMode) {
                        val dRect = getDisplayRect()
                        if (dRect.width() > 0f && dRect.height() > 0f) {
                            val factor = 1.3f
                            val dx = -distanceX * (remoteWidth.toFloat() / dRect.width()) * factor
                            val dy = -distanceY * (remoteHeight.toFloat() / dRect.height()) * factor
                            cursorX = (cursorX + dx).coerceIn(0f, (remoteWidth - 1).toFloat())
                            cursorY = (cursorY + dy).coerceIn(0f, (remoteHeight - 1).toFloat())
                            coalesceMouseMove(cursorX.toInt(), cursorY.toInt())
                        }
                    } else {
                        // In Touch mode, single finger scroll moves mouse or pans if zoomed
                        val (rx, ry) = mapToRemote(e2.x, e2.y) ?: return false
                        coalesceMouseMove(rx, ry)
                    }
                    return true
                }
                return false
            }
        })
    }

    override fun onCheckIsTextEditor(): Boolean = true

    override fun onCreateInputConnection(outAttrs: EditorInfo): InputConnection {
        outAttrs.inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
        outAttrs.imeOptions = EditorInfo.IME_ACTION_NONE or EditorInfo.IME_FLAG_NO_FULLSCREEN or EditorInfo.IME_FLAG_NO_EXTRACT_UI

        return object : BaseInputConnection(this, false) {
            override fun commitText(text: CharSequence?, newCursorPosition: Int): Boolean {
                if (text.isNullOrEmpty()) return true
                for (i in 0 until text.length) {
                    val ch = text[i]
                    sendUnicodeChar(ch)
                }
                return true
            }

            override fun deleteSurroundingText(beforeLength: Int, afterLength: Int): Boolean {
                for (i in 0 until beforeLength) {
                    sendVirtualKey(0x08.toShort(), isDown = true) // VK_BACK
                    sendVirtualKey(0x08.toShort(), isDown = false)
                }
                for (i in 0 until afterLength) {
                    sendVirtualKey(0x2E.toShort(), isDown = true, isExtended = true) // VK_DELETE
                    sendVirtualKey(0x2E.toShort(), isDown = false, isExtended = true)
                }
                return true
            }

            override fun sendKeyEvent(event: KeyEvent): Boolean {
                dispatchKeyEvent(event)
                return true
            }
        }
    }

    fun sendUnicodeChar(ch: Char) {
        val scan = ch.code.toShort()
        val downFlags = (KeyFlags.KEY_DOWN.toInt() or KeyFlags.UNICODE.toInt()).toShort()
        val upFlags = (KeyFlags.KEY_UP.toInt() or KeyFlags.UNICODE.toInt()).toShort()
        onKeyboardInput?.invoke(KeyboardInput(downFlags, 0, scan))
        onKeyboardInput?.invoke(KeyboardInput(upFlags, 0, scan))
    }

    fun sendVirtualKey(vk: Short, isDown: Boolean, isExtended: Boolean = false) {
        var flags = if (isDown) KeyFlags.KEY_DOWN else KeyFlags.KEY_UP
        if (isExtended) {
            flags = (flags.toInt() or KeyFlags.EXTENDED_KEY.toInt()).toShort()
        }
        onKeyboardInput?.invoke(KeyboardInput(flags, vk, 0))
    }

    fun submitFrame(frame: ScreenFrame) {
        remoteWidth = frame.width
        remoteHeight = frame.height
        latestFrame.set(frame)
    }

    fun resetView() {
        zoomScale = 1.0f
        panOffsetX = 0f
        panOffsetY = 0f
    }

    fun getDisplayRect(): RectF {
        val currentW = baseDstRect.width() * zoomScale
        val currentH = baseDstRect.height() * zoomScale
        val left = baseDstRect.left + panOffsetX
        val top = baseDstRect.top + panOffsetY
        return RectF(left, top, left + currentW, top + currentH)
    }

    private fun clampPan() {
        if (zoomScale <= 1.0f) {
            panOffsetX = 0f
            panOffsetY = 0f
            return
        }

        val dW = baseDstRect.width() * zoomScale
        val dH = baseDstRect.height() * zoomScale

        // Ensure at least 15% of the desktop is always visible on screen
        val viewW = width.toFloat()
        val viewH = height.toFloat()

        val minPanX = viewW - baseDstRect.left - dW + (viewW * 0.15f)
        val maxPanX = -baseDstRect.left - (dW * 0.15f) + viewW

        val minPanY = viewH - baseDstRect.top - dH + (viewH * 0.15f)
        val maxPanY = -baseDstRect.top - (dH * 0.15f) + viewH

        panOffsetX = panOffsetX.coerceIn(min(minPanX, maxPanX), max(minPanX, maxPanX))
        panOffsetY = panOffsetY.coerceIn(min(minPanY, maxPanY), max(minPanY, maxPanY))
    }

    fun mapToRemote(touchX: Float, touchY: Float): Pair<Int, Int>? {
        val dRect = getDisplayRect()
        if (dRect.width() <= 0f || dRect.height() <= 0f) return null

        // Margin of 20dp around borders to allow clicking edge items comfortably
        val margin = 40f
        if (touchX < dRect.left - margin || touchX > dRect.right + margin ||
            touchY < dRect.top - margin || touchY > dRect.bottom + margin) {
            return null
        }

        val normX = ((touchX - dRect.left) / dRect.width()).coerceIn(0f, 1f)
        val normY = ((touchY - dRect.top) / dRect.height()).coerceIn(0f, 1f)

        val rx = (normX * (remoteWidth - 1)).toInt().coerceIn(0, remoteWidth - 1)
        val ry = (normY * (remoteHeight - 1)).toInt().coerceIn(0, remoteHeight - 1)
        return Pair(rx, ry)
    }

    private fun dispatchClick(rx: Int, ry: Int, isRight: Boolean) {
        val downFlag = if (isRight) MouseFlags.RIGHT_DOWN else MouseFlags.LEFT_DOWN
        val upFlag = if (isRight) MouseFlags.RIGHT_UP else MouseFlags.LEFT_UP
        onMouseInput?.invoke(MouseInput(downFlag, rx, ry, 0))
        onMouseInput?.invoke(MouseInput(upFlag, rx, ry, 0))
    }

    private fun coalesceMouseMove(rx: Int, ry: Int) {
        val now = SystemClock.uptimeMillis()
        if ((rx != lastSentRx || ry != lastSentRy) && (now - lastSentMoveMs >= 16)) {
            lastSentRx = rx
            lastSentRy = ry
            lastSentMoveMs = now
            onMouseInput?.invoke(MouseInput(MouseFlags.MOVE, rx, ry, 0))
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        scaleDetector.onTouchEvent(event)

        if (event.pointerCount == 2 && !isPinching) {
            // Two-finger handling: Pan when zoomed, or Mouse Wheel / Right click
            when (event.actionMasked) {
                MotionEvent.ACTION_POINTER_DOWN -> {
                    twoFingerLastMidY = (event.getY(0) + event.getY(1)) / 2f
                    twoFingerStartDistance = abs(event.getY(0) - event.getY(1))
                    twoFingerIsScroll = false
                }
                MotionEvent.ACTION_MOVE -> {
                    val currentMidX = (event.getX(0) + event.getX(1)) / 2f
                    val currentMidY = (event.getY(0) + event.getY(1)) / 2f
                    val deltaY = currentMidY - twoFingerLastMidY

                    if (zoomScale > 1.05f) {
                        // When zoomed, two-finger drag pans the viewport
                        panOffsetY += deltaY
                        clampPan()
                        twoFingerLastMidY = currentMidY
                    } else if (abs(deltaY) > 12f) {
                        // When not zoomed, two-finger drag scrolls remote mouse wheel
                        val (rx, ry) = if (isTrackpadMode) {
                            Pair(cursorX.toInt(), cursorY.toInt())
                        } else {
                            mapToRemote(currentMidX, currentMidY) ?: Pair(remoteWidth / 2, remoteHeight / 2)
                        }
                        val wheelDelta = if (deltaY > 0) 120.toShort() else (-120).toShort()
                        onMouseInput?.invoke(MouseInput(MouseFlags.WHEEL, rx, ry, wheelDelta))
                        twoFingerLastMidY = currentMidY
                        twoFingerIsScroll = true
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
        computeBaseDstRect(width, height)
    }

    override fun surfaceDestroyed(holder: SurfaceHolder) {
        isRunning = false
        renderThread?.interrupt()
        try {
            renderThread?.join(500)
        } catch (_: InterruptedException) {}
        renderThread = null
    }

    private fun computeBaseDstRect(viewW: Int, viewH: Int) {
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
        baseDstRect = RectF(left, top, left + dstW, top + dstH)
        clampPan()
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
                            computeBaseDstRect(canvas.width, canvas.height)
                            val dRect = getDisplayRect()

                            // Clear background with black pillar/letterbox borders
                            canvas.drawRect(0f, 0f, canvas.width.toFloat(), canvas.height.toFloat(), bgPaint)

                            // Render scaled & panned remote desktop bitmap
                            canvas.drawBitmap(bitmap, null, dRect, paint)

                            // Draw remote cursor pointer if in Trackpad mode
                            if (isTrackpadMode) {
                                drawCursor(canvas, dRect)
                            }
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

    private fun drawCursor(canvas: Canvas, dRect: RectF) {
        if (remoteWidth <= 0 || remoteHeight <= 0 || dRect.width() <= 0f) return
        val cx = dRect.left + (cursorX / (remoteWidth - 1)) * dRect.width()
        val cy = dRect.top + (cursorY / (remoteHeight - 1)) * dRect.height()

        cursorPath.reset()
        cursorPath.moveTo(cx, cy)
        cursorPath.lineTo(cx, cy + 24f)
        cursorPath.lineTo(cx + 6f, cy + 18f)
        cursorPath.lineTo(cx + 12f, cy + 26f)
        cursorPath.lineTo(cx + 15f, cy + 23f)
        cursorPath.lineTo(cx + 10f, cy + 16f)
        cursorPath.lineTo(cx + 18f, cy + 16f)
        cursorPath.close()

        canvas.drawPath(cursorPath, cursorBorderPaint)
        canvas.drawPath(cursorPath, cursorFillPaint)
    }
}
