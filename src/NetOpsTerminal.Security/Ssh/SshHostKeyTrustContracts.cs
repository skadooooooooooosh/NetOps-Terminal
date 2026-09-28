namespace NetOpsTerminal.Security.Ssh;

public enum KnownHostKeyStatus
{
    Unknown,
    Trusted,
    Changed,
}

public enum SshHostKeyPromptDecision
{
    Cancel,
    TrustOnce,
    TrustAndSave,
}

public enum SshHostKeyVerificationResult
{
    Rejected,
    Trusted,
    Changed,
}

public sealed record KnownHostKeyLookup(
    KnownHostKeyStatus Status,
    SshHostKey? ExistingKey = null);

public interface IKnownHostsStore
{
    Task<SshHostKey?> GetTrustedKeyAsync(
        SshHostEndpoint endpoint,
        CancellationToken cancellationToken);

    Task<KnownHostKeyLookup> LookupAsync(
        SshHostEndpoint endpoint,
        SshHostKey presentedKey,
        CancellationToken cancellationToken);

    Task TrustAsync(
        SshHostEndpoint endpoint,
        SshHostKey hostKey,
        CancellationToken cancellationToken);
}

public interface ISshHostKeyPrompt
{
    Task<SshHostKeyPromptDecision> PromptUnknownAsync(
        SshHostEndpoint endpoint,
        SshHostKey presentedKey,
        CancellationToken cancellationToken);

    Task ShowChangedAsync(
        SshHostEndpoint endpoint,
        SshHostKey? trustedKey,
        SshHostKey presentedKey,
        CancellationToken cancellationToken);
}

public interface ISshHostKeyTrustService
{
    Task<SshHostKey?> GetTrustedKeyAsync(
        SshHostEndpoint endpoint,
        CancellationToken cancellationToken);

    Task<SshHostKeyVerificationResult> VerifyAsync(
        SshHostEndpoint endpoint,
        SshHostKey presentedKey,
        CancellationToken cancellationToken);
}
