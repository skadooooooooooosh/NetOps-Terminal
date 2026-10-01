using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.Infrastructure.Platform;
using NetOpsTerminal.Security.Ssh;

namespace NetOpsTerminal.Infrastructure.Ssh;

public sealed partial class OpenSshKnownHostsStore : IKnownHostsStore, IDisposable
{
    private readonly IApplicationDataPathProvider _pathProvider;
    private readonly ILogger<OpenSshKnownHostsStore> _logger;
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private bool _disposed;

    public OpenSshKnownHostsStore(
        IApplicationDataPathProvider pathProvider,
        ILogger<OpenSshKnownHostsStore> logger)
    {
        _pathProvider = pathProvider;
        _logger = logger;
    }

    public async Task<SshHostKey?> GetTrustedKeyAsync(
        SshHostEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] lines = await ReadLinesAsync(cancellationToken).ConfigureAwait(false);
            return FindSingleTrustedKey(lines, endpoint);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task<KnownHostKeyLookup> LookupAsync(
        SshHostEndpoint endpoint,
        SshHostKey presentedKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(presentedKey);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] lines = await ReadLinesAsync(cancellationToken).ConfigureAwait(false);
            return Lookup(lines, endpoint, presentedKey);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task TrustAsync(
        SshHostEndpoint endpoint,
        SshHostKey hostKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(hostKey);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] existingLines = await ReadLinesAsync(cancellationToken).ConfigureAwait(false);
            KnownHostKeyLookup lookup = Lookup(existingLines, endpoint, hostKey);
            if (lookup.Status == KnownHostKeyStatus.Trusted)
            {
                return;
            }

            if (lookup.Status == KnownHostKeyStatus.Changed)
            {
                throw new InvalidOperationException(
                    "A different SSH host key is already trusted. It will not be overwritten automatically.");
            }

            string path = _pathProvider.KnownHostsFilePath;
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("The known_hosts path has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            string newLine = $"{FormatEndpointToken(endpoint)} {hostKey.Algorithm} {hostKey.KeyBase64}";
            string[] updatedLines = [.. existingLines, newLine];

            try
            {
                await File.WriteAllLinesAsync(
                    temporaryPath,
                    updatedLines,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, path, overwrite: true);
                LogHostKeySaved(_logger, endpoint.Host, endpoint.Port, hostKey.FingerprintSha256);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task ReplaceTrustedKeyAsync(
        SshHostEndpoint endpoint,
        SshHostKey expectedKey,
        SshHostKey replacementKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(expectedKey);
        ArgumentNullException.ThrowIfNull(replacementKey);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] lines = await ReadLinesAsync(cancellationToken).ConfigureAwait(false);
            var matches = lines.Select((line, index) => (line, index))
                .Where(item => TryParseLine(item.line, out ParsedKnownHost? parsed)
                    && parsed is not null
                    && parsed.HostPatterns.Any(pattern => MatchesEndpoint(pattern, endpoint)))
                .ToArray();
            if (matches.Length != 1
                || !TryParseLine(matches[0].line, out ParsedKnownHost? current)
                || current is null
                || current.IsRevoked
                || current.HostPatterns.Count != 1
                || current.HostPatterns[0].StartsWith('@')
                || !string.Equals(current.HostKey.Algorithm, expectedKey.Algorithm, StringComparison.Ordinal)
                || !CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(current.HostKey.KeyBase64),
                    Convert.FromBase64String(expectedKey.KeyBase64)))
            {
                throw new InvalidOperationException(
                    "The trusted SSH key changed in storage or has multiple matching entries. No key was replaced.");
            }

            string path = _pathProvider.KnownHostsFilePath;
            string directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("The known_hosts path has no parent directory.");
            string temporaryPath = Path.Combine(directory,
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            lines[matches[0].index] =
                $"{current.HostPatterns[0]} {replacementKey.Algorithm} {replacementKey.KeyBase64}";
            try
            {
                await File.WriteAllLinesAsync(temporaryPath, lines,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, path, overwrite: true);
                LogHostKeySaved(_logger, endpoint.Host, endpoint.Port,
                    replacementKey.FingerprintSha256);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fileGate.Dispose();
    }

    internal static KnownHostKeyLookup Lookup(
        IEnumerable<string> lines,
        SshHostEndpoint endpoint,
        SshHostKey presentedKey)
    {
        bool endpointFound = false;
        bool trustedMatchFound = false;
        bool revokedMatchFound = false;
        SshHostKey? existingKey = null;

        foreach (string line in lines)
        {
            if (!TryParseLine(line, out ParsedKnownHost? parsed)
                || parsed is null
                || !parsed.HostPatterns.Any(pattern => MatchesEndpoint(pattern, endpoint)))
            {
                continue;
            }

            endpointFound = true;
            existingKey ??= parsed.HostKey;
            bool keyMatches = string.Equals(
                    parsed.HostKey.Algorithm,
                    presentedKey.Algorithm,
                    StringComparison.Ordinal)
                && CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(parsed.HostKey.KeyBase64),
                    Convert.FromBase64String(presentedKey.KeyBase64));
            if (keyMatches)
            {
                revokedMatchFound |= parsed.IsRevoked;
                trustedMatchFound |= !parsed.IsRevoked;
            }
        }

        if (trustedMatchFound && !revokedMatchFound)
        {
            return new KnownHostKeyLookup(KnownHostKeyStatus.Trusted, presentedKey);
        }

        return endpointFound
            ? new KnownHostKeyLookup(KnownHostKeyStatus.Changed, existingKey)
            : new KnownHostKeyLookup(KnownHostKeyStatus.Unknown);
    }

    internal static SshHostKey? FindSingleTrustedKey(
        IEnumerable<string> lines,
        SshHostEndpoint endpoint)
    {
        var matchingKeys = new Dictionary<string, (SshHostKey Key, bool Trusted, bool Revoked)>(
            StringComparer.Ordinal);

        foreach (string line in lines)
        {
            if (!TryParseLine(line, out ParsedKnownHost? parsed)
                || parsed is null
                || !parsed.HostPatterns.Any(pattern => MatchesEndpoint(pattern, endpoint)))
            {
                continue;
            }

            string identity = $"{parsed.HostKey.Algorithm}\0{parsed.HostKey.KeyBase64}";
            matchingKeys.TryGetValue(identity, out var state);
            matchingKeys[identity] = (
                parsed.HostKey,
                state.Trusted || !parsed.IsRevoked,
                state.Revoked || parsed.IsRevoked);
        }

        SshHostKey[] trustedKeys = matchingKeys.Values
            .Where(value => value.Trusted && !value.Revoked)
            .Select(value => value.Key)
            .ToArray();

        return trustedKeys.Length == 1 ? trustedKeys[0] : null;
    }

    internal static string FormatEndpointToken(SshHostEndpoint endpoint)
    {
        return endpoint.Port == 22
            ? endpoint.Host
            : $"[{endpoint.Host}]:{endpoint.Port}";
    }

    private async Task<string[]> ReadLinesAsync(CancellationToken cancellationToken)
    {
        string path = _pathProvider.KnownHostsFilePath;
        return File.Exists(path)
            ? await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false)
            : [];
    }

