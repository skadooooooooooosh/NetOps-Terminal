using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Devices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NetOpsTerminal.Infrastructure.Persistence;
using NetOpsTerminal.Infrastructure.Platform;

namespace NetOpsTerminal.Infrastructure.Tests;

public sealed class Phase8MigrationTests
{
    [Fact]
    public async Task FreshInstallCreatesCurrentValidSchema()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        using var initializer = CreateInitializer(factory);

        await initializer.InitializeAsync(CancellationToken.None);

        Assert.Equal(SqliteDatabaseInitializer.CurrentSchemaVersion, await ReadVersionAsync(factory));
        Assert.Equal("ok", await ReadTextAsync(factory, "PRAGMA quick_check;"));
        Assert.Equal(1L, await ReadLongAsync(
            factory,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'CommandSnippets';"));
    }

    [Fact]
    public async Task VersionFourUpgradeAddsNestedFoldersWithoutLosingGroups()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        await CreateCurrentDatabaseAsync(factory);
        var sites = new SqliteSiteRepository(factory);
        var groups = new SqliteDeviceGroupRepository(factory);
        Site site = await sites.GetOrCreateAsync("Existing site", CancellationToken.None);
        DeviceGroup group = await groups.GetOrCreateAsync(
            "Existing group", site.Id, CancellationToken.None);
        await ExecuteAsync(factory, """
            DROP INDEX IX_DeviceGroups_Parent_Name;
            DROP INDEX IX_DeviceGroups_ParentId;
            DROP TABLE ExplorerOrder;
            ALTER TABLE Devices DROP COLUMN DefaultOpenAction;
            ALTER TABLE Devices DROP COLUMN BrowserPreference;
            ALTER TABLE Devices DROP COLUMN WebUrl;
            ALTER TABLE Devices DROP COLUMN AccessMode;
            ALTER TABLE DeviceGroups DROP COLUMN ParentGroupId;
            CREATE UNIQUE INDEX IX_DeviceGroups_Site_Name
                ON DeviceGroups(IFNULL(SiteId, ''), Name COLLATE NOCASE);
            PRAGMA user_version = 4;
            """);

        using var initializer = CreateInitializer(factory);
        await initializer.InitializeAsync(CancellationToken.None);

        Assert.Equal(SqliteDatabaseInitializer.CurrentSchemaVersion, await ReadVersionAsync(factory));
        DeviceGroup restored = Assert.Single(
            await groups.GetAllAsync(CancellationToken.None));
        Assert.Equal(group.Id, restored.Id);
        Assert.Null(restored.ParentGroupId);
        DeviceGroup nested = await groups.GetOrCreateChildAsync(
            "Child", site.Id, restored.Id, CancellationToken.None);
        Assert.Equal(restored.Id, nested.ParentGroupId);
    }

    [Fact]
    public async Task VersionFiveUpgradeDefaultsExistingDevicesToTerminalOnly()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        await CreateCurrentDatabaseAsync(factory);
        await ExecuteAsync(factory, """
            ALTER TABLE Devices DROP COLUMN DefaultOpenAction;
            ALTER TABLE Devices DROP COLUMN BrowserPreference;
            ALTER TABLE Devices DROP COLUMN WebUrl;
            ALTER TABLE Devices DROP COLUMN AccessMode;
            INSERT INTO Devices
                (Id, Name, Description, Hostname, Port, ConnectionType, ProtocolOptions,
                 DeviceType, Vendor, Model, Environment, IsFavorite, Notes, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ('33333333-3333-3333-3333-333333333333', 'Legacy terminal', '', 'legacy.test',
                 23, 'Telnet', '{}', 'Switch', '', '', 'Unknown', 0, '',
                 '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
            PRAGMA user_version = 5;
            """);
        using var initializer = CreateInitializer(factory);

        await initializer.InitializeAsync(CancellationToken.None);

