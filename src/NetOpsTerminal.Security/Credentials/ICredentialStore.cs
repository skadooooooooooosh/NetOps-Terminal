namespace NetOpsTerminal.Security.Credentials;

public interface ICredentialStore
{
    Task<string?> GetSecretAsync(string secretId, CancellationToken cancellationToken);

    Task SetSecretAsync(string secretId, string value, CancellationToken cancellationToken);

    Task DeleteSecretAsync(string secretId, CancellationToken cancellationToken);
}
