using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Settings;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Tests;

public sealed class ApplicationSettingsPolicyTests
{
    [Fact]
    public void NormalizeUsesDefaultsForMissingSettings()
    {
        var result = ApplicationSettingsPolicy.Normalize(null);
        Assert.False(result.IsSidebarCollapsed);
        Assert.Equal(ApplicationSettingsPolicy.DefaultSidebarWidth, result.SidebarWidth);
        Assert.Equal(TerminalFontSizePolicy.Default, result.TerminalFontSize);
    }

    [Theory]
    [InlineData(1, TerminalFontSizePolicy.Minimum)]
    [InlineData(100, TerminalFontSizePolicy.Maximum)]
    public void NormalizeClampsTerminalFontSize(int input, int expected)
    {
        var result = ApplicationSettingsPolicy.Normalize(
            new ApplicationSettings { TerminalFontSize = input });

        Assert.Equal(expected, result.TerminalFontSize);
    }

    [Theory]
    [InlineData(10, ApplicationSettingsPolicy.MinimumSidebarWidth)]
    [InlineData(1_000, ApplicationSettingsPolicy.MaximumSidebarWidth)]
    public void NormalizeClampsSidebarWidth(double input, double expected)
    {
        var result = ApplicationSettingsPolicy.Normalize(new ApplicationSettings { SidebarWidth = input });

        Assert.Equal(expected, result.SidebarWidth);
    }

    [Fact]
    public async Task ServiceNormalizesBeforeSaving()
    {
        var store = new RecordingStore();
        var service = new ApplicationSettingsService(store);

        await service.SaveAsync(
            new ApplicationSettings { SidebarWidth = -20 },
            CancellationToken.None);

        Assert.NotNull(store.Saved);
        Assert.Equal(ApplicationSettingsPolicy.MinimumSidebarWidth, store.Saved.SidebarWidth);
    }

    [Fact]
    public async Task LoadMigratesOlderSettingsSchema()
    {
        var store = new RecordingStore(
            new ApplicationSettings { SchemaVersion = 2, SidebarWidth = 300 });
        var service = new ApplicationSettingsService(store);

        var result = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, result.SchemaVersion);
        Assert.NotNull(store.Saved);
        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, store.Saved.SchemaVersion);
        Assert.Equal(300, store.Saved.SidebarWidth);
    }

    [Fact]
    public void NormalizeMigratesThePreviousDefaultSidebarWidths()
    {
        var wideResult = ApplicationSettingsPolicy.Normalize(
            new ApplicationSettings { SchemaVersion = 3, SidebarWidth = 248 });
        var compactResult = ApplicationSettingsPolicy.Normalize(
            new ApplicationSettings { SchemaVersion = 4, SidebarWidth = 200 });
        var workstationResult = ApplicationSettingsPolicy.Normalize(
            new ApplicationSettings { SchemaVersion = 5, SidebarWidth = 232 });

        Assert.Equal(ApplicationSettingsPolicy.DefaultSidebarWidth, wideResult.SidebarWidth);
        Assert.Equal(ApplicationSettingsPolicy.DefaultSidebarWidth, compactResult.SidebarWidth);
        Assert.Equal(ApplicationSettingsPolicy.DefaultSidebarWidth, workstationResult.SidebarWidth);
        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, wideResult.SchemaVersion);
        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, compactResult.SchemaVersion);
        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, workstationResult.SchemaVersion);
    }
    private sealed class RecordingStore(ApplicationSettings? loaded = null) : IApplicationSettingsStore
    {
        public ApplicationSettings? Saved { get; private set; }

        public Task<ApplicationSettings?> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(loaded);
        }

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }
}
