using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Application.Devices;

public interface IDeviceWebAccessService
{
    void Open(DeviceWebAccessSettings settings);
    Task<bool> CheckPortAsync(DeviceWebAccessSettings settings, CancellationToken cancellationToken);
}
