package com.thanhvnc.client.ui

import android.content.Context
import android.os.Bundle
import android.view.KeyEvent
import android.view.View
import android.view.inputmethod.InputMethodManager
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.thanhvnc.client.R
import com.thanhvnc.client.databinding.ActivityMainBinding
import com.thanhvnc.client.network.VncClient
import com.thanhvnc.client.network.VncConnectionState
import com.thanhvnc.client.protocol.KeyFlags
import com.thanhvnc.client.protocol.KeyboardInput
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private val client = VncClient()

    private var frameCount = 0
    private var lastFpsTimestamp = System.currentTimeMillis()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        setupListeners()
        observeClient()
    }

    private fun setupListeners() {
        binding.btnConnect.setOnClickListener {
            val currentState = client.state.value
            if (currentState is VncConnectionState.Connected || currentState is VncConnectionState.Connecting) {
                client.disconnect()
            } else {
                val host = binding.inputHost.text.toString().trim()
                val portStr = binding.inputPort.text.toString().trim()
                val password = binding.inputPassword.text.toString().trim()

                if (host.isEmpty()) {
                    Toast.makeText(this, "Please enter server IP", Toast.LENGTH_SHORT).show()
                    return@setOnClickListener
                }

                val port = portStr.toIntOrNull() ?: 5901
                client.connect(host, port, password)
            }
        }

        binding.btnKeyboard.setOnClickListener {
            val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
            imm.toggleSoftInput(InputMethodManager.SHOW_FORCED, 0)
        }

        binding.vncSurfaceView.onMouseInput = { mouseInput ->
            client.sendMouseInput(mouseInput)
        }
    }

    private fun observeClient() {
        lifecycleScope.launch {
            client.state.collectLatest { state ->
                when (state) {
                    is VncConnectionState.Disconnected -> {
                        binding.textStatus.text = getString(R.string.status_disconnected)
                        binding.btnConnect.text = getString(R.string.connect)
                        binding.btnConnect.isEnabled = true
                    }
                    is VncConnectionState.Connecting -> {
                        binding.textStatus.text = getString(R.string.status_connecting)
                        binding.btnConnect.text = getString(R.string.disconnect)
                    }
                    is VncConnectionState.Handshaking -> {
                        binding.textStatus.text = "Handshaking..."
                    }
                    is VncConnectionState.Authenticating -> {
                        binding.textStatus.text = "Authenticating..."
                    }
                    is VncConnectionState.Connected -> {
                        binding.textStatus.text = "${getString(R.string.status_connected)} (${state.screenWidth}x${state.screenHeight})"
                        binding.btnConnect.text = getString(R.string.disconnect)
                    }
                    is VncConnectionState.Reconnecting -> {
                        binding.textStatus.text = "Reconnecting (attempt ${state.attempt})..."
                        binding.btnConnect.text = getString(R.string.disconnect)
                    }
                    is VncConnectionState.Error -> {
                        binding.textStatus.text = "Error: ${state.message}"
                        binding.btnConnect.text = getString(R.string.connect)
                    }
                }
            }
        }

        lifecycleScope.launch {
            client.frames.collect { frame ->
                binding.vncSurfaceView.submitFrame(frame)

                frameCount++
                val now = System.currentTimeMillis()
                if (now - lastFpsTimestamp >= 1000) {
                    currentFps = frameCount * 1000f / (now - lastFpsTimestamp)
                    updateStats(currentFps, client.rttMs.value)
                    frameCount = 0
                    lastFpsTimestamp = now
                }
            }
        }

        lifecycleScope.launch {
            client.rttMs.collectLatest { rtt ->
                updateStats(currentFps, rtt)
            }
        }
    }

    private var currentFps = 0f

    private fun updateStats(fps: Float, rttMs: Long) {
        binding.textStats.text = if (rttMs > 0) {
            String.format(java.util.Locale.US, "%.0f FPS | %d ms", fps, rttMs)
        } else {
            String.format(java.util.Locale.US, "%.0f FPS", fps)
        }
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent?): Boolean {
        if (client.state.value is VncConnectionState.Connected) {
            val vk = mapAndroidKeyToWindowsVk(keyCode)
            if (vk != 0.toShort()) {
                client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_DOWN, vk, 0))
                return true
            }
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent?): Boolean {
        if (client.state.value is VncConnectionState.Connected) {
            val vk = mapAndroidKeyToWindowsVk(keyCode)
            if (vk != 0.toShort()) {
                client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, vk, 0))
                return true
            }
        }
        return super.onKeyUp(keyCode, event)
    }

    private fun mapAndroidKeyToWindowsVk(keyCode: Int): Short {
        return when (keyCode) {
            KeyEvent.KEYCODE_DEL -> 0x08 // VK_BACK
            KeyEvent.KEYCODE_TAB -> 0x09 // VK_TAB
            KeyEvent.KEYCODE_ENTER -> 0x0D // VK_RETURN
            KeyEvent.KEYCODE_ESCAPE -> 0x1B // VK_ESCAPE
            KeyEvent.KEYCODE_SPACE -> 0x20 // VK_SPACE
            KeyEvent.KEYCODE_DPAD_LEFT -> 0x25 // VK_LEFT
            KeyEvent.KEYCODE_DPAD_UP -> 0x26 // VK_UP
            KeyEvent.KEYCODE_DPAD_RIGHT -> 0x27 // VK_RIGHT
            KeyEvent.KEYCODE_DPAD_DOWN -> 0x28 // VK_DOWN
            in KeyEvent.KEYCODE_A..KeyEvent.KEYCODE_Z -> (0x41 + (keyCode - KeyEvent.KEYCODE_A)).toShort()
            in KeyEvent.KEYCODE_0..KeyEvent.KEYCODE_9 -> (0x30 + (keyCode - KeyEvent.KEYCODE_0)).toShort()
            else -> 0
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        client.disconnect()
    }
}
