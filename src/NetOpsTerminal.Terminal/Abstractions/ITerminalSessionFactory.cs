using NetOpsTerminal.Core.Connections;

namespace NetOpsTerminal.Terminal.Abstractions;

public interface ITerminalSessionFactory
{
    Task<ITerminalSession> CreateAsync(
        TerminalSessionRequest request,
        CancellationToken cancellationToken);
}

public sealed class TerminalSessionRequest
{
    public TerminalSessionRequest(
        Guid sessionId,
        string displayName,
        LocalShellKind shellKind,
        string? workingDirectory = null)
    {
        SessionId = sessionId;
        DisplayName = displayName;
        ConnectionType = ConnectionType.LocalShell;
        ShellKind = shellKind;
        WorkingDirectory = workingDirectory;
    }

    public TerminalSessionRequest(
        Guid sessionId,
        string displayName,
        SshConnectionOptions sshOptions)
    {
        SessionId = sessionId;
        DisplayName = displayName;
        ConnectionType = ConnectionType.Ssh;
        SshOptions = sshOptions ?? throw new ArgumentNullException(nameof(sshOptions));
    }

    public TerminalSessionRequest(
        Guid sessionId,
        string displayName,
        TelnetConnectionOptions telnetOptions)
    {
        SessionId = sessionId;
        DisplayName = displayName;
        ConnectionType = ConnectionType.Telnet;
        TelnetOptions = telnetOptions ?? throw new ArgumentNullException(nameof(telnetOptions));
    }

    public TerminalSessionRequest(
        Guid sessionId,
        string displayName,
        SerialConnectionOptions serialOptions)
    {
        SessionId = sessionId;
        DisplayName = displayName;
        ConnectionType = ConnectionType.Serial;
        SerialOptions = serialOptions ?? throw new ArgumentNullException(nameof(serialOptions));
    }

    public TerminalSessionRequest(
        Guid sessionId,
        string displayName,
        RawTcpConnectionOptions rawTcpOptions)
    {
        SessionId = sessionId;
        DisplayName = displayName;
        ConnectionType = ConnectionType.RawTcp;
        RawTcpOptions = rawTcpOptions ?? throw new ArgumentNullException(nameof(rawTcpOptions));
    }

    public Guid SessionId { get; }

    public string DisplayName { get; }

    public ConnectionType ConnectionType { get; }

    public LocalShellKind? ShellKind { get; }

    public string? WorkingDirectory { get; }

    public SshConnectionOptions? SshOptions { get; }

    public TelnetConnectionOptions? TelnetOptions { get; }

    public SerialConnectionOptions? SerialOptions { get; }

    public RawTcpConnectionOptions? RawTcpOptions { get; }

    public override string ToString()
    {
        return ConnectionType switch
        {
            ConnectionType.Ssh => $"SSH session {SessionId} ({SshOptions})",
            ConnectionType.Telnet => $"Telnet session {SessionId} ({TelnetOptions})",
            ConnectionType.Serial => $"Serial session {SessionId} ({SerialOptions})",
            ConnectionType.RawTcp => $"Raw TCP session {SessionId} ({RawTcpOptions})",
            _ => $"Local session {SessionId} ({ShellKind})",
        };
    }
}
