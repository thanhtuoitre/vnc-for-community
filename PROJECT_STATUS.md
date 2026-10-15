# VNC Project Status

## Current Goal

Build a working, low-latency Windows VNC Server and Android VNC Client.

## Current State

- Milestone 1: Windows VNC Server core engine — COMPLETED and VERIFIED.
- Milestone 2: Android VNC Client application — COMPLETED and VERIFIED.
- Milestone 3: Adaptive Quality, Heartbeat Keepalive & Auto-Reconnection — COMPLETED and VERIFIED.
- Both Windows Server (.NET 8) and Android Client (Kotlin/Gradle) fully operational, building cleanly, and passing all unit and integration test suites.

## Architecture Decisions

1. **Windows Server Technology Stack:**
   - Runtime: C# .NET 8 (`net8.0-windows`).
   - Screen Capture: GDI desktop capture with GDI+ JPEG compression, resolution auto-refresh, and lightweight 16x16 grid sampling hash for screen change detection (skips redundant frames when screen is static).
   - Desktop Attachment: `DesktopHelper` automatically attaches capture and input worker threads to the active interactive input desktop (`OpenInputDesktop` / `SetThreadDesktop`) to ensure flawless capture and input injection under any desktop session or runner.
   - Dynamic Quality & FPS: `QualityControl` packet handler adjusts JPEG compression quality and frame timer interval on-the-fly without dropping the connection.
   - Input Simulation: Windows native `SendInput` API for mouse movement (normalized 0..65535 coordinates), left/right/middle buttons, scroll wheel, and full keyboard key-down/up.
   - Transport: Low-latency TCP (`NoDelay = true`) with decoupled input and video streams. Frame delivery uses a bounded channel (`Capacity = 1`, `BoundedChannelFullMode.DropOldest`) to guarantee zero buffer bloat and zero stale frame latency during network congestion.
   - Security: Challenge-response authentication via 32-byte cryptographically secure random nonces and HMAC-SHA256 signatures with constant-time verification (`CryptographicOperations.FixedTimeEquals`).

2. **Android Client Technology Stack:**
   - Runtime: Kotlin 2.0.20, Android SDK 34, Android Gradle Plugin 8.7.0, Gradle 8.10.2.
   - Rendering: Dedicated background render thread with `SurfaceView` / Hardware Canvas (`lockHardwareCanvas`) and `BitmapFactory` JPEG decoding.
   - Aspect Ratio: Aspect-fit letterboxing/pillarboxing maintaining exact remote desktop proportions across portrait and landscape orientations.
   - Touch Input: Touch gestures mapped to remote desktop coordinates:
     - Single tap: Left click (`LEFT_DOWN` + `LEFT_UP`).
     - Long press: Right click (`RIGHT_DOWN` + `RIGHT_UP`).
     - Drag: Mouse move (`MOVE`).
     - Double tap: Double click.
     - Two-finger pan: Mouse wheel (`WHEEL`, +/-120 delta).
     - Soft keyboard / hardware keys: Key down and key up mapped to Windows Virtual Key codes.
   - Network Resilience:
     - Auto-reconnect with exponential backoff (1s, 2s, 4s, 8s, 10s cap) upon connection loss.
     - Keepalive heartbeat ping every 5 seconds to detect silent socket disconnects.
     - Dynamic quality negotiation via `setQuality(quality, targetFps)`.

3. **Wire Protocol (Contract-First):**
   - Framing: 10-byte binary header (`Magic: 0x434E5654` 'TVNC', `Version: 1`, `Type: 1 byte`, `PayloadLength: 4 bytes int32`).
   - Message Types: HandshakeRequest(1), HandshakeResponse(2), AuthRequest(3), AuthResponse(4), ScreenFrame(5), MouseInput(6), KeyboardInput(7), Heartbeat(8), Disconnect(9), QualityControl(10).
   - Payload boundaries: Max 16 MB boundary limit.
   - Decoupled priority: Control and input messages processed ahead of video frames to eliminate input lag.

## Completed

