using Microsoft.Extensions.Logging.Abstractions;
using NetOpsTerminal.Application.Sessions;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Sessions;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Tests;

public sealed class SessionManagerTests
{
    [Fact]
    public async Task OpenAsyncConnectsAndRegistersSession()
    {
        var factory = new RecordingSessionFactory();
        await using var manager = CreateManager(factory);
        var request = NewRequest();

        ITerminalSession session = await manager.OpenAsync(request, CancellationToken.None);

        Assert.Equal(SessionState.Connected, session.State);
        Assert.Same(session, Assert.Single(manager.Sessions));
        Assert.Equal(request, factory.LastRequest);
    }

    [Fact]
    public async Task OpenAsyncRemovesAndDisposesSessionWhenConnectFails()
    {
        var expected = new InvalidOperationException("Connect failed.");
        var factory = new RecordingSessionFactory(() => new RecordingSession(NewRequest(), expected));
        await using var manager = CreateManager(factory);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.OpenAsync(NewRequest(), CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Empty(manager.Sessions);
        Assert.True(factory.LastSession!.WasDisposed);
    }

    [Fact]
    public async Task CloseAsyncDisconnectsAndDisposesOnlyRequestedSession()
    {
        var factory = new RecordingSessionFactory();
        await using var manager = CreateManager(factory);
        ITerminalSession first = await manager.OpenAsync(NewRequest(), CancellationToken.None);
        ITerminalSession second = await manager.OpenAsync(NewRequest(), CancellationToken.None);

        await manager.CloseAsync(second.Id, CancellationToken.None);

        Assert.Same(first, Assert.Single(manager.Sessions));
        Assert.Equal(SessionState.Connected, first.State);
        Assert.Equal(SessionState.Disposed, second.State);
    }

    [Fact]
    public async Task CloseAllAsyncAttemptsEverySessionWhenOneDisconnectFails()
    {
        var sessions = new Queue<RecordingSession>(
        [
            new RecordingSession(NewRequest(), disconnectFailure: new InvalidOperationException("Stop failed.")),
            new RecordingSession(NewRequest()),
        ]);
        var factory = new RecordingSessionFactory(() => sessions.Dequeue());
        await using var manager = CreateManager(factory);
        RecordingSession first = (RecordingSession)await manager.OpenAsync(NewRequest(), CancellationToken.None);
        RecordingSession second = (RecordingSession)await manager.OpenAsync(NewRequest(), CancellationToken.None);

        AggregateException error = await Assert.ThrowsAsync<AggregateException>(
            () => manager.CloseAllAsync(CancellationToken.None));

        Assert.Single(error.InnerExceptions);
        Assert.True(first.WasDisposed);
        Assert.True(second.WasDisposed);
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public async Task DuplicateSessionIdIsRejectedWithoutCreatingAnotherSession()
    {
        var factory = new RecordingSessionFactory();
        await using var manager = CreateManager(factory);
        TerminalSessionRequest request = NewRequest();
        await manager.OpenAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.OpenAsync(request, CancellationToken.None));

        Assert.Equal(1, factory.CreateCount);
        Assert.Single(manager.Sessions);
    }

    [Fact]
    public async Task RepeatedOpenCloseCyclesDisposeEverySessionAndReleaseManagerEntries()
    {
        const int cycleCount = 100;
        var factory = new RecordingSessionFactory();
        await using var manager = CreateManager(factory);

        for (int index = 0; index < cycleCount; index++)
        {
            ITerminalSession session = await manager.OpenAsync(NewRequest(), CancellationToken.None);
            await manager.CloseAsync(session.Id, CancellationToken.None);
        }

        Assert.Empty(manager.Sessions);
        Assert.Equal(cycleCount, factory.CreatedSessions.Count);
        Assert.All(factory.CreatedSessions, session => Assert.True(session.WasDisposed));
    }

    [Fact]
    public async Task CancelledOpenDisposesPartialSessionAndLeavesManagerReusable()
    {
        var connectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new Queue<RecordingSession>(
        [
            new RecordingSession(
                NewRequest(),
                connectAction: async cancellationToken =>
                {
                    connectStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }),
            new RecordingSession(NewRequest()),
        ]);
        var factory = new RecordingSessionFactory(() => sessions.Dequeue());
        await using var manager = CreateManager(factory);
        using var cancellation = new CancellationTokenSource();

        Task<ITerminalSession> pendingOpen = manager.OpenAsync(NewRequest(), cancellation.Token);
        await connectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingOpen);
        Assert.Empty(manager.Sessions);
        Assert.True(factory.CreatedSessions[0].WasDisposed);

        ITerminalSession recovered = await manager.OpenAsync(NewRequest(), CancellationToken.None);
        Assert.Equal(SessionState.Connected, recovered.State);
        Assert.Same(recovered, Assert.Single(manager.Sessions));
    }

