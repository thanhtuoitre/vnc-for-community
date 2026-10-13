# VNC Project — Gemini Engineering Rules

## 1. Project Mission

Build a production-oriented VNC remote desktop system with:

- Windows VNC Server
- Android VNC Client
- Real-time remote screen viewing
- Mouse control
- Keyboard control
- Mouse wheel / scrolling
- Clipboard support where practical
- Authentication
- Encrypted communication
- Automatic reconnect
- Adaptive streaming
- Low latency
- Good performance on LAN and WAN
- Stable operation over unreliable networks

The project must be developed toward a working MVP first, then improved incrementally.

Do NOT build a fake/demo-only UI or a skeleton that does not actually perform the core VNC functions.

---

# 2. Core Development Principle

Always prefer:

WORKING MVP → VERIFY → IMPROVE → VERIFY AGAIN

Never:

DESIGN EVERYTHING → WRITE HUGE AMOUNT OF CODE → HOPE IT WORKS

Every meaningful milestone must produce something buildable and testable.

---

# 3. Before Doing Any Work

At the beginning of every session:

1. Read this GEMINI.md.
2. Read PROJECT_STATUS.md.
3. Inspect the current repository structure.
4. Check Git status.
5. Inspect recent changes/commits when relevant.
6. Determine what is already implemented.
7. Identify the smallest next useful milestone.

Do not assume that a feature is unfinished just because it is listed in an old plan.

The actual source code is the source of truth.

---

# 4. Project Structure

Preferred high-level structure:

    thanhvnc/
    ├── .gemini/
    ├── server/
    │   └── Windows VNC Server
    ├── client/
    │   └── Android VNC Client
    ├── tests/
    ├── PROJECT_STATUS.md
    ├── README.md
    └── GEMINI.md

The exact technology stack may be changed when engineering evidence shows that another approach is significantly better.

Do not force a technology merely because it was mentioned in an early plan.

---

# 5. Architecture Requirements

The architecture must prioritize:

- Low latency
- Stable frame delivery
- Efficient CPU usage
- Efficient memory usage
- Network resilience
- Input responsiveness
- Secure communication
- Maintainability
- Easy debugging
- Incremental implementation

Separate these logical concerns:

1. Screen capture
2. Frame processing
3. Encoding/compression
4. Network transport
5. Authentication/security
6. Input processing
7. Client rendering
8. Connection/reconnection
9. Performance monitoring

Avoid unnecessary coupling between these components.

---

# 6. VNC MVP Priority

Implement features in approximately this order unless technical evidence suggests a better sequence:

### Phase 1 — Connection

- Windows server starts successfully.
- Android client can discover/connect to server.
- Authentication works.
- Connection failures are reported clearly.

### Phase 2 — Screen

- Capture Windows desktop.
- Encode/compress frames.
- Send frames to Android.
- Decode and render on Android.
- Maintain usable frame rate.
- Avoid unnecessary full-screen frame transmission.

### Phase 3 — Input

Implement:

- Mouse move
- Left click
- Right click
- Double click
- Mouse wheel
- Keyboard input
- Special keys

Input must be responsive and must not block video processing.

### Phase 4 — Reliability

Implement:

- Connection timeout
- Disconnect detection
- Automatic reconnect
- Session recovery where appropriate
- Graceful server shutdown
- Graceful client shutdown

### Phase 5 — Performance

Improve:

- Frame rate
- Encoding efficiency
- Network bandwidth
- CPU usage
- Memory usage
- Input latency
- Frame latency

Use measurements instead of guessing.

### Phase 6 — Security

Implement:

- Authentication
- Encrypted transport
- Secure credential handling
- Replay/session protection where appropriate
- Safe handling of malformed packets
- Rate limiting where appropriate

Never invent cryptography.

Use established cryptographic libraries/protocols.

---

# 7. Low-Latency Rules

Latency is a first-class requirement.

Prefer:

- Asynchronous I/O
- Separate input and video paths
- Small control messages
- Frame dropping when necessary
- Avoiding stale frames
- Dirty-region or changed-region updates when practical
- Adaptive quality
- Adaptive bitrate
- Efficient encoding
- Backpressure
- Bounded queues

Do NOT allow an overloaded video pipeline to make mouse/keyboard input feel frozen.

If the network becomes congested:

INPUT PRIORITY > CURRENT FRAME > OLD FRAME

