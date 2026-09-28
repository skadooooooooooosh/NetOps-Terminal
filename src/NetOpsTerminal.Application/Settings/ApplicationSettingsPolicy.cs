using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Settings;

public static class ApplicationSettingsPolicy
{
    public const double DefaultSidebarWidth = 264;
    public const double MinimumSidebarWidth = 208;
    public const double MaximumSidebarWidth = 420;

    private const double PreviousWideDefaultSidebarWidth = 248;
    private const double PreviousCompactDefaultSidebarWidth = 200;
    private const double PreviousWorkstationDefaultSidebarWidth = 232;

    public static ApplicationSettings Normalize(ApplicationSettings? settings)
    {
        if (settings is null)
        {
            return new ApplicationSettings();
        }

        var width = double.IsFinite(settings.SidebarWidth)
            ? Math.Clamp(settings.SidebarWidth, MinimumSidebarWidth, MaximumSidebarWidth)
            : DefaultSidebarWidth;

        // The previous defaults were tuned for the compact pass. Migrate those
        // untouched defaults to the approved larger workstation scale while
        // preserving explicitly resized sidebars.
        if (settings.SchemaVersion < ApplicationSettings.CurrentSchemaVersion &&
            (Math.Abs(settings.SidebarWidth - PreviousWideDefaultSidebarWidth) < 0.1 ||
             Math.Abs(settings.SidebarWidth - PreviousCompactDefaultSidebarWidth) < 0.1 ||
             Math.Abs(settings.SidebarWidth - PreviousWorkstationDefaultSidebarWidth) < 0.1))
        {
            width = DefaultSidebarWidth;
        }

        return settings with
        {
            SchemaVersion = ApplicationSettings.CurrentSchemaVersion,
            SidebarWidth = width,
            TerminalFontSize = TerminalFontSizePolicy.Normalize(settings.TerminalFontSize),
        };
    }
}
