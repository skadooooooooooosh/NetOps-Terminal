using System.Text.Json;
using NetOpsTerminal.Core.Connections;
using System.Globalization;
using Microsoft.Data.Sqlite;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Infrastructure.Persistence;

internal static class SqliteMapping
{
    public static string ToStorage(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset FromStorage(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    public static Guid? ReadNullableGuid(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : Guid.Parse(reader.GetString(ordinal));
    }

    public static DeviceProfile ReadDevice(SqliteDataReader reader)
    {
        ConnectionType type = Enum.Parse<ConnectionType>(reader.GetString(5));
        string host = reader.GetString(3);
        int port = reader.GetInt32(4);
        string json = reader.GetString(6);
        SavedDeviceConnectionSettings connection = json == "{}"
            ? new SavedDeviceConnectionSettings(type, host, port)
            : JsonSerializer.Deserialize<SavedDeviceConnectionSettings>(json)
                ?? throw new InvalidDataException("Saved-device protocol options are missing.");
        if (connection.Type != type || connection.Host != host || connection.Port != port)
            throw new InvalidDataException("Saved-device protocol metadata is inconsistent.");

        DeviceAccessMode accessMode = Enum.Parse<DeviceAccessMode>(reader.GetString(20));
        DeviceWebAccessSettings? webAccess = accessMode == DeviceAccessMode.TerminalOnly
            ? null
            : new DeviceWebAccessSettings(
                reader.GetString(21),
                Enum.Parse<WebBrowserPreference>(reader.GetString(22)),
                Enum.Parse<DeviceOpenAction>(reader.GetString(23)));

        return new DeviceProfile(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            connection,
            Enum.Parse<DeviceType>(reader.GetString(7)),
            reader.GetString(8),
            reader.GetString(9),
            ReadNullableGuid(reader, 10),
            ReadNullableGuid(reader, 11),
            Enum.Parse<DeviceEnvironment>(reader.GetString(12)),
            ReadNullableGuid(reader, 13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.GetBoolean(15),
            reader.GetString(16),
            FromStorage(reader.GetString(17)),
            FromStorage(reader.GetString(18)),
            reader.IsDBNull(19) ? null : FromStorage(reader.GetString(19)),
            accessMode,
            webAccess);
    }

    public static CredentialProfile ReadCredential(SqliteDataReader reader)
    {
        return new CredentialProfile(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            FromStorage(reader.GetString(4)),
            FromStorage(reader.GetString(5)));
    }

    public const string DeviceColumns = """
        Id, Name, Description, Hostname, Port, ConnectionType, ProtocolOptions, DeviceType, Vendor, Model,
        SiteId, GroupId, Environment, CredentialProfileId, UsernameOverride, IsFavorite,
        Notes, CreatedAtUtc, UpdatedAtUtc, LastConnectedAtUtc,
        AccessMode, WebUrl, BrowserPreference, DefaultOpenAction
        """;
}
