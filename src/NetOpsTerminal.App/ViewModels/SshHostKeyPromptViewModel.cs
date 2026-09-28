using NetOpsTerminal.Security.Ssh;

namespace NetOpsTerminal.App.ViewModels;

public sealed class SshHostKeyPromptViewModel
{
    public SshHostKeyPromptViewModel(
        SshHostEndpoint endpoint,
        SshHostKey presentedKey,
        SshHostKey? trustedKey,
        bool isChanged)
    {
        Host = endpoint.Host;
        Port = endpoint.Port;
        Algorithm = presentedKey.Algorithm;
        FingerprintSha256 = presentedKey.FingerprintSha256;
        TrustedFingerprintSha256 = trustedKey?.FingerprintSha256 ?? "Unavailable";
        IsChanged = isChanged;
        IsUnknown = !isChanged;
        Heading = isChanged
            ? "SSH HOST KEY CHANGED"
            : "Host authenticity is not established";
        Explanation = isChanged
            ? "The key presented by this host differs from the saved key. The connection has been blocked."
            : "Verify this SHA256 fingerprint through a trusted channel before continuing.";
    }

    public string Host { get; }

    public int Port { get; }

    public string Algorithm { get; }

    public string FingerprintSha256 { get; }

    public string TrustedFingerprintSha256 { get; }

    public bool IsChanged { get; }

    public bool IsUnknown { get; }

    public string Heading { get; }

    public string Explanation { get; }
}
