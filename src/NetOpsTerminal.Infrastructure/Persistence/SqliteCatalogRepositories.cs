using Microsoft.Data.Sqlite;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Infrastructure.Persistence;

public sealed class SqliteSiteRepository(SqliteConnectionFactory connectionFactory)
    : ISiteRepository
{
    public async Task<IReadOnlyList<Site>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM Sites ORDER BY Name COLLATE NOCASE;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var sites = new List<Site>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sites.Add(new Site(Guid.Parse(reader.GetString(0)), reader.GetString(1)));
        }

        return sites;
    }

    public async Task<Site> GetOrCreateAsync(string name, CancellationToken cancellationToken)
    {
        var candidate = new Site(Guid.NewGuid(), name);
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Sites (Id, Name) VALUES (@id, @name)
            ON CONFLICT(Name) DO NOTHING;
            SELECT Id, Name FROM Sites WHERE Name = @name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("@id", candidate.Id.ToString("D"));
        command.Parameters.AddWithValue("@name", candidate.Name);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The site could not be created.");
        }

        return new Site(Guid.Parse(reader.GetString(0)), reader.GetString(1));
    }
}

public sealed class SqliteDeviceGroupRepository(SqliteConnectionFactory connectionFactory)
    : IDeviceGroupRepository
{
    public async Task<IReadOnlyList<DeviceGroup>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, SiteId FROM DeviceGroups ORDER BY Name COLLATE NOCASE;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var groups = new List<DeviceGroup>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            groups.Add(new DeviceGroup(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                SqliteMapping.ReadNullableGuid(reader, 2)));
        }

        return groups;
    }

    public async Task<DeviceGroup> GetOrCreateAsync(
        string name,
        Guid? siteId,
        CancellationToken cancellationToken)
    {
        var candidate = new DeviceGroup(Guid.NewGuid(), name, siteId);
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DeviceGroups (Id, Name, SiteId) VALUES (@id, @name, @siteId)
            ON CONFLICT DO NOTHING;
            SELECT Id, Name, SiteId
            FROM DeviceGroups
            WHERE Name = @name COLLATE NOCASE
              AND ((SiteId IS NULL AND @siteId IS NULL) OR SiteId = @siteId);
            """;
        command.Parameters.AddWithValue("@id", candidate.Id.ToString("D"));
        command.Parameters.AddWithValue("@name", candidate.Name);
        command.Parameters.AddWithValue("@siteId", (object?)siteId?.ToString("D") ?? DBNull.Value);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The device group could not be created.");
        }

        return new DeviceGroup(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            SqliteMapping.ReadNullableGuid(reader, 2));
    }
}

public sealed class SqliteCredentialProfileRepository(SqliteConnectionFactory connectionFactory)
    : ICredentialProfileRepository
{
    public async Task<IReadOnlyList<CredentialProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Username, SecretReference, CreatedAtUtc, UpdatedAtUtc
            FROM CredentialProfiles ORDER BY Name COLLATE NOCASE;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var profiles = new List<CredentialProfile>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            profiles.Add(SqliteMapping.ReadCredential(reader));
        }

        return profiles;
    }

    public async Task<CredentialProfile?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Username, SecretReference, CreatedAtUtc, UpdatedAtUtc
            FROM CredentialProfiles WHERE Id = @id;
            """;
        command.Parameters.AddWithValue("@id", id.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? SqliteMapping.ReadCredential(reader)
            : null;
    }

    public async Task SaveAsync(CredentialProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CredentialProfiles (
                Id, Name, Username, SecretReference, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@id, @name, @username, @secretReference, @created, @updated)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Username = excluded.Username,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("@id", profile.Id.ToString("D"));
        command.Parameters.AddWithValue("@name", profile.Name);
        command.Parameters.AddWithValue("@username", profile.Username);
        command.Parameters.AddWithValue("@secretReference", profile.SecretReference);
        command.Parameters.AddWithValue("@created", SqliteMapping.ToStorage(profile.CreatedAtUtc));
        command.Parameters.AddWithValue("@updated", SqliteMapping.ToStorage(profile.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CredentialProfiles WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class SqliteRecentDeviceRepository(SqliteConnectionFactory connectionFactory)
    : IRecentDeviceRepository
{
    public async Task<IReadOnlyList<Guid>> GetRecentDeviceIdsAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT DeviceId FROM RecentSessions
            ORDER BY LastConnectedAtUtc DESC LIMIT @maximumCount;
            """;
        command.Parameters.AddWithValue("@maximumCount", maximumCount);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(Guid.Parse(reader.GetString(0)));
        }

        return ids;
    }

    public async Task RecordConnectionAsync(
        Guid deviceId,
        DateTimeOffset connectedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Devices SET LastConnectedAtUtc = @connectedAt WHERE Id = @deviceId;
            INSERT INTO RecentSessions (DeviceId, LastConnectedAtUtc, ConnectionCount)
            VALUES (@deviceId, @connectedAt, 1)
            ON CONFLICT(DeviceId) DO UPDATE SET
                LastConnectedAtUtc = excluded.LastConnectedAtUtc,
                ConnectionCount = RecentSessions.ConnectionCount + 1;
            """;
        command.Parameters.AddWithValue("@deviceId", deviceId.ToString("D"));
        command.Parameters.AddWithValue("@connectedAt", SqliteMapping.ToStorage(connectedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
