# VNC Project Status

## Current Goal

Build a working, low-latency Windows VNC Server and Android VNC Client.

## Current State

Milestone 1 (Windows VNC Server core engine, Protocol, Desktop Capture, Input Simulator, Session Host & Integration Tests) completed and fully verified. Milestone 2 (Android VNC Client) is next.

## Architecture Decisions

1. **Windows Server Technology Stack:**
   - Runtime: C# .NET 8 (`net8.0-windows`).
   - Screen Capture: GDI desktop capture with GDI+ JPEG compression, resolution auto-refresh, and lightweight 16x16 grid sampling hash for screen change detection (skips redundant frames when screen is static).
   - Desktop Attachment: `DesktopHelper` automatically attaches capture and input worker threads to the active interactive input desktop (`OpenInputDesktop` / `SetThreadDesktop`) to ensure flawless capture and input injection under any desktop session or runner.
   - Input Simulation: Windows native `SendInput` API for mouse movement (normalized 0..65535 coordinates), left/right/middle buttons, scroll wheel, and full keyboard key-down/up.
   - Transport: Low-latency TCP (`NoDelay = true`) with decoupled input and video streams. Frame delivery uses a bounded channel (`Capacity = 1`, `BoundedChannelFullMode.DropOldest`) to guarantee zero buffer bloat and zero stale frame latency during network congestion.
   - Security: Challenge-response authentication via 32-byte cryptographically secure random nonces and HMAC-SHA256 signatures with constant-time verification (`CryptographicOperations.FixedTimeEquals`).

2. **Android Client Technology Stack (Milestone 2):**
   - Runtime: Kotlin, Android SDK, Android Clean Architecture.
   - Rendering: SurfaceView / Hardware-accelerated Canvas with low-latency JPEG decoder pipeline.
   - Input: Touch gestures mapped to mouse moves, clicks, drag, right-click (long press), and scroll (two-finger pan).

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

## In Progress

- Milestone 2: Android VNC Client application.

## Known Problems

None.

## Tests

- Test Suite: `tests/VncServer.Tests`
- Run Command: `dotnet test server/VncServer.sln`
- Test Results: 34 passed, 0 failed, 0 skipped (duration: ~330 ms).

## Changed Files

- `.gitignore`
- `GEMINI.md`
- `PROJECT_STATUS.md`
- `server/VncServer.sln`
- `server/src/VncServer.Core/VncServer.Core.csproj`
- `server/src/VncServer.Core/Protocol/VncMessages.cs`
- `server/src/VncServer.Core/Protocol/VncProtocol.cs`
- `server/src/VncServer.Core/Capture/ScreenCapture.cs`
- `server/src/VncServer.Core/Input/InputSimulator.cs`
- `server/src/VncServer.Core/Native/DesktopHelper.cs`
- `server/src/VncServer.Core/Server/VncSession.cs`
- `server/src/VncServer.Core/Server/VncServerHost.cs`
- `server/src/VncServer.App/VncServer.App.csproj`
- `server/src/VncServer.App/Program.cs`
- `tests/VncServer.Tests/VncServer.Tests.csproj`
- `tests/VncServer.Tests/ProtocolTests.cs`
- `tests/VncServer.Tests/CaptureAndInputTests.cs`
- `tests/VncServer.Tests/AdvancedInputTests.cs`
- `tests/VncServer.Tests/ServerIntegrationTests.cs`

## Next Action

Begin Milestone 2: Initialize Android client project structure in `client/`, implement binary protocol client, TCP transport, challenge-response auth, SurfaceView screen renderer, and touch input handlers.

## Resume Instructions

At the beginning of the next session:
1. Read GEMINI.md.
2. Read this file.
3. Inspect Git status.
4. Continue from "Next Action".