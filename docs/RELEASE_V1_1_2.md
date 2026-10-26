# NetOps Terminal 1.1.2 for Windows x64

## Package refresh - 2026-10-26

The refreshed downloads retain version **1.1.2** and include:

- Pinned Device Explorer reserves its width so terminal tabs, content and footer fit beside it; unpinned Explorer remains a temporary overlay.
- Terminal copies omit trailing ASCII space padding while retaining indentation, internal spacing, tabs, Unicode and line breaks. Incoming pasted text is not trimmed.
- The paste preview opens at its first line and left edge.
- Confirm Terminal Paste defaults to 820 x 600 DIPs, remains resizable and fits to the display working area.

**Already installed 1.1.2?** Back up your data, close the app, uninstall the earlier 1.1.2 MSI, then install the refreshed MSI. Its ProductVersion and ProductCode are unchanged, so simply opening the new MSI may enter maintenance mode. The installer does not remove the per-user application profile; keep a backup anyway. For the ZIP, extract the complete refreshed package into a new folder. Use the newly supplied SHA-256 checksums.

The existing release tags remain unchanged. The release notes identify the maintenance source commit separately; GitHub's automatic source archives for the original v1.1.2 tag continue to represent the original tagged source.
## Install or upgrade

Download [NetOpsTerminal-1.1.2-win-x64.msi](https://github.com/skadooooooooooosh/NetOps-Terminal/releases/download/v1.1.2/NetOpsTerminal-1.1.2-win-x64.msi) and its [.sha256 file](https://github.com/skadooooooooooosh/NetOps-Terminal/releases/download/v1.1.2/NetOpsTerminal-1.1.2-win-x64.msi.sha256) from the [v1.1.2 GitHub Release](https://github.com/skadooooooooooosh/NetOps-Terminal/releases/tag/v1.1.2). Verify the checksum, close NetOps Terminal, and run the MSI. The wizard offers an install folder and optional Desktop and Start menu shortcuts. Administrator approval is required.

The 1.1.2 MSI uses a new ProductCode and retains the established all-users UpgradeCode, allowing upgrades from the 1.1.0 and 1.1.1 all-users MSIs. A historical per-user MSI candidate must be uninstalled separately. Published 1.0.0, 1.1.0 and 1.1.1 tags and assets are retained.

## Protect existing data

Back up each user's `%LOCALAPPDATA%\NetOpsTerminal` folder to an access-controlled location before upgrading. Reusable passwords are stored separately in Windows Credential Manager. This UI update introduces no database migration; the database remains at schema 6. Version 1.0.0 cannot read the schema-6 database.

The release uses the normal NetOpsTerminal user profile. The earlier local UI demo intentionally used an isolated `preview-data` folder and separate credential namespace; demo data is not copied to the release profile.

## What changed

- A permanent graphite/charcoal palette with consistent controls, focus states and readable text contrast.
- Tabler outline icons on the unchanged 52 DIP activity rail, aligned header branding and a balanced welcome screen.
- Clearer terminal tab controls, compact status text, a 24 DIP footer, consistent dialogs, Help and Settings.
- The existing Device Explorer design and controls are retained. Its outer host overlays the terminal when unpinned and reserves sidebar width when pinned.

The selected application icon, connection engines, terminal renderer, keyboard input, credential handling, SSH host-key verification and saved-device metadata are preserved. The copy-only selection adapter is isolated in the terminal integration layer.

## Portable ZIP

The [Windows x64 ZIP](https://github.com/skadooooooooooosh/NetOps-Terminal/releases/download/v1.1.2/NetOpsTerminal-1.1.2-win-x64.zip) includes the .NET runtime. Extract the complete archive to a writable folder and run `NetOpsTerminal.exe`. Keep all accompanying files with the EXE. This distribution stores user data in the normal LocalAppData profile, outside the extracted app folder.

## Security and package contents

The MSI and ZIP contain the self-contained application and necessary release documentation. They contain no build-machine inventory, settings, passwords, trusted host keys or session logs. Both are unsigned; verify the SHA-256 checksums before installing or distributing. The application has no automatic updater. Downloads require repository access while the GitHub repository remains private.

The operator reported that the local UI preview passed window resize/close, Quick Connect, Help/Settings, PowerShell and Command Prompt typing, and the Explorer overlay check. Automated regression and package inspection complement that report. Independent desktop automation, a full installed-upgrade test and a complete Windows DPI matrix were not performed. Detailed engineering evidence is maintained locally and in the NetOps Terminal Obsidian vault.