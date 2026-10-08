# NetOps Terminal 1.1.1 for Windows x64

## Install or upgrade

Download NetOpsTerminal-1.1.1-win-x64.msi and its .sha256 file from the [v1.1.1 GitHub Release](https://github.com/skadooooooooooosh/NetOps-Terminal/releases/tag/v1.1.1). Verify the checksum, close NetOps Terminal, and run the MSI. The wizard offers an install folder and optional Desktop and Start menu shortcuts. Administrator approval is required.

The 1.1.1 MSI has a new ProductCode and retains the all-users UpgradeCode, so it upgrades the 1.1.0 all-users MSI. An earlier per-user MSI candidate must be uninstalled separately.

## Protect existing data

Back up each user's %LOCALAPPDATA%\NetOpsTerminal folder to an access-controlled location before upgrading. Saved passwords are held separately in Windows Credential Manager. The database remains at schema 6. Version 1.0.0 cannot read the migrated database.

## What changed

The saved-device editor no longer shows Environment. Existing values remain stored. Site and Organization folders can be renamed from their context menus without moving devices. The expanded Device Explorer fits device names and explicit CLI, GUI, and CLI+GUI badges within its width and shows full text in tooltips.

## Security and package contents

The MSI contains a self-contained Windows x64 app and release documentation. It does not contain the build machine's devices, passwords, settings, trusted host keys, or logs. The MSI and ZIP are unsigned; Windows may show a publisher warning. Verify the SHA-256 checksum before installing or distributing. The application has no automatic updater.