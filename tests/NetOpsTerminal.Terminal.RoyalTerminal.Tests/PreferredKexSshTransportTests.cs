using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NetOpsTerminal.Terminal.Abstractions;
using RoyalTerminal.Terminal;

namespace NetOpsTerminal.Terminal.RoyalTerminal.Tests;

public sealed class PreferredKexSshTransportTests
{
    [Fact]
    public async Task MissingPinnedHostKeyFailsBeforeConnecting()
    {
        using EphemeralSecret password = EphemeralSecret.Create("test-only");
        var provider = new PreferredKexSshTransportProvider(password, NullLogger.Instance);
        using ITerminalTransport transport = provider.Create();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await transport.StartAsync(CreateOptions(), CancellationToken.None));

        Assert.False(transport.IsRunning);
    }

    [Fact]
    public async Task UnsupportedCommandFailsBeforeConnecting()
    {
        using EphemeralSecret password = EphemeralSecret.Create("test-only");
        var provider = new PreferredKexSshTransportProvider(password, NullLogger.Instance);
        using ITerminalTransport transport = provider.Create();
        SshTransportOptions options = CreateOptions() with
        {
            InitialCommand = "show version",
            ExpectedHostKeyFingerprintSha256 = Fingerprint([1, 2, 3]),
        };

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await transport.StartAsync(options, CancellationToken.None));

        Assert.False(transport.IsRunning);
    }

    [Fact]
    public void FingerprintParserRequiresCanonicalSha256()
    {
        byte[] key = [1, 2, 3];
        string fingerprint = Fingerprint(key);

        Assert.Equal(SHA256.HashData(key),
            PreferredKexSshTransport.ParseExpectedFingerprint(fingerprint));
        Assert.Throws<ArgumentException>(() =>
            PreferredKexSshTransport.ParseExpectedFingerprint("MD5:invalid"));
        Assert.Throws<ArgumentException>(() =>
            PreferredKexSshTransport.ParseExpectedFingerprint("SHA256:AQID"));
    }

    [Fact]
    public void PinnedHostKeyComparisonRejectsChangedKey()
    {
        byte[] trustedKey = [1, 2, 3];
        byte[] changedKey = [1, 2, 4];
        byte[] trustedHash = SHA256.HashData(trustedKey);

        Assert.True(PreferredKexSshTransport.IsExpectedHostKey(trustedKey, trustedHash));
        Assert.False(PreferredKexSshTransport.IsExpectedHostKey(changedKey, trustedHash));
    }

    private static SshTransportOptions CreateOptions() => new(
        new SshEndpointOptions("127.0.0.1", 22, "operator"),
        RequestPty: true,
        TerminalType: "xterm-256color",
        InitialCommand: string.Empty,
        new SshAuthenticationOptions(
            UsePassword: true,
            PasswordSecretId: "test-only",
            PrivateKeySecretIds: [],
            UseAgent: false),
        new TerminalSessionDimensions(80, 24, 800, 600));

    private static string Fingerprint(byte[] key) =>
        $"SHA256:{Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=')}";
}
