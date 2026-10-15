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
import android.widget.TextView
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
import com.thanhvnc.client.domain.model.DiscoveredServer
import com.thanhvnc.client.domain.usecase.DiscoverServersUseCase
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
    private val discoverServersUseCase = DiscoverServersUseCase()
    private lateinit var repository: ServerProfileRepository
    private lateinit var adapter: ServerProfileAdapter

    private var activeProfileId: String? = null
    private var frameCount = 0
    private var lastFpsTimestamp = System.currentTimeMillis()
    private var currentFps = 0f

    // Modifier Key State
    private var isCtrlActive = false
    private var isAltActive = false
    private var isShiftActive = false
    private var isWinActive = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        com.thanhvnc.client.core.logging.AppLogger.init(this)
        com.thanhvnc.client.core.logging.CrashHandler.install(this)
        com.thanhvnc.client.core.logging.AppLogger.i("MainActivity created, initialized logging & crash handling")

        // Register network listener for smart Wi-Fi/Cellular handover
        val cm = getSystemService(Context.CONNECTIVITY_SERVICE) as? android.net.ConnectivityManager
        cm?.registerDefaultNetworkCallback(object : android.net.ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: android.net.Network) {
                com.thanhvnc.client.core.logging.AppLogger.i("Network available: notifying client to reconnect")
                client.notifyNetworkChanged()
            }
        })

        repository = ServerProfileRepository(this)

        setupRecyclerView()
        setupListeners()
        observeClient()
        refreshServerList()
    }

    override fun onStart() {
        super.onStart()
        discoverServersUseCase.startDiscovery()
    }

    override fun onStop() {
        super.onStop()
        discoverServersUseCase.stopDiscovery()
    }

    private fun setupRecyclerView() {
        adapter = ServerProfileAdapter(
            onConnect = { profile ->
                activeProfileId = profile.id
                connectToServer(profile.host, profile.port, profile.password, profile.useTls, profile.expectedFingerprint)
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

        // Three-finger tap gesture: toggle soft keyboard
        binding.vncSurfaceView.onThreeFingerTap = {
            binding.vncSurfaceView.requestFocus()
            val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
            imm.toggleSoftInput(InputMethodManager.SHOW_FORCED, 0)
        }

        // Cancel connection button
        binding.btnCancelConnect.setOnClickListener {
            client.disconnect()
        }

        // Floating HUD actions
        binding.btnToggleKeyboard.setOnClickListener {
            binding.vncSurfaceView.requestFocus()
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

        binding.btnResetZoom.setOnClickListener {
            binding.vncSurfaceView.resetView()
            Toast.makeText(this, "Zoom reset (Fit to screen)", Toast.LENGTH_SHORT).show()
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
            binding.overlayControlBar.visibility = View.GONE
            binding.btnExpandHudTrigger.visibility = View.VISIBLE
        }

        binding.btnExpandHudTrigger.setOnClickListener {
            binding.overlayControlBar.visibility = View.VISIBLE
            binding.btnExpandHudTrigger.visibility = View.GONE
        }

        // Setup Modifier Keys
        binding.btnKeyEsc.setOnClickListener {
            sendVirtualKeyPress(0x1B) // VK_ESCAPE
        }

        binding.btnKeyTab.setOnClickListener {
            sendVirtualKeyPress(0x09) // VK_TAB
        }

        binding.btnKeyCtrl.setOnClickListener {
            isCtrlActive = !isCtrlActive
            updateModifierButton(binding.btnKeyCtrl, isCtrlActive)
            client.sendKeyboardInput(KeyboardInput(if (isCtrlActive) KeyFlags.KEY_DOWN else KeyFlags.KEY_UP, 0x11, 0))
        }

        binding.btnKeyAlt.setOnClickListener {
            isAltActive = !isAltActive
            updateModifierButton(binding.btnKeyAlt, isAltActive)
            client.sendKeyboardInput(KeyboardInput(if (isAltActive) KeyFlags.KEY_DOWN else KeyFlags.KEY_UP, 0x12, 0))
        }

        binding.btnKeyShift.setOnClickListener {
            isShiftActive = !isShiftActive
            updateModifierButton(binding.btnKeyShift, isShiftActive)
            client.sendKeyboardInput(KeyboardInput(if (isShiftActive) KeyFlags.KEY_DOWN else KeyFlags.KEY_UP, 0x10, 0))
        }

        binding.btnKeyWin.setOnClickListener {
            isWinActive = !isWinActive
            updateModifierButton(binding.btnKeyWin, isWinActive)
            val flags = if (isWinActive) {
                (KeyFlags.KEY_DOWN.toInt() or KeyFlags.EXTENDED_KEY.toInt()).toShort()
            } else {
                (KeyFlags.KEY_UP.toInt() or KeyFlags.EXTENDED_KEY.toInt()).toShort()
            }
            client.sendKeyboardInput(KeyboardInput(flags, 0x5B, 0)) // VK_LWIN
        }

        binding.btnKeyFn.setOnClickListener { view ->
            showFnPopupMenu(view)
        }

        binding.vncSurfaceView.onMouseInput = { mouseInput ->
            client.sendMouseInput(mouseInput)
        }

        binding.vncSurfaceView.onKeyboardInput = { kbInput ->
            client.sendKeyboardInput(kbInput)
        }

        binding.vncSurfaceView.onViewportChanged = { dRect, rw, rh ->
            binding.cursorOverlayView.updateDisplayRect(dRect, rw, rh)
        }

        binding.vncSurfaceView.onLocalCursorMoved = { cx, cy ->
            binding.cursorOverlayView.updateRemotePosition(cx, cy, true)
        }
    }


    private fun sendVirtualKeyPress(vk: Short, isExtended: Boolean = false) {
        var downFlags = KeyFlags.KEY_DOWN
        var upFlags = KeyFlags.KEY_UP
        if (isExtended) {
            downFlags = (downFlags.toInt() or KeyFlags.EXTENDED_KEY.toInt()).toShort()
            upFlags = (upFlags.toInt() or KeyFlags.EXTENDED_KEY.toInt()).toShort()
        }
        client.sendKeyboardInput(KeyboardInput(downFlags, vk, 0))
        client.sendKeyboardInput(KeyboardInput(upFlags, vk, 0))
    }

    private fun updateModifierButton(view: TextView, active: Boolean) {
        if (active) {
            view.setBackgroundResource(R.drawable.bg_modifier_btn_active)
            view.setTextColor(getColor(android.R.color.white))
        } else {
            view.setBackgroundResource(R.drawable.bg_modifier_btn)
            view.setTextColor(getColor(R.color.text_primary))
        }
    }

    private fun resetModifierKeys() {
        if (isCtrlActive) {
            client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, 0x11, 0))
            isCtrlActive = false
            updateModifierButton(binding.btnKeyCtrl, false)
        }
        if (isAltActive) {
            client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, 0x12, 0))
            isAltActive = false
            updateModifierButton(binding.btnKeyAlt, false)
        }
        if (isShiftActive) {
            client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, 0x10, 0))
            isShiftActive = false
            updateModifierButton(binding.btnKeyShift, false)
        }
        if (isWinActive) {
            client.sendKeyboardInput(KeyboardInput((KeyFlags.KEY_UP.toInt() or KeyFlags.EXTENDED_KEY.toInt()).toShort(), 0x5B, 0))
            isWinActive = false
            updateModifierButton(binding.btnKeyWin, false)
        }
    }

    private fun showFnPopupMenu(anchor: View) {
        val popup = PopupMenu(this, anchor)
        popup.menu.add(0, 1, 0, "Win + D (Show Desktop)")
        popup.menu.add(0, 2, 1, "Alt + Tab (Switch Window)")
        popup.menu.add(0, 3, 2, "Ctrl + Alt + Del")
        popup.menu.add(0, 4, 3, "Enter")
        popup.menu.add(0, 5, 4, "Backspace")
        popup.menu.add(0, 6, 5, "Delete")
        popup.menu.add(0, 7, 6, "Arrow Up")
        popup.menu.add(0, 8, 7, "Arrow Down")
        popup.menu.add(0, 9, 8, "Arrow Left")
        popup.menu.add(0, 10, 9, "Arrow Right")
        popup.menu.add(0, 11, 10, "F1")
        popup.menu.add(0, 12, 11, "F2")
        popup.menu.add(0, 13, 12, "F4 (or Alt+F4)")
        popup.menu.add(0, 14, 13, "F5 (Refresh)")
        popup.menu.add(0, 15, 14, "F11 (Fullscreen)")
        popup.menu.add(0, 16, 15, "F12 (DevTools)")

        popup.setOnMenuItemClickListener { item ->
            when (item.itemId) {
                1 -> { // Win + D
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_DOWN, 0x5B, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_DOWN, 0x44, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, 0x44, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, 0x5B, 0))
                    true
                }
                2 -> { // Alt + Tab
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_DOWN, 0x12, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_DOWN, 0x09, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, 0x09, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, 0x12, 0))
                    true
                }
                3 -> { // Ctrl + Alt + Del
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_DOWN, 0x11, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_DOWN, 0x12, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_DOWN, 0x2E, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, 0x2E, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, 0x12, 0))
                    client.sendKeyboardInput(KeyboardInput(KeyFlags.KEY_UP, 0x11, 0))
                    true
                }
                4 -> { sendVirtualKeyPress(0x0D); true } // Enter
                5 -> { sendVirtualKeyPress(0x08); true } // Backspace
                6 -> { sendVirtualKeyPress(0x2E, isExtended = true); true } // Delete
                7 -> { sendVirtualKeyPress(0x26, isExtended = true); true } // Up
                8 -> { sendVirtualKeyPress(0x28, isExtended = true); true } // Down
                9 -> { sendVirtualKeyPress(0x25, isExtended = true); true } // Left
                10 -> { sendVirtualKeyPress(0x27, isExtended = true); true } // Right
                11 -> { sendVirtualKeyPress(0x70); true } // F1
                12 -> { sendVirtualKeyPress(0x71); true } // F2
                13 -> { sendVirtualKeyPress(0x73); true } // F4
                14 -> { sendVirtualKeyPress(0x74); true } // F5
                15 -> { sendVirtualKeyPress(0x7A); true } // F11
                16 -> { sendVirtualKeyPress(0x7B); true } // F12
                else -> false
            }
        }
        popup.show()
    }

    private fun connectToServer(
        host: String,
        port: Int,
        password: String,
        useTls: Boolean = false,
        expectedFingerprint: String = ""
    ) {
        binding.textConnectingHost.text = if (useTls) "$host:$port (TLS)" else "$host:$port"
        binding.layoutConnectingProgress.visibility = View.VISIBLE
        client.connect(host, port, password, useTls = useTls, expectedFingerprint = expectedFingerprint)
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
                        resetModifierKeys()
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
                        binding.overlayControlBar.visibility = View.VISIBLE
                        binding.btnExpandHudTrigger.visibility = View.GONE
                        setImmersiveFullscreen(true)

                        activeProfileId?.let { id ->
                            repository.updateLastConnected(id)
                            val p = repository.getAllProfiles().find { it.id == id }
                            if (p != null && client.activeFingerprint.isNotEmpty() && p.expectedFingerprint.isEmpty()) {
                                p.expectedFingerprint = client.activeFingerprint
                                repository.saveProfile(p)
                                com.thanhvnc.client.core.logging.AppLogger.i("Pinned TLS certificate fingerprint for ${p.name}: ${client.activeFingerprint}")
                            }
                            refreshServerList()
                        }

                        binding.cursorOverlayView.updateDisplayRect(
                            binding.vncSurfaceView.getDisplayRect(),
                            state.screenWidth,
                            state.screenHeight
                        )
                    }
                    is VncConnectionState.Reconnecting -> {
                        binding.textHudStats.text = "Reconnecting (${state.attempt}/${state.maxAttempts})..."
                    }
                    is VncConnectionState.Error -> {
                        resetModifierKeys()
                        binding.layoutConnectingProgress.visibility = View.GONE
                        Snackbar.make(binding.root, "Error: ${state.message}", Snackbar.LENGTH_LONG)
                            .setAction("Dismiss") {}
                            .show()
                    }
                }
            }
        }

        lifecycleScope.launch {
            discoverServersUseCase.servers.collect { servers ->
                renderDiscoveredServers(servers)
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

        lifecycleScope.launch {
            client.cursorPositions.collect { pos ->
                binding.cursorOverlayView.updateRemotePosition(pos.x.toFloat(), pos.y.toFloat(), pos.visible)
                binding.vncSurfaceView.setLocalCursorPosition(pos.x.toFloat(), pos.y.toFloat())
            }
        }

        lifecycleScope.launch {
            client.cursorShapes.collect { shape ->
                val bmp = android.graphics.BitmapFactory.decodeByteArray(shape.data, 0, shape.data.size)
                if (bmp != null) {
                    binding.cursorOverlayView.updateCursorShape(bmp, shape.hotspotX, shape.hotspotY)
                }
            }
        }
    }

    private fun renderDiscoveredServers(servers: List<DiscoveredServer>) {
        if (servers.isEmpty()) {
            binding.layoutDiscoveredContainer.visibility = View.GONE
            return
        }
        binding.layoutDiscoveredContainer.visibility = View.VISIBLE
        binding.textDiscoveredBadge.text = "${servers.size} Online"
        binding.layoutDiscoveredList.removeAllViews()

        for (server in servers) {
            val card = LayoutInflater.from(this).inflate(R.layout.item_server_profile, binding.layoutDiscoveredList, false)
            val textName = card.findViewById<TextView>(R.id.textProfileName)
            val textAddress = card.findViewById<TextView>(R.id.textProfileHost)
            val textBadge = card.findViewById<TextView>(R.id.textLastSeen)
            val btnMenu = card.findViewById<View>(R.id.btnProfileMenu)
            val btnConnect = card.findViewById<View>(R.id.btnConnectProfile)

            textName.text = server.name
            val tlsStr = if (server.useTls) " • TLS 1.3" else ""
            textAddress.text = "${server.host}:${server.port}$tlsStr"
            textBadge.text = "LAN Discovered"
            btnMenu.visibility = View.GONE

            btnConnect.setOnClickListener {
                binding.inputHost.setText(server.host)
                binding.inputPort.setText(server.port.toString())
                val saved = repository.getAllProfiles().firstOrNull { it.host == server.host && it.port == server.port }
                if (saved != null) {
                    activeProfileId = saved.id
                    connectToServer(saved.host, saved.port, saved.password, saved.useTls, saved.expectedFingerprint)
                } else {
                    Snackbar.make(binding.root, "Selected '${server.name}'. Enter password to connect.", Snackbar.LENGTH_SHORT).show()
                    binding.inputPassword.requestFocus()
                }
            }
            binding.layoutDiscoveredList.addView(card)
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
            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.P) {
                window.attributes.layoutInDisplayCutoutMode =
                    android.view.WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
            }
            window.addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            controller.hide(WindowInsetsCompat.Type.systemBars())
            controller.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        } else {
            WindowCompat.setDecorFitsSystemWindows(window, true)
            window.clearFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            controller.show(WindowInsetsCompat.Type.systemBars())
        }
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent?): Boolean {
        if (client.state.value is VncConnectionState.Connected) {
            val vk = mapAndroidKeyToWindowsVk(keyCode)
            if (vk != 0.toShort()) {
                val isExtended = isExtendedVirtualKey(vk)
                val flags = if (isExtended) (KeyFlags.KEY_DOWN.toInt() or KeyFlags.EXTENDED_KEY.toInt()).toShort() else KeyFlags.KEY_DOWN
                client.sendKeyboardInput(KeyboardInput(flags, vk, 0))
                return true
            } else if (event != null && event.unicodeChar > 0) {
                // Unicode character input
                val flags = (KeyFlags.KEY_DOWN.toInt() or KeyFlags.UNICODE.toInt()).toShort()
                client.sendKeyboardInput(KeyboardInput(flags, 0, event.unicodeChar.toShort()))
                return true
            }
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent?): Boolean {
        if (client.state.value is VncConnectionState.Connected) {
            val vk = mapAndroidKeyToWindowsVk(keyCode)
            if (vk != 0.toShort()) {
                val isExtended = isExtendedVirtualKey(vk)
                val flags = if (isExtended) (KeyFlags.KEY_UP.toInt() or KeyFlags.EXTENDED_KEY.toInt()).toShort() else KeyFlags.KEY_UP
                client.sendKeyboardInput(KeyboardInput(flags, vk, 0))
                return true
            } else if (event != null && event.unicodeChar > 0) {
                val flags = (KeyFlags.KEY_UP.toInt() or KeyFlags.UNICODE.toInt()).toShort()
                client.sendKeyboardInput(KeyboardInput(flags, 0, event.unicodeChar.toShort()))
                return true
            }
        }
        return super.onKeyUp(keyCode, event)
    }

    private fun isExtendedVirtualKey(vk: Short): Boolean {
        return when (vk) {
            0x21.toShort(), // Page Up
            0x22.toShort(), // Page Down
            0x23.toShort(), // End
            0x24.toShort(), // Home
            0x25.toShort(), // Left
            0x26.toShort(), // Up
            0x27.toShort(), // Right
            0x28.toShort(), // Down
            0x2D.toShort(), // Insert
            0x2E.toShort(), // Delete
            0x5B.toShort() -> true // Win Key
            else -> false
        }
    }

    private fun mapAndroidKeyToWindowsVk(keyCode: Int): Short {
        return when (keyCode) {
            KeyEvent.KEYCODE_DEL -> 0x08 // VK_BACK
            KeyEvent.KEYCODE_TAB -> 0x09 // VK_TAB
            KeyEvent.KEYCODE_ENTER -> 0x0D // VK_RETURN
            KeyEvent.KEYCODE_ESCAPE -> 0x1B // VK_ESCAPE
            KeyEvent.KEYCODE_SPACE -> 0x20 // VK_SPACE
            KeyEvent.KEYCODE_PAGE_UP -> 0x21 // VK_PRIOR
            KeyEvent.KEYCODE_PAGE_DOWN -> 0x22 // VK_NEXT
            KeyEvent.KEYCODE_MOVE_END -> 0x23 // VK_END
            KeyEvent.KEYCODE_MOVE_HOME -> 0x24 // VK_HOME
            KeyEvent.KEYCODE_DPAD_LEFT -> 0x25 // VK_LEFT
            KeyEvent.KEYCODE_DPAD_UP -> 0x26 // VK_UP
            KeyEvent.KEYCODE_DPAD_RIGHT -> 0x27 // VK_RIGHT
            KeyEvent.KEYCODE_DPAD_DOWN -> 0x28 // VK_DOWN
            KeyEvent.KEYCODE_INSERT -> 0x2D // VK_INSERT
            KeyEvent.KEYCODE_FORWARD_DEL -> 0x2E // VK_DELETE
            in KeyEvent.KEYCODE_0..KeyEvent.KEYCODE_9 -> (0x30 + (keyCode - KeyEvent.KEYCODE_0)).toShort()
            in KeyEvent.KEYCODE_A..KeyEvent.KEYCODE_Z -> (0x41 + (keyCode - KeyEvent.KEYCODE_A)).toShort()
            KeyEvent.KEYCODE_F1 -> 0x70
            KeyEvent.KEYCODE_F2 -> 0x71
            KeyEvent.KEYCODE_F3 -> 0x72
            KeyEvent.KEYCODE_F4 -> 0x73
            KeyEvent.KEYCODE_F5 -> 0x74
            KeyEvent.KEYCODE_F6 -> 0x75
            KeyEvent.KEYCODE_F7 -> 0x76
            KeyEvent.KEYCODE_F8 -> 0x77
            KeyEvent.KEYCODE_F9 -> 0x78
            KeyEvent.KEYCODE_F10 -> 0x79
            KeyEvent.KEYCODE_F11 -> 0x7A
            KeyEvent.KEYCODE_F12 -> 0x7B
            KeyEvent.KEYCODE_CTRL_LEFT, KeyEvent.KEYCODE_CTRL_RIGHT -> 0x11
            KeyEvent.KEYCODE_ALT_LEFT, KeyEvent.KEYCODE_ALT_RIGHT -> 0x12
            KeyEvent.KEYCODE_SHIFT_LEFT, KeyEvent.KEYCODE_SHIFT_RIGHT -> 0x10
            KeyEvent.KEYCODE_WINDOW -> 0x5B
            else -> 0
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        resetModifierKeys()
        client.disconnect()
    }
}
