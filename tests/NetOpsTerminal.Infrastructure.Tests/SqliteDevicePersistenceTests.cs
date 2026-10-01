using NetOpsTerminal.Application.Devices;
using Microsoft.Data.Sqlite;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Infrastructure.Persistence;
using NetOpsTerminal.Infrastructure.Platform;

namespace NetOpsTerminal.Infrastructure.Tests;

public sealed class SqliteDevicePersistenceTests
{
    [Fact]
    public async Task InitializerCreatesVersionedSchemaWithoutPasswordColumns()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        using var initializer = new SqliteDatabaseInitializer(
            factory,
            NullLogger<SqliteDatabaseInitializer>.Instance);

        await initializer.InitializeAsync(CancellationToken.None);

        await using SqliteConnection connection = await factory.OpenAsync(CancellationToken.None);
        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        Assert.Equal(
            SqliteDatabaseInitializer.CurrentSchemaVersion,
            Convert.ToInt32(
                await versionCommand.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture));

        await using SqliteCommand columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = """
            SELECT name FROM pragma_table_info('Devices')
            UNION ALL SELECT name FROM pragma_table_info('CredentialProfiles');
            """;
        await using SqliteDataReader reader = await columnsCommand.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.DoesNotContain(
            columns,
            column => column.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("SecretReference", columns);
    }

