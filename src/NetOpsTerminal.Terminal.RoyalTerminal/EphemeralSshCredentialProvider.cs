using NetOpsTerminal.Terminal.Abstractions;
using RoyalTerminal.Terminal;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal sealed class EphemeralSshCredentialProvider(EphemeralSecret password)
    : ISshCredentialProvider
{
    public ValueTask<SshResolvedCredentials> ResolveAsync(
        SshCredentialRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new SshResolvedCredentials(
            password.Reveal(),
            [],
            UseAgent: false));
    }
}
