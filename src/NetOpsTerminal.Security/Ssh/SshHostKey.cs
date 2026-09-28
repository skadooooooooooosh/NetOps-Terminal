using System.Security.Cryptography;

namespace NetOpsTerminal.Security.Ssh;

public sealed record SshHostKey
{
    public SshHostKey(
        string algorithm,
        string fingerprintSha256,
        string keyBase64,
        int keyLengthBits)
    {
        if (string.IsNullOrWhiteSpace(algorithm) || algorithm.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A valid SSH host-key algorithm is required.", nameof(algorithm));
        }

        if (string.IsNullOrWhiteSpace(fingerprintSha256)
            || !fingerprintSha256.StartsWith("SHA256:", StringComparison.Ordinal))
        {
            throw new ArgumentException("A SHA256 SSH host-key fingerprint is required.", nameof(fingerprintSha256));
        }

        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            throw new ArgumentException("SSH host-key material is required.", nameof(keyBase64));
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("SSH host-key material must be valid Base64.", nameof(keyBase64), exception);
        }

        string calculatedFingerprint = $"SHA256:{Convert.ToBase64String(SHA256.HashData(keyBytes)).TrimEnd('=')}";
        if (!string.Equals(fingerprintSha256, calculatedFingerprint, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The SSH host-key fingerprint does not match the supplied public key.",
                nameof(fingerprintSha256));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keyLengthBits);

        Algorithm = algorithm;
        FingerprintSha256 = fingerprintSha256;
        KeyBase64 = keyBase64;
        KeyLengthBits = keyLengthBits;
    }

    public string Algorithm { get; }

    public string FingerprintSha256 { get; }

    public string KeyBase64 { get; }

    public int KeyLengthBits { get; }
}
