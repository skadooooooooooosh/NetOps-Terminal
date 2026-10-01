using Microsoft.Extensions.Logging.Abstractions;
using NetOpsTerminal.Infrastructure.Platform;
using NetOpsTerminal.Infrastructure.Ssh;
using NetOpsTerminal.Security.Ssh;

namespace NetOpsTerminal.Infrastructure.Tests;

public sealed class OpenSshKnownHostsStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "NetOpsTerminal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TrustAsyncWritesStandardKnownHostsEntryAndRoundTrips()
    {
        using var store = CreateStore();
        var endpoint = new SshHostEndpoint("switch.test", 22);
        SshHostKey key = CreateKey([1, 2, 3, 4]);

        await store.TrustAsync(endpoint, key, CancellationToken.None);
        KnownHostKeyLookup lookup = await store.LookupAsync(endpoint, key, CancellationToken.None);
        SshHostKey? trustedKey = await store.GetTrustedKeyAsync(endpoint, CancellationToken.None);

        Assert.Equal(KnownHostKeyStatus.Trusted, lookup.Status);
        Assert.Equal(key.FingerprintSha256, trustedKey?.FingerprintSha256);
        string content = await File.ReadAllTextAsync(KnownHostsPath, CancellationToken.None);
        Assert.Equal($"switch.test {key.Algorithm} {key.KeyBase64}{Environment.NewLine}", content);
    }

    [Fact]
    public async Task NonDefaultPortUsesOpenSshBracketNotation()
    {
        using var store = CreateStore();
        var endpoint = new SshHostEndpoint("switch.test", 2222);
        SshHostKey key = CreateKey([1, 2, 3, 4]);

        await store.TrustAsync(endpoint, key, CancellationToken.None);

        string content = await File.ReadAllTextAsync(KnownHostsPath, CancellationToken.None);
        Assert.StartsWith("[switch.test]:2222 ", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DifferentSavedKeyReturnsChangedAndCannotBeOverwritten()
    {
        using var store = CreateStore();
        var endpoint = new SshHostEndpoint("switch.test", 22);
        SshHostKey trusted = CreateKey([1, 2, 3, 4]);
        SshHostKey changed = CreateKey([5, 6, 7, 8]);
        await store.TrustAsync(endpoint, trusted, CancellationToken.None);

        KnownHostKeyLookup lookup = await store.LookupAsync(endpoint, changed, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.TrustAsync(endpoint, changed, CancellationToken.None));

        Assert.Equal(KnownHostKeyStatus.Changed, lookup.Status);
        Assert.Equal(trusted.FingerprintSha256, lookup.ExistingKey?.FingerprintSha256);
        string content = await File.ReadAllTextAsync(KnownHostsPath, CancellationToken.None);
        Assert.Contains(trusted.KeyBase64, content, StringComparison.Ordinal);
        Assert.DoesNotContain(changed.KeyBase64, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitReplacementChangesOnlyTheSelectedEndpoint()
    {
        using var store = CreateStore();
        var endpoint = new SshHostEndpoint("switch.test", 22);
        var other = new SshHostEndpoint("other.test", 22);
        SshHostKey oldKey = CreateKey([1, 2, 3, 4]);
        SshHostKey newKey = CreateKey([5, 6, 7, 8]);
        await store.TrustAsync(endpoint, oldKey, CancellationToken.None);
        await store.TrustAsync(other, oldKey, CancellationToken.None);

        await store.ReplaceTrustedKeyAsync(endpoint, oldKey, newKey, CancellationToken.None);

        Assert.Equal(newKey.FingerprintSha256,
            (await store.GetTrustedKeyAsync(endpoint, CancellationToken.None))?.FingerprintSha256);
        Assert.Equal(oldKey.FingerprintSha256,
            (await store.GetTrustedKeyAsync(other, CancellationToken.None))?.FingerprintSha256);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ReplaceTrustedKeyAsync(endpoint, oldKey, oldKey, CancellationToken.None));
    }

    [Fact]
    public async Task ReplacementRefusesAmbiguousAndRevokedEntries()
    {
        Directory.CreateDirectory(_testDirectory);
        var endpoint = new SshHostEndpoint("switch.test", 22);
        SshHostKey oldKey = CreateKey([1, 2, 3, 4]);
        SshHostKey newKey = CreateKey([5, 6, 7, 8]);
        string oldLine = $"switch.test {oldKey.Algorithm} {oldKey.KeyBase64}";
        using var store = CreateStore();

        await File.WriteAllLinesAsync(KnownHostsPath, [oldLine, oldLine]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ReplaceTrustedKeyAsync(endpoint, oldKey, newKey, CancellationToken.None));
        await File.WriteAllLinesAsync(KnownHostsPath,
            [$"@revoked {oldLine}"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ReplaceTrustedKeyAsync(endpoint, oldKey, newKey, CancellationToken.None));
        Assert.DoesNotContain(newKey.KeyBase64,
            await File.ReadAllTextAsync(KnownHostsPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedAndCommentLinesAreIgnoredSafely()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllLinesAsync(
            KnownHostsPath,
            ["# comment", "malformed", "host ssh-ed25519 not-base64"],
            CancellationToken.None);
        using var store = CreateStore();

        KnownHostKeyLookup lookup = await store.LookupAsync(
            new SshHostEndpoint("host", 22),
            CreateKey([1, 2, 3, 4]),
            CancellationToken.None);

        Assert.Equal(KnownHostKeyStatus.Unknown, lookup.Status);
    }

    [Fact]
    public async Task RevokedMatchingKeyTakesPrecedenceOverDuplicateTrustedEntry()
    {
        SshHostKey key = CreateKey([1, 2, 3, 4]);
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllLinesAsync(
            KnownHostsPath,
            [
                $"switch.test {key.Algorithm} {key.KeyBase64}",
                $"@revoked switch.test {key.Algorithm} {key.KeyBase64}",
            ],
            CancellationToken.None);
        using var store = CreateStore();

        KnownHostKeyLookup lookup = await store.LookupAsync(
            new SshHostEndpoint("switch.test", 22),
            key,
            CancellationToken.None);

        Assert.Equal(KnownHostKeyStatus.Changed, lookup.Status);
        Assert.Null(await store.GetTrustedKeyAsync(
            new SshHostEndpoint("switch.test", 22),
            CancellationToken.None));
    }

    [Fact]
    public async Task MultipleTrustedKeysFallBackToTheProbePath()
    {
        SshHostKey first = CreateKey([1, 2, 3, 4]);
        SshHostKey second = CreateKey([5, 6, 7, 8]);
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllLinesAsync(
            KnownHostsPath,
            [
                $"switch.test {first.Algorithm} {first.KeyBase64}",
                $"switch.test {second.Algorithm} {second.KeyBase64}",
            ],
            CancellationToken.None);
        using var store = CreateStore();

        SshHostKey? trustedKey = await store.GetTrustedKeyAsync(
            new SshHostEndpoint("switch.test", 22),
            CancellationToken.None);

        Assert.Null(trustedKey);
    }

    [Fact]
    public async Task UnknownKnownHostsMarkerIsIgnored()
    {
        SshHostKey key = CreateKey([1, 2, 3, 4]);
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(
            KnownHostsPath,
            $"@unsupported switch.test {key.Algorithm} {key.KeyBase64}",
            CancellationToken.None);
        using var store = CreateStore();

        KnownHostKeyLookup lookup = await store.LookupAsync(
            new SshHostEndpoint("switch.test", 22),
            key,
            CancellationToken.None);

        Assert.Equal(KnownHostKeyStatus.Unknown, lookup.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private string KnownHostsPath => Path.Combine(_testDirectory, "known_hosts");

    private OpenSshKnownHostsStore CreateStore()
    {
        return new OpenSshKnownHostsStore(
            new FixedPathProvider(
                Path.Combine(_testDirectory, "settings.json"),
                KnownHostsPath),
            NullLogger<OpenSshKnownHostsStore>.Instance);
    }

    private static SshHostKey CreateKey(byte[] keyBytes)
    {
        string fingerprint = $"SHA256:{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(keyBytes)).TrimEnd('=')}";
        return new SshHostKey(
            "ssh-ed25519",
            fingerprint,
            Convert.ToBase64String(keyBytes),
            256);
    }

    private sealed record FixedPathProvider(
        string SettingsFilePath,
        string KnownHostsFilePath) : IApplicationDataPathProvider;
}
