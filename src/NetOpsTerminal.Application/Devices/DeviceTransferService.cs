using NetOpsTerminal.Core.Connections;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Application.Devices;

public sealed partial class DeviceTransferService(IDeviceCatalogService catalog) : IDeviceTransferService
{
    public const int CurrentSchemaVersion = 2;
    public const int MaximumDeviceCount = 10_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public async Task<string> ExportAsync(DeviceExportOptions options, CancellationToken cancellationToken)
    {
        Item[] items = await CreateExportItemsAsync(options, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new Document(CurrentSchemaVersion, DateTimeOffset.UtcNow, items), JsonOptions);
    }

    private async Task<Item[]> CreateExportItemsAsync(
        DeviceExportOptions options,
        CancellationToken cancellationToken)
    {
        DeviceCatalogSnapshot data = await catalog
            .LoadAsync(null, DeviceCatalogFilter.All, cancellationToken).ConfigureAwait(false);
        HashSet<Guid>? ids = options.DeviceIds?.ToHashSet();
        return data.Devices.Where(x => ids is null || ids.Contains(x.Id))
            .Select(x => CreateItem(x, data, options)).ToArray();
    }

    public async Task<DeviceImportPreview> ReviewImportAsync(string json, CancellationToken cancellationToken)
    {
        Document document = Parse(json);
        DeviceCatalogSnapshot data = await catalog.LoadAsync(null, DeviceCatalogFilter.All, cancellationToken).ConfigureAwait(false);
        DeviceImportPreviewItem[] items = document.Devices.Select(x => new DeviceImportPreviewItem(
            x.Name, x.Host, x.Port, x.SiteName, x.GroupName, x.CredentialName, x.CredentialUsername,
            x.ConnectionType != ConnectionType.Ssh || FindCredential(data, x) is not null,
            FindConflict(data.Devices, x) is not null)
        {
            ConnectionType = x.ConnectionType,
        }).ToArray();
        return new DeviceImportPreview(document.SchemaVersion, document.ExportedAtUtc, items);
    }
    public Task<DeviceImportResult> ImportAsync(string json, DeviceImportConflictPolicy conflictPolicy, Guid? fallbackCredentialId, CancellationToken cancellationToken)
        => ImportCoreAsync(Parse(json), conflictPolicy, fallbackCredentialId, cancellationToken);

    private async Task<DeviceImportResult> ImportCoreAsync(Document doc, DeviceImportConflictPolicy policy, Guid? fallback, CancellationToken token)
    {
        var data = await catalog.LoadAsync(null, DeviceCatalogFilter.All, token);
        return await ImportItemsAsync(doc.Devices, data, policy, fallback, token);
    }

    private async Task<DeviceImportResult> ImportItemsAsync(IReadOnlyList<Item> items, DeviceCatalogSnapshot data, DeviceImportConflictPolicy policy, Guid? fallback, CancellationToken token)
    {
        int a = 0, u = 0, s = 0, r = 0;
        foreach (Item item in items)
        {
            var n = await ImportOneAsync(item, data, policy, fallback, token);
            a += n.Added; u += n.Updated; s += n.Skipped; r += n.Required;
        }
        return new(a, u, s, r);
    }

    private async Task<(int Added, int Updated, int Skipped, int Required)> ImportOneAsync(Item item, DeviceCatalogSnapshot data, DeviceImportConflictPolicy policy, Guid? fallback, CancellationToken token)
    {
        DeviceProfile? conflict = FindConflict(data.Devices, item);
        if (conflict is not null && policy == DeviceImportConflictPolicy.Skip) return (0, 0, 1, 0);
        Guid? credential = item.ConnectionType == ConnectionType.Ssh
            ? FindCredential(data, item)?.Id ?? fallback
            : null;
        if (item.ConnectionType == ConnectionType.Ssh
            && (credential is null || data.Credentials.All(x => x.Id != credential)))
            return (0, 0, 0, 1);
        return await SaveImportedAsync(item, conflict, policy, credential, token);
    }

    private async Task<(int Added, int Updated, int Skipped, int Required)> SaveImportedAsync(Item item, DeviceProfile? conflict, DeviceImportConflictPolicy policy, Guid? credential, CancellationToken token)
    {
        bool update = conflict is not null && policy == DeviceImportConflictPolicy.Update;
        Guid id = update ? conflict!.Id : Guid.NewGuid();
        return await SaveResolvedAsync(item, conflict, policy, credential, id, update, token);
    }

    private async Task<(int Added, int Updated, int Skipped, int Required)> SaveResolvedAsync(Item item, DeviceProfile? conflict, DeviceImportConflictPolicy policy, Guid? credential, Guid id, bool update, CancellationToken token)
    {
        return await SaveFinalAsync(item, conflict, policy, credential, id, update, token);
    }

    private async Task<(int Added, int Updated, int Skipped, int Required)> SaveFinalAsync(Item item, DeviceProfile? conflict, DeviceImportConflictPolicy policy, Guid? credential, Guid id, bool update, CancellationToken token)
    {
        string name = conflict is not null && policy == DeviceImportConflictPolicy.KeepBoth
            ? $"{item.Name} (imported)"
            : item.Name;
        await SaveRequestAsync(item, id, name, credential, token);
        return await FinishSaveAsync(item, id, update, token);
    }

    private Task SaveRequestAsync(Item x, Guid id, string name, Guid? credential, CancellationToken token)
    {
        return catalog.SaveAsync(CreateImportRequest(x, id, name, credential), token);
    }

