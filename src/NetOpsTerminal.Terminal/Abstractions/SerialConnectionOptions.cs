namespace NetOpsTerminal.Terminal.Abstractions;

public enum SerialParity
{
    None,
    Odd,
    Even,
    Mark,
    Space,
}

public enum SerialStopBits
{
    One,
    OnePointFive,
    Two,
}

public enum SerialFlowControl
{
    None,
    XOnXOff,
    RequestToSend,
    RequestToSendXOnXOff,
}

public sealed record SerialConnectionOptions
{
    public const int DefaultBaudRate = 9_600;
    public const int DefaultDataBits = 8;

    public SerialConnectionOptions(
        string portName,
        int baudRate = DefaultBaudRate,
        int dataBits = DefaultDataBits,
        SerialParity parity = SerialParity.None,
        SerialStopBits stopBits = SerialStopBits.One,
        SerialFlowControl flowControl = SerialFlowControl.None,
        TerminalTextEncoding encoding = TerminalTextEncoding.Utf8)
    {
        if (string.IsNullOrWhiteSpace(portName)
            || portName.Length > 64
            || portName.Any(char.IsControl)
            || portName.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Select a valid serial port.", nameof(portName));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(baudRate, 50);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(baudRate, 4_000_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(dataBits, 5);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dataBits, 8);
        if (!Enum.IsDefined(parity))
        {
            throw new ArgumentOutOfRangeException(nameof(parity));
        }

        if (!Enum.IsDefined(stopBits))
        {
            throw new ArgumentOutOfRangeException(nameof(stopBits));
        }

        if (!Enum.IsDefined(flowControl))
        {
            throw new ArgumentOutOfRangeException(nameof(flowControl));
        }

        ConnectionOptionsValidation.ValidateEncoding(encoding);
        PortName = portName.Trim().ToUpperInvariant();
        BaudRate = baudRate;
        DataBits = dataBits;
        Parity = parity;
        StopBits = stopBits;
        FlowControl = flowControl;
        Encoding = encoding;
    }

    public string PortName { get; }

    public int BaudRate { get; }

    public int DataBits { get; }

    public SerialParity Parity { get; }

    public SerialStopBits StopBits { get; }

    public SerialFlowControl FlowControl { get; }

    public TerminalTextEncoding Encoding { get; }

    public override string ToString()
    {
        return $"{PortName} ({BaudRate} {DataBits}{Parity.ToString()[0]}{StopBits}, {FlowControl}, {Encoding})";
    }
}
