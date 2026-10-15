# ThanhVNC System Architecture

## 1. System Overview

ThanhVNC is a high-performance, low-latency remote desktop system engineered for controlling Windows desktops from Android mobile devices. The architecture prioritizes responsive input processing, zero frame-bloat under network congestion, and native OS interaction.

```
+-------------------------------------------------------------------------------+
|                             Windows VNC Server                                |
|                                                                               |
|  +--------------------+   +-----------------------+   +--------------------+  |
|  | ScreenCapture (GDI)|   | CursorCapture (Win32) |   | InputSimulator     |  |
|  | + Grid Hash Dirty  |   | + ARGB / Masked icons |   | (SendInput Native) |  |
|  +---------+----------+   +-----------+-----------+   +---------^----------+  |
|            |                          |                             |         |
|            v                          v                             |         |
|  +------------------------------------------------+                 |         |
|  |             VncSession & Channel<T>            |                 |         |
|  |    (Bounded Drop-Oldest Video Queue, Cap=1)    |                 |         |
|  +------------------------+-----------------------+                 |         |
|                           |                                         |         |
|                           v                                         |         |
|  +------------------------------------------------+                 |         |
|  |       Low-Latency TCP Socket (NoDelay=true)    +-----------------+         |
+--+------------------------+-----------------------+---------------------------+
                            |
                     TCP Network Stream
                            |
+---------------------------+---------------------------------------------------+
|                           v                                                   |
|  +------------------------------------------------+                           |
|  |       VncClient Coroutines (ByteBuffer Stream) |                           |
|  +---------+--------------------------+-----------+                           |
|            |                          |                                       |
|            v                          v                                       |
|  +--------------------+   +-----------------------+   +--------------------+  |
|  | VncSurfaceView     |   | CursorOverlayView     |   | Input Engine       |  |
|  | (Hardware Canvas   |   | (Vector / Custom Icon |   | (Touchpad / Touch  |  |
|  |  + Bitmap Decode)  |   |  Zero Canvas Contend) |   |  IME Unicode)      |  |
|  +--------------------+   +-----------------------+   +--------------------+  |
|                               Android Client                                  |
+-------------------------------------------------------------------------------+
```

---

## 2. Windows Server Subsystems

### 2.1 Screen Capture Engine (`VncServer.Core/Capture`)
- **DesktopHelper**: Attaches capture and input worker threads to the active interactive input desktop via `OpenInputDesktop` and `SetThreadDesktop`. Supports DPI Awareness V2 (`SetProcessDpiAwarenessContext`) to capture full physical pixels.
- **ScreenCapture**: Captures desktop using Win32 GDI (`CreateCompatibleDC`, `BitBlt`). Monitors display geometry (`SM_XVIRTUALSCREEN`, `SM_CXVIRTUALSCREEN`) for multi-monitor virtual screen coverage.
- **Dirty Frame Detection**: Samples a 16x16 grid across the raw framebuffer. Calculates a lightweight 64-bit sampling hash; static screens bypass JPEG compression and network transmission, reducing idle CPU usage to ~1%.

### 2.2 Remote Cursor Subsystem (`CursorCapture.cs`)
- Uses Win32 `GetCursorInfo` and `GetIconInfo` to capture live pointer coordinates, visibility, hotspot, and bitmap shapes.
- Extracts 32-bit ARGB smooth alpha icons, 1-bit masked color icons, and monochrome AND/XOR cursors (I-beam, resizing, wait).
- Dispatches changes at 60 Hz via a dedicated thread to ensure immediate cursor tracking.

### 2.3 Input Simulation Engine (`InputSimulator.cs`)
- Injects native Windows input via Win32 `SendInput`.
- Normalizes mouse coordinates to the absolute virtual desktop range `[0..65535]`.
- Click and wheel events inject coordinate updates simultaneously with the button action to prevent offset clicks.
- Supports `KeyFlags.Unicode` using `KEYEVENTF_UNICODE` for full Unicode and Vietnamese IME input.

### 2.4 Session Management & Concurrency (`VncSession.cs`)
- Dual-channel pipeline:
  - Video frames flow through `Channel<ScreenFramePacket>.CreateBounded(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest })`. When bandwidth drops, obsolete frames are instantly discarded.
  - Input, clipboard, and cursor packets are read and processed asynchronously without blocking on frame transmission.
- Packet transmission is serialized via `SemaphoreSlim _sendLock` to prevent packet interleaving.

---

## 3. Android Client Subsystems

