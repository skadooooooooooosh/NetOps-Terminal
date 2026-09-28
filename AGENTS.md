# Agent Instructions

## Scope

This is the standalone **NetOps Terminal** repository.

Work only inside this repository unless the user explicitly provides another in-scope path.

Do not inspect, reference, reuse, import, connect to, or copy from prior projects, databases, configuration, Git repositories, project-memory folders, or unrelated applications.

Do not add a Git remote unless the user explicitly supplies or approves one.

The current repository is the source of truth for the application that has already been implemented.

---

## Required reading

Before implementation, review, QA, release, or redesign work, read these files completely when they exist:

1. `AGENTS.md`
2. `PROJECT_CONTEXT.md`
3. `CURRENT_STATE.md`
4. `DECISIONS.md`
5. `SECURITY.md`
6. `README.md`
7. `QA_PHASE_GATE.md`
8. Relevant files under `docs/testing/`
9. The latest QA and defect reports

If a referenced file does not exist, report that fact instead of inventing its contents.

---

## Architecture rules

- The UI manages workspaces and presentation state.
- The Session Manager owns connection lifecycles.
- Transport adapters own protocol behavior.
- The terminal layer owns rendering and input.
- The credential service owns secrets.
- Persistence stores application metadata, never plaintext secrets.
- Core remains independent of UI frameworks, databases, transport implementations, and operating-system-specific APIs.
- Views and ViewModels must not directly call SQLite, SSH.NET, serial APIs, OS secret APIs, or RoyalTerminal implementation types.
- RoyalTerminal-specific types must remain isolated behind NetOps-owned abstractions.
- Vendor-specific device behavior belongs in metadata, presets, snippets, or dedicated services—not the generic transport layer.
- Prefer small, explicit interfaces over broad service classes.
- Preserve working architecture unless a concrete defect or approved requirement requires change.

Expected dependency direction:

```text
Views
  ↓
ViewModels
  ↓
Application Services
  ↓
Core Contracts / Domain
  ↓
Infrastructure / Transport / Security Implementations
```

---

## Engineering standards

- Target `.NET 10`.
- Use nullable reference types.
- Keep analyzers enabled.
- Pin NuGet versions centrally in `Directory.Packages.props` when the project uses central package management.
- Use `async` / `await` for I/O.
- Use `CancellationToken` for long-running and network operations.
- Do not block async work with `.Result` or `.Wait()`.
- Do not swallow exceptions.
- Do not use arbitrary delays as synchronization.
- Dispose sessions, streams, transports, timers, subscriptions, and local processes correctly.
- Keep network operations off the UI thread.
- Do not store secrets in source code, test fixtures, logs, SQLite, exported profiles, or settings files.
- Do not weaken SSH host-key validation for convenience.
- Do not automatically execute destructive commands against network devices.
- Treat terminal output and remote-device data as untrusted input.
- Prefer the smallest safe change over broad refactoring during stabilization work.
- Do not bypass phase gates for a visual demonstration.

---

## Validation

Every required development phase must complete its normal QA gate before it is considered accepted.

For validation, run and fix project-related failures from:

```powershell
dotnet restore
dotnet build
dotnet build -c Release
dotnet test
```

Where applicable, launch the real application and test actual current-phase functionality.

QA must verify the implementation rather than repeat the developer report.

Use exactly one QA result in a phase or full regression report:

- `PASS`
- `PASS WITH MINOR ISSUES`
- `FAIL`

Critical or High defects, failed builds, material failed tests, major regression, security violations, or unmet acceptance criteria require `FAIL`.

Record QA evidence and defects under `docs/testing/` when that structure exists.

After QA, update `CURRENT_STATE.md`.

Do not represent an unavailable interaction or unperformed manual test as a pass.

---

# Version 1 Build Boundary

## Phase 8 is the final required build phase for Version 1

For the current NetOps Terminal roadmap:

> **Phase 8 — Hardening and Release Candidate is the final required application-building phase for Version 1.**

