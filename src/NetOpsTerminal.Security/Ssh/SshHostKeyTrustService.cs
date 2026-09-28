namespace NetOpsTerminal.Security.Ssh;

public sealed class SshHostKeyTrustService : ISshHostKeyTrustService
{
    private readonly IKnownHostsStore _knownHostsStore;
    private readonly ISshHostKeyPrompt _prompt;

    public SshHostKeyTrustService(
        IKnownHostsStore knownHostsStore,
        ISshHostKeyPrompt prompt)
    {
        _knownHostsStore = knownHostsStore;
        _prompt = prompt;
    }

    public Task<SshHostKey?> GetTrustedKeyAsync(
        SshHostEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return _knownHostsStore.GetTrustedKeyAsync(endpoint, cancellationToken);
    }

    public async Task<SshHostKeyVerificationResult> VerifyAsync(
        SshHostEndpoint endpoint,
        SshHostKey presentedKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(presentedKey);

        KnownHostKeyLookup lookup = await _knownHostsStore
            .LookupAsync(endpoint, presentedKey, cancellationToken)
            .ConfigureAwait(false);

        if (lookup.Status == KnownHostKeyStatus.Trusted)
        {
            return SshHostKeyVerificationResult.Trusted;
        }

        if (lookup.Status == KnownHostKeyStatus.Changed)
        {
            await _prompt
                .ShowChangedAsync(endpoint, lookup.ExistingKey, presentedKey, cancellationToken)
                .ConfigureAwait(false);
            return SshHostKeyVerificationResult.Changed;
        }

        SshHostKeyPromptDecision decision = await _prompt
            .PromptUnknownAsync(endpoint, presentedKey, cancellationToken)
            .ConfigureAwait(false);

        if (decision == SshHostKeyPromptDecision.TrustAndSave)
        {
            await _knownHostsStore
                .TrustAsync(endpoint, presentedKey, cancellationToken)
                .ConfigureAwait(false);
        }

        return decision == SshHostKeyPromptDecision.Cancel
            ? SshHostKeyVerificationResult.Rejected
            : SshHostKeyVerificationResult.Trusted;
    }
}
