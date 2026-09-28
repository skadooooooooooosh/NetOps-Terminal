using NetOpsTerminal.Application.Settings;

namespace NetOpsTerminal.Application.Abstractions;

public interface IApplicationSettingsStore
{
    Task<ApplicationSettings?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken);
}
