using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Devices;

public sealed record SavedDeviceSessionResult(
    DeviceProfile Device,
    ITerminalSession Session);

public interface ISavedDeviceSessionService
{
    Task<SavedDeviceSessionResult> OpenAsync(
        Guid deviceId,
        CancellationToken cancellationToken);
}

public sealed class SavedDeviceConnectionException(string message) : Exception(message);
