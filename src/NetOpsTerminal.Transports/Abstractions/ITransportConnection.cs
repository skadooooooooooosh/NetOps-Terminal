namespace NetOpsTerminal.Transports.Abstractions;

public interface ITransportConnection : IAsyncDisposable
{
    bool IsConnected { get; }

    Task DisconnectAsync(CancellationToken cancellationToken);
}
