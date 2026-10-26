# Changelog

## [1.1.2] - 2026-10-26

- Refined the dark interface with reusable graphite colors, stronger text contrast, and consistent focus and control states.
- Unified compact activity-rail icons with licensed Tabler outline vectors while preserving the selected app icon and 52 DIP rail.
- Improved welcome-screen spacing, header alignment, terminal tab controls, the 24 DIP footer, Help, Settings, and dialog styling.
- Kept the existing Device Explorer design and controls; its overlay no longer resizes the terminal workspace.
- Added automated headless UI coverage for layout, overlay behavior, Quick Connect validation, and contrast.
- Preserved connection protocols, terminal rendering/input, saved data, credentials, and SSH host-key verification.

## [1.1.1] - 2026-10-08

- Removed the Environment selector from the saved-device editor while preserving existing metadata.
- Added Rename to Site and Organization folder context menus; device assignments and manual order stay intact.
- Improved Device Explorer sizing, spacing, tooltips, and CLI, GUI, and CLI+GUI badges.

## [1.1.0] - 2026-10-01

- Refreshed the 1.1.0 packages on 2026-10-07: Excel import accepts blank optional template cells, GUI-only Excel exports leave terminal columns blank, and import errors identify the failing step.
- Added saved-device CLI only, GUI only, and CLI + GUI modes, with browser choice, an explicit web action, and an optional manual web-port check.
- Added site folders, manual Device Explorer ordering, multi-device moves and deletion, and drag ordering for terminal tabs.
- Gave each connection attempt a visible tab, aligned connection status indicators, and added PowerShell and Command Prompt shortcuts.
- Improved bulk import updates, protocol and default-port validation, and Excel dropdown guidance for web access.
- Added per-session Backspace choices and explicit review before replacing a changed SSH host key.
- Unified popup styling and Device Explorer action buttons.
- Migrated saved-device metadata to SQLite schema 6. Existing devices remain CLI only; database downgrade to v1.0.0 is unsupported.

## [1.0.0] - 2026-09-28

- Released SSH, Telnet, Serial / COM, Raw TCP, PowerShell, and Command Prompt sessions for Windows x64.
- Added Quick Connect, saved devices and groups, Favorites and Recent, credential profiles, terminal tabs and split panes, search, reconnect, optional output logging, Input Lock, and keyboard shortcuts.
- Added explicit SSH host-key trust, changed-key blocking, protected reusable credentials, and review before multiline terminal paste.
- Added JSON and Excel device import/export, CSV import, and the dark Device Explorer workspace.
- Packaged a self-contained all-users MSI with install-folder and shortcut choices and a portable ZIP.