    [Fact]
    public async Task RepositoriesRoundTripHierarchyFavoritesRecentsAndSharedCredential()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        using var initializer = new SqliteDatabaseInitializer(
            factory,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync(CancellationToken.None);

        var credentials = new SqliteCredentialProfileRepository(factory);
        var sites = new SqliteSiteRepository(factory);
        var groups = new SqliteDeviceGroupRepository(factory);
        var devices = new SqliteDeviceRepository(factory);
        var recents = new SqliteRecentDeviceRepository(factory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var credential = new CredentialProfile(
            Guid.NewGuid(),
            "Network Admin",
            "operator",
            "credential-test-reference",
            now,
            now);
        await credentials.SaveAsync(credential, CancellationToken.None);
        Site site = await sites.GetOrCreateAsync("Lab Site", CancellationToken.None);
        DeviceGroup group = await groups.GetOrCreateAsync(
            "Core",
            site.Id,
            CancellationToken.None);
        DeviceProfile first = CreateDevice(
            "LAB-SW-01",
            "192.0.2.10",
            site.Id,
            group.Id,
            credential.Id,
            isFavorite: true,
            now,
            backspaceSendsControlH: true);
        DeviceProfile second = CreateDevice(
            "LAB-SW-02",
            "192.0.2.11",
            site.Id,
            group.Id,
            credential.Id,
            isFavorite: false,
            now,
            backspaceSendsDelete: true);

        await devices.SaveAsync(first, CancellationToken.None);
        await devices.SaveAsync(second, CancellationToken.None);
        await recents.RecordConnectionAsync(
            second.Id,
            now.AddMinutes(1),
            CancellationToken.None);

        IReadOnlyList<DeviceProfile> loaded = await devices.GetAllAsync(CancellationToken.None);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(2, await devices.CountByCredentialAsync(credential.Id, CancellationToken.None));
        Assert.True(loaded.Single(device => device.Id == first.Id).IsFavorite);
        Assert.True(loaded.Single(device => device.Id == first.Id).Connection.BackspaceSendsControlH);
        Assert.False(loaded.Single(device => device.Id == second.Id).Connection.BackspaceSendsControlH);
        Assert.True(loaded.Single(device => device.Id == second.Id).Connection.BackspaceSendsDelete);
        Assert.False(loaded.Single(device => device.Id == first.Id).Connection.BackspaceSendsDelete);
        Assert.Equal(
            second.Id,
            Assert.Single(await recents.GetRecentDeviceIdsAsync(10, CancellationToken.None)));
        Assert.NotNull((await devices.GetAsync(second.Id, CancellationToken.None))!.LastConnectedAtUtc);
        Assert.Single(await sites.GetAllAsync(CancellationToken.None));
        Assert.Single(await groups.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SiteFoldersAndManualOrderSurviveReloadAndDeviceMove()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        using var initializer = new SqliteDatabaseInitializer(
            factory, NullLogger<SqliteDatabaseInitializer>.Instance);
        var devices = new SqliteDeviceRepository(factory);
        var credentials = new SqliteCredentialProfileRepository(factory);
        var sites = new SqliteSiteRepository(factory);
        var groups = new SqliteDeviceGroupRepository(factory);
        var order = new SqliteExplorerOrderRepository(factory);
        using var catalog = new DeviceCatalogService(
            initializer, devices, sites, groups, order, credentials,
            new SqliteRecentDeviceRepository(factory),
            NullLogger<DeviceCatalogService>.Instance);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var credential = new CredentialProfile(
            Guid.NewGuid(), "Admin", "operator", "protected-test-reference", now, now);
        await catalog.InitializeAsync(CancellationToken.None);
        await credentials.SaveAsync(credential, CancellationToken.None);
        Site site = await catalog.CreateSiteAsync("DC", CancellationToken.None);
        DeviceGroup root = await catalog.CreateFolderAsync(
            "Core", site.Id, null, CancellationToken.None);
        DeviceGroup child = await catalog.CreateFolderAsync(
            "Switches", site.Id, null, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            catalog.CreateFolderAsync("Nested", site.Id, root.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            catalog.CreateFolderAsync("Unassigned folder", null, null, CancellationToken.None));
        DeviceGroup sibling = await catalog.CreateFolderAsync(
            "Edge", site.Id, null, CancellationToken.None);
        var request = new DeviceEditRequest(
            null, "SW-01", "kept", "192.0.2.10", 22, DeviceType.Switch,
            string.Empty, string.Empty, "DC", "Core",
            DeviceEnvironment.Production, credential.Id, null, true, "note");
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.SaveAsync(
            request with { GroupName = "Core / Switches" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.SaveAsync(
            request with { SiteName = string.Empty }, CancellationToken.None));
        await catalog.SaveAsync(request, CancellationToken.None);
        DeviceProfile saved = Assert.Single(await devices.GetAllAsync(CancellationToken.None));
        Assert.Equal(root.Id, saved.GroupId);
        await catalog.MoveDeviceAsync(saved.Id, site.Id, sibling.Id, CancellationToken.None);
        await catalog.SaveExplorerOrderAsync([sibling.Id, root.Id], CancellationToken.None);

        DeviceCatalogSnapshot snapshot = await catalog.LoadAsync(
            null, DeviceCatalogFilter.All, CancellationToken.None);
        DeviceProfile moved = Assert.Single(snapshot.Devices);
        Assert.Equal(site.Id, moved.SiteId);
        Assert.Equal(sibling.Id, moved.GroupId);
        Assert.Equal("kept", moved.Description);
        Assert.True(moved.IsFavorite);
        Assert.Equal(credential.Id, moved.CredentialProfileId);
        Assert.Equal(root.Id, Assert.Single(snapshot.Groups, item => item.Id == root.Id).Id);
        Assert.Null(Assert.Single(snapshot.Groups, item => item.Id == child.Id).ParentGroupId);
        Assert.Equal(0, snapshot.ExplorerOrder![sibling.Id]);
        Assert.Equal(1, snapshot.ExplorerOrder[root.Id]);
        await catalog.ResetExplorerOrderAsync([root.Id], CancellationToken.None);
        DeviceCatalogSnapshot resetSnapshot = await catalog.LoadAsync(
            null, DeviceCatalogFilter.All, CancellationToken.None);
        Assert.False(resetSnapshot.ExplorerOrder!.ContainsKey(root.Id));
        Assert.Equal(0, resetSnapshot.ExplorerOrder[sibling.Id]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.DeleteEmptyFolderAsync(sibling.Id, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.DeleteEmptySiteAsync(site.Id, CancellationToken.None));
        await catalog.DeleteEmptyFolderAsync(child.Id, CancellationToken.None);
        Assert.DoesNotContain(await groups.GetAllAsync(CancellationToken.None),
            item => item.Id == child.Id);
    }

    [Fact]
    public async Task WebOnlyDeviceRoundTripsWithoutCredential()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        using var initializer = new SqliteDatabaseInitializer(
            factory, NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync(CancellationToken.None);
        var repository = new SqliteDeviceRepository(factory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var web = new DeviceWebAccessSettings("https://switch.test/gui",
            WebBrowserPreference.GoogleChrome, DeviceOpenAction.Web);
        var device = new DeviceProfile(Guid.NewGuid(), "GUI switch", string.Empty,
            new SavedDeviceConnectionSettings(NetOpsTerminal.Core.Connections.ConnectionType.Ssh,
                "switch.test", 22),
            DeviceType.Switch, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Unknown, null, null, false, string.Empty,
            now, now, null, DeviceAccessMode.WebOnly, web);

        await repository.SaveAsync(device, CancellationToken.None);
        DeviceProfile loaded = Assert.IsType<DeviceProfile>(
            await repository.GetAsync(device.Id, CancellationToken.None));

        Assert.Equal(DeviceAccessMode.WebOnly, loaded.AccessMode);
        Assert.Equal(web, loaded.WebAccess);
        Assert.Null(loaded.CredentialProfileId);
    }
    private static DeviceProfile CreateDevice(
        string name,
        string host,
        Guid siteId,
        Guid groupId,
        Guid credentialId,
        bool isFavorite,
        DateTimeOffset now,
        bool backspaceSendsControlH = false,
        bool backspaceSendsDelete = false)
    {
        return new DeviceProfile(
            Guid.NewGuid(),
            name,
            string.Empty,
            new SavedDeviceConnectionSettings(
                NetOpsTerminal.Core.Connections.ConnectionType.Ssh,
                host, 22, backspaceSendsControlH: backspaceSendsControlH,
                backspaceSendsDelete: backspaceSendsDelete),
            DeviceType.Switch,
            "Test Vendor",
            "Test Model",
            siteId,
            groupId,
            DeviceEnvironment.Lab,
            credentialId,
            null,
            isFavorite,
            string.Empty,
            now,
            now,
            null);
    }

    private sealed class TemporaryStorage : IApplicationDataPathProvider, IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"netops-terminal-sqlite-tests-{Guid.NewGuid():N}");

        public string SettingsFilePath => Path.Combine(_directory, "settings.json");

        public string KnownHostsFilePath => Path.Combine(_directory, "ssh", "known_hosts");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
