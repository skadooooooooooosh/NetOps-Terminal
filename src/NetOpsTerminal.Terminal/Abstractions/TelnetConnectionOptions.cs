namespace NetOpsTerminal.Terminal.Abstractions;

public sealed record TelnetConnectionOptions
{
    public const int DefaultPort = 23;
    public const int DefaultConnectTimeoutSeconds = 15;

    public TelnetConnectionOptions(
        string host,
        int port = DefaultPort,
        string terminalType = "xterm-256color",
        TerminalTextEncoding encoding = TerminalTextEncoding.Utf8,
        int connectTimeoutSeconds = DefaultConnectTimeoutSeconds)
    {
        Host = ConnectionOptionsValidation.NormalizeHost(host);
        ConnectionOptionsValidation.ValidatePort(port);
        if (string.IsNullOrWhiteSpace(terminalType)
            || terminalType.Length > 64
            || terminalType.Any(char.IsControl)
            || terminalType.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Enter a valid Telnet terminal type.", nameof(terminalType));
        }

        ConnectionOptionsValidation.ValidateEncoding(encoding);
        ConnectionOptionsValidation.ValidateTimeout(connectTimeoutSeconds);
        Port = port;
        TerminalType = terminalType.Trim();
        Encoding = encoding;
        ConnectTimeoutSeconds = connectTimeoutSeconds;
    }

    public string Host { get; }

    public int Port { get; }

    public string TerminalType { get; }

    public TerminalTextEncoding Encoding { get; }

    public int ConnectTimeoutSeconds { get; }

    public override string ToString() => $"{Host}:{Port} ({TerminalType}, {Encoding})";
}
