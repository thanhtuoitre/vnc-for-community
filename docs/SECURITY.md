# ThanhVNC Security Architecture & Threat Model

This document outlines the security architecture, cryptographic controls, threat model, and defense mechanisms implemented in ThanhVNC.

---

## 1. Threat Model & Security Posture

ThanhVNC is designed under the assumption of a hostile network (untrusted LAN or public WAN). The system protects against:
- **Eavesdropping / Packet Sniffing**: Passive interception of screen frames, keystrokes, and clipboard data.
- **Man-In-The-Middle (MITM)**: Active network spoofing, ARP poisoning, and rogue proxy redirection.
- **Replay Attacks**: Re-transmitting recorded authentication packets to gain unauthorized access.
- **Brute-Force & Credential Stuffing**: Automated dictionary and password guessing attacks.
- **Buffer Overflow & Denial of Service (DoS)**: Maliciously crafted packets, oversized headers, and memory exhaustion.

---

## 2. End-to-End Transport Encryption

### 2.1 TLS 1.3 / 1.2 Transport
- All sessions are encrypted on the wire using standard TLS 1.3 (with fallback to TLS 1.2).
- Zero plaintext TCP mode is allowed in production; unencrypted frame or input transmission is strictly prevented.
- **Windows Server**:
  - Automatically provisions a dedicated 2048-bit RSA / ECDSA X.509 self-signed certificate stored in `%LOCALAPPDATA%\ThanhVNC\certs\server.pfx`.
  - Terminates TLS via native .NET `SslStream` (`TlsVncTransport`).
- **Android Client**:
  - Connects using `SSLSocket` with enforced protocols: `TLSv1.3` and `TLSv1.2`.

### 2.2 Certificate Fingerprint Pinning (TOFU)
- Rather than relying on untrusted public Certificate Authorities (CAs) for private local IPs, ThanhVNC implements an **SSH-style Trust-On-First-Use (TOFU)** model.
- During the TLS handshake, the Android client extracts the SHA-256 fingerprint of the server's certificate:
  `SHA256(Certificate.RawData)` formatted as `XX:XX:XX:...`.
- On first connect, the fingerprint is saved to the client's profile.
- On subsequent connections, if the fingerprint differs, the client immediately terminates the connection with a `SecurityException`, alerting the user to a potential MITM attack.

---

## 3. Authentication & Key Derivation

### 3.1 Challenge-Response HMAC-SHA256
- Prevents replay attacks and prevents plaintext passwords from ever crossing the wire.
- Workflow:
  1. Server generates a cryptographically secure 32-byte nonce via `RandomNumberGenerator`.
  2. Client receives the nonce, computes `HMAC-SHA256(challenge, password)`, and transmits the 32-byte signature.
  3. Server verifies signature in constant time using `CryptographicOperations.FixedTimeEquals` to prevent timing side-channel attacks.

### 3.2 Strong Password Storage (PBKDF2)
- Server-side password storage uses PBKDF2 with HMAC-SHA256:
  - Iterations: 100,000 rounds.
  - Salt: 16 bytes cryptographically random per user/server profile.
  - Format: `pbkdf2$100000$<saltHex>$<hashHex>`.

---

## 4. Brute-Force & DoS Mitigation

### 4.1 Progressive Lockout Escalation (`IpRateLimiter`)
- Sliding-window connection limiter caps rapid TCP connection attempts.
- Progressive lockout upon consecutive failed HMAC authentication attempts:
  - **3 failed attempts**: IP blocked for 30 seconds.
  - **5 failed attempts**: IP blocked for 5 minutes.
  - **10 failed attempts**: IP blocked for 1 hour.
- Loopback (`127.0.0.1`, `::1`) is excluded from lockout for internal testing.

### 4.2 Bounded Buffering & Zero Buffer Bloat
- Video frame queues use bounded channels with `Capacity = 1` and `BoundedChannelFullMode.DropOldest`.
- Stale frames are dropped instantly under network congestion; memory cannot grow unbounded.

### 4.3 Input Validation & Bounds Checking
- Packet header size: strictly enforced 10 bytes (`TVNC` magic + Version + Type + Length).
- Maximum message payload limit: 16 MB (`MaxPayloadSize`). Any packet specifying `Length > 16 MB` or `Length < 0` is rejected immediately.
- Clipboard text limit: 1 MB. Payloads exceeding 1 MB throw `InvalidDataException`.

---

## 5. Granular Session Permissions

The server enforces bitmask permissions (`SessionPermissions`):
- `ViewOnly` (0x01): Screen viewing only. All incoming mouse and keyboard packets are discarded at the server level.
- `InputControl` (0x02): Allows mouse clicking, mouse movement, and keyboard input injection.
- `ClipboardSync` (0x04): Allows bidirectional text synchronization.
- `FullControl` (0x0F): Default full remote desktop access.

---

## 6. Session Audit Trail

All security-sensitive operations are appended to a non-volatile audit log:
- **Location**: `%LOCALAPPDATA%\ThanhVNC\logs\audit.log`
- **Logged Events**:
  - `HANDSHAKE`: Client name, protocol version, remote IP and port.
  - `AUTH_SUCCESS`: Granted session permissions, IP address.
  - `AUTH_FAILURE`: Failed authentication attempt, IP address.
  - `LOCKOUT`: IP address throttled or temporarily blocked.
  - `DISCONNECT`: Session termination, duration, final state.
