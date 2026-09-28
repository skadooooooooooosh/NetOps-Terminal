using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Settings;

public sealed record ApplicationSettings
{
    public const int CurrentSchemaVersion = 6;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool IsSidebarCollapsed { get; init; }

    public double SidebarWidth { get; init; } = ApplicationSettingsPolicy.DefaultSidebarWidth;

    public int TerminalFontSize { get; init; } = TerminalFontSizePolicy.Default;
}
