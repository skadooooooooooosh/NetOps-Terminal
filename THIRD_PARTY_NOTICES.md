# Third-Party Notices

NetOps Terminal uses third-party packages pinned through `Directory.Packages.props`. The complete resolved graph can be inspected with:

```powershell
dotnet list NetOpsTerminal.sln package --include-transitive
```

## RoyalTerminal

- Packages: `RoyalApps.RoyalTerminal.Avalonia`, `RoyalApps.RoyalTerminal.Terminal.Transport.Ssh.SshNet`, `RoyalApps.RoyalTerminal.Terminal.Transport.Telnet`, `RoyalApps.RoyalTerminal.Terminal.Transport.Serial`, and `RoyalApps.RoyalTerminal.Terminal.Transport.Raw` 0.5.2
- Project: <https://github.com/royalapplications/RoyalTerminal>
- License: MIT
- Use in NetOps Terminal: terminal rendering, managed VT processing, selection/clipboard behavior, local platform PTY integration, and the isolated SSH terminal transport

The terminal renderer and SSH, Telnet, Serial, and Raw TCP providers are explicitly active behind the NetOps-owned adapter boundary as of Phase 5.

## Fluent UI System Icons

- Project: <https://github.com/microsoft/fluentui-system-icons>
- License: MIT
- Use in NetOps Terminal: centralized regular outline geometry for the application logo and other existing controls

## Lucide Icons

- Project: <https://lucide.dev/icons/>
- License: ISC, <https://github.com/lucide-icons/lucide/blob/main/LICENSE>
- Use in NetOps Terminal: ten Lucide outline SVG geometries adapted to Avalonia vector paths for the Device Explorer rail. Compact device-category icon concepts also use the catalog as a visual reference. No Lucide package or external image is loaded at runtime.

## System.IO.Ports

- Package: `System.IO.Ports` 10.0.12
- Project: <https://github.com/dotnet/runtime>
- License: MIT
- Use in NetOps Terminal: Windows COM-port discovery and serial transport support behind the terminal adapter boundary

## SSH.NET

- Package: SSH.NET 2026.0.0
- Project: <https://github.com/sshnet/SSH.NET>
- License: MIT
- Use in NetOps Terminal: SSH key exchange, password authentication, connection cancellation/timeouts, and SHA256-pinned server host-key validation behind the RoyalTerminal adapter boundary

## Microsoft.Data.Sqlite

- Package: `Microsoft.Data.Sqlite` 10.0.12
- Project: <https://learn.microsoft.com/dotnet/standard/data/sqlite/>
- License: MIT
- Use in NetOps Terminal: application-owned, schema-versioned persistence for device, site, group, favorite, recent-session, and credential-profile metadata; credential secret values are not stored in SQLite

## Other packages

Avalonia, CommunityToolkit.Mvvm, Microsoft.Extensions packages, xUnit, coverlet, and their transitive dependencies retain their respective upstream licenses and notices. Every release package includes `documentation/package-inventory.json` with the resolved application dependency graph. Review that inventory and the upstream licenses for the exact artifact before distribution; the Phase 8 vulnerability audit reported no known vulnerable direct or transitive packages.

## WiX Toolset UI extension

- Package: `WixToolset.UI.wixext` 5.0.2 (installer build and dialog assets)
- Project: <https://wixtoolset.org/>
- License: Microsoft Reciprocal License (MS-RL), <https://github.com/wixtoolset/wix/blob/main/LICENSE.TXT>
- Use in NetOps Terminal: Windows Installer wizard dialogs, including the agreement and install-directory picker. The application does not load this extension at runtime.
