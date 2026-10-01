using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.Application.Abstractions;

namespace NetOpsTerminal.Infrastructure.Persistence;

public sealed partial class SqliteDatabaseInitializer(
    SqliteConnectionFactory connectionFactory,
    ILogger<SqliteDatabaseInitializer> logger)
    : IApplicationDatabaseInitializer, IDisposable
{
    public const int CurrentSchemaVersion = 6;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public void Dispose()
    {
        _gate.Dispose();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using SqliteConnection connection = await connectionFactory
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            await using SqliteCommand versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            long version = (long)(await versionCommand
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0L);
            long startingVersion = version;
            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"The device database schema version {version} is newer than this application supports.");
            }

            if (version == 0)
            {
                await ApplyVersionOneAsync(connection, cancellationToken).ConfigureAwait(false);
                version = 1;
            }

            if (version == 1)
            {
                await ApplyVersionTwoAsync(connection, cancellationToken).ConfigureAwait(false);
                version = 2;
            }

            if (version == 2)
            {
                await ApplyVersionThreeAsync(connection, cancellationToken).ConfigureAwait(false);
                version = 3;
            }

            if (version == 3)
            {
                await ApplyVersionFourAsync(connection, cancellationToken).ConfigureAwait(false);
                version = 4;
            }

            if (version == 4)
            {
                await ApplyVersionFiveAsync(connection, cancellationToken).ConfigureAwait(false);
                version = 5;
            }

            if (version == 5)
            {
                await ApplyVersionSixAsync(connection, cancellationToken).ConfigureAwait(false);
                version = 6;
            }

            if (startingVersion != version)
            {
                await ValidateMigratedDatabaseAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
            LogDatabaseReady(logger, connectionFactory.DatabaseFilePath, version);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task ValidateMigratedDatabaseAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand quickCheck = connection.CreateCommand();
        quickCheck.CommandText = "PRAGMA quick_check;";
        string result = Convert.ToString(
            await quickCheck.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The device database failed its post-migration integrity check.");
        }

        await using SqliteCommand foreignKeyCheck = connection.CreateCommand();
        foreignKeyCheck.CommandText = "PRAGMA foreign_key_check;";
        await using SqliteDataReader violations = await foreignKeyCheck
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (await violations.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The device database contains a foreign-key violation after migration.");
        }

        await using SqliteCommand versionCheck = connection.CreateCommand();
        versionCheck.CommandText = "PRAGMA user_version;";
        long version = (long)(await versionCheck
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0L);
        if (version != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                "The device database migration did not reach the current schema.");
        }
    }

    private static async Task ApplyVersionSixAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE Devices ADD COLUMN AccessMode TEXT NOT NULL DEFAULT 'TerminalOnly';
            ALTER TABLE Devices ADD COLUMN WebUrl TEXT NULL;
            ALTER TABLE Devices ADD COLUMN BrowserPreference TEXT NOT NULL DEFAULT 'WindowsDefault';
            ALTER TABLE Devices ADD COLUMN DefaultOpenAction TEXT NOT NULL DEFAULT 'Terminal';
            PRAGMA user_version = 6;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyVersionFiveAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand columnCheck = connection.CreateCommand();
        columnCheck.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('DeviceGroups') WHERE name = 'ParentGroupId';";
        bool hasParentColumn = (long)(await columnCheck.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0L) > 0;

        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = (hasParentColumn ? string.Empty
            : "ALTER TABLE DeviceGroups ADD COLUMN ParentGroupId TEXT NULL REFERENCES DeviceGroups(Id) ON DELETE SET NULL;")
            + """
            DROP INDEX IF EXISTS IX_DeviceGroups_Site_Name;
            CREATE UNIQUE INDEX IF NOT EXISTS IX_DeviceGroups_Parent_Name
                ON DeviceGroups(IFNULL(SiteId, ''), IFNULL(ParentGroupId, ''), Name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS IX_DeviceGroups_ParentId ON DeviceGroups(ParentGroupId);
            CREATE TABLE IF NOT EXISTS ExplorerOrder (
                EntityId TEXT PRIMARY KEY NOT NULL,
                SortPosition INTEGER NOT NULL
            );
            PRAGMA user_version = 5;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyVersionFourAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand disableKeys = connection.CreateCommand())
        {
            disableKeys.CommandText = "PRAGMA foreign_keys = OFF;";
            await disableKeys.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE DevicesV4 (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    Description TEXT NOT NULL,
                    Hostname TEXT NOT NULL,
                    Port INTEGER NOT NULL CHECK (Port BETWEEN 1 AND 4000000),
                    ConnectionType TEXT NOT NULL CHECK (ConnectionType IN ('Ssh', 'Telnet', 'Serial', 'RawTcp')),
                    ProtocolOptions TEXT NOT NULL DEFAULT '{}',
                    DeviceType TEXT NOT NULL,
                    Vendor TEXT NOT NULL,
                    Model TEXT NOT NULL,
                    SiteId TEXT NULL,
                    GroupId TEXT NULL,
                    Environment TEXT NOT NULL,
                    CredentialProfileId TEXT NULL,
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
                INSERT INTO DevicesV4 (
                    Id, Name, Description, Hostname, Port, ConnectionType, ProtocolOptions,
                    DeviceType, Vendor, Model, SiteId, GroupId, Environment, CredentialProfileId,
                    UsernameOverride, IsFavorite, Notes, CreatedAtUtc, UpdatedAtUtc, LastConnectedAtUtc)
                SELECT Id, Name, Description, Hostname, Port, ConnectionType, '{}',
                    DeviceType, Vendor, Model, SiteId, GroupId, Environment, CredentialProfileId,
                    UsernameOverride, IsFavorite, Notes, CreatedAtUtc, UpdatedAtUtc, LastConnectedAtUtc
                FROM Devices;
                DROP TABLE Devices;
                ALTER TABLE DevicesV4 RENAME TO Devices;
                CREATE INDEX IX_Devices_Hostname ON Devices(Hostname COLLATE NOCASE);
                CREATE INDEX IX_Devices_SiteId ON Devices(SiteId);
                CREATE INDEX IX_Devices_GroupId ON Devices(GroupId);
                CREATE INDEX IX_Devices_CredentialProfileId ON Devices(CredentialProfileId);
                CREATE INDEX IX_Devices_Favorite ON Devices(IsFavorite);
                PRAGMA user_version = 4;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await using SqliteCommand enableKeys = connection.CreateCommand();
            enableKeys.CommandText = "PRAGMA foreign_keys = ON;";
            await enableKeys.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task ApplyVersionThreeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CommandSnippets (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL COLLATE NOCASE,
                CommandText TEXT NOT NULL,
                Category TEXT NOT NULL COLLATE NOCASE,
                Vendor TEXT NOT NULL COLLATE NOCASE,
                IsDestructive INTEGER NOT NULL CHECK (IsDestructive IN (0, 1)),
                RequiresConfirmation INTEGER NOT NULL CHECK (RequiresConfirmation IN (0, 1)),
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_CommandSnippets_Name
                ON CommandSnippets(Name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS IX_CommandSnippets_CategoryVendor
                ON CommandSnippets(Category COLLATE NOCASE, Vendor COLLATE NOCASE);

            PRAGMA user_version = 3;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyVersionTwoAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS WorkspaceState (
                Id INTEGER PRIMARY KEY NOT NULL CHECK (Id = 1),
                DocumentJson TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            PRAGMA user_version = 2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyVersionOneAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Sites (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE
            );

            CREATE TABLE IF NOT EXISTS DeviceGroups (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL COLLATE NOCASE,
                SiteId TEXT NULL,
                FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE SET NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_DeviceGroups_Site_Name
                ON DeviceGroups(IFNULL(SiteId, ''), Name COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS CredentialProfiles (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                Username TEXT NOT NULL,
                SecretReference TEXT NOT NULL UNIQUE,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Devices (
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
            CREATE INDEX IF NOT EXISTS IX_Devices_Hostname ON Devices(Hostname COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS IX_Devices_SiteId ON Devices(SiteId);
            CREATE INDEX IF NOT EXISTS IX_Devices_GroupId ON Devices(GroupId);
            CREATE INDEX IF NOT EXISTS IX_Devices_CredentialProfileId ON Devices(CredentialProfileId);
            CREATE INDEX IF NOT EXISTS IX_Devices_Favorite ON Devices(IsFavorite);

            CREATE TABLE IF NOT EXISTS RecentSessions (
                DeviceId TEXT PRIMARY KEY NOT NULL,
                LastConnectedAtUtc TEXT NOT NULL,
                ConnectionCount INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (DeviceId) REFERENCES Devices(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_RecentSessions_LastConnectedAt
                ON RecentSessions(LastConnectedAtUtc DESC);

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 501,
        Level = LogLevel.Information,
        Message = "Device database ready at {DatabasePath} with schema version {SchemaVersion}")]
    private static partial void LogDatabaseReady(
        ILogger logger,
        string databasePath,
        long schemaVersion);
}
