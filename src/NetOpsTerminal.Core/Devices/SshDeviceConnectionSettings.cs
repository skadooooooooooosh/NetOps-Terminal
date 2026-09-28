namespace NetOpsTerminal.Core.Devices;

public sealed record SshDeviceConnectionSettings
{
    public const int DefaultPort = 22;

    public SshDeviceConnectionSettings(string host, int port = DefaultPort)
    {
        if (string.IsNullOrWhiteSpace(host)
            || host.Length > 253
            || host.Any(char.IsControl)
            || host.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Enter a valid host name or IP address.", nameof(host));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65_535);
        Host = host.Trim().TrimEnd('.');
        Port = port;
    }

    public string Host { get; }

    public int Port { get; }
}
