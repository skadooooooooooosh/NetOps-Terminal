using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Tests;

public sealed class Phase5ConnectionOptionsTests
{
    [Fact]
    public void TelnetDefaultsAreNetworkEngineeringSafeDefaults()
    {
        var options = new TelnetConnectionOptions("switch.example");

        Assert.Equal("switch.example", options.Host);
        Assert.Equal(23, options.Port);
        Assert.Equal("xterm-256color", options.TerminalType);
        Assert.Equal(TerminalTextEncoding.Utf8, options.Encoding);
        Assert.Equal(15, options.ConnectTimeoutSeconds);
    }

    [Fact]
    public void CiscoSerialDefaultsAre9600EightNOneWithoutFlowControl()
    {
        var options = new SerialConnectionOptions("com12");

        Assert.Equal("COM12", options.PortName);
        Assert.Equal(9600, options.BaudRate);
        Assert.Equal(8, options.DataBits);
        Assert.Equal(SerialParity.None, options.Parity);
        Assert.Equal(SerialStopBits.One, options.StopBits);
        Assert.Equal(SerialFlowControl.None, options.FlowControl);
    }

    [Fact]
    public void RawTcpDefaultsToUtf8CrLfAndFiniteTimeout()
    {
        var options = new RawTcpConnectionOptions("console.example", 2001);

        Assert.Equal(TerminalTextEncoding.Utf8, options.Encoding);
        Assert.Equal(TerminalLineEnding.CarriageReturnLineFeed, options.LineEnding);
        Assert.Equal(15, options.ConnectTimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void NetworkOptionsRejectInvalidPorts(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelnetConnectionOptions("host", port));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RawTcpConnectionOptions("host", port));
    }

    [Fact]
    public void SerialOptionsRejectInvalidFraming()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SerialConnectionOptions("COM1", dataBits: 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SerialConnectionOptions("COM1", baudRate: 49));
    }

    [Fact]
    public void SessionRequestsPreserveEachProtocolBoundary()
    {
        var telnet = new TerminalSessionRequest(Guid.NewGuid(), "Telnet", new TelnetConnectionOptions("host"));
        var serial = new TerminalSessionRequest(Guid.NewGuid(), "Serial", new SerialConnectionOptions("COM1"));
        var raw = new TerminalSessionRequest(Guid.NewGuid(), "Raw", new RawTcpConnectionOptions("host", 2001));

        Assert.Equal(ConnectionType.Telnet, telnet.ConnectionType);
        Assert.NotNull(telnet.TelnetOptions);
        Assert.Equal(ConnectionType.Serial, serial.ConnectionType);
        Assert.NotNull(serial.SerialOptions);
        Assert.Equal(ConnectionType.RawTcp, raw.ConnectionType);
        Assert.NotNull(raw.RawTcpOptions);
    }
}
