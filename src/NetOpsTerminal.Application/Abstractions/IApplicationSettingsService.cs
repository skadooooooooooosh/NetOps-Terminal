using NetOpsTerminal.Application.Settings;

namespace NetOpsTerminal.Application.Abstractions;

public interface IApplicationSettingsService
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken);
}