    private static DeviceEditRequest CreateImportRequest(Item x, Guid? id, string name, Guid? credential)
        => new(id, name, x.Description, x.Host, x.Port,
            x.DeviceType, x.Vendor, x.Model, x.SiteName, x.GroupName, x.Environment,
            credential, x.ConnectionType == ConnectionType.Ssh ? x.UsernameOverride : null,
            x.IsFavorite, x.Notes)
        {
            ConnectionType = x.ConnectionType,
            TerminalType = x.TerminalType,
            Encoding = x.Encoding,
            ConnectTimeoutSeconds = x.ConnectTimeoutSeconds,
            SerialDataBits = x.SerialDataBits,
            SerialParity = x.SerialParity,
            SerialStopBits = x.SerialStopBits,
            SerialFlowControl = x.SerialFlowControl,
            RawLineEnding = x.RawLineEnding,
            TelnetAcknowledged = x.TelnetAcknowledged,
        };

    private async Task<(int, int, int, int)> FinishSaveAsync(Item item, Guid id, bool update, CancellationToken token)
    {
        if (item.IsRecent) await catalog.RecordRecentAsync(id, DateTimeOffset.UtcNow, token);
        return update ? (0, 1, 0, 0) : (1, 0, 0, 0);
    }

    private static Item CreateItem(DeviceProfile x, DeviceCatalogSnapshot data, DeviceExportOptions options)
    {
        string site = data.Sites.FirstOrDefault(y => y.Id == x.SiteId)?.Name ?? string.Empty;
        string group = data.Groups.FirstOrDefault(y => y.Id == x.GroupId)?.Name ?? string.Empty;
        var credential = data.Credentials.FirstOrDefault(y => y.Id == x.CredentialProfileId);
        return new Item(x.Name, x.Description, x.Connection.Host, x.Connection.Port,
            x.DeviceType, x.Vendor, x.Model, site, group, x.Environment,
            credential?.Name ?? string.Empty, credential?.Username ?? string.Empty,
            x.UsernameOverride, options.IncludeFavorites && x.IsFavorite,
            options.IncludeRecent && data.RecentDeviceIds.Contains(x.Id), x.Notes)
        {
            ConnectionType = x.ConnectionType,
            TerminalType = x.Connection.TerminalType,
            Encoding = x.Connection.Encoding,
            ConnectTimeoutSeconds = x.Connection.ConnectTimeoutSeconds,
            SerialDataBits = x.Connection.SerialDataBits,
            SerialParity = x.Connection.SerialParity,
            SerialStopBits = x.Connection.SerialStopBits,
            SerialFlowControl = x.Connection.SerialFlowControl,
            RawLineEnding = x.Connection.RawLineEnding,
            TelnetAcknowledged = x.Connection.TelnetAcknowledged,
        };
    }

    private static Core.Credentials.CredentialProfile? FindCredential(DeviceCatalogSnapshot data, Item item)
        => data.Credentials.FirstOrDefault(x =>
            string.Equals(x.Name, item.CredentialName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Username, item.CredentialUsername, StringComparison.OrdinalIgnoreCase));

    private static DeviceProfile? FindConflict(IEnumerable<DeviceProfile> devices, Item item)
        => devices.FirstOrDefault(x => x.ConnectionType == item.ConnectionType
            && x.Connection.Port == item.Port
            && string.Equals(x.Connection.Host, item.Host, StringComparison.OrdinalIgnoreCase));

    private static Document Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("The selected file is empty.");
        return Deserialize(json);
    }

    private static Document Deserialize(string json)
    {
        try
        {
            return Validate(JsonSerializer.Deserialize<Document>(json, JsonOptions));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("This is not a valid NetOps Terminal device export.", exception);
        }
    }

    private static Document Validate(Document? document)
    {
        return ValidateCore(document);
    }

    private static Document ValidateCore(Document? document)
    {
        Document d = RequireSupported(document);
        return ValidateItems(d);
    }

    private static Document ValidateItems(Document document)
    {
        if (document.Devices is null || document.Devices.Count > MaximumDeviceCount)
            throw new InvalidDataException($"A device export may contain at most {MaximumDeviceCount:N0} devices.");
        foreach (Item item in document.Devices)
        {
            DeviceEditRequest request = CreateImportRequest(item, null, item.Name,
                item.ConnectionType == ConnectionType.Ssh ? Guid.NewGuid() : null);
            _ = DeviceCatalogService.CreateConnection(request);
            _ = new DeviceProfile(Guid.NewGuid(), item.Name, item.Description,
                new SavedDeviceConnectionSettings(item.ConnectionType, item.Host, item.Port,
                    item.TerminalType, item.Encoding, item.ConnectTimeoutSeconds,
                    item.SerialDataBits, item.SerialParity, item.SerialStopBits,
                    item.SerialFlowControl, item.RawLineEnding, item.TelnetAcknowledged), item.DeviceType,
                item.Vendor, item.Model, null, null, item.Environment,
                request.CredentialProfileId, request.UsernameOverride, item.IsFavorite,
                item.Notes, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        }
        return document;
    }

    private static Document RequireSupported(Document? document)
        => document?.SchemaVersion is 1 or CurrentSchemaVersion
            ? document
            : throw new InvalidDataException("This device export version is not supported.");

    private sealed record Document(int SchemaVersion, DateTimeOffset ExportedAtUtc, IReadOnlyList<Item> Devices);

    private sealed record Item(
        string Name, string Description, string Host, int Port,
        DeviceType DeviceType, string Vendor, string Model,
        string SiteName, string GroupName, DeviceEnvironment Environment,
        string CredentialName, string CredentialUsername, string? UsernameOverride,
        bool IsFavorite, bool IsRecent, string Notes)
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
}
