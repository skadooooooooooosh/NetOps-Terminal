using RoyalTerminal.Terminal;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal sealed class UnavailableSshCredentialProvider : ISshCredentialProvider
{
    public ValueTask<SshResolvedCredentials> ResolveAsync(
        SshCredentialRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<SshResolvedCredentials>(
            new NotSupportedException("SSH credentials are unavailable in this non-SSH terminal session."));
    }
}
