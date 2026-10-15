# ThanhVNC WAN & Remote Deployment Guide

This document describes how to connect the Android VNC Client to the Windows VNC Server across Local Area Networks (LAN) and Wide Area Networks (WAN).

---

## 1. Local Network (LAN) Configuration

When the Windows Server and Android Client are connected to the same Wi-Fi or local subnet:

1. **Find Server Local IP Address:**
   - Launch `VncServer.App.exe`. The console outputs all active IPv4 addresses (e.g., `192.168.1.50`).
2. **Configure Windows Defender Firewall:**
   - Allow TCP port `5901` inbound.
   - Run PowerShell as Administrator:
     ```powershell
     New-NetFirewallRule -DisplayName "ThanhVNC Server" -Direction Inbound -LocalPort 5901 -Protocol TCP -Action Allow
     ```
3. **Connect from Android Client:**
   - Enter `192.168.1.50`, Port `5901`, and the configured password.
   - Tap **Connect**.

---

## 2. Zero-Config LAN Discovery (UDP 5902)

ThanhVNC Server includes an automated LAN discovery responder:
- **UDP Port:** `5902`.
- **Mechanism:** Periodically broadcasts `THVNC_BEACON:` and responds immediately to `THVNC_PROBE` queries from clients on the subnet.
- **Android Client:** Discovers nearby Windows machines automatically on launch and displays them under the **"Discovered on LAN"** dashboard card with 1-tap connection.
- **CLI Flags:**
  - `--no-discovery`: Disables the background UDP beacon.
  - `--name <ServerName>`: Sets a custom display name broadcasted on LAN.

---

## 3. Automatic Router Port Mapping via UPnP

ThanhVNC Server features built-in Universal Plug and Play (UPnP) IGD automation:
- Automatically detects the home router's Internet Gateway Device via SSDP discovery (`239.255.255.250:1900`).
- Configures port forwarding rules over SOAP (`WANIPConnection:1` / `WANPPPConnection:1`) without requiring manual router admin login.
- Queries and prints your public external WAN IP on startup.
- **Usage:**
  ```powershell
  dotnet run --project server/src/VncServer.App -- --port 5901 --upnp
  ```
- **Cleanup:** On server shutdown, the port mapping is automatically released.

---

## 4. WAN Deployment via Manual Router Port Forwarding

If your router does not support UPnP or has UPnP disabled:

1. **Access Router Management Interface:**
   - Navigate to default gateway (e.g., `192.168.1.1`).
2. **Add Port Forwarding Rule:**
   - Service Name: `ThanhVNC`
   - Protocol: `TCP`
   - External Port: `5901` (or custom e.g., `25901`)
   - Internal IP: Windows Host LAN IP (e.g., `192.168.1.50`)
   - Internal Port: `5901`
3. **Dynamic DNS (DDNS):**
   - Configure a DDNS hostname (e.g., via DuckDNS, No-IP, or Cloudflare) to track dynamic public IP changes.
4. **Security Notice:**
   - ThanhVNC mandates TLS 1.3 / 1.2 end-to-end encryption and PBKDF2 challenge-response authentication.
   - Progressive brute-force lockout blocks malicious WAN scanners after 3 failed attempts.

---

## 3. Recommended WAN Solution: Zero-Config Mesh VPN (Tailscale / WireGuard)

For carrier-grade NAT (CGNAT, mobile LTE/5G networks, symmetric NATs):

### Why Mesh VPN is Preferred
- **No Port Forwarding Required:** Bypasses firewalls and CGNAT seamlessly.
- **End-to-End Encryption:** Direct WireGuard encrypted tunnel between Android and Windows.
- **Zero Exposure:** The VNC port is never exposed to public internet scanners.

### Setup Steps
1. Install **Tailscale** on Windows and Android from [tailscale.com](https://tailscale.com) or Google Play.
2. Sign in with the same account on both devices.
3. Note the 100.x.y.z Tailscale IP assigned to the Windows PC (e.g., `100.85.12.34`).
4. In the Android VNC Client, enter the Tailscale IP `100.85.12.34` and Port `5901`.

---

## 4. Production Relay & NAT Traversal Roadmap (STUN / TURN / WebRTC)

For a fully automated consumer product without third-party VPN apps:

```
[Android Client] ─── (TCP/UDP) ───► [STUN Server] (Discover Reflexive IP/Port)
       │                                     │
       ├─── Attempt Direct P2P Hole Punch ───┘
       │
       └─── (Fallback if Symmetric NAT) ───► [TURN Relay Server] ───► [Windows Server]
```

1. **ICE / STUN Hole Punching:**
   - Use standard STUN (RFC 5389) servers to discover public reflexive endpoints.
   - Direct hole-punching succeeds for Full-Cone, Restricted-Cone, and Port-Restricted NATs (~85% of home connections).
2. **TURN Relay Fallback:**
   - For Symmetric NATs (cellular data, enterprise firewalls), relay encrypted frames through a cloud-hosted TURN relay (e.g., Coturn).
3. **Adaptive WAN Streaming:**
   - The ThanhVNC protocol includes built-in round-trip time (RTT) telemetry via 5-second Heartbeat echoes.
   - When RTT exceeds 200 ms, the client automatically requests reduced JPEG quality (40%) and frame rate (20 FPS) to maintain smooth, responsive control without pipeline congestion.
