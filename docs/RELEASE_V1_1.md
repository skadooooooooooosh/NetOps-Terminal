# NetOps Terminal 1.1.0 for Windows x64

## Install or upgrade

Download `NetOpsTerminal-1.1.0-win-x64.msi` and its `.sha256` file from the v1.1.0 GitHub Release. Verify the hash, close any running NetOps Terminal window, then run the MSI. The wizard defaults to `C:\Program Files\NetOps Terminal`, lets you choose another folder, and asks for administrator approval. Desktop and Start menu shortcuts are optional. The installer includes the .NET runtime and is unsigned, so Windows may show a publisher warning.

The v1.1.0 MSI uses the same all-users UpgradeCode and a new ProductCode, allowing Windows Installer to upgrade the published v1.0.0 all-users MSI. If you installed an older per-user MSI candidate, uninstall that candidate first; Windows Installer cannot upgrade between per-user and per-machine contexts.

The 2026-10-07 package refresh keeps version 1.1.0 and the same MSI product identity. If an earlier 1.1.0 MSI is already installed, uninstall it before installing the refreshed MSI; Windows Installer may otherwise open maintenance mode instead of replacing the app files. Your per-user device data is retained by uninstall. Verify the new checksum after downloading because the files under the existing release tag have changed.

## Protect existing data

Before upgrading, back up each user's `%LOCALAPPDATA%\NetOpsTerminal` folder to an access-controlled location. Saved passwords remain in that user's Windows Credential Manager and are not in the folder backup. The v1.1.0 app migrates the database to schema 6. That migration is forward-only; the v1.0.0 app cannot read the upgraded database.

Uninstall removes program files and selected shortcuts. User metadata, trusted host keys, logs, and credential entries remain until you remove them separately.

## New in 1.1.0

Saved devices can use CLI, an external web interface, or both. Device Explorer supports folders, manual ordering, multi-device moves and deletion. Terminal tabs can be reordered, and each connection attempt is visible in its own tab. Bulk import and Excel templates include improved protocol, port, and web-access handling. A changed SSH host key still requires explicit user review before the saved key can be replaced. See the changelog for the full list.

## Security and package contents

The MSI contains a self-contained Windows x64 app, its release guide, software agreement, third-party notices, dependency inventory, and file manifest. It does not include devices, saved passwords, user settings, trusted host keys, or logs from the build machine. The application has no automatic updater. Telnet and Raw TCP are plaintext; HTTP web pages may also expose traffic. The manual web-port check tests reachability only.

This MSI has no Authenticode signature. Verify its SHA256 value before distribution. The bundled .NET runtime must be refreshed when runtime security updates are adopted.

## Build

From the repository root on Windows with the pinned .NET 10 SDK:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish-Msi.ps1

The script restores, builds, tests, publishes, builds and inspects the MSI, and writes the MSI plus SHA256 sidecar to `artifacts\release`.