namespace NetOpsTerminal.Terminal.Abstractions;

public sealed record RawTcpConnectionOptions
{
    public const int DefaultConnectTimeoutSeconds = 15;

    public RawTcpConnectionOptions(
        string host,
        int port,
        TerminalTextEncoding encoding = TerminalTextEncoding.Utf8,
        TerminalLineEnding lineEnding = TerminalLineEnding.CarriageReturnLineFeed,
        int connectTimeoutSeconds = DefaultConnectTimeoutSeconds)
    {
        Host = ConnectionOptionsValidation.NormalizeHost(host);
        ConnectionOptionsValidation.ValidatePort(port);
        ConnectionOptionsValidation.ValidateEncoding(encoding);
        if (!Enum.IsDefined(lineEnding))
        {
            throw new ArgumentOutOfRangeException(nameof(lineEnding));
        }

        ConnectionOptionsValidation.ValidateTimeout(connectTimeoutSeconds);
        Port = port;
        Encoding = encoding;
        LineEnding = lineEnding;
        ConnectTimeoutSeconds = connectTimeoutSeconds;
    }

    public string Host { get; }

    public int Port { get; }

    public TerminalTextEncoding Encoding { get; }

    public TerminalLineEnding LineEnding { get; }

    public int ConnectTimeoutSeconds { get; }

    public override string ToString() => $"{Host}:{Port} ({Encoding}, {LineEnding})";
}
