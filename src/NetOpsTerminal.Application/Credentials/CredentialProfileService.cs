using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Security.Credentials;
using Microsoft.Extensions.Logging;

namespace NetOpsTerminal.Application.Credentials;

public sealed partial class CredentialProfileService(
    IApplicationDatabaseInitializer databaseInitializer,
    ICredentialProfileRepository credentialRepository,
    IDeviceRepository deviceRepository,
    ICredentialStore credentialStore,
    ILogger<CredentialProfileService> logger)
    : ICredentialProfileService
{
    public async Task<IReadOnlyList<CredentialProfile>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await credentialRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(
        CredentialProfileEditRequest request,
        string? password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

        CredentialProfile? existing = request.Id is { } id
            ? await credentialRepository.GetAsync(id, cancellationToken).ConfigureAwait(false)
            : null;
        if (request.Id is not null && existing is null)
        {
            throw new InvalidOperationException("The credential profile no longer exists.");
        }

        if (existing is null && string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Enter a password for the new credential profile.", nameof(password));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string secretReference = existing?.SecretReference ?? $"credential-{Guid.NewGuid():N}";
        var profile = new CredentialProfile(
            request.Id ?? Guid.NewGuid(),
            request.Name,
            request.Username,
            secretReference,
            existing?.CreatedAtUtc ?? now,
            now);

        string? priorSecret = null;
        bool secretChanged = password is not null;
        if (secretChanged)
        {
            priorSecret = existing is null
                ? null
                : await credentialStore
                    .GetSecretAsync(secretReference, cancellationToken)
                    .ConfigureAwait(false);
            await credentialStore
                .SetSecretAsync(secretReference, password!, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            await credentialRepository.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
            LogCredentialProfileSaved(logger, profile.Id, password is not null);
        }
        catch
        {
            if (secretChanged)
            {
                if (priorSecret is null)
                {
                    await credentialStore
                        .DeleteSecretAsync(secretReference, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else
                {
                    await credentialStore
                        .SetSecretAsync(secretReference, priorSecret, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        CredentialProfile? profile = await credentialRepository
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
        {
            return;
        }

        int references = await deviceRepository
            .CountByCredentialAsync(id, cancellationToken)
            .ConfigureAwait(false);
        if (references > 0)
        {
            throw new InvalidOperationException(
                $"This credential is used by {references} saved device(s). Reassign those devices before deleting it.");
        }

        string? priorSecret = await credentialStore
            .GetSecretAsync(profile.SecretReference, cancellationToken)
            .ConfigureAwait(false);
        await credentialStore
            .DeleteSecretAsync(profile.SecretReference, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await credentialRepository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            LogCredentialProfileDeleted(logger, id);
        }
        catch
        {
            if (priorSecret is not null)
            {
                await credentialStore
                    .SetSecretAsync(profile.SecretReference, priorSecret, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            throw;
        }
    }

    [LoggerMessage(
        EventId = 231,
        Level = LogLevel.Information,
        Message = "Saved credential profile metadata {CredentialProfileId}; protected secret changed {SecretChanged}")]
    private static partial void LogCredentialProfileSaved(
        ILogger logger,
        Guid credentialProfileId,
        bool secretChanged);

    [LoggerMessage(
        EventId = 232,
        Level = LogLevel.Information,
        Message = "Deleted credential profile metadata and protected secret {CredentialProfileId}")]
    private static partial void LogCredentialProfileDeleted(
        ILogger logger,
        Guid credentialProfileId);
}
