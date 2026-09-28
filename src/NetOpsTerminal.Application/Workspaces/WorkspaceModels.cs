using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Workspaces;

public enum PaneSplitDirection
{
    Right,
    Down,
}

public enum WorkspaceLaunchKind
{
    LocalShell,
    SavedDevice,
    TransientConnection,
}

public sealed record WorkspaceLaunchReference
{
    public required WorkspaceLaunchKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public ConnectionType ConnectionType { get; init; }
    public LocalShellKind? ShellKind { get; init; }
    public Guid? DeviceId { get; init; }
    public DeviceEnvironment Environment { get; init; } = DeviceEnvironment.Unknown;
    public bool CanReconnect => Kind is WorkspaceLaunchKind.LocalShell or WorkspaceLaunchKind.SavedDevice;
}

public sealed record PaneNodeDocument
{
    public required Guid NodeId { get; init; }
    public bool IsSplit { get; init; }
    public PaneSplitDirection SplitDirection { get; init; }
    public double SplitRatio { get; init; } = 0.5;
    public PaneNodeDocument? First { get; init; }
    public PaneNodeDocument? Second { get; init; }
    public WorkspaceLaunchReference? Launch { get; init; }
}

public sealed record WorkspaceTabDocument
{
    public required Guid TabId { get; init; }
    public required Guid ActivePaneId { get; init; }
    public required PaneNodeDocument Root { get; init; }
}

public sealed record WorkspaceDocument
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public Guid? ActiveTabId { get; init; }
    public IReadOnlyList<WorkspaceTabDocument> Tabs { get; init; } = [];
}
