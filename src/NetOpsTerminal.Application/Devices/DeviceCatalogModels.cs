using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Core.Connections;

namespace NetOpsTerminal.Application.Devices;

public enum DeviceCatalogFilter
{
    All,
    Favorites,
    Recent,
}

public sealed record DeviceEditRequest(
    Guid? Id,
    string Name,
    string Description,
    string Host,
    int Port,
    DeviceType DeviceType,
    string Vendor,
    string Model,
    string SiteName,
    string GroupName,
    DeviceEnvironment Environment,
    Guid? CredentialProfileId,
    string? UsernameOverride,
    bool IsFavorite,
    string Notes)
{
    public ConnectionType ConnectionType { get; init; } = ConnectionType.Ssh;
    public string TerminalType { get; init; } = "xterm-256color";
    public string Encoding { get; init; } = "Utf8";
    public int ConnectTimeoutSeconds { get; init; } = 15;
    public int SerialDataBits { get; init; } = 8;
    public string SerialParity { get; init; } = "None";
    public string SerialStopBits { get; init; } = "One";
    public string SerialFlowControl { get; init; } = "None";
    public string RawLineEnding { get; init; } = "CarriageReturnLineFeed";
    public bool TelnetAcknowledged { get; init; }
}

public sealed record DeviceCatalogSnapshot(
    IReadOnlyList<DeviceProfile> Devices,
    IReadOnlyList<Site> Sites,
    IReadOnlyList<DeviceGroup> Groups,
    IReadOnlyList<CredentialProfile> Credentials,
    IReadOnlyList<Guid> RecentDeviceIds,
    int? AllDeviceCount = null,
    int? FavoriteDeviceCount = null,
    int? RecentDeviceCount = null);