Never build an unbounded frame queue.

Old video frames should be discarded when they become stale.

---

# 8. LAN and WAN

The system must work on:

- Local LAN
- Typical home networks
- Higher-latency WAN connections

Do not assume LAN conditions.

Test with:

- High latency
- Packet loss
- Bandwidth limitation
- Temporary disconnect
- Reconnection
- Server restart

If direct WAN connectivity is unreliable, investigate appropriate NAT traversal/relay architecture rather than assuming port forwarding will always work.

---

# 9. Transport Decisions

Evaluate transport technologies based on measured requirements.

Possible technologies include:

- TCP
- UDP-based protocols
- QUIC
- WebRTC
- Existing VNC/RFB-compatible transport
- Other appropriate protocols

Do not choose a protocol simply because it is fashionable.

Document important architecture decisions in:

    PROJECT_STATUS.md

or an ADR when the decision is significant.

---

# 10. Security Rules

Security is mandatory.

Never:

- Hard-code production passwords
- Store plaintext credentials unnecessarily
- Invent encryption algorithms
- Disable certificate/security verification merely to make tests pass
- Trust arbitrary network input
- Execute commands received from the client unless explicitly required and secured
- Log passwords or authentication tokens

Validate:

- Packet lengths
- Message types
- Input ranges
- Frame metadata
- Authentication state
- Session state

Assume the network is hostile.

---

# 11. Windows Server Rules

The Windows server must behave like a real desktop application/service.

Consider:

- Windows DPI scaling
- Multiple monitors
- Different resolutions
- Display changes
- Screen locking
- Session changes
- Sleep/resume
- Network changes
- Firewall configuration
- Windows permissions
- High-DPI environments

Do not assume one fixed resolution or one monitor.

---

# 12. Android Client Rules

The Android client must:

- Handle different screen sizes
- Handle portrait/landscape
- Avoid unnecessary allocations
- Avoid UI-thread blocking
- Handle reconnects
- Maintain responsive touch/input
- Render frames efficiently
- Handle lifecycle events
- Handle network changes

Never perform expensive decoding/network operations directly on the main UI thread.

---

# 13. Testing Rules

Every meaningful implementation must be tested.

At minimum:

1. Build
2. Unit tests where applicable
3. Integration tests where applicable
4. End-to-end tests for major flows
5. Manual verification when automated testing cannot reproduce the real environment

Never claim:

"Done"

without verification.

If something cannot be tested automatically, explicitly state:

- What was tested
- What was not tested
- Why it could not be tested
- What manual test should be performed

---

# 14. Build → Test → Fix Loop

After implementing a feature:

    CODE
      ↓
    BUILD
      ↓
    TEST
      ↓
    INSPECT ERROR
      ↓
    FIX
      ↓
    BUILD AGAIN
      ↓
    TEST AGAIN

Do not stop after writing code.

Compilation errors, test failures and runtime errors are part of the implementation process.

---

# 15. Search Before Implementing

Before implementing unfamiliar functionality:

1. Inspect the existing code.
2. Search the repository.
3. Check official documentation.
4. Check existing dependencies.
5. Prefer established libraries over reinventing complex infrastructure.
6. Only then implement.

Do not duplicate functionality that already exists.

---

# 16. Dependency Rules

Before adding a dependency:

- Explain why it is needed.
- Check whether an existing dependency already solves the problem.
- Prefer mature and actively maintained libraries.
- Avoid unnecessary dependencies.
- Consider Android size and Windows deployment complexity.
- Consider license compatibility.

Do not add libraries merely because they make a prototype easier.

---

# 17. Code Quality

Prefer:

- Small focused modules
- Clear interfaces
- Explicit error handling
- Meaningful names
- Testable components
- Minimal duplication
- Controlled concurrency
- Bounded resource usage

Avoid:

- Giant classes
- Giant methods
- Global mutable state
- Silent exception swallowing
- Magic constants
- Dead code
- Temporary hacks left undocumented

Do not perform large refactors unless they are necessary for the current milestone.

---

# 18. Existing Code Protection

Before modifying existing functionality:

1. Understand it.
2. Identify dependencies.
3. Check tests.
4. Make the smallest safe change.
5. Rebuild.
6. Re-test affected functionality.

Do not rewrite entire files unnecessarily.

Do not delete working functionality just to make a new feature easier.

---

# 19. Project Status / Checkpointing

