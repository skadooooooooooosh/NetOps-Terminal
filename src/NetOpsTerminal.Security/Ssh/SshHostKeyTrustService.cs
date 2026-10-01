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
            SshHostKeyPromptDecision changedDecision = await _prompt
                .PromptChangedAsync(endpoint, lookup.ExistingKey, presentedKey, cancellationToken)
                .ConfigureAwait(false);
            if (changedDecision != SshHostKeyPromptDecision.ReplaceAndSave
                || lookup.ExistingKey is null)
            {
                return SshHostKeyVerificationResult.Changed;
            }

            await _knownHostsStore.ReplaceTrustedKeyAsync(
                endpoint, lookup.ExistingKey, presentedKey, cancellationToken).ConfigureAwait(false);
            return SshHostKeyVerificationResult.Trusted;
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

        return decision is SshHostKeyPromptDecision.TrustOnce or SshHostKeyPromptDecision.TrustAndSave
            ? SshHostKeyVerificationResult.Trusted
            : SshHostKeyVerificationResult.Rejected;
    }
}
