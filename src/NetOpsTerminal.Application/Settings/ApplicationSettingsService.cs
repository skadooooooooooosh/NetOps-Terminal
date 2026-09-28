using NetOpsTerminal.Application.Abstractions;

namespace NetOpsTerminal.Application.Settings;

public sealed class ApplicationSettingsService(IApplicationSettingsStore store)
    : IApplicationSettingsService
{
    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var normalized = ApplicationSettingsPolicy.Normalize(settings);
        if (settings is not null && settings.SchemaVersion < ApplicationSettings.CurrentSchemaVersion)
        {
            await store.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
        }

        return normalized;
    }

    public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return store.SaveAsync(ApplicationSettingsPolicy.Normalize(settings), cancellationToken);
    }
}
