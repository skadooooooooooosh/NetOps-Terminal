using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Application.Devices;

public interface IDeviceRepository
{
    Task<IReadOnlyList<DeviceProfile>> GetAllAsync(CancellationToken cancellationToken);

    Task<DeviceProfile?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task SaveAsync(DeviceProfile device, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<int> CountByCredentialAsync(Guid credentialProfileId, CancellationToken cancellationToken);
}

public interface ISiteRepository
{
    Task<IReadOnlyList<Site>> GetAllAsync(CancellationToken cancellationToken);

    Task<Site> GetOrCreateAsync(string name, CancellationToken cancellationToken);

    Task RenameAsync(Guid id, string name, CancellationToken cancellationToken);

    Task DeleteEmptyAsync(Guid id, CancellationToken cancellationToken);
}

public interface IDeviceGroupRepository
{
    Task<IReadOnlyList<DeviceGroup>> GetAllAsync(CancellationToken cancellationToken);

    Task<DeviceGroup> GetOrCreateAsync(
        string name,
        Guid? siteId,
        CancellationToken cancellationToken);

    Task<DeviceGroup> GetOrCreateChildAsync(
        string name,
        Guid? siteId,
        Guid parentGroupId,
        CancellationToken cancellationToken);

    Task RenameAsync(Guid id, string name, CancellationToken cancellationToken);

    Task DeleteEmptyAsync(Guid id, CancellationToken cancellationToken);
}

public interface IExplorerOrderRepository
{
    Task<IReadOnlyDictionary<Guid, int>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken);

    Task ClearAsync(IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken);
}

public interface ICredentialProfileRepository
{
    Task<IReadOnlyList<CredentialProfile>> GetAllAsync(CancellationToken cancellationToken);

    Task<CredentialProfile?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task SaveAsync(CredentialProfile profile, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IRecentDeviceRepository
{
    Task<IReadOnlyList<Guid>> GetRecentDeviceIdsAsync(
        int maximumCount,
        CancellationToken cancellationToken);

    Task RecordConnectionAsync(
        Guid deviceId,
        DateTimeOffset connectedAtUtc,
        CancellationToken cancellationToken);
}
