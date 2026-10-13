# VNC Project Status

## Current Goal

Build a working, low-latency Windows VNC Server and Android VNC Client.

## Current State

- Milestone 1: Windows VNC Server core engine — COMPLETED and VERIFIED.
- Milestone 2: Android VNC Client application — COMPLETED and VERIFIED.
- Ready for end-to-end LAN/WAN operational validation and adaptive performance improvements (Milestone 3).

## Architecture Decisions

1. **Windows Server Technology Stack:**
   - Runtime: C# .NET 8 (`net8.0-windows`).
   - Screen Capture: GDI desktop capture with GDI+ JPEG compression, resolution auto-refresh, and lightweight 16x16 grid sampling hash for screen change detection (skips redundant frames when screen is static).
   - Desktop Attachment: `DesktopHelper` automatically attaches capture and input worker threads to the active interactive input desktop (`OpenInputDesktop` / `SetThreadDesktop`) to ensure flawless capture and input injection under any desktop session or runner.
   - Input Simulation: Windows native `SendInput` API for mouse movement (normalized 0..65535 coordinates), left/right/middle buttons, scroll wheel, and full keyboard key-down/up.
   - Transport: Low-latency TCP (`NoDelay = true`) with decoupled input and video streams. Frame delivery uses a bounded channel (`Capacity = 1`, `BoundedChannelFullMode.DropOldest`) to guarantee zero buffer bloat and zero stale frame latency during network congestion.
   - Security: Challenge-response authentication via 32-byte cryptographically secure random nonces and HMAC-SHA256 signatures with constant-time verification (`CryptographicOperations.FixedTimeEquals`).

2. **Android Client Technology Stack:**
   - Runtime: Kotlin 2.0.20, Android SDK 34, Android Gradle Plugin 8.7.0, Gradle 8.10.2.
   - Rendering: Dedicated background render thread with `SurfaceView` / Hardware Canvas (`lockHardwareCanvas`) and `BitmapFactory` JPEG decoding.
   - Aspect Ratio: Aspect-fit letterboxing/pillarboxing maintaining exact remote desktop proportions across portrait and landscape orientations.
   - Input: Touch gestures mapped to remote desktop coordinates:
     - Single tap: Left click (`LEFT_DOWN` + `LEFT_UP`).
     - Long press: Right click (`RIGHT_DOWN` + `RIGHT_UP`).
     - Drag: Mouse move (`MOVE`).
     - Double tap: Double click.
     - Two-finger pan: Mouse wheel (`WHEEL`, +/-120 delta).
     - Soft keyboard / hardware keys: Key down and key up mapped to Windows Virtual Key codes.
   - Network: Coroutine-based TCP client (`VncClient.kt`) with `TCP_NODELAY`, state machine, high-priority unbuffered input channel, and incoming frame flow.

3. **Wire Protocol (Contract-First):**
   - Framing: 10-byte binary header (`Magic: 0x434E5654` 'TVNC', `Version: 1`, `Type: 1 byte`, `PayloadLength: 4 bytes int32`).
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
  - Automated test suite with 34 tests covering framing, serialization, auth, capture, input simulation, special keys, and loopback TCP integration (`ProtocolTests.cs`, `CaptureAndInputTests.cs`, `AdvancedInputTests.cs`, `ServerIntegrationTests.cs`). All 34 tests passing in ~330ms.
- **Milestone 2 — Android VNC Client Application:**
  - Complete Android client project in `client/` configured with Gradle wrapper 8.10.2 and compileSdk 34.
  - Wire protocol reader/writer (`VncMessages.kt`, `VncProtocol.kt`) in Kotlin with ByteBuffer little-endian framing.
  - Challenge-response HMAC-SHA256 authentication (`HmacAuth.kt`).
  - Coroutine TCP client (`VncClient.kt`) with low-latency priority input channel and frame stream.
  - Hardware-accelerated `SurfaceView` renderer (`VncSurfaceView.kt`) with double buffering, aspect-ratio scaling, and touch gesture recognizer (tap, long press, drag, double tap, two-finger scroll).
  - Modern client UI (`MainActivity.kt`, `activity_main.xml`) with connection controls, real-time FPS/status indicators, and soft keyboard support.
  - Android unit test suite (`VncProtocolTest.kt`, `CoordinateMappingTest.kt`) executed and passed via `gradlew test`.
  - Full debug APK assembled and verified: `client/app/build/outputs/apk/debug/app-debug.apk` (5.87 MB).

## In Progress

- Milestone 3: End-to-end integration verification, connection resilience (auto-reconnect on network drop), and adaptive video quality.

## Known Problems

None.

## Tests

- Windows Server Tests: `dotnet test server/VncServer.sln` — 34 passed, 0 failed, 0 skipped (~350 ms).
- Android Client Tests: `gradlew test` (in `client/`) — 11 passed, 0 failed, 0 skipped.
- Android APK Assembly: `gradlew assembleDebug` (in `client/`) — BUILD SUCCESSFUL (output: `app-debug.apk`).

## Changed Files

- `PROJECT_STATUS.md`
- `client/settings.gradle.kts`
- `client/build.gradle.kts`
- `client/gradle.properties`
- `client/gradle/wrapper/gradle-wrapper.jar`
- `client/gradle/wrapper/gradle-wrapper.properties`
- `client/gradlew`
- `client/gradlew.bat`
- `client/app/build.gradle.kts`
- `client/app/proguard-rules.pro`
- `client/app/src/main/AndroidManifest.xml`
- `client/app/src/main/res/values/strings.xml`
- `client/app/src/main/res/values/colors.xml`
- `client/app/src/main/res/values/themes.xml`
- `client/app/src/main/res/layout/activity_main.xml`
- `client/app/src/main/java/com/thanhvnc/client/protocol/VncMessages.kt`
- `client/app/src/main/java/com/thanhvnc/client/protocol/VncProtocol.kt`
- `client/app/src/main/java/com/thanhvnc/client/protocol/HmacAuth.kt`
- `client/app/src/main/java/com/thanhvnc/client/network/VncClient.kt`
- `client/app/src/main/java/com/thanhvnc/client/ui/VncSurfaceView.kt`
- `client/app/src/main/java/com/thanhvnc/client/ui/MainActivity.kt`
- `client/app/src/test/java/com/thanhvnc/client/VncProtocolTest.kt`
- `client/app/src/test/java/com/thanhvnc/client/CoordinateMappingTest.kt`

## Next Action

Milestone 3: Implement client auto-reconnection with exponential backoff on network drop, add adaptive JPEG quality negotiation in the protocol, and run end-to-end network tests.

## Resume Instructions

At the beginning of the next session:
1. Read GEMINI.md.
2. Read this file.
3. Inspect Git status.
4. Continue from "Next Action".