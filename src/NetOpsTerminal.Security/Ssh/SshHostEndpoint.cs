namespace NetOpsTerminal.Security.Ssh;

public sealed record SshHostEndpoint
{
    public SshHostEndpoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("A host name or IP address is required.", nameof(host));
        }

        if (host.Length > 253 || host.Any(char.IsControl) || host.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The SSH host name or IP address is invalid.", nameof(host));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65_535);

        Host = host.Trim().TrimEnd('.');
        Port = port;
    }

    public string Host { get; }

    public int Port { get; }

    public string DisplayName => $"{Host}:{Port}";
}
