# Security

## Data and credentials

NetOps Terminal stores device and workspace metadata in `%LOCALAPPDATA%\NetOpsTerminal\data\netops-terminal.db` and non-secret preferences in `%LOCALAPPDATA%\NetOpsTerminal\settings.json`. Reusable SSH passwords use Windows Credential Manager; SQLite stores only credential profile references. Quick Connect passwords remain in the active session and are not deliberately persisted or logged. Trusted public SSH host keys are stored in the application-owned `%LOCALAPPDATA%\NetOpsTerminal\ssh\known_hosts` file.

Saved-device exports exclude passwords, private keys, secret references, and trusted host keys. Workspace restore starts disconnected. Session output is written to `%LOCALAPPDATA%\NetOpsTerminal\session-logs` only when logging is explicitly enabled; remote systems can echo sensitive material, so protect those files.

## Connection and terminal safety

Unknown SSH host keys require an explicit trust choice. Changed or revoked keys are blocked; they are never accepted or overwritten automatically. Telnet and Raw TCP do not provide encryption or peer authentication and should be used only on authorized trusted networks. Serial uses a local COM port.

Terminal output is untrusted. Input Lock blocks keyboard, paste, and programmatic sends to the locked session while preserving output and copy. Multiline or control-sequence paste requires review and confirmation. Production indicators use text as well as color, and restored Production sessions never reconnect automatically. Copying selected terminal text places it on the Windows clipboard; the app does not clear Windows clipboard history.

## Release package

The Windows x64 MSI and ZIP include the .NET runtime and no user settings, devices, passwords, keys, or logs from the build machine. They are unsigned: verify the published SHA256 checksum and distribute them through an approved restricted channel. The app has no automatic updater. Runtime security updates require rebuilding and redistributing the package.

## Vulnerability reporting

Report vulnerabilities privately to the repository owner. Do not include real infrastructure addresses, credentials, private keys, or sensitive terminal output in issue reports or test data.

## Saved-device web access in 1.1.0

A saved web address is application metadata in SQLite and in device exports. The editor accepts only HTTP or HTTPS URLs without embedded credentials, query strings, or fragments. The app does not store browser passwords or handle web sign-in; the external browser owns that session. HTTP pages can transmit data without encryption. Check access performs a manual TCP port check and does not verify identity, TLS certificates, or authentication. GUI-only devices cannot open a terminal session and do not require an SSH credential. Upgrading a local database to schema 6 prevents v1.0.0 from reading that same database.