        Assert.Equal(SqliteDatabaseInitializer.CurrentSchemaVersion, await ReadVersionAsync(factory));
        var repository = new SqliteDeviceRepository(factory);
        DeviceProfile loaded = Assert.IsType<DeviceProfile>(await repository.GetAsync(
            Guid.Parse("33333333-3333-3333-3333-333333333333"), CancellationToken.None));
        Assert.Equal(DeviceAccessMode.TerminalOnly, loaded.AccessMode);
        Assert.Null(loaded.WebAccess);
    }
    [Fact]
    public async Task VersionOneUpgradePreservesSavedDeviceMetadata()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        await CreateCurrentDatabaseAsync(factory);
        await ExecuteAsync(
            factory,
            """
            INSERT INTO CredentialProfiles
                (Id, Name, Username, SecretReference, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ('11111111-1111-1111-1111-111111111111', 'Legacy credential', 'operator',
                 'NetOpsTerminal:test-profile', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');

            INSERT INTO Devices
                (Id, Name, Description, Hostname, Port, ConnectionType, DeviceType, Vendor, Model,
                 SiteId, GroupId, Environment, CredentialProfileId, UsernameOverride, IsFavorite,
                 Notes, CreatedAtUtc, UpdatedAtUtc, LastConnectedAtUtc)
            VALUES
                ('22222222-2222-2222-2222-222222222222', 'Legacy switch', '', 'switch.test', 22,
                 'Ssh', 'Switch', 'Test Vendor', 'Test Model', NULL, NULL, 'Lab',
                 '11111111-1111-1111-1111-111111111111', NULL, 1, '',
                 '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00', NULL);

            DROP TABLE WorkspaceState;
            DROP TABLE CommandSnippets;
            PRAGMA user_version = 1;
            """);

        using var initializer = CreateInitializer(factory);
        await initializer.InitializeAsync(CancellationToken.None);

        Assert.Equal(SqliteDatabaseInitializer.CurrentSchemaVersion, await ReadVersionAsync(factory));
        Assert.Equal(1L, await ReadLongAsync(
            factory,
            "SELECT COUNT(*) FROM Devices WHERE Name = 'Legacy switch' AND Hostname = 'switch.test';"));
        Assert.Equal(1L, await ReadLongAsync(
            factory,
            "SELECT COUNT(*) FROM CredentialProfiles WHERE Name = 'Legacy credential';"));
        Assert.Equal(1L, await ReadLongAsync(
            factory,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'WorkspaceState';"));
        Assert.Equal(1L, await ReadLongAsync(
            factory,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'CommandSnippets';"));
    }

    [Fact]
    public async Task VersionTwoUpgradePreservesWorkspaceDocument()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        await CreateCurrentDatabaseAsync(factory);
        await ExecuteAsync(
            factory,
            """
            INSERT INTO WorkspaceState (Id, DocumentJson, UpdatedAtUtc)
            VALUES (1, '{"version":1,"tabs":[]}', '2026-01-01T00:00:00+00:00');
            DROP TABLE CommandSnippets;
            PRAGMA user_version = 2;
            """);

        using var initializer = CreateInitializer(factory);
        await initializer.InitializeAsync(CancellationToken.None);

        Assert.Equal(SqliteDatabaseInitializer.CurrentSchemaVersion, await ReadVersionAsync(factory));
        Assert.Equal(
            "{\"version\":1,\"tabs\":[]}",
            await ReadTextAsync(factory, "SELECT DocumentJson FROM WorkspaceState WHERE Id = 1;"));
        Assert.Equal(1L, await ReadLongAsync(
            factory,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'CommandSnippets';"));
    }

    [Fact]
    public async Task VersionThreeUpgradePreservesSshDeviceAndRecentHistory()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        await CreateCurrentDatabaseAsync(factory);
        await ExecuteAsync(factory, """
            INSERT INTO CredentialProfiles
                (Id, Name, Username, SecretReference, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('11111111-1111-1111-1111-111111111111', 'Legacy', 'operator',
                'NetOpsTerminal:test', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
            INSERT INTO Devices
                (Id, Name, Description, Hostname, Port, ConnectionType, DeviceType, Vendor, Model,
                 SiteId, GroupId, Environment, CredentialProfileId, UsernameOverride, IsFavorite,
                 Notes, CreatedAtUtc, UpdatedAtUtc, LastConnectedAtUtc)
            VALUES ('22222222-2222-2222-2222-222222222222', 'Existing SSH', '', 'ssh.test',
                22, 'Ssh', 'Switch', '', '', NULL, NULL, 'Lab',
                '11111111-1111-1111-1111-111111111111', NULL, 0, '',
                '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00', NULL);
            INSERT INTO RecentSessions (DeviceId, LastConnectedAtUtc, ConnectionCount)
            VALUES ('22222222-2222-2222-2222-222222222222', '2026-01-02T00:00:00+00:00', 3);
            PRAGMA foreign_keys = OFF;
            CREATE TABLE DevicesOld (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                Description TEXT NOT NULL,
                Hostname TEXT NOT NULL,
                Port INTEGER NOT NULL CHECK (Port BETWEEN 1 AND 65535),
                ConnectionType TEXT NOT NULL CHECK (ConnectionType = 'Ssh'),
                DeviceType TEXT NOT NULL,
                Vendor TEXT NOT NULL,
                Model TEXT NOT NULL,
                SiteId TEXT NULL,
                GroupId TEXT NULL,
                Environment TEXT NOT NULL,
                CredentialProfileId TEXT NOT NULL,
                UsernameOverride TEXT NULL,
                IsFavorite INTEGER NOT NULL CHECK (IsFavorite IN (0, 1)),
                Notes TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                LastConnectedAtUtc TEXT NULL,
                FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE SET NULL,
                FOREIGN KEY (GroupId) REFERENCES DeviceGroups(Id) ON DELETE SET NULL,
                FOREIGN KEY (CredentialProfileId) REFERENCES CredentialProfiles(Id) ON DELETE RESTRICT
            );
            INSERT INTO DevicesOld
                SELECT Id, Name, Description, Hostname, Port, ConnectionType, DeviceType,
                    Vendor, Model, SiteId, GroupId, Environment, CredentialProfileId,
                    UsernameOverride, IsFavorite, Notes, CreatedAtUtc, UpdatedAtUtc,
                    LastConnectedAtUtc FROM Devices;
            DROP TABLE Devices;
            ALTER TABLE DevicesOld RENAME TO Devices;
            PRAGMA user_version = 3;
            PRAGMA foreign_keys = ON;
            """);

        using var initializer = CreateInitializer(factory);
        await initializer.InitializeAsync(CancellationToken.None);
        var repository = new SqliteDeviceRepository(factory);
        DeviceProfile loaded = Assert.IsType<DeviceProfile>(
            await repository.GetAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"),
                CancellationToken.None));

        Assert.Equal(ConnectionType.Ssh, loaded.ConnectionType);
        Assert.Equal("ssh.test", loaded.Connection.Host);
        Assert.Equal(3L, await ReadLongAsync(factory,
            "SELECT ConnectionCount FROM RecentSessions WHERE DeviceId = '22222222-2222-2222-2222-222222222222';"));
        Assert.Equal("ok", await ReadTextAsync(factory, "PRAGMA quick_check;"));
    }

    [Theory]
    [InlineData(ConnectionType.Telnet, "legacy.test", 23)]
    [InlineData(ConnectionType.Serial, "COM3", 115200)]
    [InlineData(ConnectionType.RawTcp, "console.test", 2001)]
    public async Task SavedNonSshProtocolRoundTripsWithoutCredential(
        ConnectionType type, string host, int port)
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        using var initializer = CreateInitializer(factory);
        await initializer.InitializeAsync(CancellationToken.None);
        var repository = new SqliteDeviceRepository(factory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var device = new DeviceProfile(
            Guid.NewGuid(), type.ToString(), string.Empty,
            new SavedDeviceConnectionSettings(type, host, port,
                encoding: "Ascii", serialDataBits: 7, rawLineEnding: "LineFeed",
                telnetAcknowledged: type == ConnectionType.Telnet),
            DeviceType.Other, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Lab, null, null, false, string.Empty, now, now, null);

        await repository.SaveAsync(device, CancellationToken.None);
        DeviceProfile loaded = Assert.IsType<DeviceProfile>(
            await repository.GetAsync(device.Id, CancellationToken.None));

        Assert.Equal(type, loaded.ConnectionType);
        Assert.Equal(host, loaded.Connection.Host);
        Assert.Equal(port, loaded.Connection.Port);
        Assert.Equal("Ascii", loaded.Connection.Encoding);
        Assert.Equal(7, loaded.Connection.SerialDataBits);
        Assert.Equal("LineFeed", loaded.Connection.RawLineEnding);
        Assert.Equal(type == ConnectionType.Telnet, loaded.Connection.TelnetAcknowledged);
        Assert.Null(loaded.CredentialProfileId);
    }

    [Fact]
    public async Task FutureSchemaIsRejectedWithoutChangingVersion()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        await CreateCurrentDatabaseAsync(factory);
        await ExecuteAsync(factory, "PRAGMA user_version = 99;");
        using var initializer = CreateInitializer(factory);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.InitializeAsync(CancellationToken.None));

        Assert.Contains("newer than this application supports", error.Message, StringComparison.Ordinal);
        Assert.Equal(99L, await ReadVersionAsync(factory));
    }

    private static SqliteDatabaseInitializer CreateInitializer(SqliteConnectionFactory factory)
    {
        return new SqliteDatabaseInitializer(
            factory,
            NullLogger<SqliteDatabaseInitializer>.Instance);
    }

    private static async Task CreateCurrentDatabaseAsync(SqliteConnectionFactory factory)
    {
        using var initializer = CreateInitializer(factory);
        await initializer.InitializeAsync(CancellationToken.None);
    }

    private static Task<long> ReadVersionAsync(SqliteConnectionFactory factory)
    {
        return ReadLongAsync(factory, "PRAGMA user_version;");
    }

    private static async Task<long> ReadLongAsync(
        SqliteConnectionFactory factory,
        string commandText)
    {
        await using SqliteConnection connection = await factory.OpenAsync(CancellationToken.None);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        object? value = await command.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadTextAsync(
        SqliteConnectionFactory factory,
        string commandText)
    {
        await using SqliteConnection connection = await factory.OpenAsync(CancellationToken.None);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        object? value = await command.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task ExecuteAsync(
        SqliteConnectionFactory factory,
        string commandText)
    {
        await using SqliteConnection connection = await factory.OpenAsync(CancellationToken.None);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private sealed class TemporaryStorage : IApplicationDataPathProvider, IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"netops-terminal-phase8-migrations-{Guid.NewGuid():N}");

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
