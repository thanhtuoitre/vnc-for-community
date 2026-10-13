# VNC Remote Desktop

A low-latency remote desktop system consisting of:

- **Windows VNC Server**
- **Android VNC Client**

The goal is to provide a responsive and reliable remote desktop experience over both LAN and WAN networks.

---

## 1. Project Goal

Build a real, usable VNC remote desktop application.

The system should allow an Android phone to remotely control a Windows PC.

Core capabilities:

- View the Windows desktop in real time
- Mouse movement
- Left/right click
- Double click
- Mouse wheel
- Keyboard input
- Connection authentication
- Encrypted communication
- Automatic reconnect
- Adaptive video quality
- Low latency
- Efficient bandwidth usage
- Stable operation over LAN and WAN

The project should prioritize a working MVP first and improve performance and reliability incrementally.

---

## 2. Components

```text
                    Network
                       │
                       │
              ┌────────▼────────┐
              │ Windows VNC     │
              │ Server          │
              │                 │
              │ Screen Capture  │
              │ Encoder         │
              │ Network         │
              │ Input Handler   │
              └────────▲────────┘
                       │
                       │
                 LAN / WAN
                       │
                       │
              ┌────────▼────────┐
              │ Android VNC     │
              │ Client          │
              │                 │
              │ Decoder         │
              │ Renderer        │
              │ Touch Input     │
              │ Keyboard Input  │
              └─────────────────┘
```

---

## 3. Main Requirements

### Windows Server

The Windows server must:

- Capture the desktop
- Detect screen changes efficiently
- Encode/compress screen data
- Send frames to connected clients
- Receive mouse input
- Receive keyboard input
- Handle multiple resolutions
- Support high-DPI displays
- Handle connection/disconnection
- Authenticate clients
- Encrypt network communication
- Recover from network interruptions

### Android Client

The Android client must:

- Connect to the Windows server
- Authenticate
- Receive and decode screen frames
- Render the desktop smoothly
- Send mouse/touch events
- Send keyboard events
- Support scrolling
- Handle orientation changes
- Handle network changes
- Automatically reconnect when appropriate

---

# 4. MVP

The first milestone is a functional end-to-end MVP.

## MVP-01 — Connection

The Android client can connect to the Windows server.

Requirements:

- Server starts successfully
- Client can enter server address
- Authentication works
- Connection state is visible
- Disconnect is handled safely

---

## MVP-02 — Remote Screen

The Android client receives the actual Windows desktop.

Requirements:

- Real Windows screen capture
- Real network transmission
- Real decoding
- Real Android rendering
- No mock frames
- No placeholder video

---

## MVP-03 — Mouse Control

Support:

- Mouse move
- Left click
- Right click
- Double click
- Mouse wheel

Input must remain responsive while video is being transmitted.

---

## MVP-04 — Keyboard Control

Support:

- Normal keyboard input
- Backspace
- Enter
- Escape
- Tab
- Arrow keys
- Modifier keys
- Common function keys

---

## MVP-05 — Reconnection

The system should recover from:

- Temporary Wi-Fi interruption
- Network timeout
- Server restart
- Client reconnect
- Temporary packet loss

---

## MVP-06 — Security

The MVP should include:

- Authentication
- Encrypted communication
- Secure credential handling
- Input validation
- Safe packet parsing

Do not implement custom cryptography.

Use established cryptographic protocols and libraries.

---

# 5. Performance Goals

Performance is a major project requirement.

The system should aim for:

- Low input latency
- Smooth screen rendering
- Minimal unnecessary bandwidth
- Low CPU usage
- Low memory usage
- No unbounded frame queues

When the network becomes congested:

```text
Input responsiveness
        >
Current frame
        >
Old/stale frames
```

Old frames should be discarded when they are no longer useful.

The system must avoid displaying very old frames after network congestion.

---

# 6. LAN and WAN

The application must work in both:

### LAN

Typical:

```text
Phone ───── Wi-Fi ───── Windows PC
```

### WAN

Typical:

```text
Android
   │
   │ Internet
   │
   ▼
Home/Office Network
   │
   ▼
Windows PC
```

WAN testing must consider:

- Higher latency
- Packet loss
- Bandwidth limitations
- Temporary disconnects
- NAT
- Reconnection

