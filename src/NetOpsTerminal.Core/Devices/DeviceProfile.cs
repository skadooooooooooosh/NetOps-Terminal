using NetOpsTerminal.Core.Connections;

namespace NetOpsTerminal.Core.Devices;

public sealed record DeviceProfile
{
    public DeviceProfile(
        Guid id,
        string name,
        string description,
        SshDeviceConnectionSettings connection,
        DeviceType deviceType,
        string vendor,
        string model,
        Guid? siteId,
        Guid? groupId,
        DeviceEnvironment environment,
        Guid credentialProfileId,
        string? usernameOverride,
        bool isFavorite,
        string notes,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? lastConnectedAtUtc)
        : this(id, name, description,
            new SavedDeviceConnectionSettings(ConnectionType.Ssh, connection.Host, connection.Port),
            deviceType, vendor, model, siteId, groupId, environment, credentialProfileId,
            usernameOverride, isFavorite, notes, createdAtUtc, updatedAtUtc, lastConnectedAtUtc)
    {
    }

    public DeviceProfile(
        Guid id,
        string name,
        string description,
        SavedDeviceConnectionSettings connection,
        DeviceType deviceType,
        string vendor,
        string model,
        Guid? siteId,
        Guid? groupId,
        DeviceEnvironment environment,
        Guid? credentialProfileId,
        string? usernameOverride,
        bool isFavorite,
        string notes,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? lastConnectedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A device ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 160
            || name.Any(char.IsControl))
        {
            throw new ArgumentException("Enter a valid device name.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(connection);
        if (connection.Type == ConnectionType.Ssh && (credentialProfileId is null || credentialProfileId == Guid.Empty))
        {
            throw new ArgumentException("An SSH credential profile is required.", nameof(credentialProfileId));
        }
        if (connection.Type != ConnectionType.Ssh && credentialProfileId is not null)
        {
            throw new ArgumentException("Only SSH devices use credential profiles.", nameof(credentialProfileId));
        }

        if (usernameOverride is { Length: > 255 }
            || usernameOverride?.Any(char.IsControl) == true)
        {
            throw new ArgumentException("Enter a valid username override.", nameof(usernameOverride));
        }

        Id = id;
        Name = name.Trim();
        Description = NormalizeOptional(description, 500, nameof(description));
        Connection = connection;
        DeviceType = deviceType;
        Vendor = NormalizeOptional(vendor, 120, nameof(vendor));
        Model = NormalizeOptional(model, 120, nameof(model));
        SiteId = siteId;
        GroupId = groupId;
        Environment = environment;
        CredentialProfileId = credentialProfileId;
        UsernameOverride = string.IsNullOrWhiteSpace(usernameOverride)
            ? null
            : usernameOverride.Trim();
        IsFavorite = isFavorite;
        Notes = NormalizeOptional(notes, 4_000, nameof(notes));
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        LastConnectedAtUtc = lastConnectedAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Description { get; }

    public ConnectionType ConnectionType => Connection.Type;

    public SavedDeviceConnectionSettings Connection { get; }

    public DeviceType DeviceType { get; }

    public string Vendor { get; }

    public string Model { get; }

    public Guid? SiteId { get; }

    public Guid? GroupId { get; }

    public DeviceEnvironment Environment { get; }

    public Guid? CredentialProfileId { get; }

    public string? UsernameOverride { get; }

    public bool IsFavorite { get; }

    public string Notes { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public DateTimeOffset? LastConnectedAtUtc { get; }

    private static string NormalizeOptional(string value, int maximumLength, string parameterName)
    {
        value ??= string.Empty;
        if (value.Length > maximumLength || value.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
        {
            throw new ArgumentException($"The value must be {maximumLength} characters or fewer and contain no unsupported control characters.", parameterName);
        }

        return value.Trim();
    }
}