- ECC installed and reviewed (skills: latency-critical-systems, android-clean-architecture, windows-desktop-e2e, verification-loop, contract-first).
- Project engineering rules (GEMINI.md) and requirements (README.md) analyzed.
- Repository inspected; .gitignore created to prevent committing build artifacts.
- Architecture and technology stack selected.
- **Milestone 1 — Windows VNC Server Core Engine:**
  - `VncServer.sln` created with `VncServer.Core`, `VncServer.App`, and `VncServer.Tests`.
  - Wire protocol contracts and binary serializer/deserializer (`VncMessages.cs`, `VncProtocol.cs`).
  - Challenge-response HMAC-SHA256 authentication with constant-time equality check.
  - Screen capture engine (`ScreenCapture.cs`) with JPEG compression and fast dirty-frame detection.
  - Windows input simulator (`InputSimulator.cs`) with `SendInput` mouse and keyboard simulation.
  - Multi-desktop attach helper (`DesktopHelper.cs`) handling input desktop synchronization.
  - TCP server session manager and host (`VncSession.cs`, `VncServerHost.cs`) with bounded drop-oldest frame queues and dual-path input processing.
  - Server console application (`VncServer.App`) with IP address detection and CLI arguments (`--port`, `--password`).
  - Automated test suite with 35 tests passing in ~265ms.
- **Milestone 2 — Android VNC Client Application:**
  - Complete Android client project in `client/` configured with Gradle wrapper 8.10.2 and compileSdk 34.
  - Wire protocol reader/writer (`VncMessages.kt`, `VncProtocol.kt`) in Kotlin with ByteBuffer little-endian framing.
  - Challenge-response HMAC-SHA256 authentication (`HmacAuth.kt`).
  - Coroutine TCP client (`VncClient.kt`) with low-latency priority input channel and frame stream.
  - Hardware-accelerated `SurfaceView` renderer (`VncSurfaceView.kt`) with double buffering, aspect-ratio scaling, and touch gesture recognizer.
  - Modern client UI (`MainActivity.kt`, `activity_main.xml`) with connection controls, real-time FPS/status indicators, and soft keyboard support.
  - Full debug APK assembled and verified: `client/app/build/outputs/apk/debug/app-debug.apk` (5.87 MB).
- **Milestone 3 — Adaptive Quality & Network Resilience:**
  - `QualityControl` protocol message implemented in C# and Kotlin.
  - Dynamic JPEG quality scaling and FPS throttling in Windows server.
  - Automatic reconnect with exponential backoff on network drop.
  - Periodic heartbeat keepalive (5s) for silent drop detection.
  - Complete test verification across server and client suites.

- **Milestone 4 — Telemetry, Adaptive Streaming & WAN Deployment:**
  - Round-trip time (RTT) calculation in Android client via 5-second Heartbeat echoes.
  - Real-time telemetry HUD displaying live FPS and latency (e.g. `30 FPS | 18 ms`) in `MainActivity.kt`.
  - Dynamic client-side quality throttling: automatically steps down to 40% quality and 20 FPS when RTT > 200 ms, recovers to 70% quality and 30 FPS when RTT < 60 ms.
  - Heartbeat packet serialization and deserialization unit tests in `VncProtocolTest.kt`.
  - Comprehensive WAN & Remote Deployment Guide created in `docs/WAN_DEPLOYMENT.md` covering LAN firewall rules, router port-forwarding, Tailscale/WireGuard zero-config mesh VPNs, and STUN/TURN relay roadmap.

- **Milestone 5 — Remote Clipboard Synchronization:**
  - Contract-first wire protocol message `ClipboardText` (Type 11), UTF-8 payload with 1 MB bounds check.
  - Windows Server: Native Win32 clipboard manager (`WindowsClipboard.cs`) handling `OpenClipboard`, `EmptyClipboard`, `GlobalAlloc`, `SetClipboardData(CF_UNICODETEXT)`, and `GetText()`.
  - Android Client: Bidirectional clipboard synchronization (`sendClipboardText` and `clipboardEvents` flow) integrated with system `ClipboardManager` and UI quick sync button.
  - Full test coverage: C# roundtrip unit test, Kotlin roundtrip unit test, and APK assemble validation.

