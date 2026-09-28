using NetOpsTerminal.Core.Connections;

namespace NetOpsTerminal.Core.Devices;

public sealed record SavedDeviceConnectionSettings
{
    public SavedDeviceConnectionSettings(
        ConnectionType type,
        string host,
        int port,
        string terminalType = "xterm-256color",
        string encoding = "Utf8",
        int connectTimeoutSeconds = 15,
        int serialDataBits = 8,
        string serialParity = "None",
        string serialStopBits = "One",
        string serialFlowControl = "None",
        string rawLineEnding = "CarriageReturnLineFeed",
        bool telnetAcknowledged = false)
    {
        if (type is not (ConnectionType.Ssh or ConnectionType.Telnet or ConnectionType.Serial or ConnectionType.RawTcp))
            throw new ArgumentException("Select a supported saved-device protocol.", nameof(type));
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(char.IsWhiteSpace) || host.Any(char.IsControl))
            throw new ArgumentException(type == ConnectionType.Serial ? "Select a valid COM port." : "Enter a valid host name or IP address.", nameof(host));
        int maximum = type == ConnectionType.Serial ? 4_000_000 : 65_535;
        if (port < 1 || port > maximum)
            throw new ArgumentOutOfRangeException(nameof(port), type == ConnectionType.Serial ? "Enter a valid baud rate." : "Port must be between 1 and 65535.");
        Type = type;
        Host = host.Trim().TrimEnd('.');
        Port = port;
        TerminalType = terminalType;
        Encoding = encoding;
        ConnectTimeoutSeconds = connectTimeoutSeconds;
        SerialDataBits = serialDataBits;
        SerialParity = serialParity;
        SerialStopBits = serialStopBits;
        SerialFlowControl = serialFlowControl;
        RawLineEnding = rawLineEnding;
        TelnetAcknowledged = telnetAcknowledged;
    }

    public ConnectionType Type { get; }
    public string Host { get; }
    public int Port { get; }
    public string TerminalType { get; }
    public string Encoding { get; }
    public int ConnectTimeoutSeconds { get; }
    public int SerialDataBits { get; }
    public string SerialParity { get; }
    public string SerialStopBits { get; }
    public string SerialFlowControl { get; }
    public string RawLineEnding { get; }
    public bool TelnetAcknowledged { get; }
}