Phases after Phase 8 are **not part of the current mandatory Version 1 build**.

They are optional future-version or improvement work only.

Examples of possible future work may include:

- advanced SSH authentication / MFA
- SSH agent support
- jump or bastion hosts
- proxy support
- SFTP / SCP
- SSH port forwarding
- import/export enhancements
- encrypted backup/restore
- multi-device workflows
- monitoring/tool integrations
- additional diagnostics
- other approved Version 2+ improvements

These optional items must not begin automatically.

They require explicit user approval and should be treated as a new version, new milestone, or separately approved improvement.

Do not continue implementing Phase 9+ merely because such phases exist in an older roadmap.

---

## Existing-work protection

Phases 0–7 are existing implementation and must not be recreated.

During Phase 8 and post-build review, do not:

- restart the project
- regenerate the solution
- recreate completed phases
- replace stable working features with placeholders
- rewrite functioning subsystems merely to match an earlier blueprint
- perform unrelated refactors
- broaden Version 1 scope without approval
- introduce optional future-version features
- import code from unrelated projects
- change major architecture because of personal preference

Inspect the actual repository first.

The original build blueprint remains a reference; the existing repository is the source of truth for what has actually been implemented.

---

# Phase 8 — Final Version 1 Build Phase

Phase 8 focuses on:

- hardening
- release-candidate stabilization
- defect correction
- regression prevention
- lifecycle/resource cleanup
- error handling
- performance review
- package/dependency health
- database/settings safety
- release packaging
- documentation completion
- final Version 1 QA readiness

Phase 8 is not a feature-expansion phase.

Do not add major optional features unless the user explicitly approves them as required Version 1 work.

### Phase 8 completion workflow

Use this order:

```text
Phase 8 implementation / hardening
        ↓
Normal Phase 8 developer validation
        ↓
Normal Phase 8 QA gate
        ↓
Fix blocking Phase 8 defects
        ↓
QA retest
        ↓
Phase 8 accepted
        ↓
Post-build specialist review
```

Do not start optional future-version phases after Phase 8.

---

# Post-build specialist review

Run this workflow only after Phase 8 and its normal QA gate are complete.

The post-build review validates the completed Version 1 application. It is not another feature-development phase.

### Review order

1. `netops-architecture-reviewer`
   - Check architecture drift, dependency boundaries, transport/session ownership, persistence boundaries, credential boundaries, and RoyalTerminal isolation.
   - Major architectural rewrites require user approval before implementation.

2. `netops-terminal-tester`
   - Test implemented SSH, Telnet, Serial, Raw TCP, Local Shell/PTTY, tabs, split panes, terminal focus, resize, copy/paste, Ctrl+C, reconnect, search, Input Lock, and session cleanup.
   - Test normal operation and relevant failure scenarios.

3. `netops-security-reviewer`
   - Review credential storage, SSH host-key validation, `known_hosts`, logs, private keys, SQLite metadata, temporary files, imports/exports, error handling, path handling, and command execution.
   - Release blockers include:
     - plaintext saved passwords
     - plaintext passwords in SQLite
     - credentials committed to source control
     - automatic acceptance of changed SSH host keys
     - secrets/private keys/passphrases deliberately written to logs
     - unresolved Critical or High security defects

4. `netops-qa-tester`
   - Run full Version 1 regression QA.
   - Result must be `PASS`, `PASS WITH MINOR ISSUES`, or `FAIL`.
   - Any Critical or High functional defect requires `FAIL`.

5. `netops-doc-maintainer`
   - Synchronize `README.md`, `PROJECT_CONTEXT.md`, `CURRENT_STATE.md`, `DECISIONS.md`, `SECURITY.md`, `CHANGELOG.md`, and relevant `docs/testing/` files with the actual implementation.

6. `netops-release-reviewer`
   - Verify:
     ```powershell
     dotnet restore
     dotnet build
     dotnet build -c Release
     dotnet test
     ```
   - Also review startup/shutdown, settings initialization, database initialization/migrations, Windows x64 publish, runtime dependencies, dependency/package status, open defects, security status, QA status, and release documentation.
   - Final result must be:
     - `RELEASE READY`
     - `RELEASE BLOCKED`