PROJECT_STATUS.md is the persistent project memory.

Update it after every meaningful milestone.

It must contain:

## Current Goal

What is currently being implemented.

## Current State

What works right now.

## Completed

List completed milestones.

## In Progress

Current implementation.

## Known Problems

Current bugs, limitations and technical debt.

## Architecture

Important architecture decisions.

## Tests

Last known build/test status.

## Changed Files

Important files changed during the current milestone.

## Next Action

The exact next task to perform.

## Resume Instructions

Instructions for the next Gemini session.

---

# 20. Context Limit / Long Sessions

This project is expected to continue across multiple Gemini sessions.

If context is becoming large:

DO NOT start a large new implementation.

Instead:

1. Finish the smallest safe operation.
2. Save current progress.
3. Update PROJECT_STATUS.md.
4. Record unfinished work.
5. Record known errors.
6. Record the exact next step.
7. Stop safely.

At the next session:

1. Read GEMINI.md.
2. Read PROJECT_STATUS.md.
3. Inspect Git status.
4. Verify the current code.
5. Continue from the recorded Next Action.

Never restart the project from scratch because a previous session ended.

---

# 21. Git Checkpoints

Use Git for meaningful milestones.

Before risky changes:

    git status

After a stable milestone:

    git add .
    git commit -m "<meaningful milestone>"

Do not create meaningless commits for every tiny edit.

Never destroy existing user work.

Before large architectural changes, create a recoverable checkpoint.

---

# 22. Autonomous Execution

When the requested task is clear, continue through the full engineering loop.

Do not stop after:

- Creating folders
- Creating empty classes
- Creating interfaces
- Creating TODO comments
- Writing a README
- Producing architecture diagrams

The implementation must progress toward working software.

If a decision is genuinely blocking and cannot be resolved from the repository, documentation or reasonable engineering assumptions, ask the user.

Otherwise make the safest reasonable decision and document it.

---

# 23. Definition of Done

A feature is DONE only when:

- Code exists
- Code builds
- Relevant tests pass
- Runtime behavior has been verified where possible
- Errors are handled
- Security implications are considered
- Performance implications are considered
- PROJECT_STATUS.md is updated
- Next milestone is clear

A UI that only looks correct is NOT a completed feature.

A server that only starts is NOT a completed VNC implementation.

A client that only displays mock frames is NOT a completed VNC client.

---

# 24. Communication Style

Before major work, briefly state:

- Current state
- Goal
- Plan

During implementation, focus on execution.

After implementation, report:

- What changed
- Files changed
- Build result
- Test result
- Known limitations
- Next step

Do not repeatedly ask for permission for routine engineering steps.

---

# 25. Golden Rule

Always move the project closer to a REAL, TESTED, USABLE VNC system.

Do not optimize for:

- Number of files
- Number of classes
- Number of lines of code
- Pretty architecture documents
- Fake completion

# 26. GitHub Checkpoint Rules

GitHub is the remote backup and checkpoint system for this project.

The repository already has a configured `origin` remote.

Before starting significant work:

    git status
    git remote -v

Never modify or remove the existing `origin` remote unless explicitly instructed by the user.

---

## Checkpoint Policy

After every meaningful, stable milestone:

1. Build the affected components.
2. Run relevant tests.
3. Verify that the implementation works.
4. Inspect changed files.
5. Inspect the Git diff.
6. Update PROJECT_STATUS.md.
7. Commit the completed milestone.
8. Push the checkpoint to the configured GitHub `origin`.

Recommended workflow:

    git status
    git diff --stat
    git diff

    git add <relevant files>

    git commit -m "<meaningful milestone description>"

    git push origin HEAD

Do not push unfinished or knowingly broken work unless the user explicitly requests it.

---

## Commit Rules

Commits should represent meaningful milestones.

Good examples:

    feat(server): implement desktop capture
    feat(network): implement client-server connection
    feat(client): render remote desktop frames
    feat(input): implement mouse control
    feat(input): implement keyboard control
    feat(network): add automatic reconnect
    feat(security): add authenticated encrypted transport
    perf(video): reduce frame latency
    test(e2e): verify remote desktop control

Avoid meaningless commits such as:

    update
    changes
    test
    fix
    work
    stuff

---

## Before Push

Before pushing a checkpoint:

