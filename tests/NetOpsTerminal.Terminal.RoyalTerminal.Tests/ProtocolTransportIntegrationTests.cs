using System.Net;
using System.Net.Sockets;
using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Raw;
using RoyalTerminal.Terminal.Transport.Telnet;

namespace NetOpsTerminal.Terminal.RoyalTerminal.Tests;

public sealed class ProtocolTransportIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TerminalSessionDimensions Dimensions = new(80, 24, 800, 600);

    [Fact]
    public async Task RawTcpTransportConnectsSendsReceivesAndObservesRemoteClose()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource(Timeout);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        ITerminalTransport transport = new RawTcpTerminalTransportProvider().Create();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.DataReceived += (data, count) =>
            received.TrySetResult(Encoding.UTF8.GetString(data, 0, count));
        transport.ProcessExited += exitCode => exited.TrySetResult(exitCode);

        try
        {
            await transport.StartAsync(
                new RawTcpTransportOptions("127.0.0.1", port, Dimensions),
                cancellation.Token);
            using TcpClient client = await acceptTask.WaitAsync(cancellation.Token);
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("\u001b[32mready\u001b[0m"), cancellation.Token);

            Assert.Equal("\u001b[32mready\u001b[0m", await received.Task.WaitAsync(cancellation.Token));

            transport.SendInput(Encoding.UTF8.GetBytes("show version\r\n"));
            byte[] buffer = new byte[64];
            int count = await stream.ReadAsync(buffer, cancellation.Token);
            Assert.Equal("show version\r\n", Encoding.UTF8.GetString(buffer, 0, count));

            client.Close();
            await exited.Task.WaitAsync(cancellation.Token);
        }
        finally
        {
            await transport.StopAsync();
            (transport as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task TelnetTransportConnectsAndCarriesTerminalData()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource(Timeout);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        ITerminalTransport transport = new TelnetTerminalTransportProvider().Create();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.DataReceived += (data, count) =>
        {
            string text = Encoding.UTF8.GetString(data, 0, count);
            if (text.Contains("ready", StringComparison.Ordinal))
            {
                received.TrySetResult(text);
            }
        };

        try
        {
            await transport.StartAsync(
                new TelnetTransportOptions("127.0.0.1", port, "xterm-256color", Dimensions),
                cancellation.Token);
            using TcpClient client = await acceptTask.WaitAsync(cancellation.Token);
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("\u001b[36mready\u001b[0m\r\n"), cancellation.Token);

            string terminalData = await received.Task.WaitAsync(cancellation.Token);
            Assert.Contains("\u001b[36mready\u001b[0m", terminalData, StringComparison.Ordinal);
        }
        finally
        {
            await transport.StopAsync();
            (transport as IDisposable)?.Dispose();
        }
    }
}