Do not assume LAN-quality network conditions.

---

# 7. Architecture Principles

Separate the system into logical components:

```text
Screen Capture
      │
      ▼
Frame Processing
      │
      ▼
Encoder
      │
      ▼
Transport
      │
      ├──────────────► Input Channel
      │
      ▼
Android Decoder
      │
      ▼
Renderer
```

Control/input traffic should not be blocked by video traffic.

Use asynchronous processing where appropriate.

Avoid unbounded queues.

Avoid unnecessary memory copies.

Measure performance before optimizing.

---

# 8. Technology Selection

Technology choices are not permanently fixed.

The implementation should evaluate technologies based on:

- Latency
- Throughput
- CPU usage
- Hardware acceleration
- Android compatibility
- Windows compatibility
- Network reliability
- Security
- Development complexity
- Long-term maintainability

Potential technologies may include:

- TCP
- UDP
- QUIC
- WebRTC
- RFB/VNC protocol
- Hardware video encoders
- Platform-native screen capture APIs

Do not choose a technology merely because it is popular.

Record important architecture decisions in project documentation.

---

# 9. Development Workflow

The project follows:

```text
Inspect
   ↓
Plan
   ↓
Implement
   ↓
Build
   ↓
Test
   ↓
Debug
   ↓
Fix
   ↓
Verify
   ↓
Checkpoint
   ↓
Next milestone
```

Every meaningful feature must be verified.

Do not mark features as complete simply because the code compiles.

---

# 10. Testing

Testing should include:

### Unit Tests

For:

- Packet parsing
- Encoding/decoding logic
- Input conversion
- Connection state
- Authentication logic

### Integration Tests

For:

- Server/client connection
- Authentication
- Frame transmission
- Input transmission
- Reconnection

### End-to-End Tests

For:

```text
Android
   ↓
Connect
   ↓
Authenticate
   ↓
Receive Windows screen
   ↓
Move mouse
   ↓
Click
   ↓
Type keyboard input
   ↓
Disconnect
   ↓
Reconnect
```

Manual testing is required when automated tests cannot reproduce real desktop behavior.

---

# 11. Security

Security must be considered from the beginning.

Never:

- Hard-code passwords
- Log passwords
- Invent encryption algorithms
- Trust arbitrary packets
- Execute arbitrary commands received from clients
- Disable security checks merely to make development easier

All network input must be validated.

Assume the network is hostile.

---

# 12. Repository Structure

The preferred structure is:

```text
thanhvnc/
│
├── .gemini/
│
├── server/
│   └── Windows VNC Server
│
├── client/
│   └── Android VNC Client
│
├── tests/
│
├── GEMINI.md
├── PROJECT_STATUS.md
└── README.md
```

The exact internal structure may change as the architecture becomes clearer.

---

# 13. Documentation

Important project documentation:

### README.md

Product overview, requirements and development information.

### GEMINI.md

Rules that Gemini must follow while developing the project.

### PROJECT_STATUS.md

Persistent project state and session checkpoint.

Important architecture decisions should be documented separately when necessary.

---

# 14. Checkpointing

This project is expected to span multiple AI sessions.

`PROJECT_STATUS.md` must be updated after every meaningful milestone.

The checkpoint should record:

- Current goal
- Current implementation state
- Completed features
- Features in progress
- Known bugs
- Architecture decisions
- Test results
- Changed files
- Exact next task

When the current AI session is approaching its context limit, save the project state before stopping.

The next session must continue from the checkpoint rather than restarting the project.

---

# 15. Definition of Done

A feature is considered complete only when:

- Implementation exists
- Code builds
- Relevant tests pass
- Runtime behavior is verified
- Error handling exists
- Security implications are considered
- Performance implications are considered
- PROJECT_STATUS.md is updated

A mock implementation is not considered complete.

A UI prototype is not considered complete.

A server that only starts is not considered a VNC server.

A client that only displays fake frames is not considered a VNC client.

---

# 16. Current Status

See:

```text
PROJECT_STATUS.md
```

for the current implementation state and next development task.

---

# 17. Development Philosophy

The project prioritizes:

1. Working software
2. Correctness
3. Low latency
4. Reliability
5. Security
6. Performance
7. Maintainability

Avoid unnecessary complexity.

Build the smallest working system first, verify it, then improve it.