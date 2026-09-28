using Microsoft.Extensions.Logging.Abstractions;
using NetOpsTerminal.Application.Settings;
using NetOpsTerminal.Infrastructure.Platform;
using NetOpsTerminal.Infrastructure.Settings;

namespace NetOpsTerminal.Infrastructure.Tests;

public sealed class JsonApplicationSettingsStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "NetOpsTerminal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsyncReturnsNullWhenFileDoesNotExist()
    {
        using var store = CreateStore();

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripsNonSecretUiSettings()
    {
        using var store = CreateStore();
        var expected = new ApplicationSettings
        {
            IsSidebarCollapsed = true,
            SidebarWidth = 344,
        };

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LegacyThemePreferenceIsRemovedDuringSettingsMigration()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(
            SettingsPath,
            """
            {
              "schemaVersion": 2,
              "theme": "Light",
              "isSidebarCollapsed": true,
              "sidebarWidth": 300,
              "terminalFontSize": 13
            }
            """,
            CancellationToken.None);
        using var store = CreateStore();
        var service = new ApplicationSettingsService(store);

        var settings = await service.LoadAsync(CancellationToken.None);
        var migratedJson = await File.ReadAllTextAsync(SettingsPath, CancellationToken.None);

        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.True(settings.IsSidebarCollapsed);
        Assert.DoesNotContain("\"theme\"", migratedJson, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task LoadAsyncReturnsNullForMalformedJson()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(SettingsPath, "{ malformed", CancellationToken.None);
        using var store = CreateStore();

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.Null(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private string SettingsPath => Path.Combine(_testDirectory, "settings.json");

    private JsonApplicationSettingsStore CreateStore()
    {
        return new JsonApplicationSettingsStore(
            new FixedPathProvider(SettingsPath),
            NullLogger<JsonApplicationSettingsStore>.Instance);
    }

    private sealed record FixedPathProvider(string SettingsFilePath) : IApplicationDataPathProvider
    {
        public string KnownHostsFilePath => Path.Combine(
            Path.GetDirectoryName(SettingsFilePath)!,
            "known_hosts");
    }
}
