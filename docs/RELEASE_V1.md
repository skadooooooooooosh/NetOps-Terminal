# NetOps Terminal 1.0.0 for Windows x64

## Install

Run `NetOpsTerminal-1.0.0-win-x64.msi`. The wizard shows the software agreement, lets you choose the application folder, and defaults to the system's Program Files folder (normally `C:\Program Files\NetOps Terminal`). Windows will request administrator approval because this is an all-users installation. It includes the .NET runtime. The install-folder page offers checked-by-default options for a desktop shortcut and a Start menu shortcut. Windows decides whether a new Start menu entry appears in Recently added.

Close any running NetOps Terminal window first. Verify the MSI SHA256 against `NetOpsTerminal-1.0.0-win-x64.msi.sha256`. This MSI is unsigned, so Windows may display a SmartScreen warning. Distribute it through an approved channel and verify its hash on each destination device.

## Upgrade and uninstall

If an earlier build of the 1.0.0 all-users MSI was installed, uninstall it before testing this replacement candidate; both builds share the same product version. The earlier per-user MSI candidate used a different Windows Installer product family. Windows Installer cannot perform a major upgrade across per-user and per-machine contexts. If that candidate was installed, uninstall it first; its application data and Windows Credential Manager entries remain available in that user's account. An earlier extracted ZIP is not registered with Windows Installer, so remove an old extracted app folder separately to avoid launching the wrong copy.

For future all-users MSI upgrades, keep the machine-wide UpgradeCode and assign a new ProductCode for each new version. Close the app before upgrading. Back up `%LOCALAPPDATA%\NetOpsTerminal` for each user whose data must be preserved; database migrations are forward-only.

Uninstall from Windows Settings > Installed apps. Uninstall removes the shared application files and whichever shortcuts were selected. Each user's saved devices, settings, trusted SSH host keys, and logs remain under that user's `%LOCALAPPDATA%\NetOpsTerminal`. Windows Credential Manager retains that user's saved secrets. Remove those separately only when intentionally retiring the data.

## Package contents and security

The MSI includes the complete self-contained Windows x64 publish, the user agreement, third-party notices, dependency inventory, release guide, and file manifest. It contains no saved devices, passwords, private keys, settings, known hosts, or logs from the build machine. There are no application-defined installer custom actions or automatic updates. Telnet and Raw TCP are plaintext; changed or revoked SSH host keys remain blocked.

The bundled .NET runtime must be refreshed when runtime security updates are adopted. The MSI has no Authenticode signature; signing requires a trusted code-signing certificate and a separate approved release step.

## Build and validation

From the repository root on Windows with the pinned .NET 10 SDK:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish-Msi.ps1
```

The script restores, builds, tests, publishes, generates installer artwork and agreement RTF, builds and validates the MSI, checks its all-users scope and wizard, and writes the MSI plus SHA256 sidecar to `artifacts\release`. Before wider distribution, verify installer behavior on a clean or disposable Windows 10/11 x64 device.
