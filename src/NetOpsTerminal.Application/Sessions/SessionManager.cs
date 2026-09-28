using Microsoft.Extensions.Logging;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Sessions;

public sealed partial class SessionManager : ISessionManager
{
    private readonly ITerminalSessionFactory _sessionFactory;
    private readonly ILogger<SessionManager> _logger;
    private readonly Dictionary<Guid, ITerminalSession> _sessions = [];
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    public SessionManager(
        ITerminalSessionFactory sessionFactory,
        ILogger<SessionManager> logger)
    {
        _sessionFactory = sessionFactory;
        _logger = logger;
    }

    public IReadOnlyCollection<ITerminalSession> Sessions
    {
        get
        {
            lock (_sessions)
            {
                return _sessions.Values.ToArray();
            }
        }
    }

    public async Task<ITerminalSession> OpenAsync(
        TerminalSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (request.SessionId == Guid.Empty)
            {
                throw new ArgumentException("A session ID is required.", nameof(request));
            }

            lock (_sessions)
            {
                if (_sessions.ContainsKey(request.SessionId))
                {
                    throw new InvalidOperationException($"Session {request.SessionId} is already registered.");
                }
            }

            ITerminalSession session = await _sessionFactory
                .CreateAsync(request, cancellationToken)
                .ConfigureAwait(false);

            lock (_sessions)
            {
                _sessions.Add(session.Id, session);
            }

            try
            {
                await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
                LogSessionOpened(_logger, session.Id, session.ConnectionType);
                return session;
            }
            catch
            {
                lock (_sessions)
                {
                    _sessions.Remove(session.Id);
                }

                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task CloseAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ITerminalSession? session;
            lock (_sessions)
            {
                _sessions.Remove(sessionId, out session);
            }

            if (session is null)
            {
                return;
            }

            try
            {
                await session.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            LogSessionClosed(_logger, sessionId);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task CloseAllAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ITerminalSession[] sessions;
            lock (_sessions)
            {
                sessions = _sessions.Values.ToArray();
                _sessions.Clear();
            }

            List<Exception>? failures = null;
            foreach (ITerminalSession session in sessions)
            {
                try
                {
                    await session.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
                finally
                {
                    try
                    {
                        await session.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        (failures ??= []).Add(exception);
                    }
                }
            }

            if (failures is not null)
            {
                throw new AggregateException(
                    "One or more terminal sessions failed to close cleanly.",
                    failures);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await CloseAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            _operationGate.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [LoggerMessage(
        EventId = 201,
        Level = LogLevel.Information,
        Message = "Opened terminal session {SessionId} using {ConnectionType}")]
    private static partial void LogSessionOpened(
        ILogger logger,
        Guid sessionId,
        ConnectionType connectionType);

    [LoggerMessage(
        EventId = 202,
        Level = LogLevel.Information,
        Message = "Closed local terminal session {SessionId}")]
    private static partial void LogSessionClosed(ILogger logger, Guid sessionId);
}
