using NetOpsTerminal.Core.Credentials;

namespace NetOpsTerminal.Application.Credentials;

public sealed record CredentialProfileEditRequest(
    Guid? Id,
    string Name,
    string Username);

public interface ICredentialProfileService
{
    Task<IReadOnlyList<CredentialProfile>> GetAllAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        CredentialProfileEditRequest request,
        string? password,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