    private static bool TryParseLine(string line, out ParsedKnownHost? parsed)
    {
        parsed = null;
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return false;
        }

        string[] fields = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool hasMarker = fields[0].StartsWith('@');
        if (hasMarker
            && !string.Equals(fields[0], "@revoked", StringComparison.Ordinal)
            && !string.Equals(fields[0], "@cert-authority", StringComparison.Ordinal))
        {
            return false;
        }

        int offset = hasMarker ? 1 : 0;
        if (fields.Length < offset + 3 || string.Equals(fields[0], "@cert-authority", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            byte[] keyBytes = Convert.FromBase64String(fields[offset + 2]);
            string fingerprint = $"SHA256:{Convert.ToBase64String(SHA256.HashData(keyBytes)).TrimEnd('=')}";
            parsed = new ParsedKnownHost(
                fields[offset].Split(',', StringSplitOptions.RemoveEmptyEntries),
                new SshHostKey(fields[offset + 1], fingerprint, fields[offset + 2], keyBytes.Length * 8),
                string.Equals(fields[0], "@revoked", StringComparison.Ordinal));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "OpenSSH hashed known_hosts entries are defined as HMAC-SHA1; this code verifies compatibility tokens and does not protect secrets.")]
    private static bool MatchesEndpoint(string pattern, SshHostEndpoint endpoint)
    {
        string[] candidates = endpoint.Port == 22
            ? [endpoint.Host, $"[{endpoint.Host}]:22"]
            : [FormatEndpointToken(endpoint)];

        if (pattern.StartsWith("|1|", StringComparison.Ordinal))
        {
            string[] hashFields = pattern.Split('|');
            if (hashFields.Length != 4)
            {
                return false;
            }

            try
            {
                byte[] salt = Convert.FromBase64String(hashFields[2]);
                byte[] expectedHash = Convert.FromBase64String(hashFields[3]);
                using var hmac = new HMACSHA1(salt);
                return candidates.Any(candidate => CryptographicOperations.FixedTimeEquals(
                    hmac.ComputeHash(Encoding.UTF8.GetBytes(candidate)),
                    expectedHash));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return candidates.Any(candidate => string.Equals(pattern, candidate, StringComparison.OrdinalIgnoreCase));
    }

    [LoggerMessage(
        EventId = 401,
        Level = LogLevel.Information,
        Message = "Saved trusted SSH host key for {Host}:{Port} with fingerprint {FingerprintSha256}")]
    private static partial void LogHostKeySaved(
        ILogger logger,
        string host,
        int port,
        string fingerprintSha256);

    private sealed record ParsedKnownHost(
        IReadOnlyList<string> HostPatterns,
        SshHostKey HostKey,
        bool IsRevoked);
}
