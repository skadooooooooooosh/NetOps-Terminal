using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Sessions;

public interface ISessionManager : IAsyncDisposable
{
    IReadOnlyCollection<ITerminalSession> Sessions { get; }

    Task<ITerminalSession> OpenAsync(
        TerminalSessionRequest request,
        CancellationToken cancellationToken);

    Task CloseAsync(Guid sessionId, CancellationToken cancellationToken);

    Task CloseAllAsync(CancellationToken cancellationToken);
}
