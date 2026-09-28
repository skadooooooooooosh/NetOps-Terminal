using NetOpsTerminal.Core.Connections;

namespace NetOpsTerminal.Transports.Abstractions;

public interface ITransportProvider
{
    ConnectionType Type { get; }

    Task<ITransportConnection> ConnectAsync(
        NetworkEndpoint endpoint,
        CancellationToken cancellationToken);
}
