# NetOps Terminal 1.0.0-rc.2

## Release-candidate scope

This release candidate contains the completed Phase 0 through Phase 8 Version 1 functionality plus the approved post-build Operator Workspace UI/UX redesign. It is an offline-first Windows x64 desktop application. SSH private-key authentication, SSH agent authentication, jump hosts, file transfer, port forwarding, encrypted export, multi-device broadcast, automatic updates, and an MSI/MSIX installer remain outside Version 1.

## Package format

The supported Phase 8 package is a self-contained Windows x64 folder distributed as a ZIP archive. It includes the .NET runtime and does not require a separately installed .NET SDK or runtime. The folder layout is intentional because terminal and transport dependencies include platform-specific files.

The release script produces:

- `artifacts\release\NetOpsTerminal-1.0.0-rc.2-win-x64\`
- `artifacts\release\NetOpsTerminal-1.0.0-rc.2-win-x64.zip`
- `artifacts\release\NetOpsTerminal-1.0.0-rc.2-win-x64.zip.sha256`

Run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish-Release.ps1
```

## Install and run

1. Verify the ZIP SHA256 value against the accompanying `.sha256` file.
2. Extract the entire ZIP to a user-writable folder such as `%LOCALAPPDATA%\Programs\NetOpsTerminal`.
3. Run `NetOpsTerminal.exe` from the extracted folder.
4. Keep every published file together; do not copy only the executable.

The RC package is not code-signed. Windows SmartScreen may show an unrecognized-app warning. Distribute it only through an approved internal channel and verify its SHA256 value before running it.

## Upgrade and data preservation

Close NetOps Terminal before upgrading. Replace the extracted application folder with the new package. Application data is stored separately under `%LOCALAPPDATA%\NetOpsTerminal`, so replacing application binaries does not delete devices, credential-profile metadata, trusted host keys, settings, workspace layout, snippets, or session logs.

Before an upgrade, copy `%LOCALAPPDATA%\NetOpsTerminal` to an access-controlled backup location. Password values remain in Windows Credential Manager under the `NetOpsTerminal:` prefix and are not contained in the file backup. Database migrations are forward-only; do not open an upgraded database with an older build.

## Uninstall

Close the application and delete the extracted application folder. To remove user data as well, separately remove `%LOCALAPPDATA%\NetOpsTerminal` only after taking any required backup. Reusable credentials must be removed through NetOps Terminal before uninstall or manually from Windows Credential Manager by an authorized user.

## Security and operational notes

- The self-contained package carries its own .NET runtime and must be republished when .NET security updates are adopted.
- Session logging remains opt-in and output-only, but remote output can contain sensitive information.
- Telnet and Raw TCP are plaintext protocols and should be used only on authorized trusted networks.
- Changed or revoked SSH host keys remain blocked.
- Snippets are metadata, not a secret store.
- The application does not implement automatic update or background network access.

## Release validation

Use `docs\user-guide\PHASE_08_MANUAL_CHECKLIST.md` on a clean or disposable Windows x64 account before wider distribution. The Phase 8 QA report records automated evidence, package hash, fresh-start result, upgrade-preservation tests, and any remaining host limitations.
