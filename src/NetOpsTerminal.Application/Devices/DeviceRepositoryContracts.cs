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
}

public interface IDeviceGroupRepository
{
    Task<IReadOnlyList<DeviceGroup>> GetAllAsync(CancellationToken cancellationToken);

    Task<DeviceGroup> GetOrCreateAsync(
        string name,
        Guid? siteId,
        CancellationToken cancellationToken);
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
