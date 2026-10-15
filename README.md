# ThanhVNC — Production-Grade Remote Desktop System

[![CI Build & Test](https://github.com/thanhtuoitre/vnc-for-community/actions/workflows/ci.yml/badge.svg)](https://github.com/thanhtuoitre/vnc-for-community/actions/workflows/ci.yml)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Android-blue)
![.NET 8](https://img.shields.io/badge/.NET-8.0--windows-purple)
![Kotlin](https://img.shields.io/badge/Kotlin-2.0.20-orange)
![Security](https://img.shields.io/badge/Encryption-TLS%201.3%20%2F%20AES--256--GCM-green)
![Latency](https://img.shields.io/badge/Latency-18--35ms%20(LAN)-brightgreen)

ThanhVNC is an ultra-low-latency, production-ready remote desktop system connecting Android clients to Windows 10/11 PCs. Built without legacy RFB protocol overhead, it achieves **18–35 ms interactive latency** on standard Wi-Fi/LAN, hardware Direct3D 11 screen capture with near-zero idle CPU usage, mandatory TLS 1.3 encryption, and seamless mobile input with native Vietnamese IME support.

---

## Architecture Overview

```text
  Android Client (Kotlin / Compose / SurfaceView)
       │
       │  [Priority Channel] Mouse / Keyboard / Quick Actions (Zero-Lag)
       ├─────────────────────────────────────────────────────────────────┐
       │  [Video Channel] Bounded Drop-Oldest Frame Queue (Zero Bloat)  │
       │  [TLS 1.3 / 1.2 Channel] AES-256-GCM Encrypted Socket (Port 5900)│
       ▼                                                                 ▼
  Windows Server (.NET 8 Native Single-File Engine)
  ├── Screen Capture: Direct3D 11 / DXGI Desktop Duplication (GDI Fallback)
  ├── Cursor Engine: Decoupled 60 Hz Remote Cursor Position & Shape Tracking
  ├── Input Simulator: Win32 SendInput + Unicode Character Injection (Telex/VNI)
  ├── System Controller: Remote Lock, Reboot, Shutdown & Special Key Combinations
  ├── Network Discovery: UDP 5902 Zero-Config LAN Beacon & Probe
  └── WAN Traversal: Automatic UPnP IGD Port Forwarding & Mesh VPN Support
```

---

## Key Features

- **Direct3D 11 / DXGI Desktop Duplication**: Hardware-accelerated screen capture delivering steady 30–60 FPS with **0.0% CPU usage** when the screen is static.
- **Zero Buffer Bloat**: Video pipeline uses a bounded queue (`Capacity = 1`, `DropOldest`). Network lag never delays mouse or keyboard input.
- **Mandatory TLS 1.3 Encryption**: End-to-end encryption with auto-generated X.509 certificates and Trust-On-First-Use (TOFU) SHA-256 certificate fingerprint pinning. Plaintext connections are rejected.
- **Single-Use Session Resumption**: 16-byte cryptographic resumption tokens (60-second TTL) allow immediate reconnection (< 200 ms) across Wi-Fi ↔ 4G transitions without re-entering passwords.
- **Rapid Dead-Socket Watchdog**: 12-second socket read timeout on mobile and 15-second server inactivity timeout prevent frozen UI hangs.
- **RealVNC-Grade Mobile Input**:
  - **Direct Touch Mode**: Natural tap-to-click directly under finger.
  - **Trackpad Mode**: Precision relative cursor navigation with velocity dampening.
  - **Two-Finger Tap**: Instant native right-click with haptic feedback (< 350 ms).
  - **Two-Finger Drag**: Windows mouse wheel scrolling (120/-120 delta) or viewport panning when zoomed.
  - **Three-Finger Tap**: Toggles virtual keyboard instantly.
  - **Vietnamese IME Support**: Custom `InputConnection` emits UTF-16 code units directly injected via Win32 `KEYEVENTF_UNICODE`, guaranteeing 100% correct Telex/VNI typing in all Windows applications.
- **Quick Actions & Remote Power Control**: Floating HUD toolbar allows 1-tap Lock Workstation, Show Desktop (Win+D), Power Menu (Win+X), Task Switcher (Alt+Tab), Task Manager, and confirmed Reboot/Shutdown.
- **Instant Full-Res Screenshots**: 1-tap screenshot saves the active remote framebuffer to `Pictures/ThanhVNC` in full resolution without pausing video streaming.
- **Zero-Config LAN Discovery & UPnP WAN**: Automatic UDP 5902 LAN discovery card on Android and automatic router port forwarding via SSDP / SOAP `WANIPConnection:1`.

---

## Performance Comparison vs. RealVNC

| Metric | RealVNC Viewer / Server | ThanhVNC |
|---|---|---|
| **Input-to-Display Latency (LAN)** | 45 – 75 ms | **18 – 35 ms** |
| **Idle Desktop CPU Usage** | 2.5% – 6.0% | **~0.0% – 0.5%** |
| **Transport Encryption** | Proprietary / Paid tiers | **TLS 1.3 / AES-256-GCM (Mandatory)** |
| **Vietnamese IME (Telex / VNI)** | Garbled / Dropped diacritics | **Flawless Native Unicode Injection** |
| **Server Executable Size** | ~35 MB | **2.18 MB (Single-File Binary)** |
| **Session Resumption (< 60s drop)** | Requires manual password re-entry | **Instant (< 200 ms token resumption)** |

*For detailed benchmarks and test methodology, see [docs/REALVNC_COMPARISON.md](docs/REALVNC_COMPARISON.md).*

---

## Quick Start Guide

### 1. Windows Server Setup

#### Option A: Running from Prebuilt Release
1. Download or build `dist\windows-server\VncServer.App.exe`.
2. Launch the server from PowerShell or Command Prompt:
   ```powershell
   .\VncServer.App.exe --port 5900 --password "YourStrongPassword" --upnp
   ```
3. Command-line options:
   - `--port <number>`: TCP listening port (default: `5900`).
   - `--password <string>`: Authentication password.
   - `--tls`: Force TLS encryption (recommended, enabled by default).
   - `--upnp`: Automatically configure router port forwarding for WAN remote access.
   - `--autostart`: Register server in Windows Startup registry.
   - `--no-discovery`: Disable UDP 5902 LAN broadcast beacon.

#### Option B: Clean Automated Installation
1. Right-click `packaging\install-service.bat` and select **Run as administrator**.
2. Automatically configures Windows Defender Firewall rules for port 5900 (TCP) and 5902 (UDP) and registers auto-start on login.
3. To uninstall cleanly, run `packaging\uninstall-service.bat` as administrator.

---

### 2. Android Client Setup

1. Install `dist/ThanhVNC.apk` on your Android device (Android 8.0 / API 26 or higher).
2. Open **ThanhVNC**:
   - **On LAN**: The app automatically discovers active servers under **Discovered on LAN**. Tap the server card, enter the password, and connect.
   - **Manual / WAN**: Enter the server IP/hostname, port (default `5900`), and password, then tap **Connect**.
3. On first connection, ThanhVNC displays the server's SHA-256 TLS fingerprint. Tap **Trust & Connect** to pin the fingerprint (TOFU).

---

## Touch Gestures & Controls Cheat Sheet

| Gesture | Action |
|---|---|
| **Single Tap** | Left mouse click |
| **Double Tap** | Double left click |
| **Two-Finger Tap (< 350 ms)** | Right mouse click (with haptic feedback) |
| **Long Press (> 400 ms)** | Context menu / Right click |
| **Single Finger Drag** | Move cursor / Drag item |
| **Two-Finger Vertical Drag** | Scroll mouse wheel (1.0x zoom) or Pan viewport (zoomed) |
| **Pinch In / Out** | Zoom remote screen (1.0x to 5.0x focal zoom) |
| **Three-Finger Tap** | Toggle virtual keyboard |
| **HUD Power Icon** | Open Quick Actions (Lock, Win+D, Win+X, Alt+Tab, Taskmgr, Reboot, Shutdown) |
| **HUD Camera Icon** | Save full-resolution screenshot to `Pictures/ThanhVNC` |

---

## Developer Guide & Building from Source

### Prerequisites
- Windows 10 or 11 (64-bit)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Java 17+ (JDK) and Android SDK (API 34)

### Running Automated Test Suite
Run all 92 Windows server tests and 35 Android client tests in one command:
```cmd
cmd.exe /c scripts\test-all.bat
```

### Building Release Binaries
To build the single-file Windows Server executable and production Android APK:
```cmd
cmd.exe /c scripts\build-release.bat
```
Output files will be generated in `dist/`:
- `dist\windows-server\VncServer.App.exe` (2.18 MB)
- `dist\ThanhVNC.apk` (~6.9 MB)

---

## Project Documentation

- [docs/PROTOCOL.md](docs/PROTOCOL.md) — Comprehensive binary wire protocol specification (Framing, Header, Message Types 1–15, Little-Endian layouts, sequence diagrams).
- [docs/REALVNC_COMPARISON.md](docs/REALVNC_COMPARISON.md) — Technical performance benchmarks and feature comparisons against RealVNC Viewer/Server.
- [docs/SECURITY.md](docs/SECURITY.md) — Security threat model, TLS 1.3 cryptographic implementation, PBKDF2 hashing, rate limiting, and audit logging.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — Layered decoupling, pipeline isolation, and thread safety models.
- [docs/WAN_DEPLOYMENT.md](docs/WAN_DEPLOYMENT.md) — Remote WAN access guide, router port forwarding, UPnP, and WireGuard / Tailscale mesh VPN setups.
- [docs/ROADMAP.md](docs/ROADMAP.md) — Project milestones, engineering debt log, and future capabilities.

---

## License

This project is developed for the community under the MIT License.