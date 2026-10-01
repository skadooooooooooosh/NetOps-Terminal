using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Application.Devices;

public enum DeviceImportConflictPolicy { KeepBoth, Skip, Update }

public enum DeviceTransferFormat { Json, Csv, Excel }

public sealed record DeviceExportOptions(IReadOnlyCollection<Guid>? DeviceIds = null, bool IncludeFavorites = true, bool IncludeRecent = true);

public sealed record DeviceImportPreviewItem(string Name, string Host, int Port, string SiteName, string GroupName, string CredentialName, string CredentialUsername, bool HasCredentialMatch, bool HasConflict)
{
    public ConnectionType ConnectionType { get; init; } = ConnectionType.Ssh;
    public DeviceAccessMode AccessMode { get; init; } = DeviceAccessMode.TerminalOnly;
    public string WebAddress { get; init; } = string.Empty;
    public string EndpointLabel => AccessMode == DeviceAccessMode.WebOnly
        ? $"WEB · {new Uri(WebAddress).Authority}"
        : ConnectionType == ConnectionType.Serial
        ? $"SERIAL · {Host} · {Port} baud"
        : $"{(ConnectionType == ConnectionType.RawTcp ? "RAW TCP" : ConnectionType.ToString().ToUpperInvariant())} · {Host}:{Port}";
}

public sealed record DeviceImportPreview(int SchemaVersion, DateTimeOffset ExportedAtUtc, IReadOnlyList<DeviceImportPreviewItem> Devices)
{
    public int DeviceCount => Devices.Count;
    public int GroupCount => Devices.Select(x => (x.SiteName, x.GroupName)).Where(x => !string.IsNullOrWhiteSpace(x.GroupName)).Distinct().Count();
    public int CredentialRequiredCount => Devices.Count(x => !x.HasCredentialMatch);
    public int ConflictCount => Devices.Count(x => x.HasConflict);
}

public sealed record DeviceImportResult(int ImportedCount, int UpdatedCount, int SkippedCount, int CredentialRequiredCount);
