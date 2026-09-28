using NetOpsTerminal.Security.Ssh;

namespace NetOpsTerminal.Security.Tests;

public sealed class SshHostKeyTrustServiceTests
{
    private static readonly SshHostEndpoint Endpoint = new("switch.test", 22);
    private static readonly SshHostKey PresentedKey = CreateKey([1, 2, 3, 4]);

    [Fact]
    public async Task TrustedKeyConnectsWithoutPrompt()
    {
        var store = new RecordingKnownHostsStore(KnownHostKeyStatus.Trusted);
        var prompt = new RecordingPrompt();
        var service = new SshHostKeyTrustService(store, prompt);

        SshHostKeyVerificationResult result = await service.VerifyAsync(
            Endpoint,
            PresentedKey,
            CancellationToken.None);

        Assert.Equal(SshHostKeyVerificationResult.Trusted, result);
        Assert.Equal(0, prompt.UnknownPromptCount);
        Assert.Equal(0, prompt.ChangedPromptCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task TrustedKeyCanBeReadBeforeOpeningTheTransport()
    {
        var store = new RecordingKnownHostsStore(KnownHostKeyStatus.Trusted, PresentedKey);
        var service = new SshHostKeyTrustService(store, new RecordingPrompt());

        SshHostKey? trustedKey = await service.GetTrustedKeyAsync(
            Endpoint,
            CancellationToken.None);

        Assert.Same(PresentedKey, trustedKey);
        Assert.Equal(1, store.TrustedKeyReadCount);
    }

    [Theory]
    [InlineData(SshHostKeyPromptDecision.TrustOnce, SshHostKeyVerificationResult.Trusted, 0)]
    [InlineData(SshHostKeyPromptDecision.TrustAndSave, SshHostKeyVerificationResult.Trusted, 1)]
    [InlineData(SshHostKeyPromptDecision.Cancel, SshHostKeyVerificationResult.Rejected, 0)]
    public async Task UnknownKeyRequiresExplicitDecision(
        SshHostKeyPromptDecision decision,
        SshHostKeyVerificationResult expectedResult,
        int expectedSaveCount)
    {
        var store = new RecordingKnownHostsStore(KnownHostKeyStatus.Unknown);
        var prompt = new RecordingPrompt { UnknownDecision = decision };
        var service = new SshHostKeyTrustService(store, prompt);

        SshHostKeyVerificationResult result = await service.VerifyAsync(
            Endpoint,
            PresentedKey,
            CancellationToken.None);

        Assert.Equal(expectedResult, result);
        Assert.Equal(1, prompt.UnknownPromptCount);
        Assert.Equal(expectedSaveCount, store.SaveCount);
    }

    [Fact]
    public async Task ChangedKeyIsBlockedAndNeverOverwritten()
    {
        SshHostKey existingKey = CreateKey([9, 8, 7, 6]);
        var store = new RecordingKnownHostsStore(KnownHostKeyStatus.Changed, existingKey);
        var prompt = new RecordingPrompt();
        var service = new SshHostKeyTrustService(store, prompt);

        SshHostKeyVerificationResult result = await service.VerifyAsync(
            Endpoint,
            PresentedKey,
            CancellationToken.None);

        Assert.Equal(SshHostKeyVerificationResult.Changed, result);
        Assert.Equal(1, prompt.ChangedPromptCount);
        Assert.Equal(0, prompt.UnknownPromptCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Same(existingKey, prompt.TrustedKey);
    }

    [Fact]
    public void HostKeyRejectsFingerprintThatDoesNotMatchPublicKey()
    {
        Assert.Throws<ArgumentException>(
            () => new SshHostKey(
                "ssh-ed25519",
                "SHA256:not-the-key-fingerprint",
                Convert.ToBase64String([1, 2, 3, 4]),
                256));
    }

    private static SshHostKey CreateKey(byte[] keyBytes)
    {
        string fingerprint = $"SHA256:{Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(keyBytes)).TrimEnd('=')}";
        return new SshHostKey(
            "ssh-ed25519",
            fingerprint,
            Convert.ToBase64String(keyBytes),
            256);
    }

    private sealed class RecordingKnownHostsStore(
        KnownHostKeyStatus status,
        SshHostKey? existingKey = null) : IKnownHostsStore
    {
        public int SaveCount { get; private set; }

        public int TrustedKeyReadCount { get; private set; }

        public Task<SshHostKey?> GetTrustedKeyAsync(
            SshHostEndpoint endpoint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TrustedKeyReadCount++;
            return Task.FromResult(status == KnownHostKeyStatus.Trusted ? existingKey : null);
        }

        public Task<KnownHostKeyLookup> LookupAsync(
            SshHostEndpoint endpoint,
            SshHostKey presentedKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new KnownHostKeyLookup(status, existingKey));
        }

        public Task TrustAsync(
            SshHostEndpoint endpoint,
            SshHostKey hostKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPrompt : ISshHostKeyPrompt
    {
        public SshHostKeyPromptDecision UnknownDecision { get; init; }
            = SshHostKeyPromptDecision.Cancel;

        public int UnknownPromptCount { get; private set; }

        public int ChangedPromptCount { get; private set; }

        public SshHostKey? TrustedKey { get; private set; }

        public Task<SshHostKeyPromptDecision> PromptUnknownAsync(
            SshHostEndpoint endpoint,
            SshHostKey presentedKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UnknownPromptCount++;
            return Task.FromResult(UnknownDecision);
        }

        public Task ShowChangedAsync(
            SshHostEndpoint endpoint,
            SshHostKey? trustedKey,
            SshHostKey presentedKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChangedPromptCount++;
            TrustedKey = trustedKey;
            return Task.CompletedTask;
        }
    }
}
