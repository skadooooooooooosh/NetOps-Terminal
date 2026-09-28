namespace NetOpsTerminal.Terminal.Abstractions;

public sealed class SshConnectionOptions : IDisposable
{
    public const int DefaultPort = 22;
    public const int DefaultConnectTimeoutSeconds = 15;

    public SshConnectionOptions(
        string host,
        int port,
        string username,
        EphemeralSecret password,
        int connectTimeoutSeconds = DefaultConnectTimeoutSeconds)
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

        if (string.IsNullOrWhiteSpace(username)
            || username.Length > 255
            || username.Any(char.IsControl))
        {
            throw new ArgumentException("Enter a valid SSH username.", nameof(username));
        }

        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(connectTimeoutSeconds, 5);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(connectTimeoutSeconds, 120);

        Host = host.Trim().TrimEnd('.');
        Port = port;
        Username = username.Trim();
        Password = password;
        ConnectTimeoutSeconds = connectTimeoutSeconds;
    }

    public string Host { get; }

    public int Port { get; }

    public string Username { get; }

    public EphemeralSecret Password { get; }

    public int ConnectTimeoutSeconds { get; }

    public void Dispose()
    {
        Password.Dispose();
    }

    public override string ToString()
    {
        return $"{Username}@{Host}:{Port} (password [REDACTED], timeout {ConnectTimeoutSeconds}s)";
    }
}
