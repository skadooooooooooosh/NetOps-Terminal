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
            now);
        DeviceProfile second = CreateDevice(
            "LAB-SW-02",
            "192.0.2.11",
            site.Id,
            group.Id,
            credential.Id,
            isFavorite: false,
            now);

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
        Assert.Equal(
            second.Id,
            Assert.Single(await recents.GetRecentDeviceIdsAsync(10, CancellationToken.None)));
        Assert.NotNull((await devices.GetAsync(second.Id, CancellationToken.None))!.LastConnectedAtUtc);
        Assert.Single(await sites.GetAllAsync(CancellationToken.None));
        Assert.Single(await groups.GetAllAsync(CancellationToken.None));
    }

    private static DeviceProfile CreateDevice(
        string name,
        string host,
        Guid siteId,
        Guid groupId,
        Guid credentialId,
        bool isFavorite,
        DateTimeOffset now)
    {
        return new DeviceProfile(
            Guid.NewGuid(),
            name,
            string.Empty,
            new SshDeviceConnectionSettings(host),
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
