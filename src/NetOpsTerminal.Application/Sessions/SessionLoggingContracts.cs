using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Sessions;

public sealed record SessionLogHandle(Guid SessionId, string FilePath, DateTimeOffset StartedAtUtc);

public interface ISessionLogService : IAsyncDisposable
{
    bool IsLogging(Guid sessionId);

    string? GetLogFilePath(Guid sessionId);

    Task<SessionLogHandle> StartAsync(
        ITerminalSession session,
        CancellationToken cancellationToken = default);

    Task StopAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
