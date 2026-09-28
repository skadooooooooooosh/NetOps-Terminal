# NetOps Terminal v1.0.0

## Highlights

- Connect through Quick Connect or saved devices, and organize devices with groups, Favorites, and Recent.
- Work in terminal tabs and split panes with search, reconnect, Input Lock, optional output logging, and keyboard shortcuts.
- Save reusable SSH credentials in Windows Credential Manager and verify SSH host keys before authentication.
- Import and export device metadata with JSON and Excel; CSV import is also available.
- Install for all Windows users with the MSI wizard, or use the portable ZIP.

## Supported Connection Types

- SSH
- Telnet
- Serial / COM
- Raw TCP
- Windows PowerShell
- Windows Command Prompt

## Verification Status

On 2026-09-28, `dotnet clean`, `dotnet restore`, Debug and Release builds passed with zero warnings and errors. All 225 automated tests passed in Debug and again in Release, with no failures or skips. The approved release pipeline built the Windows x64 MSI and portable ZIP; WiX reported zero warnings and errors, and the MSI contains 295 published files. SHA256 sidecars match the regenerated artifacts. The operator reported that all Version 1 manual checklists passed. That host result is user-reported; separate test logs and device details were not supplied.

## Known Limitations

- The MSI and ZIP are unsigned. Windows may display an unknown-publisher warning; verify the SHA256 checksum before use.
- SSH private-key and SSH-agent authentication are not included in Version 1.
- Telnet and Raw TCP are plaintext protocols.
- The application does not update itself; runtime security updates require a new package.

## Attached Artifacts

- `NetOpsTerminal-1.0.0-win-x64.msi`
- `NetOpsTerminal-1.0.0-win-x64.zip`
- `NetOpsTerminal-1.0.0-win-x64.msi.sha256`
- `NetOpsTerminal-1.0.0-win-x64.zip.sha256`
- `SHA256SUMS.txt`
