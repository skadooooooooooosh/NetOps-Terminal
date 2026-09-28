using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Terminal.Abstractions;
using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;
using Microsoft.Extensions.Logging;

namespace NetOpsTerminal.Application.Devices;

public sealed partial class DeviceCatalogService(
    IApplicationDatabaseInitializer databaseInitializer,
    IDeviceRepository deviceRepository,
    ISiteRepository siteRepository,
    IDeviceGroupRepository groupRepository,
    ICredentialProfileRepository credentialRepository,
    IRecentDeviceRepository recentDeviceRepository,
    ILogger<DeviceCatalogService> logger)
    : IDeviceCatalogService, IDisposable
{
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;

    public void Dispose()
    {
        _initializationGate.Dispose();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_initialized)
            {
                await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
                _initialized = true;
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<DeviceCatalogSnapshot> LoadAsync(
        string? searchText,
        DeviceCatalogFilter filter,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DeviceProfile> allDevices = await deviceRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<Site> sites = await siteRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<DeviceGroup> groups = await groupRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<CredentialProfile> credentials = await credentialRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<Guid> recentIds = await recentDeviceRepository
            .GetRecentDeviceIdsAsync(50, cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<DeviceProfile> devices = allDevices;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string query = searchText.Trim();
            var siteNames = sites.ToDictionary(site => site.Id, site => site.Name);
            var groupNames = groups.ToDictionary(group => group.Id, group => group.Name);
            devices = devices.Where(device =>
                Contains(device.Name, query)
                || Contains(device.Connection.Host, query)
                || Contains(device.Vendor, query)
                || Contains(device.Model, query)
                || (device.SiteId is { } siteId
                    && siteNames.TryGetValue(siteId, out string? siteName)
                    && Contains(siteName, query))
                || (device.GroupId is { } groupId
                    && groupNames.TryGetValue(groupId, out string? groupName)
                    && Contains(groupName, query)));
        }

        if (filter == DeviceCatalogFilter.Favorites)
        {
            devices = devices.Where(device => device.IsFavorite);
        }
        else if (filter == DeviceCatalogFilter.Recent)
        {
            var order = recentIds
                .Select((id, index) => (id, index))
                .ToDictionary(item => item.id, item => item.index);
            devices = devices
                .Where(device => order.ContainsKey(device.Id))
                .OrderBy(device => order[device.Id]);
        }

        HashSet<Guid> allDeviceIds = allDevices
            .Select(device => device.Id)
            .ToHashSet();
        return new DeviceCatalogSnapshot(
            devices.ToArray(),
            sites,
            groups,
            credentials,
            recentIds,
            allDevices.Count,
            allDevices.Count(device => device.IsFavorite),
            recentIds.Count(allDeviceIds.Contains));
    }

    public async Task SaveAsync(DeviceEditRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        CredentialProfile? credential = null;
        if (request.ConnectionType == ConnectionType.Ssh)
        {
            if (request.CredentialProfileId is not { } credentialId)
                throw new InvalidOperationException("Select an existing SSH credential profile.");
            credential = await credentialRepository
                .GetAsync(credentialId, cancellationToken)
                .ConfigureAwait(false);
            if (credential is null)
                throw new InvalidOperationException("Select an existing SSH credential profile.");
        }
        else if (request.CredentialProfileId is not null || !string.IsNullOrWhiteSpace(request.UsernameOverride))
        {
            throw new ArgumentException("Only SSH devices can use saved credentials or a username override.");
        }

        SavedDeviceConnectionSettings connection = CreateConnection(request);

        Site? site = string.IsNullOrWhiteSpace(request.SiteName)
            ? null
            : await siteRepository
                .GetOrCreateAsync(request.SiteName, cancellationToken)
                .ConfigureAwait(false);
        DeviceGroup? group = string.IsNullOrWhiteSpace(request.GroupName)
            ? null
            : await groupRepository
                .GetOrCreateAsync(request.GroupName, site?.Id, cancellationToken)
                .ConfigureAwait(false);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeviceProfile? existing = request.Id is { } id
            ? await deviceRepository.GetAsync(id, cancellationToken).ConfigureAwait(false)
            : null;
        var device = new DeviceProfile(
            request.Id ?? Guid.NewGuid(),
            request.Name,
            request.Description,
            connection,
            request.DeviceType,
            request.Vendor,
            request.Model,
            site?.Id,
            group?.Id,
            request.Environment,
            credential?.Id,
            request.UsernameOverride,
            request.IsFavorite,
            request.Notes,
            existing?.CreatedAtUtc ?? now,
            now,
            existing?.LastConnectedAtUtc);
        await deviceRepository.SaveAsync(device, cancellationToken).ConfigureAwait(false);
        LogDeviceSaved(logger, device.Id, device.ConnectionType, device.Environment);
    }

    internal static SavedDeviceConnectionSettings CreateConnection(DeviceEditRequest request)
    {
        TerminalTextEncoding encoding = Enum.Parse<TerminalTextEncoding>(request.Encoding);
        switch (request.ConnectionType)
        {
            case ConnectionType.Ssh:
                _ = new SshDeviceConnectionSettings(request.Host, request.Port);
                break;
            case ConnectionType.Telnet:
                if (!request.TelnetAcknowledged)
                    throw new ArgumentException("Acknowledge that Telnet sends data without encryption.");
                _ = new TelnetConnectionOptions(request.Host, request.Port, request.TerminalType,
                    encoding, request.ConnectTimeoutSeconds);
                break;
            case ConnectionType.Serial:
                _ = new SerialConnectionOptions(request.Host, request.Port, request.SerialDataBits,
                    Enum.Parse<SerialParity>(request.SerialParity),
                    Enum.Parse<SerialStopBits>(request.SerialStopBits),
                    Enum.Parse<SerialFlowControl>(request.SerialFlowControl), encoding);
                break;
            case ConnectionType.RawTcp:
                _ = new RawTcpConnectionOptions(request.Host, request.Port, encoding,
                    Enum.Parse<TerminalLineEnding>(request.RawLineEnding), request.ConnectTimeoutSeconds);
                break;
            default:
                throw new ArgumentException("Select a supported saved-device protocol.");
        }

        return new SavedDeviceConnectionSettings(request.ConnectionType, request.Host, request.Port,
            request.TerminalType, request.Encoding, request.ConnectTimeoutSeconds,
            request.SerialDataBits, request.SerialParity, request.SerialStopBits,
            request.SerialFlowControl, request.RawLineEnding, request.TelnetAcknowledged);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A device ID is required.", nameof(id));
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await deviceRepository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        LogDeviceDeleted(logger, id);
    }

    public async Task RecordRecentAsync(Guid deviceId, DateTimeOffset connectedAtUtc, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await recentDeviceRepository.RecordConnectionAsync(deviceId, connectedAtUtc, cancellationToken).ConfigureAwait(false);
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    [LoggerMessage(
        EventId = 221,
        Level = LogLevel.Information,
        Message = "Saved device metadata {DeviceId} for {ConnectionType} in environment {Environment}")]
    private static partial void LogDeviceSaved(
        ILogger logger,
        Guid deviceId,
        Core.Connections.ConnectionType connectionType,
        DeviceEnvironment environment);

    [LoggerMessage(
        EventId = 222,
        Level = LogLevel.Information,
        Message = "Deleted saved device metadata {DeviceId}")]
    private static partial void LogDeviceDeleted(ILogger logger, Guid deviceId);
}
