namespace NetOpsTerminal.Terminal.Abstractions;

public enum TerminalConnectionFailureKind
{
    Cancelled,
    HostKeyRejected,
    HostKeyChanged,
    AuthenticationFailed,
    TimedOut,
    DnsFailure,
    ConnectionRefused,
    NetworkFailure,
    SerialPortUnavailable,
    SerialPortInUse,
    Unknown,
}

public sealed class TerminalConnectionException : Exception
{
    public TerminalConnectionException(
        TerminalConnectionFailureKind failureKind,
        string userMessage,
        string technicalDetails,
        Exception? innerException = null)
        : base(userMessage, innerException)
    {
        FailureKind = failureKind;
        UserMessage = userMessage;
        TechnicalDetails = technicalDetails;
    }

    public TerminalConnectionFailureKind FailureKind { get; }

    public string UserMessage { get; }

    public string TechnicalDetails { get; }
}
