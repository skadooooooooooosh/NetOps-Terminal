using System.Text.Json;
using Microsoft.Data.Sqlite;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Infrastructure.Persistence;

public sealed class SqliteDeviceRepository(SqliteConnectionFactory connectionFactory)
    : IDeviceRepository
{
    public async Task<IReadOnlyList<DeviceProfile>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {SqliteMapping.DeviceColumns} FROM Devices ORDER BY Name COLLATE NOCASE;";
        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var devices = new List<DeviceProfile>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            devices.Add(SqliteMapping.ReadDevice(reader));
        }

        return devices;
    }

    public async Task<DeviceProfile?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {SqliteMapping.DeviceColumns} FROM Devices WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString("D"));
        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? SqliteMapping.ReadDevice(reader)
            : null;
    }

    public async Task SaveAsync(DeviceProfile device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        await using SqliteConnection connection = await connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Devices (
                Id, Name, Description, Hostname, Port, ConnectionType, ProtocolOptions, DeviceType, Vendor, Model,
                SiteId, GroupId, Environment, CredentialProfileId, UsernameOverride, IsFavorite,
                Notes, CreatedAtUtc, UpdatedAtUtc, LastConnectedAtUtc)
            VALUES (
                @id, @name, @description, @host, @port, @connectionType, @protocolOptions, @deviceType, @vendor, @model,
                @siteId, @groupId, @environment, @credentialId, @usernameOverride, @favorite,
                @notes, @created, @updated, @lastConnected)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                Hostname = excluded.Hostname,
                Port = excluded.Port,
                ConnectionType = excluded.ConnectionType,
                ProtocolOptions = excluded.ProtocolOptions,
                DeviceType = excluded.DeviceType,
                Vendor = excluded.Vendor,
                Model = excluded.Model,
                SiteId = excluded.SiteId,
                GroupId = excluded.GroupId,
                Environment = excluded.Environment,
                CredentialProfileId = excluded.CredentialProfileId,
                UsernameOverride = excluded.UsernameOverride,
                IsFavorite = excluded.IsFavorite,
                Notes = excluded.Notes,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("@id", device.Id.ToString("D"));
        command.Parameters.AddWithValue("@name", device.Name);
        command.Parameters.AddWithValue("@description", device.Description);
        command.Parameters.AddWithValue("@host", device.Connection.Host);
        command.Parameters.AddWithValue("@port", device.Connection.Port);
        command.Parameters.AddWithValue("@connectionType", device.ConnectionType.ToString());
        command.Parameters.AddWithValue("@protocolOptions", JsonSerializer.Serialize(device.Connection));
        command.Parameters.AddWithValue("@deviceType", device.DeviceType.ToString());
        command.Parameters.AddWithValue("@vendor", device.Vendor);
        command.Parameters.AddWithValue("@model", device.Model);
        command.Parameters.AddWithValue("@siteId", (object?)device.SiteId?.ToString("D") ?? DBNull.Value);
        command.Parameters.AddWithValue("@groupId", (object?)device.GroupId?.ToString("D") ?? DBNull.Value);
        command.Parameters.AddWithValue("@environment", device.Environment.ToString());
        command.Parameters.AddWithValue("@credentialId", (object?)device.CredentialProfileId?.ToString("D") ?? DBNull.Value);
        command.Parameters.AddWithValue("@usernameOverride", (object?)device.UsernameOverride ?? DBNull.Value);
        command.Parameters.AddWithValue("@favorite", device.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("@notes", device.Notes);
        command.Parameters.AddWithValue("@created", SqliteMapping.ToStorage(device.CreatedAtUtc));
        command.Parameters.AddWithValue("@updated", SqliteMapping.ToStorage(device.UpdatedAtUtc));
        command.Parameters.AddWithValue(
            "@lastConnected",
            device.LastConnectedAtUtc is { } lastConnected
                ? SqliteMapping.ToStorage(lastConnected)
                : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Devices WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountByCredentialAsync(
        Guid credentialProfileId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Devices WHERE CredentialProfileId = @id;";
        command.Parameters.AddWithValue("@id", credentialProfileId.ToString("D"));
        long count = (long)(await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0L);
        return checked((int)count);
    }
}