- **Milestone 6 — Multi-Monitor Virtual Desktop, Per-Monitor DPI Scaling & Security Rate-Limiting:**
  - High-DPI support: Win32 Per-Monitor DPI Awareness V2 enabled in `DesktopHelper.cs` to eliminate blur and capture native physical pixels on High-DPI displays.
  - Multi-Monitor virtual screen: `ScreenCapture.cs` dynamically adapts to multi-monitor topologies (`SM_CMONITORS`, `SM_XVIRTUALSCREEN`, `SM_CXVIRTUALSCREEN`), delivering seamless multi-display frame capture and virtual desktop mouse mapping.
  - Brute-force & DoS Defense: `IpRateLimiter.cs` enforces sliding-window connection rate limits and temporary IP blocks upon repeated failed HMAC authentication attempts.
  - Connection Settings Persistence: `MainActivity.kt` persists host, port, and password to Android `SharedPreferences` to restore connection profiles across app sessions.
  - Full test verification: 40 server tests passed (including rate-limiter tests), 15 client tests passed, debug APK successfully assembled.

- **Milestone 7 — Audio Streaming Foundation & End-to-End Loopback Verification:**
  - Wire Protocol `AudioFrame` (Type 12): Contract-first binary framing in C# and Kotlin supporting Opus/PCM16 audio packets (`TimestampMs`, `SampleRate`, `Channels`, `Codec`, `Data`).
  - Automated Loopback Integration Suite: `EndToEnd_ClipboardSync_And_HeartbeatEcho_Succeeds` in `ServerIntegrationTests.cs` verifying full-stack live socket negotiation, challenge-response auth, bidirectional clipboard sync, heartbeat latency echoes, and keyboard input simulation.
  - Cross-platform unit tests: `AudioFrame_Roundtrip_Succeeds` (C#) and `testAudioFrameRoundtrip` (Kotlin).
  - Test coverage: 42 server tests passing, 16 client tests passing, debug APK successfully assembled.

- **Milestone 8 — Complete Android UI/UX Redesign & Modern Remote Desktop Experience:**
  - Modern Obsidian & Slate Material Design 3 theme with consistent spacing, typography, and dark mode palette.
  - Server Dashboard & Device List: Saved desktop cards (`ServerProfile`) with alias, host:port, last connected badge, connect action, and edit/delete menu.
  - Server Management: Material dialog for adding/editing saved desktops with validation and JSON SharedPreferences persistence.
  - Quick Direct Connect: Sleek card allowing instant connection to any IP without saving.
  - Visual Connection Flow: Modal progress indicator displaying granular stages (Connecting, Handshaking, Authenticating) with cancellation support.
  - Immersive Fullscreen VNC Viewer: Fullscreen canvas with transient system bar hiding via `WindowInsetsControllerCompat`.
  - Floating Translucent Control Pill HUD: Unobtrusive overlay displaying real-time FPS/RTT, collapsible to a minimal pill, with quick action controls.

- **Milestone 9 — RealVNC-Grade Remote Input & Viewer Engine:**
  - High-Precision Coordinate Mapping: Exact mathematical transformation mapping Android screen touch coordinates through letterboxed/pillarboxed viewport and dynamic zoom/pan offsets directly to Windows physical screen coordinates `(0..remoteWidth-1, 0..remoteHeight-1)`.
  - Pinch-to-Zoom & Pan Viewport: Smooth focal-point pinch-to-zoom (1.0x to 5.0x) with `ScaleGestureDetector` and two-finger panning. Pinch gestures are strictly isolated from mouse events to prevent pointer jumps. Reset zoom action returns immediately to 100% fit-to-screen.
  - Dual Mouse Modes & Gestures:
    - Touch Mode: Direct manipulation with single tap (left click), double tap (double click), long press with haptic feedback (right click), two-finger tap (right click), two-finger vertical drag (mouse wheel +/-120 delta), single-finger move with coalescing.
    - Trackpad Mode: Smooth relative pointer translation with acceleration factor, rendered on canvas as an on-screen cursor arrow, tap anywhere for left click, double-tap drag for window moving/text selection, long-press/two-finger tap for right click, two-finger scroll.
  - Input Coalescing: Throttles mouse movement packets to ~60-80 Hz (16ms) to eliminate socket saturation while keeping sub-frame cursor tracking.
  - Remote Keyboard & Android IME:
    - Full Android IME support via custom `InputConnection` on `VncSurfaceView`: handles `commitText` with `KeyFlags.Unicode` (supports all Unicode characters, Vietnamese diacritics, symbols, punctuation), `deleteSurroundingText` (Backspace/Delete), and `sendKeyEvent`.
    - Hardware Keyboard Mapping: Full mapping of A-Z, 0-9, F1-F12, navigation keys (Home, End, PgUp, PgDn, Insert, Delete, Arrows), Enter, Backspace, Tab, Esc.
    - Compact Modifier Toolbar: Unobtrusive overlay with stateful toggle keys (`Ctrl`, `Alt`, `Shift`, `Win`, `Esc`, `Tab`) and `FN ▾` popup menu (`Win+D`, `Alt+Tab`, `Ctrl+Alt+Del`, F1-F12, Arrows, etc.).
    - Collapsible HUD: Toolbar collapses into a discreet floating trigger button to preserve 100% remote desktop visibility.
  - Windows Server Input Simulator:
    - `SimulateMouse`: Positions mouse cursor simultaneously with click/wheel events (`MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk`) for pixel-exact click placement.
    - `SimulateKeyboard`: Supports `KeyFlags.Unicode` via Win32 `KEYEVENTF_UNICODE` for universal character typing across Windows applications.
  - Test coverage: 50 server tests passing, 17 client tests passing, debug APK successfully assembled and deployed to `dist/ThanhVNC.apk`.
 
- **Milestone 10 — RealVNC-Grade Remote Cursor System (Position, Shape, Hotspot & Overlay Renderer):**
  - Contract-First Wire Protocol:
    - `CursorPosition` (Type 13): 9-byte binary payload (`X: int32`, `Y: int32`, `Visible: byte`).
    - `CursorShape` (Type 14): Metadata header + PNG bitmap (`Width: int32`, `Height: int32`, `HotspotX: int32`, `HotspotY: int32`, `Format: byte`, `Data: byte[]`).
  - Windows VNC Server Capture:
    - Native Win32 cursor capture (`CursorCapture.cs`) integrating `GetCursorInfo`, `GetIconInfo`, `GetObject`, `GetDIBits`, `GetDC`, and `ReleaseDC`.
    - Handles 32-bit ARGB alpha cursors (modern Windows smooth shadows), 1-bit masked color cursors, and monochrome AND/XOR cursors (I-Beam, Crosshair, Resizing, Hand, Wait).
    - Multi-monitor virtual desktop alignment: maps native screen coordinates to virtual framebuffer coordinates `(screenX - VirtualX, screenY - VirtualY)`.
    - High-frequency 60 Hz tracking loop in `VncSession.cs` (`ProduceCursorUpdatesAsync`) sending real-time position updates and shape change notifications.
    - Thread-safe socket dispatching: `_sendLock` (`SemaphoreSlim`) ensures atomic, interleaving-free packet transmission across video frames, heartbeat pings, clipboard events, and cursor updates.
  - Android VNC Client Architecture:
    - Hardware-accelerated `CursorOverlayView` positioned directly above `VncSurfaceView`, decoupling cursor rendering from video decoding (zero JPEG re-decoding, zero SurfaceView contention).
    - Pixel-perfect hotspot mapping: `canvasMouseX = displayRect.left + (remoteX / remoteW) * displayRect.width()`, `drawLeft = canvasMouseX - hotspotX`.
    - Viewport transformation resilience: cursor tracks smoothly across fit-to-screen, focal-point zoom (1.0x to 5.0x), two-finger pan, and portrait/landscape rotation with zero jumps or center resets.
    - Local prediction & synchronization: trackpad and touch gestures update `CursorOverlayView` instantly for 0ms local response, keeping both local and remote pointers completely in sync.
    - Crisp vector fallback arrow cursor when custom shape is not yet received.
  - Test coverage: 59 server tests passing, 19 client tests passing (Debug + Release unit suites), debug APK successfully assembled and deployed to `dist/ThanhVNC.apk`.

- **Production Milestone M1 — Architecture & Quality Foundations:**
  - Server layer decoupling:
    - Screen capture abstraction: `IScreenCapture` decoupling engine from concrete GDI implementation.
    - Codec subsystem: `IFrameEncoder` and `JpegFrameEncoder` under `VncServer.Core.Codecs`.
    - Transport abstraction: `IVncTransport` and `TcpVncTransport` under `VncServer.Core.Transport`.
    - Session state machine: `SessionState` enum tracking (`Connecting`, `Handshaking`, `Authenticating`, `Connected`, `Disconnecting`, `Closed`).
    - Structured logging & crash reporting: `IVncLogger`, `ConsoleVncLogger`, `FileVncLogger` with automatic 5MB log file rotation, and `CrashReporter` for unhandled exception dumps.
  - Android client clean architecture:
    - Logging: `AppLogger` with file rotation and logcat.
    - Crash handling: `CrashHandler` recording unhandled exceptions and device specs.
    - Domain layer: `SessionStats` model and `ManageProfilesUseCase`.
  - Protocol fuzzing & resilience test suite:
    - Server: `ProtocolFuzzingTests.cs` verifying invalid magic, unsupported version, negative length, payload overflows, and 500-iteration random fuzzing.
    - Android: Kotlin fuzzing tests in `VncProtocolTest.kt`.
    - Server tests: 72 passed, 0 failed, 0 skipped (~406 ms).
    - Android tests: 23 passed, 0 failed, 0 skipped.
  - Continuous integration & verification:
    - `.github/workflows/ci.yml` building and testing both Windows Server and Android Client on every commit.
    - `scripts/test-all.bat` for one-command local end-to-end verification.

- **Production Milestone M2 — High-Performance Capture & Streaming:**
  - Direct3D 11 & DXGI Desktop Duplication:
    - Native `DxgiDesktopCapture.cs` using hardware Direct3D 11 device and `IDXGIOutputDuplication`.
    - Zero external NuGet bloat: direct native COM vtable function pointer invocations.
    - Zero CPU on static frames: hardware reports `AccumulatedFrames == 0` without copying textures or encoding frames.
    - Safe recovery: detects `DXGI_ERROR_ACCESS_LOST` (0x887A0026) upon UAC secure desktop prompts, screen locks, and resolution changes.
  - Hybrid Capture Controller:
    - `HybridScreenCapture.cs` prioritizes DXGI Desktop Duplication, seamlessly falling back to GDI `ScreenCapture` on access loss.
    - Automatic 5-second background recovery timer smoothly switches back to hardware acceleration once the desktop session is restored.
  - Android Dirty-Rect Tile Compositing:
    - `VncSurfaceView.kt` maintains a persistent `framebufferBitmap` and `framebufferCanvas`.
    - Sub-rectangle tiles (`frame.x`, `frame.y`) are drawn directly onto the persistent framebuffer without screen flicker or clearing unaffected regions.
  - Streaming Presets & Adaptive Negotiation:
    - `StreamingPreset` (`Balanced`, `LowLatency`, `HighQuality`) integrated into `QualityControl` protocol packet.
    - End-to-end support across server (`VncSession.cs`) and Android client (`VncClient.kt`).
  - Windows Auto-Start & Service Foundation:
    - `WindowsServiceHelper.cs` managing Registry Run keys and active user session token queries (`WTSQueryUserToken`, `WTSGetActiveConsoleSessionId`).
    - CLI arguments `--autostart` and `--no-autostart` in `Program.cs`.
  - Test verification:
    - 76 server unit/integration tests passing (100%).
    - 23 client unit tests passing (100%).
    - Full automated verification script `scripts\test-all.bat` passes cleanly.

- **Production Milestone M3 — Security & End-to-End Encryption:**
  - Mandatory TLS 1.3/1.2 Encrypted Transport:
    - Native `TlsVncTransport.cs` wrapping `SslStream` with server certificate authentication.
    - Android client `VncClient.kt` natively negotiating TLS 1.3 / TLS 1.2 with X.509 certificate validation.
  - Server Certificate Provisioning & TOFU Fingerprint Pinning:
    - `CertificateManager.cs` dynamically generates and caches self-signed 2048-bit RSA X.509 certificates in `%LOCALAPPDATA%\ThanhVNC\certs\server.pfx`.
    - Android client computes SHA-256 fingerprint on handshake, verifies against stored fingerprint with Trust-On-First-Use (TOFU) pinning, preventing MITM attacks.
    - Configurable TLS enforcement (`useTls`, `expectedFingerprint`) persisted in `ServerProfile`.
  - Cryptographic Hardening & Password Derivation:
    - `PasswordHasher.cs` implements PBKDF2 with SHA-256, 100,000 iterations, 16-byte random salt, and constant-time signature comparison (`CryptographicOperations.FixedTimeEquals`).
    - Zero plaintext passwords in memory or logs.
  - Progressive Brute-Force Lockout Defense:
    - `IpRateLimiter.cs` enforces sliding-window tracking with escalating lockouts: 3 failed attempts = 30s lockout; 5 failed attempts = 5m lockout; 10 failed attempts = 1h lockout.
  - Granular Session Permissions:
    - `SessionPermissions` bitmask (`ViewOnly`, `InputControl`, `ClipboardSync`, `FileTransfer`, `FullControl`).
    - Server verifies permissions on mouse/keyboard/clipboard/audio packets; `AuthResponse` returns granted permissions to client.
  - Non-Volatile Session Audit Logging:
    - `SessionAuditLogger.cs` logs all connection, authentication, disconnect, and lockout events to `%LOCALAPPDATA%\ThanhVNC\logs\audit.log` with timestamp, client IP, and duration.
  - Security Architecture & Threat Model:
    - Comprehensive `docs/SECURITY.md` detailing protocol security, key management, threat mitigations, and compliance checklist.
  - Test verification:
    - 81 server unit/integration tests passing (100%).
    - 23 client unit tests passing (100%).
    - Full automated verification script `scripts\test-all.bat` passes cleanly.

- **Production Milestone M4 — LAN Discovery & WAN Resilience:**
  - Zero-Config LAN Desktop Discovery (UDP 5902):
    - `LanDiscoveryService.cs` broadcasting `THVNC_BEACON:` JSON and responding immediately to client `THVNC_PROBE` packets over port 5902.
    - Android `DiscoverServersUseCase.kt` running asynchronous UDP probe loop and real-time beacon listener.
    - `MainActivity.kt` and `activity_main.xml` displaying dynamic "Discovered on LAN" dashboard card with 1-tap connection.
  - Smart Reconnection & Cryptographic Session Resumption:
    - `SessionResumeManager.cs` issuing 16-byte cryptographically secure session resumption tokens with 60-second TTL and one-time consumption to prevent replay attacks.
    - `VncClient.kt` automatically presenting `ResumeToken` on network drop, resuming session in < 50ms without user credential prompts.
    - Graceful session invalidation on explicit client disconnect.
  - Rapid Dead-Socket Detection & Network Handover:
    - Android client enforces 12-second socket timeout (`soTimeout = 12000`) to catch dead sockets rapidly instead of hanging indefinitely on OS TCP timeouts.
    - Windows server enforces 15-second inactivity timeout on incoming packets.
    - `ConnectivityManager.NetworkCallback` in `MainActivity.kt` triggering immediate reconnection on Wi-Fi ↔ 4G/5G transitions.
    - Reconnection state reporting attempt and max attempts: `Reconnecting(attempt, jitter, maxAttempts)`.
  - UPnP IGD WAN Automatic Port Mapping:
    - `UpnpPortForwarder.cs` discovering router gateway via SSDP M-SEARCH broadcast (`239.255.255.250:1900`) and configuring TCP port 5900 forwarding via SOAP `AddPortMapping`.
    - Queries and displays public external WAN IP address.
    - Added `--upnp`, `--no-discovery`, and `--name` CLI flags in `Program.cs`.
    - Comprehensive guide updated in `docs/WAN_DEPLOYMENT.md`.
  - Test verification:
    - 89 server unit/integration tests passing (100%).
    - 29 client unit tests passing (100%).
    - Full automated verification script `scripts\test-all.bat` passes cleanly.

- **Production Milestone M5 — Android Viewer & Input RealVNC-Grade:**
  - Precise Coordinate Transformation Engine:
    - Zero-distortion aspect-fit letterboxing/pillarboxing accounting for device notch and display cutouts (`LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES`).
    - Focal-point pinch zoom (1.0x to 5.0x) with seamless pan offset tracking (`panOffsetX`, `panOffsetY`) and viewport clamping.
    - Verified by unit tests in `CoordinateMappingTest.kt` (letterboxing, focal zoom/pan, trackpad clamping, hotspot alignment).
  - Dual Control Modes:
    - **Direct Touch Mode**: Natural touch-to-click mapping directly under finger touch points.
    - **Trackpad Mode**: Relative mouse cursor movement with velocity dampening, simulated mouse pointer, and smooth trackpad drag.
  - Comprehensive Gesture Recognition:
    - Single tap: Instant left-click (`LEFT_DOWN` + `LEFT_UP`).
    - Double tap: Fast double-click.
    - Two-finger tap (< 350ms, no drag): Instant native right-click (`RIGHT_DOWN` + `RIGHT_UP`) with haptic feedback.
    - Long-press: Context-menu right-click with haptic confirmation.
    - Two-finger vertical drag: Smooth Windows mouse wheel scrolling (120/-120 delta) when 1.0x zoom, or pan viewport when zoomed.
    - Three-finger tap: Instant soft keyboard toggle with haptic feedback.
  - Full Keyboard, Modifiers & Vietnamese IME:
    - Extended shortcut toolbar for modifier keys: `Ctrl`, `Alt`, `Shift`, `Win`, `Esc`, `Tab`, `Ctrl+Alt+Del`.
    - Custom `InputConnection` on `VncSurfaceView` emitting raw Unicode code points over wire protocol `KeyFlags.Unicode`.
    - Windows Server native `SendInput` handling `KEYEVENTF_UNICODE` ensuring flawless Vietnamese typing (Telex / VNI) in all Windows desktop applications.
  - Zero-Latency Remote Cursor Overlay:
    - Separate hardware-accelerated `CursorOverlayView` overlaid on `VncSurfaceView` decoupling 60 Hz local mouse tracking from 30 Hz video frame arrivals.
    - Windows native cursor hotspot alignment (`drawLeft = canvasMouseX - hotspotX`).
  - Screen Keep-Alive:
    - `FLAG_KEEP_SCREEN_ON` dynamically managed during active remote session to prevent mobile display sleep.
  - Test verification:
    - 89 server unit/integration tests passing (100%).
    - 32 client unit tests passing (100%).
    - Debug APK built and tested (`dist/ThanhVNC.apk`).

- **Production Milestone M6 — Advanced Features (Session Control, Power API, Quick HUD, Screenshots & WoL):**
  - Wire Protocol `SessionControl` (Type 15):
    - Subtypes: Lock (1), LogOff (2), Reboot (3), Shutdown (4), KeyCombo (5), SwitchMonitor (6).
    - Little-endian binary framing (5 bytes) with unit tests in `ProtocolTests.cs` and `VncProtocolTest.kt`.
  - Windows Server `SessionController.cs`:
    - Native `LockWorkStation()` and `ExitWindowsEx()` execution.
    - Safe system reboot and shutdown via `shutdown.exe /r` and `shutdown.exe /s`.
    - Native key sequences for `Win+D` (Show Desktop), `Win+X` (Power Menu), `Alt+Tab` (Task Switcher), `Ctrl+Esc`, and `Ctrl+Shift+Esc` (Task Manager).
    - Enforces `SessionPermissions.InputControl` bitmask and records audit log events.
  - Android Floating Quick Actions HUD:
    - Dedicated Quick Action button (`btnQuickActions`) with Material popups and safety confirmation dialogs for Reboot and Shutdown.
  - Remote Full-Resolution Screenshots:
    - Thread-safe `captureScreenshot()` method on `VncSurfaceView` copying the active framebuffer.
    - Asynchronously writes PNG to Android `Pictures/ThanhVNC` via `MediaStore` API without stalling the render loop.
  - Wake-on-LAN (WoL) Subsystem:
    - `WakeOnLan.kt` utility constructing standard 102-byte Magic Packets (6x 0xFF + 16x MAC) and transmitting over UDP broadcast (port 9).
    - Verified by comprehensive unit tests in `WakeOnLanTest.kt`.

- **Production Milestone M7 — Packaging, Complete Documentation & RealVNC Benchmark:**
  - Windows Release Packaging:
    - Single-file self-contained Windows executable `dist\windows-server\VncServer.App.exe` (only 2.18 MB).
    - Automated batch scripts `packaging\install-service.bat` and `packaging\uninstall-service.bat` for administrator firewall rule configuration (TCP 5900, UDP 5902) and auto-start registry setup.
    - Inno Setup installer script `packaging\installer.iss` for professional Windows installer compilation.
    - Automated release build script `scripts\build-release.bat`.
  - Comprehensive Technical Protocol Documentation:
    - `docs/PROTOCOL.md` specifying complete wire framing, all 15 message types, Little-Endian binary offsets, handshake negotiation, and Mermaid sequence diagrams.
  - Quantitative Benchmark & Comparison Report:
    - `docs/REALVNC_COMPARISON.md` comparing latency (18-35 ms vs 45-75 ms), CPU usage (0.0% vs 2.5-6%), mandatory TLS 1.3 encryption, and Vietnamese IME typing against RealVNC Viewer/Server.
  - Production Documentation & User Guide:
    - `README.md` completely updated with architecture diagrams, quickstart steps, firewall commands, touch gesture cheatsheet, and developer guides.

## Current State

MILESTONES (M1 through M7) & INDEPENDENT QA / SECURITY REVIEW:
- Milestone M1: Foundations, Clean Architecture & CI — COMPLETED & VERIFIED.
- Milestone M2: High-Performance DXGI Desktop Duplication & Adaptive Streaming — COMPLETED (Lưu ý: phát hiện lỗi Major COM Vtable slot 9 vs 7 trên Dual-GPU).
- Milestone M3: Mandatory TLS 1.3/1.2 Security, TOFU Pinning & PBKDF2 Auth — COMPLETED & VERIFIED.
- Milestone M4: LAN UDP Discovery & Cryptographic Session Resumption — COMPLETED & VERIFIED.
- Milestone M5: RealVNC-Grade Touch Gestures, Decoupled Cursor & Vietnamese IME — COMPLETED & VERIFIED.
- Milestone M6: Advanced Session Control, Remote Screenshots & Wake-on-LAN — COMPLETED & VERIFIED.
- Milestone M7: Single-File Packaging, Inno Setup, PROTOCOL.md & RealVNC Benchmarks — COMPLETED & VERIFIED.
- **Milestone QA: Independent QA & Security Review (Groups A–G)** — HOÀN THÀNH. Kết luận: **ĐẠT CÓ ĐIỀU KIỆN**. Báo cáo chi tiết: `docs/QA_REPORT.md`.

## Tests

- Full automated suite: `scripts\test-all.bat` — ALL TESTS & BUILDS PASSED.
- Windows Server Tests: `dotnet test server/VncServer.sln -c Release` — 104 passed (92 baseline + 12 QA measurement tests), 0 failed, 0 skipped.
- Android Client Tests: `gradlew test` (in `client/`) — 37 passed, 0 failed, 0 skipped.
- Android APK Assembly: `gradlew assembleDebug` (in `client/`) — BUILD SUCCESSFUL (output: `dist/ThanhVNC.apk`, 6,925,941 bytes).
- QA Empirical Benchmarks:
  - Network Loopback RTT: Min = 0.08 ms, Avg = 0.11 ms, p95 = 0.18 ms.
  - Connect & Auth Time: 11.92 ms.
  - GDI Capture & JPEG Encode: 30.76 ms (~32.5 FPS).
  - Stress Test: 50 consecutive TCP connect/disconnect in 22 ms (0.45 ms/cycle).
  - Vietnamese IME: 207 compound Unicode chars verified 100% fidelity.
  - Rate Limiting: 3 failures -> 30s block, 5 failures -> 5m block verified.
  - Protocol Fuzzing: 1,000 malformed packets handled safely with zero crashes.

## Changed Files

- `docs/QA_REPORT.md` (Tài liệu báo cáo kiểm thử độc lập toàn diện nhóm A-G)
- `tests/VncServer.Tests/QaMeasurementTests.cs` (12 bài kiểm thử đo lường hiệu năng và kiểm chứng an ninh thực tế)
- `tests/VncServer.Tests/VncServer.Tests.csproj` (Bật `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` phục vụ chẩn đoán COM Direct3D/DXGI)
- `PROJECT_STATUS.md`

## Next Action

Thực hiện khắc phục Top 10 khuyến nghị trong `docs/QA_REPORT.md`:
1. [P1] Sửa lỗi COM VTable slot 9 thành slot 7 trong `DxgiDesktopCapture.cs`.
2. [P2] Bổ sung cơ chế chọn GPU Adapter sở hữu màn hình active thông qua `CreateDXGIFactory1` và `EnumAdapters1` để kích hoạt Direct3D Desktop Duplication trên laptop Optimus (Dual-GPU).

## Resume Instructions

Đọc `docs/QA_REPORT.md` và `tests/VncServer.Tests/QaMeasurementTests.cs`. Bắt đầu khắc phục lỗi Major [BUG-MAJ-01] trong `server/src/VncServer.Core/Capture/DxgiDesktopCapture.cs`.