### 3.1 Network Transport (`VncClient.kt`)
- Asynchronous coroutine-driven TCP socket with separate read and write loops.
- Priority input channel sends mouse, keyboard, and clipboard events immediately.
- Heartbeat loop emits ping packets every 5 seconds; computes RTT from server echo.
- Auto-reconnect loop uses exponential backoff (1s, 2s, 4s, 8s, 10s max) upon disconnection.

### 3.2 Display & Rendering (`VncSurfaceView.kt`)
- Dedicated render thread acquires hardware canvas (`lockHardwareCanvas`) for zero-jitter rendering.
- Computes aspect-fit letterbox/pillarbox bounds.
- Supports pinch-to-zoom (1.0x to 5.0x) with focal-point tracking and two-finger pan.

### 3.3 Decoupled Cursor Overlay (`CursorOverlayView.kt`)
- Sits above `VncSurfaceView` in a `FrameLayout`.
- Renders the remote cursor shape and hotspot directly over the viewport without triggering SurfaceView redraws or JPEG decoding.
- Maps virtual desktop coordinates dynamically through zoom, pan, and letterbox matrices.

### 3.4 Input & IME System
- **Touch Mode**: Direct tap, double tap, long-press right click, two-finger scroll.
- **Trackpad Mode**: Relative pointer motion with acceleration, on-screen cursor control, tap to click.
- **InputConnection**: Custom IME implementation supporting `commitText` with Unicode flag, backspace/delete, and modifier state machine.

---

## 4. Wire Protocol Specification (`TVNC/1`)

All messages use a fixed 10-byte binary header:

| Offset | Type | Field | Description |
|---|---|---|---|
| 0 | uint32 (BE) | `Magic` | `0x434E5654` ('TVNC') |
| 4 | uint8 | `Version` | Currently `1` |
| 5 | uint8 | `Type` | Packet message ID |
| 6 | int32 (LE) | `PayloadLength`| Size of payload in bytes (Max 16 MB) |

### Packet Types:
- `1: HandshakeRequest` — Client protocol version, client ID.
- `2: HandshakeResponse` — Server protocol version, auth challenge (32-byte nonce).
- `3: AuthRequest` — HMAC-SHA256 signature of challenge nonce.
- `4: AuthResponse` — Success byte + optional error message.
- `5: ScreenFrame` — Timestamp, width, height, format, JPEG buffer.
- `6: MouseInput` — X (0..65535), Y (0..65535), buttons, wheel delta.
- `7: KeyboardInput` — KeyCode, flags (Down, Up, Unicode), char.
- `8: Heartbeat` — TimestampMs echo.
- `9: Disconnect` — Reason code and message.
- `10: QualityControl` — Target JPEG quality (10..100) and FPS (5..60).
- `11: ClipboardText` — UTF-8 text synchronization.
- `12: AudioFrame` — Timestamp, SampleRate, Channels, Codec, Data.
- `13: CursorPosition` — Remote pointer X, Y, Visibility.
- `14: CursorShape` — Width, Height, HotspotX, HotspotY, Format, PNG Data.

---

## 5. Baseline Performance & Technical Debt

### 5.1 Baseline Metrics (LAN, 1920x1080 @ 30 FPS)
- **Input-to-Display Latency**: ~20 - 45 ms (LAN Wi-Fi 5).
- **Frame Rate**: 30 FPS steady on dynamic screen updates.
- **Idle Server CPU**: 0.8% - 2.5% (Screen static, hash check active).
- **Active Server CPU**: 12% - 18% (GDI BitBlt + GDI+ JPEG compression).
- **Server RAM**: ~45 MB working set.
- **Client RAM**: ~65 MB heap/native allocations.
- **Reconnection Time**: ~1.2 s upon socket interruption.

### 5.2 Technical Debt & Upgrade Targets
1. **Screen Capture**: GDI BitBlt cannot capture hardware-accelerated games/apps without copy overhead and lacks dirty-rect metadata. → Upgrade to DXGI Desktop Duplication API with GDI fallback.
2. **Video Compression**: Full-frame JPEG sends unneeded data when only a small rect changes. → Support dirty-rect tile updates and H.264 / Tight streaming.
3. **Transport Security**: Challenge-response protects against replay/auth attacks, but transport is cleartext TCP. → Upgrade to TLS 1.3 / encrypted transport with certificate fingerprinting.
4. **Architecture Cleanliness**: Server lacks structured logging and clean service hosting; Android `MainActivity.kt` contains business logic that belongs in Domain/Data layers. → Modularize into Clean Architecture.
5. **LAN Discovery**: Server IP must be typed manually. → Add mDNS / UDP broadcast discovery.
