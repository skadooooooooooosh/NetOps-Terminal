namespace NetOpsTerminal.Transports.Abstractions;

public sealed record NetworkEndpoint
{
    public NetworkEndpoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("A host name or IP address is required.", nameof(host));
        }

        if (port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");
        }

        Host = host.Trim();
        Port = port;
    }

    public string Host { get; }

    public int Port { get; }
}