    private static SessionManager CreateManager(ITerminalSessionFactory factory)
    {
        return new SessionManager(factory, NullLogger<SessionManager>.Instance);
    }

    private static TerminalSessionRequest NewRequest()
    {
        return new TerminalSessionRequest(
            Guid.NewGuid(),
            "Local PowerShell",
            LocalShellKind.PowerShell);
    }

    private sealed class RecordingSessionFactory(Func<RecordingSession>? createSession = null)
        : ITerminalSessionFactory
    {
        private readonly List<RecordingSession> _createdSessions = [];

        public TerminalSessionRequest? LastRequest { get; private set; }

        public RecordingSession? LastSession { get; private set; }
        public List<RecordingSession> CreatedSessions => _createdSessions;


        public int CreateCount { get; private set; }

        public Task<ITerminalSession> CreateAsync(
            TerminalSessionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            CreateCount++;
            LastSession = createSession?.Invoke() ?? new RecordingSession(request);
            _createdSessions.Add(LastSession);
            return Task.FromResult<ITerminalSession>(LastSession);
        }
    }

    private sealed class RecordingSession : ITerminalSession
    {
        private readonly Exception? _connectFailure;
        private readonly Exception? _disconnectFailure;
        private readonly Func<CancellationToken, Task>? _connectAction;

        public RecordingSession(
            TerminalSessionRequest request,
            Exception? connectFailure = null,
            Exception? disconnectFailure = null,
            Func<CancellationToken, Task>? connectAction = null)
        {
            Id = request.SessionId;
            DisplayName = request.DisplayName;
            ShellKind = request.ShellKind;
            _connectFailure = connectFailure;
            _disconnectFailure = disconnectFailure;
            _connectAction = connectAction;
        }

        public Guid Id { get; }

        public string DisplayName { get; }

        public ConnectionType ConnectionType => ConnectionType.LocalShell;

        public string ConnectionLabel => "POWERSHELL · LOCAL";

        public LocalShellKind? ShellKind { get; }

        public SessionState State { get; private set; } = SessionState.Created;

        public int FontSize { get; private set; } = TerminalFontSizePolicy.Default;

        public bool IsInputLocked { get; private set; }

        public bool WasDisposed { get; private set; }

        public event EventHandler<SessionState>? StateChanged;

        public event EventHandler<Exception>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_connectFailure is not null)
            {
                throw _connectFailure;
            }

            if (_connectAction is not null)
            {
                await _connectAction(cancellationToken);
            }

            SetState(SessionState.Connected);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disconnectFailure is not null)
            {
                throw _disconnectFailure;
            }

            SetState(SessionState.Disconnected);
            return Task.CompletedTask;
        }

        public Task ReconnectAsync(CancellationToken cancellationToken)
        {
            return ConnectAsync(cancellationToken);
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void SetFontSize(int fontSize)
        {
            FontSize = TerminalFontSizePolicy.Normalize(fontSize);
        }

        public void SetInputLocked(bool isLocked)
        {
            IsInputLocked = isLocked;
        }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            SetState(SessionState.Disposed);
            return ValueTask.CompletedTask;
        }

        private void SetState(SessionState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }
    }
}