### Post-build review workflow

```text
Phase 8 accepted
        ↓
Architecture Review
        ↓
Terminal / Protocol Review
        ↓
Security Review
        ↓
Fix approved blocking findings
        ↓
Full Version 1 Regression QA
        ↓
Documentation Synchronization
        ↓
Release Readiness Review
        ↓
STOP FOR USER REVIEW
```

---

# Approval gates

Explicit user approval is required before:

- major architectural rewrites
- replacement of stable core dependencies
- major transport/session redesign
- major persistence/database redesign
- adding optional future-version functionality
- starting the final UI/UX redesign
- implementing the approved UI/UX redesign
- beginning any Phase 9+ / Version 2+ work

Do not infer approval from silence.

Do not treat QA approval as approval for a redesign or a future version.

---

# Release blockers

Do not mark Version 1 release-ready and do not proceed to the final UI/UX redesign while any of these remain:

- QA result = `FAIL`
- release result = `RELEASE BLOCKED`
- unresolved Critical functional defects
- unresolved High functional defects
- unresolved Critical security defects
- unresolved High security defects
- failed Debug build caused by project code
- failed Release build caused by project code
- materially failed automated tests
- known credential exposure
- silent acceptance of a changed trusted SSH host key
- significant unresolved data-corruption or session-routing risk

Blocking findings must be fixed and retested.

---

# Final UI/UX redesign gate

The final `netops-uiux-designer` is a **post-build refinement step**, not a Version 1 functional build phase.

It may start only after:

- Phase 8 is complete and accepted
- full Version 1 regression QA passes
- security review passes
- release review returns `RELEASE READY`
- no unresolved Critical or High defects remain

The UI/UX designer must:

1. Audit the finished application.
2. Review approved design references.
3. Create multiple design directions.
4. Create visual previews/mockups.
5. Present Current vs Proposed.
6. Explain layout, fonts, colors, icons, spacing, and workflow changes.
7. STOP.
8. Wait for explicit user approval.

No production UI redesign may begin before the user approves the preview.

After the approved redesign is implemented:

- run build validation
- run full regression QA
- run relevant security regression checks
- compare implementation with the approved preview
- resolve release blockers
- perform final release-readiness review

---

# Optional future versions / improvements

Any work after Version 1 release is optional unless explicitly approved.

Treat future improvements as one of:

```text
Version 1.x maintenance
Version 2
Optional enhancement
Approved new milestone
```

Before beginning optional work:

1. Define the requested scope.
2. Identify whether it affects security or architecture.
3. Create acceptance criteria.
4. Obtain explicit user approval.
5. Update `CURRENT_STATE.md` / roadmap.
6. Build and QA it as a separate approved milestone.

Do not reopen completed Version 1 phases to implement optional roadmap ideas.

---

# Current phase gate

The project has completed:

> **Phase 8 — Hardening and Release Candidate**

Phase 8 was the final required application-building phase for the current Version 1 scope and closed as PASS WITH MINOR ISSUES on 2026-09-17.

Phases 0–8 are existing work and must not be recreated.

The project is eligible for the consolidated post-build specialist review defined above, but that review requires explicit user authorization before it begins.

Do not start the final UI/UX redesign until the post-build regression QA, security review, and release-readiness review have passed and its separate approval gate is satisfied.

Do not begin Phase 9+ or other optional future-version work without explicit user authorization.

---

# Final governance rule

For the current NetOps Terminal project:

> **Phase 8 is the final required Version 1 build phase.**

> **Phase 9+ work is optional future-version/improvement work and must never begin automatically.**

> **Completed phases must not be recreated.**

> **The current repository is the source of truth.**

> **Critical and High defects are release blockers.**

> **Major architectural changes require approval.**

> **Final UI/UX work is approval-gated and preview-first.**

> **No future-version work begins without explicit user approval.**
