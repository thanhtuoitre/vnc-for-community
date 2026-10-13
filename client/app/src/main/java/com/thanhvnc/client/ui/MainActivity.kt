package com.thanhvnc.client.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.os.Bundle
import android.view.KeyEvent
import android.view.LayoutInflater
import android.view.View
import android.view.inputmethod.InputMethodManager
import android.widget.PopupMenu
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.snackbar.Snackbar
import com.thanhvnc.client.R
import com.thanhvnc.client.databinding.ActivityMainBinding
import com.thanhvnc.client.databinding.DialogEditServerBinding
import com.thanhvnc.client.model.ServerProfile
import com.thanhvnc.client.model.ServerProfileRepository
import com.thanhvnc.client.network.VncClient
import com.thanhvnc.client.network.VncConnectionState
import com.thanhvnc.client.protocol.KeyFlags
import com.thanhvnc.client.protocol.KeyboardInput
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch
import java.util.Locale

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private val client = VncClient()
    private lateinit var repository: ServerProfileRepository
    private lateinit var adapter: ServerProfileAdapter

    private var activeProfileId: String? = null
    private var frameCount = 0
    private var lastFpsTimestamp = System.currentTimeMillis()
    private var currentFps = 0f

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        repository = ServerProfileRepository(this)

        setupRecyclerView()
        setupListeners()
        observeClient()
        refreshServerList()
    }

    private fun setupRecyclerView() {
        adapter = ServerProfileAdapter(
            onConnect = { profile ->
                activeProfileId = profile.id
                connectToServer(profile.host, profile.port, profile.password)
            },
            onEdit = { profile ->
                showEditServerDialog(profile)
            },
            onDelete = { profile ->
                MaterialAlertDialogBuilder(this)
                    .setTitle(R.string.delete_server)
                    .setMessage("Remove '${profile.name}' from saved desktops?")
                    .setNegativeButton(R.string.cancel, null)
                    .setPositiveButton(R.string.delete) { _, _ ->
                        repository.deleteProfile(profile.id)
                        refreshServerList()
                    }
                    .show()
            }
        )
        binding.recyclerServers.layoutManager = LinearLayoutManager(this)
        binding.recyclerServers.adapter = adapter
    }

    private fun refreshServerList() {
        val profiles = repository.getAllProfiles()
        adapter.submitList(profiles)
        binding.textServerCount.text = "${profiles.size} saved"
        binding.layoutEmptyServers.visibility = if (profiles.isEmpty()) View.VISIBLE else View.GONE
    }

    private fun setupListeners() {
        // Add Server button in toolbar
        binding.btnAddServer.setOnClickListener {
            showEditServerDialog(null)
        }

        // Quick Direct Connect
        binding.btnQuickConnect.setOnClickListener {
            val host = binding.inputHost.text.toString().trim()
            val portStr = binding.inputPort.text.toString().trim()
            val password = binding.inputPassword.text.toString().trim()

            if (host.isEmpty()) {
                Snackbar.make(binding.root, "Please enter a host address", Snackbar.LENGTH_SHORT).show()
                return@setOnClickListener
            }

            val port = portStr.toIntOrNull() ?: 5901
            activeProfileId = null
            connectToServer(host, port, password)
        }

        // Cancel connection button
        binding.btnCancelConnect.setOnClickListener {
            client.disconnect()
        }

        // Floating HUD actions
        binding.btnToggleKeyboard.setOnClickListener {
            val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
            imm.toggleSoftInput(InputMethodManager.SHOW_FORCED, 0)
        }

        binding.btnSyncClipboard.setOnClickListener {
            val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            val clipData = clipboard.primaryClip
            if (clipData != null && clipData.itemCount > 0) {
                val text = clipData.getItemAt(0).text?.toString()
                if (!text.isNullOrEmpty()) {
                    client.sendClipboardText(text)
                    Toast.makeText(this, "Sent ${text.length} chars to remote clipboard", Toast.LENGTH_SHORT).show()
                } else {
                    Toast.makeText(this, "Android clipboard is empty", Toast.LENGTH_SHORT).show()
                }
            } else {
                Toast.makeText(this, "Android clipboard is empty", Toast.LENGTH_SHORT).show()
            }
        }

        binding.btnToggleMouseMode.setOnClickListener {
            binding.vncSurfaceView.isTrackpadMode = !binding.vncSurfaceView.isTrackpadMode
            val isTrackpad = binding.vncSurfaceView.isTrackpadMode
            val modeName = if (isTrackpad) getString(R.string.mode_trackpad) else getString(R.string.mode_touch)
            Toast.makeText(this, "Switched to: $modeName", Toast.LENGTH_SHORT).show()
            binding.btnToggleMouseMode.setColorFilter(
                if (isTrackpad) getColor(R.color.primary_accent) else getColor(R.color.text_primary)
            )
        }

        binding.btnQualityMenu.setOnClickListener { view ->
            val popup = PopupMenu(this, view)
            popup.menu.add(0, 1, 0, "Fast (40% | 20 FPS)")
            popup.menu.add(0, 2, 1, "Balanced (70% | 30 FPS)")
            popup.menu.add(0, 3, 2, "High Quality (90% | 45 FPS)")
            popup.setOnMenuItemClickListener { item ->
                when (item.itemId) {
                    1 -> { client.setQuality(40.toByte(), 20.toByte()); true }
                    2 -> { client.setQuality(70.toByte(), 30.toByte()); true }
                    3 -> { client.setQuality(90.toByte(), 45.toByte()); true }
                    else -> false
                }
            }
            popup.show()
        }

        binding.btnDisconnectSession.setOnClickListener {
            MaterialAlertDialogBuilder(this)
                .setTitle(R.string.confirm_disconnect)
                .setMessage("Are you sure you want to end this remote session?")
                .setNegativeButton(R.string.cancel, null)
                .setPositiveButton(R.string.disconnect) { _, _ ->
                    client.disconnect()
                }
                .show()
        }

        binding.btnCollapseHud.setOnClickListener {
            val isVisible = binding.layoutHudActions.visibility == View.VISIBLE
            binding.layoutHudActions.visibility = if (isVisible) View.GONE else View.VISIBLE
        }

        binding.vncSurfaceView.onMouseInput = { mouseInput ->
            client.sendMouseInput(mouseInput)
        }
    }

    private fun connectToServer(host: String, port: Int, password: String) {
        binding.textConnectingHost.text = "$host:$port"
        binding.layoutConnectingProgress.visibility = View.VISIBLE
        client.connect(host, port, password)
    }

    private fun showEditServerDialog(existing: ServerProfile?) {
        val dialogBinding = DialogEditServerBinding.inflate(LayoutInflater.from(this))
        val isEdit = existing != null

        dialogBinding.dialogTitle.text = if (isEdit) getString(R.string.edit_server) else getString(R.string.add_server)
        if (existing != null) {
            dialogBinding.editServerName.setText(existing.name)
            dialogBinding.editServerHost.setText(existing.host)
            dialogBinding.editServerPort.setText(existing.port.toString())
            dialogBinding.editServerPassword.setText(existing.password)
        }

        val dialog = MaterialAlertDialogBuilder(this)
            .setView(dialogBinding.root)
            .create()

        dialogBinding.btnDialogCancel.setOnClickListener {
            dialog.dismiss()
        }

        dialogBinding.btnDialogSave.setOnClickListener {
            val name = dialogBinding.editServerName.text.toString().trim()
            val host = dialogBinding.editServerHost.text.toString().trim()
            val portStr = dialogBinding.editServerPort.text.toString().trim()
            val password = dialogBinding.editServerPassword.text.toString().trim()

            if (name.isEmpty() || host.isEmpty()) {
                Toast.makeText(this, "Please enter both name and host", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }

            val port = portStr.toIntOrNull() ?: 5901
            val profile = existing?.apply {
                this.name = name
                this.host = host
                this.port = port
                this.password = password
            } ?: ServerProfile(
                name = name,
                host = host,
                port = port,
                password = password
            )

            repository.saveProfile(profile)
            refreshServerList()
            dialog.dismiss()
        }

        dialog.show()
    }

    private fun observeClient() {
        lifecycleScope.launch {
            client.state.collectLatest { state ->
                when (state) {
                    is VncConnectionState.Disconnected -> {
                        binding.layoutConnectingProgress.visibility = View.GONE
                        binding.layoutViewer.visibility = View.GONE
                        binding.layoutDashboard.visibility = View.VISIBLE
                        setImmersiveFullscreen(false)
                        activeProfileId = null
                    }
                    is VncConnectionState.Connecting -> {
                        binding.layoutConnectingProgress.visibility = View.VISIBLE
                        binding.textConnectingStatus.text = getString(R.string.status_connecting)
                    }
                    is VncConnectionState.Handshaking -> {
                        binding.layoutConnectingProgress.visibility = View.VISIBLE
                        binding.textConnectingStatus.text = "Negotiating handshake..."
                    }
                    is VncConnectionState.Authenticating -> {
                        binding.layoutConnectingProgress.visibility = View.VISIBLE
                        binding.textConnectingStatus.text = "Authenticating..."
                    }
                    is VncConnectionState.Connected -> {
                        binding.layoutConnectingProgress.visibility = View.GONE
                        binding.layoutDashboard.visibility = View.GONE
                        binding.layoutViewer.visibility = View.VISIBLE
                        setImmersiveFullscreen(true)

                        activeProfileId?.let { id ->
                            repository.updateLastConnected(id)
                            refreshServerList()
                        }
                    }
                    is VncConnectionState.Reconnecting -> {
                        binding.textHudStats.text = "Reconnecting (${state.attempt})..."
                    }
                    is VncConnectionState.Error -> {
                        binding.layoutConnectingProgress.visibility = View.GONE
                        Snackbar.make(binding.root, "Error: ${state.message}", Snackbar.LENGTH_LONG)
                            .setAction("Dismiss") {}
                            .show()
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
                    updateStatsDisplay(currentFps, client.rttMs.value)
                    frameCount = 0
                    lastFpsTimestamp = now
                }
            }
        }

        lifecycleScope.launch {
            client.rttMs.collectLatest { rtt ->
                updateStatsDisplay(currentFps, rtt)
            }
        }

        lifecycleScope.launch {
            client.clipboardEvents.collect { text ->
                val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                val clip = ClipData.newPlainText("ThanhVNC", text)
                clipboard.setPrimaryClip(clip)
                Toast.makeText(this@MainActivity, "Received clipboard text from host", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun updateStatsDisplay(fps: Float, rttMs: Long) {
        val statsText = if (rttMs > 0) {
            String.format(Locale.US, "%.0f FPS | %d ms", fps, rttMs)
        } else {
            String.format(Locale.US, "%.0f FPS", fps)
        }
        binding.textHudStats.text = statsText
    }

    private fun setImmersiveFullscreen(fullscreen: Boolean) {
        val controller = WindowInsetsControllerCompat(window, binding.root)
        if (fullscreen) {
            WindowCompat.setDecorFitsSystemWindows(window, false)
            controller.hide(WindowInsetsCompat.Type.systemBars())
            controller.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        } else {
            WindowCompat.setDecorFitsSystemWindows(window, true)
            controller.show(WindowInsetsCompat.Type.systemBars())
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
