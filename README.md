# NetOps Terminal

**Version 1.0.0** — a Windows desktop terminal workspace for managing local shells and network device sessions.

## Connections

- SSH with SHA256 host-key verification and explicit trust decisions
- Telnet
- Serial / COM
- Raw TCP
- Windows PowerShell
- Windows Command Prompt

Telnet and Raw TCP send data without encryption. Use them only on networks you trust.

## What it includes

Quick Connect, saved devices and groups, Favorites and Recent, credential profiles protected by Windows Credential Manager, terminal tabs and split panes, reconnect and disconnect, terminal search, optional output logging, Input Lock, keyboard shortcuts, and a dark Device Explorer workspace. Device import and export support JSON and Excel; CSV import is also supported. Passwords and trusted host keys are excluded from device exports.

## Install

Open the [v1.0.0 GitHub Release](https://github.com/skadooooooooooosh/NetOps-Terminal/releases/tag/v1.0.0) and expand **Assets**. Download [NetOpsTerminal-1.0.0-win-x64.msi](https://github.com/skadooooooooooosh/NetOps-Terminal/releases/download/v1.0.0/NetOpsTerminal-1.0.0-win-x64.msi) and its SHA256 sidecar, then check the MSI hash before running it. GitHub sign-in and repository access are required while the repository is private. The installer includes the .NET runtime, defaults to Program Files, and asks for administrator approval. It can create Desktop and Start menu shortcuts. The MSI is unsigned, so Windows may show a publisher warning.

The portable Windows x64 ZIP is also available for users who do not need an installer. See [Version 1 release guidance](docs/RELEASE_V1.md) for install and uninstall details.

## Build from source

Requirements: Windows 10/11 x64 and the .NET 10 SDK pinned in `global.json`.

```powershell
dotnet restore
dotnet build
dotnet build -c Release
dotnet test
dotnet run --project src/NetOpsTerminal.App/NetOpsTerminal.App.csproj
```

To produce the approved self-contained MSI and ZIP:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish-Msi.ps1
```

Release output is written to the ignored `artifacts\release` directory.

## Security and local data

Saved credential secrets use Windows Credential Manager. Device and workspace metadata is stored in SQLite under `%LOCALAPPDATA%\NetOpsTerminal`; trusted SSH host keys are stored separately in an application-owned `known_hosts` file. Unknown SSH keys require an explicit choice, and changed or revoked keys are blocked. Session output is written to local files only when logging is enabled. See [SECURITY.md](SECURITY.md) for details.

SSH private-key and SSH-agent authentication are not included in Version 1.
