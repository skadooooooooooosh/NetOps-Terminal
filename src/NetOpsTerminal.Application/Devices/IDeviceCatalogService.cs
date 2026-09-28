namespace NetOpsTerminal.Application.Devices;

public interface IDeviceCatalogService
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<DeviceCatalogSnapshot> LoadAsync(
        string? searchText,
        DeviceCatalogFilter filter,
        CancellationToken cancellationToken);

    Task SaveAsync(DeviceEditRequest request, CancellationToken cancellationToken);

    Task RecordRecentAsync(
        Guid deviceId,
        DateTimeOffset connectedAtUtc,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