- Do not include passwords.
- Do not include API keys.
- Do not include private tokens.
- Do not include certificates/private keys unless intentionally required and safe.
- Do not include build artifacts unless the repository explicitly requires them.
- Do not include IDE temporary files.
- Do not include generated caches.
- Check `.gitignore`.
- Inspect `git diff --cached` before committing.

If a secret is detected:

1. Do not commit it.
2. Remove it from the staged changes.
3. Inform the user.
4. Recommend using environment variables or a secure configuration mechanism.

---

## Push Failure

If:

    git push origin HEAD

fails:

1. Inspect the error.
2. Determine whether it is authentication, network, branch, or remote-history related.
3. Fix only what is safe to fix automatically.
4. Never force-push.
5. Never delete remote history.
6. Never use `git push --force` unless explicitly authorized by the user.
7. If credentials or GitHub authentication are required, ask the user to authenticate.

---

## Remote History Protection

Never run destructive Git operations such as:

    git reset --hard

against uncommitted user work.

Never:

    git push --force

Never rewrite remote history.

Never delete branches or tags unless explicitly instructed.

If local and remote histories diverge:

1. Stop.
2. Inspect the histories.
3. Explain the situation.
4. Choose a safe merge/rebase strategy only when it will not destroy user work.

---

## Checkpoint Record

Every pushed checkpoint must also update:

    PROJECT_STATUS.md

The status file must record:

- Current milestone
- Commit hash
- What was implemented
- Build result
- Test result
- Known limitations
- Next task

Example:

    ## Latest Checkpoint

    Commit:
    abc1234

    Milestone:
    Windows screen capture MVP

    Build:
    PASS

    Tests:
    PASS

    Known limitations:
    Single-monitor capture only.

    Next Action:
    Implement frame transport to Android client.

---

## Context Limit Checkpoint

If the AI session is approaching its context limit:

1. Stop starting new large features.
2. Finish the smallest safe operation.
3. Run available verification.
4. Update PROJECT_STATUS.md.
5. Commit the current stable state.
6. Push the checkpoint to GitHub.
7. Record the exact next action.
8. Stop.

The next session must:

1. Pull/fetch the latest repository state when appropriate.
2. Read GEMINI.md.
3. Read PROJECT_STATUS.md.
4. Inspect the latest commit.
5. Continue from the recorded Next Action.

---

## Important Rule

A GitHub checkpoint is not a substitute for testing.

Never treat:

    git commit

or:

    git push

as proof that the feature works.

A checkpoint must represent a known project state with the verification status documented in PROJECT_STATUS.md.

# 27. Autonomous Execution Policy

## Default Behavior

When the user gives a clear project-level task, operate autonomously.

Do NOT repeatedly ask the user for permission to perform normal engineering actions.

Normal engineering actions include:

- Inspecting files
- Searching the repository
- Reading documentation
- Creating directories
- Creating source files
- Editing source files
- Installing project dependencies
- Running builds
- Running tests
- Running linters
- Running formatters
- Debugging errors
- Fixing implementation problems
- Updating documentation
- Updating PROJECT_STATUS.md
- Creating Git commits
- Pushing verified checkpoints to the configured GitHub origin

These actions are pre-authorized by the project instructions.

---

## Do Not Ask After Every Step

Do NOT ask questions such as:

- "Should I continue?"
- "Would you like me to implement this?"
- "Do you want me to run the tests?"
- "Should I fix this error?"
- "Can I create this file?"
- "Do you want me to proceed to the next step?"

when the action is part of the current clearly defined task.

Instead, perform the action and report the result.

Bad:

    I found a compilation error.
    Would you like me to fix it?

Good:

    The build failed because X.
    I fixed X and am running the build again.

---

## Continue Until a Meaningful Boundary

When a task contains multiple dependent steps, continue through them automatically.

Example:

    Implement feature
        ↓
    Build
        ↓
    Test
        ↓
    Fix errors
        ↓
    Test again
        ↓
    Update documentation
        ↓
    Update PROJECT_STATUS.md
        ↓
    Commit
        ↓
    Push checkpoint

Do not stop between every step to request permission.

---

## When Asking the User IS Required

Ask the user only when a decision cannot safely be made autonomously.

Examples:

### 1. Destructive action

Ask before:

- Deleting user files
- Deleting large sections of working

Optimize for:

WORKING SOFTWARE + VERIFIED BEHAVIOR + LOW LATENCY + RELIABILITY + SECURITY.