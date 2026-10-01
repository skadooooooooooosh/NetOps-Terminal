using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Application.Devices;

public interface IDeviceCatalogService
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<DeviceCatalogSnapshot> LoadAsync(
        string? searchText,
        DeviceCatalogFilter filter,
        CancellationToken cancellationToken);

    Task SaveAsync(DeviceEditRequest request, CancellationToken cancellationToken);

    Task<Site> CreateSiteAsync(string name, CancellationToken cancellationToken);

    Task DeleteEmptySiteAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteEmptyFolderAsync(Guid id, CancellationToken cancellationToken);

    Task<DeviceGroup> CreateFolderAsync(
        string name, Guid? siteId, Guid? parentGroupId, CancellationToken cancellationToken);

    Task MoveDeviceAsync(
        Guid deviceId, Guid? siteId, Guid? groupId, CancellationToken cancellationToken);

    Task SaveExplorerOrderAsync(IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken);

    Task ResetExplorerOrderAsync(IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken);

    Task RecordRecentAsync(
        Guid deviceId,
        DateTimeOffset connectedAtUtc,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
