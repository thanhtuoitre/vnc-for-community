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

## In Progress

None. Core engine, client application, resilience/adaptive quality, telemetry/WAN deployment, and remote clipboard synchronization are all fully implemented and verified.

## Known Problems

None.

## Tests

- Windows Server Tests: `dotnet test server/VncServer.sln` — 36 passed, 0 failed, 0 skipped (~332 ms).
- Android Client Tests: `gradlew test` (in `client/`) — 15 passed, 0 failed, 0 skipped (Debug + Release unit test passes).
- Android APK Assembly: `gradlew assembleDebug` (in `client/`) — BUILD SUCCESSFUL (output: `app-debug.apk`).

## Changed Files

- `PROJECT_STATUS.md`
- `server/src/VncServer.Core/Protocol/VncMessages.cs`
- `server/src/VncServer.Core/Protocol/VncProtocol.cs`
- `server/src/VncServer.Core/Input/WindowsClipboard.cs`
- `server/src/VncServer.Core/Server/VncSession.cs`
- `tests/VncServer.Tests/ProtocolTests.cs`
- `client/app/src/main/java/com/thanhvnc/client/protocol/VncMessages.kt`
- `client/app/src/main/java/com/thanhvnc/client/protocol/VncProtocol.kt`
- `client/app/src/main/java/com/thanhvnc/client/network/VncClient.kt`
- `client/app/src/main/java/com/thanhvnc/client/ui/MainActivity.kt`
- `client/app/src/main/res/layout/activity_main.xml`
- `client/app/src/test/java/com/thanhvnc/client/VncProtocolTest.kt`

## Next Action

End-to-end integration and user testing on physical hardware. Optional future extension: audio streaming support via Opus codec.

## Resume Instructions

At the beginning of the next session:
1. Read GEMINI.md.
2. Read this file.
3. Inspect Git status.
4. Continue from "Next Action".