# ThanhVNC Engineering Roadmap & Milestones

This roadmap defines the transition of ThanhVNC from an MVP into a production-grade remote desktop solution rivaling RealVNC in performance, stability, security, and usability.

---

## Milestone Execution Order

```
[M1: Architecture & Quality Foundations]
               │
               ▼
[M2: DXGI Capture & High-Performance Streaming]
               │
               ▼
[M3: End-to-End Encryption & Security Architecture]
               │
               ▼
[M4: LAN Auto-Discovery & WAN Resilience]
               │
               ▼
[M5: RealVNC-Grade Android Viewer & Touch Engine]
               │
               ▼
[M6: Advanced Remote Desktop Features]
               │
               ▼
[M7: Windows Service, Tray App & Release Packaging]
```

---

## Detailed Milestone Specifications

### M1 — Architecture & Quality Foundations
- **Server Refactoring**: Clean separation into logical layers:
  - `Capture/`: Desktop duplication & GDI fallback.
  - `Encoding/`: Codec abstractions (JPEG, Tile-based, H.264).
  - `Transport/`: Socket and stream abstraction (TLS / cleartext).
  - `Session/`: State management, rate limiting, and lifecycle.
  - `Input/`: SendInput, key state machine, and Unicode dispatch.
- **Android Architecture**: Clean Architecture refactoring:
  - `data/`: Network socket, wire protocol serialization, profile repository.
  - `domain/`: Connection state, adaptive streaming manager, input transformation.
  - `presentation/`: MainActivity, view models, custom views (SurfaceView, CursorOverlay).
- **Structured Logging & Diagnostics**:
  - Server: Microsoft.Extensions.Logging / Serilog with file rotation and log levels.
  - Android: Structured logger with session log dump capability.
- **Automated Quality Gate**:
  - Protocol fuzzing tests (malformed packets, boundary violation tests).
  - End-to-end integration test suite.
  - Local CI script for one-command test execution.

### M2 — High-Performance Capture & Streaming (Windows Server)
- **DXGI Desktop Duplication Engine**:
  - Native DirectX 11 / DXGI Desktop Duplication API implementation (`IDXGIOutputDuplication`).
  - Seamless fallback to GDI when DXGI is unavailable or fails.
  - Robust recovery from access loss (UAC prompt, display resolution change, display sleep, display topology change).
- **Dirty-Rect Updates**:
  - Extract dirty rects directly from `DXGI_OUTDUPL_FRAME_INFO`.
  - Transmit incremental rect updates rather than full-frame retransmissions.
- **Adaptive Stream Controller**:
  - Latency vs Quality presets: Low Latency, Balanced, High Quality.
  - Dynamically throttles FPS, JPEG quality, and resolution scale based on live RTT and packet drop metrics.
- **Multi-Monitor Support**:
  - Individual monitor selection or virtual desktop spanning.
- **Service & Desktop Switching**:
  - Architecture for running as a Windows Service with session helper process to display Windows Login / UAC secure desktop.

### M3 — Security & End-to-End Encryption
- **Mandatory Encrypted Transport**:
  - TLS 1.3 over TCP using SslStream on Windows and SSLEngine / SSLSocket on Android.
  - Zero plaintext transmission for frames, input, or credentials.
- **Authentication & KDF**:
  - Password hashing with PBKDF2 / Argon2id; zero plaintext password storage.
  - Progressive lockout and brute-force mitigation.
- **Server Identity Verification**:
  - SSH-style TOFU (Trust On First Use) server certificate fingerprint pinning.
  - Visual fingerprint verification to prevent MITM attacks.
- **Session Permissions & Audit**:
  - Session authorization: View-Only vs Full Control.
  - Windows notification prompt when a client connects.
  - Security audit logging (`docs/SECURITY.md`).

### M4 — LAN Discovery & WAN Resilience
- **Zero-Config LAN Discovery**:
  - UDP Broadcast / mDNS service advertisement on local network.
  - Instant discovery of online servers on the Android client dashboard.
- **Network Resilience & Smart Reconnect**:
  - Fast reconnect without losing session state or remote cursor position.
  - Network switch handling (Wi-Fi to 4G/LTE handover).
  - Dead-socket detection with rapid heartbeat timeout.
- **WAN Traversal Documentation & Relay**:
  - Complete UPnP and NAT traversal roadmap for direct WAN connectivity.

### M5 — RealVNC-Grade Android Viewer & Touch Engine
- **Touch & Gesture Perfection**:
  - High-precision coordinate transformation with full letterbox/pillarbox/zoom/pan invariance.
  - Seamless switching between Direct-Touch and Trackpad modes.
  - Complete gesture set: Tap, Double-tap, Long-press (Right click), Two-finger drag (Wheel), Pinch-to-zoom (1.0x - 5.0x).
- **Comprehensive Keyboard & Vietnamese IME**:
  - Soft keyboard, hardware keyboard, and collapsible modifier toolbar (`Ctrl`, `Alt`, `Shift`, `Win`, `Esc`, `Tab`, `Fn`).
  - Vietnamese IME (Telex/VNI) with full Unicode character injection on Windows.
- **Remote Cursor Synchronization**:
  - Live cursor shape, hotspot, and position rendering via hardware-accelerated overlay.
  - Synchronized trackpad local prediction for zero perceived lag.

### M6 — Advanced Remote Desktop Features
- **Bidirectional File Transfer**:
  - Protocol support for file transfer chunks, checksums, and resume.
- **Low-Latency Remote Audio**:
  - WASAPI loopback capture on Windows Server.
  - Low-latency PCM / Opus streaming to Android AudioTrack.
- **Session Controls**:
  - Remote Lock, Restart, and Wake-on-LAN controls.

### M7 — Windows Service, Tray App & Release Packaging
- **Windows Packaging**:
  - Standalone installer (Inno Setup / MSI).
  - Windows System Tray application (`VncServer.Tray`) for visual configuration, password setting, and connected clients viewer.
  - Windows Service integration (`VncServer.Service`).
- **Android Release**:
  - Signed release APK / AAB with Proguard/R8 optimization.
- **Documentation**:
  - Final protocol reference (`docs/PROTOCOL.md`), user manual, and benchmark comparison with RealVNC.
