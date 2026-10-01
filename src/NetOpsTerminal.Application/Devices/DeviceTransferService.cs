using NetOpsTerminal.Core.Connections;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Application.Devices;

public sealed partial class DeviceTransferService(IDeviceCatalogService catalog) : IDeviceTransferService
{
    public const int CurrentSchemaVersion = 3;
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
        DeviceImportPreviewItem[] items = document.Devices.Select(x =>
        {
            DeviceProfile? conflict = FindConflict(data.Devices, x);
            bool hasCredential = x.AccessMode == DeviceAccessMode.WebOnly
                || x.ConnectionType != ConnectionType.Ssh
                || FindCredential(data, x) is not null
                || conflict?.CredentialProfileId is { } credentialId
                    && data.Credentials.Any(credential => credential.Id == credentialId);
            return new DeviceImportPreviewItem(
                x.Name, x.Host, x.Port, x.SiteName, x.GroupName, x.CredentialName, x.CredentialUsername,
                hasCredential, conflict is not null)
            {
                ConnectionType = x.ConnectionType,
                AccessMode = x.AccessMode,
                WebAddress = x.WebAddress,
            };
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

    private async Task<DeviceImportResult> ImportItemsAsync(
        IReadOnlyList<Item> items, DeviceCatalogSnapshot data,
        DeviceImportConflictPolicy policy, Guid? fallback, CancellationToken token)
    {
        int added = 0, updated = 0, skipped = 0, credentialRequired = 0;
        var usedNames = data.Devices.Select(device => device.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (Item item in items)
        {
            DeviceProfile? conflict = FindConflict(data.Devices, item);
            if (conflict is not null && policy == DeviceImportConflictPolicy.Skip)
            {
                skipped++;
                continue;
            }

            Guid? credential = item.AccessMode != DeviceAccessMode.WebOnly
                && item.ConnectionType == ConnectionType.Ssh
                ? FindCredential(data, item)?.Id
                    ?? conflict?.CredentialProfileId
                    ?? fallback
                : null;
            if (item.AccessMode != DeviceAccessMode.WebOnly
                && item.ConnectionType == ConnectionType.Ssh
                && (credential is null || data.Credentials.All(x => x.Id != credential)))
            {
                credentialRequired++;
                continue;
            }

            bool update = conflict is not null && policy == DeviceImportConflictPolicy.Update;
            Guid id = update ? conflict!.Id : Guid.NewGuid();
            string name = conflict is not null && policy == DeviceImportConflictPolicy.KeepBoth
                ? CreateCopyName(item.Name, usedNames)
                : item.Name.Trim();
            await catalog.SaveAsync(CreateImportRequest(item, id, name, credential), token);
            usedNames.Add(name);
            if (item.IsRecent)
            {
                await catalog.RecordRecentAsync(id, DateTimeOffset.UtcNow, token);
            }

            if (update) updated++;
            else added++;
        }

        return new(added, updated, skipped, credentialRequired);
    }

    private static string CreateCopyName(string original, HashSet<string> usedNames)
    {
        for (int number = 1; ; number++)
        {
            string suffix = number == 1 ? " (imported)" : $" (imported {number})";
            string candidate = original.Trim();
            if (candidate.Length + suffix.Length > 160)
            {
                candidate = candidate[..(160 - suffix.Length)].TrimEnd();
            }

            candidate += suffix;
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }
    private static DeviceEditRequest CreateImportRequest(Item x, Guid? id, string name, Guid? credential)
    {
        DeviceWebAccessSettings? webAccess = x.AccessMode == DeviceAccessMode.TerminalOnly
            ? null : new DeviceWebAccessSettings(x.WebAddress, x.BrowserPreference, x.DefaultOpenAction);
        return new DeviceEditRequest(id, name, x.Description,
            x.AccessMode == DeviceAccessMode.WebOnly
                ? new Uri(webAccess!.Address).Host : x.Host,
            x.AccessMode == DeviceAccessMode.WebOnly ? 22 : x.Port,
            x.DeviceType, x.Vendor, x.Model, x.SiteName, x.GroupName, x.Environment,
            credential, x.AccessMode != DeviceAccessMode.WebOnly && x.ConnectionType == ConnectionType.Ssh
                ? x.UsernameOverride : null,
            x.IsFavorite, x.Notes)
        {
            ConnectionType = x.ConnectionType,
            AccessMode = x.AccessMode,
            WebAccess = webAccess,
            TerminalType = x.TerminalType,
            Encoding = x.Encoding,
            ConnectTimeoutSeconds = x.ConnectTimeoutSeconds,
            SerialDataBits = x.SerialDataBits,
            SerialParity = x.SerialParity,
            SerialStopBits = x.SerialStopBits,
            SerialFlowControl = x.SerialFlowControl,
            RawLineEnding = x.RawLineEnding,
            TelnetAcknowledged = x.TelnetAcknowledged,
            BackspaceSendsControlH = x.BackspaceSendsControlH,
            BackspaceSendsDelete = x.BackspaceSendsDelete,
        };
    }


    private static Item CreateItem(DeviceProfile x, DeviceCatalogSnapshot data, DeviceExportOptions options)
    {
        string site = data.Sites.FirstOrDefault(y => y.Id == x.SiteId)?.Name ?? string.Empty;
        string group = GetFolderPath(data.Groups, x.GroupId);
        var credential = data.Credentials.FirstOrDefault(y => y.Id == x.CredentialProfileId);
        return new Item(x.Name, x.Description, x.Connection.Host, x.Connection.Port,
            x.DeviceType, x.Vendor, x.Model, site, group, x.Environment,
            credential?.Name ?? string.Empty, credential?.Username ?? string.Empty,
            x.UsernameOverride, options.IncludeFavorites && x.IsFavorite,
            options.IncludeRecent && data.RecentDeviceIds.Contains(x.Id), x.Notes)
        {
            ConnectionType = x.ConnectionType,
            AccessMode = x.AccessMode,
            WebAddress = x.WebAccess?.Address ?? string.Empty,
            BrowserPreference = x.WebAccess?.Browser ?? WebBrowserPreference.WindowsDefault,
            DefaultOpenAction = x.WebAccess?.DefaultAction ?? DeviceOpenAction.Terminal,
            TerminalType = x.Connection.TerminalType,
            Encoding = x.Connection.Encoding,
            ConnectTimeoutSeconds = x.Connection.ConnectTimeoutSeconds,
            SerialDataBits = x.Connection.SerialDataBits,
            SerialParity = x.Connection.SerialParity,
            SerialStopBits = x.Connection.SerialStopBits,
            SerialFlowControl = x.Connection.SerialFlowControl,
            RawLineEnding = x.Connection.RawLineEnding,
            TelnetAcknowledged = x.Connection.TelnetAcknowledged,
            BackspaceSendsControlH = x.Connection.BackspaceSendsControlH,
            BackspaceSendsDelete = x.Connection.BackspaceSendsDelete,
        };
    }

    private static string GetFolderPath(IReadOnlyList<DeviceGroup> groups, Guid? folderId)
    {
        var names = new Stack<string>();
        var visited = new HashSet<Guid>();
        while (folderId is { } id && visited.Add(id))
        {
            DeviceGroup? folder = groups.FirstOrDefault(group => group.Id == id);
            if (folder is null)
                break;
            names.Push(folder.Name);
            folderId = folder.ParentGroupId;
        }

        return string.Join(" / ", names);
    }

    private static Core.Credentials.CredentialProfile? FindCredential(DeviceCatalogSnapshot data, Item item)
        => data.Credentials.FirstOrDefault(x =>
            string.Equals(x.Name, item.CredentialName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Username, item.CredentialUsername, StringComparison.OrdinalIgnoreCase));

    private static DeviceProfile? FindConflict(IEnumerable<DeviceProfile> devices, Item item)
        => devices.FirstOrDefault(x => string.Equals(
                x.Name, item.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(x => item.AccessMode == DeviceAccessMode.WebOnly
                ? x.AccessMode == DeviceAccessMode.WebOnly
                    && string.Equals(x.WebAccess?.Address, item.WebAddress, StringComparison.OrdinalIgnoreCase)
                : x.AccessMode != DeviceAccessMode.WebOnly
                    && x.ConnectionType == item.ConnectionType
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
            if (!Enum.IsDefined(item.AccessMode))
                throw new InvalidDataException($"Device '{item.Name}' has an invalid access mode.");
            if (item.AccessMode == DeviceAccessMode.WebOnly && item.ConnectionType != ConnectionType.Ssh)
                throw new InvalidDataException($"Device '{item.Name}' cannot combine GUI-only access with a terminal protocol.");
            if (item.ConnectionType == ConnectionType.Ssh && item.Port != 22)
                throw new InvalidDataException($"Device '{item.Name}' uses SSH port {item.Port}. SSH imports must use port 22.");
            if (item.ConnectionType == ConnectionType.Telnet && item.Port != 23)
                throw new InvalidDataException($"Device '{item.Name}' uses Telnet port {item.Port}. Telnet imports must use port 23.");
            DeviceEditRequest request = CreateImportRequest(item, null, item.Name,
                item.ConnectionType == ConnectionType.Ssh && item.AccessMode != DeviceAccessMode.WebOnly
                    ? Guid.NewGuid() : null);
            if (item.AccessMode != DeviceAccessMode.WebOnly)
                _ = DeviceCatalogService.CreateConnection(request);
            _ = new DeviceProfile(Guid.NewGuid(), item.Name, item.Description,
                new SavedDeviceConnectionSettings(item.ConnectionType, request.Host, request.Port,
                    item.TerminalType, item.Encoding, item.ConnectTimeoutSeconds,
                    item.SerialDataBits, item.SerialParity, item.SerialStopBits,
                    item.SerialFlowControl, item.RawLineEnding, item.TelnetAcknowledged,
                    item.BackspaceSendsControlH, item.BackspaceSendsDelete), item.DeviceType,
                item.Vendor, item.Model, null, null, item.Environment,
                request.CredentialProfileId, request.UsernameOverride, item.IsFavorite,
                item.Notes, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null,
                request.AccessMode, request.WebAccess);
        }
        return document;
    }

    private static Document RequireSupported(Document? document)
        => document?.SchemaVersion is 1 or 2 or CurrentSchemaVersion
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
        public DeviceAccessMode AccessMode { get; init; } = DeviceAccessMode.TerminalOnly;
        public string WebAddress { get; init; } = string.Empty;
        public WebBrowserPreference BrowserPreference { get; init; } = WebBrowserPreference.WindowsDefault;
        public DeviceOpenAction DefaultOpenAction { get; init; } = DeviceOpenAction.Terminal;
        public string TerminalType { get; init; } = "xterm-256color";
        public string Encoding { get; init; } = "Utf8";
        public int ConnectTimeoutSeconds { get; init; } = 15;
        public int SerialDataBits { get; init; } = 8;
        public string SerialParity { get; init; } = "None";
        public string SerialStopBits { get; init; } = "One";
        public string SerialFlowControl { get; init; } = "None";
        public string RawLineEnding { get; init; } = "CarriageReturnLineFeed";
        public bool TelnetAcknowledged { get; init; }
        public bool BackspaceSendsControlH { get; init; }
        public bool BackspaceSendsDelete { get; init; }
    }
}
