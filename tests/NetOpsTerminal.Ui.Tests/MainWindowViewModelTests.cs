using Microsoft.Extensions.Logging.Abstractions;
using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Sessions;
using NetOpsTerminal.Application.Settings;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Application.Credentials;
using NetOpsTerminal.Application.Workspaces;
using NetOpsTerminal.App.Views;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Sessions;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Ui.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void CredentialEditorResultRedactsPasswordFromStringRepresentation()
    {
        const string secret = "must-not-appear";
        var result = new CredentialEditorResult(
            new CredentialProfileEditRequest(null, "Test", "operator"),
            secret);

        Assert.DoesNotContain(secret, result.ToString(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsyncAppliesPersistedUiSettings()
    {
        var settings = new ApplicationSettings
        {
            IsSidebarCollapsed = true,
            SidebarWidth = 330,
        };
        var service = new RecordingSettingsService(settings);
        var viewModel = CreateViewModel(service);

        await viewModel.InitializeAsync();
        Assert.True(viewModel.IsSidebarCollapsed);
        Assert.Equal(330, viewModel.SidebarWidth);
        Assert.Equal(MainWindowViewModel.CollapsedSidebarWidth, viewModel.SidebarDisplayWidth);
    }

    [Fact]
    public async Task ToggleSidebarCommandOpensOverlayWithoutPersistingTransientState()
    {
        var service = new RecordingSettingsService(new ApplicationSettings());
        var viewModel = CreateViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.ToggleSidebarCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSidebarCollapsed);
        Assert.Equal(MainWindowViewModel.CollapsedSidebarWidth, viewModel.SidebarDisplayWidth);
        Assert.Null(service.Saved);
    }

    [Fact]
    public async Task PinnedDeviceExplorerIgnoresAutomaticCloseUntilUnpinned()
    {
        var viewModel = CreateViewModel(new RecordingSettingsService(new ApplicationSettings()));
        await viewModel.InitializeAsync();
        await viewModel.ToggleSidebarCommand.ExecuteAsync(null);
        viewModel.ToggleDeviceExplorerPinCommand.Execute(null);

        viewModel.CloseDeviceExplorerUnlessPinned();

        Assert.True(viewModel.IsSidebarExpanded);
        Assert.True(viewModel.IsDeviceExplorerPinned);

        viewModel.ToggleDeviceExplorerPinCommand.Execute(null);
        viewModel.CloseDeviceExplorerUnlessPinned();

        Assert.True(viewModel.IsSidebarCollapsed);
        Assert.False(viewModel.IsDeviceExplorerPinned);
    }

    [Fact]
    public async Task SuccessfulSessionOpenLeavesExplorerOverlayClosed()
    {
        var service = new RecordingSettingsService(new ApplicationSettings());
        var viewModel = CreateViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.NewPowerShellCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSidebarCollapsed);
        Assert.Equal(MainWindowViewModel.CollapsedSidebarWidth, viewModel.SidebarDisplayWidth);
        Assert.Null(service.Saved);
    }


    [Fact]
    public async Task NewLocalShellCommandsCreateIndependentTerminalTabs()
    {
        var sessionManager = new RecordingSessionManager();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            sessionManager);

        await viewModel.NewPowerShellCommand.ExecuteAsync(null);
        await viewModel.NewCommandPromptCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.TerminalTabs.Count);
        Assert.Equal(2, sessionManager.Sessions.Count);
        Assert.Equal(LocalShellKind.PowerShell, viewModel.TerminalTabs[0].Session.ShellKind);
        Assert.Equal(LocalShellKind.CommandPrompt, viewModel.TerminalTabs[1].Session.ShellKind);
        Assert.Same(viewModel.TerminalTabs[1], viewModel.SelectedTerminal);
        Assert.True(viewModel.HasTerminalSessions);
        Assert.False(viewModel.HasNoTerminalSessions);
    }

    [Fact]
    public async Task TerminalZoomUpdatesActiveTabAndPersistsDefault()
    {
        var settingsService = new RecordingSettingsService(new ApplicationSettings());
        var viewModel = CreateViewModel(
            settingsService,
            new RecordingSessionManager());
        await viewModel.InitializeAsync();
        await viewModel.NewPowerShellCommand.ExecuteAsync(null);

        await viewModel.ChangeTerminalFontSizeAsync(19);

        Assert.Equal(19, viewModel.TerminalFontSize);
        Assert.Equal(19, viewModel.SelectedTerminal!.FontSize);
        Assert.Equal(19, settingsService.Saved!.TerminalFontSize);
    }

    [Theory]
    [InlineData(1, TerminalFontSizePolicy.Minimum)]
    [InlineData(100, TerminalFontSizePolicy.Maximum)]
    public async Task TerminalZoomClampsToSafeRange(int requested, int expected)
    {
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()));

        await viewModel.ChangeTerminalFontSizeAsync(requested);

        Assert.Equal(expected, viewModel.TerminalFontSize);
    }

    [Fact]
    public async Task CloseSelectedTerminalRemovesAndDisposesOnlyThatSession()
    {
        var sessionManager = new RecordingSessionManager();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            sessionManager);
        await viewModel.NewPowerShellCommand.ExecuteAsync(null);
        await viewModel.NewCommandPromptCommand.ExecuteAsync(null);
        ITerminalSession first = viewModel.TerminalTabs[0].Session;
        ITerminalSession second = viewModel.TerminalTabs[1].Session;

        await viewModel.CloseSelectedTerminalAsync();

        Assert.Single(viewModel.TerminalTabs);
        Assert.Same(first, viewModel.TerminalTabs[0].Session);
        Assert.Equal(SessionState.Connected, first.State);
        Assert.Equal(SessionState.Disposed, second.State);
        Assert.Single(sessionManager.Sessions);
    }

    [Fact]
    public async Task OpenSshAsyncCreatesRemoteTabAndPasswordIsDisposedOnClose()
    {
        var sessionManager = new RecordingSessionManager();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            sessionManager);
        var password = EphemeralSecret.Create("test-only-password");
        var request = new TerminalSessionRequest(
            Guid.NewGuid(),
            "operator@server.test",
            new SshConnectionOptions("server.test", 22, "operator", password));

        await viewModel.OpenSshAsync(request);

        TerminalTabViewModel tab = Assert.Single(viewModel.TerminalTabs);
        Assert.Equal(ConnectionType.Ssh, tab.Session.ConnectionType);
        Assert.False(password.IsDisposed);
        Assert.Equal("Connected securely to server.test:22", viewModel.StatusMessage);

        await viewModel.CloseSelectedTerminalAsync();

        Assert.True(password.IsDisposed);
        Assert.Empty(viewModel.TerminalTabs);
    }

    public static TheoryData<TerminalSessionRequest, ConnectionType, string> Phase5Requests => new()
    {
        {
            new TerminalSessionRequest(Guid.NewGuid(), "Telnet", new TelnetConnectionOptions("router.test")),
            ConnectionType.Telnet,
            "router.test:23 (Telnet)"
        },
        {
            new TerminalSessionRequest(Guid.NewGuid(), "Serial", new SerialConnectionOptions("COM7")),
            ConnectionType.Serial,
            "COM7 (Serial)"
        },
        {
            new TerminalSessionRequest(Guid.NewGuid(), "Raw", new RawTcpConnectionOptions("console.test", 2001)),
            ConnectionType.RawTcp,
            "console.test:2001 (Raw TCP)"
        },
    };

    [Theory]
    [MemberData(nameof(Phase5Requests))]
    public async Task OpenRemoteAsyncCreatesProtocolTabAndSafeStatus(
        TerminalSessionRequest request,
        ConnectionType expectedType,
        string safeTarget)
    {
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            new RecordingSessionManager());

        await viewModel.OpenRemoteAsync(request);

        TerminalTabViewModel tab = Assert.Single(viewModel.TerminalTabs);
        Assert.Equal(expectedType, tab.Session.ConnectionType);
        Assert.Equal($"Connected to {safeTarget}", viewModel.StatusMessage);
        Assert.False(viewModel.IsConnectionBusy);
    }

    [Fact]
    public async Task OpenRemoteAsyncSurfacesActionableConnectionFailure()
    {
        var failure = new TerminalConnectionException(
            TerminalConnectionFailureKind.ConnectionRefused,
            "Connection to router.test:23 was refused.",
            "Socket error: ConnectionRefused");
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            new FailingSessionManager(failure));
        var request = new TerminalSessionRequest(
            Guid.NewGuid(),
            "Telnet",
            new TelnetConnectionOptions("router.test"));

        await viewModel.OpenRemoteAsync(request);

        Assert.Empty(viewModel.TerminalTabs);
        Assert.Equal(failure.UserMessage, viewModel.ConnectionErrorMessage);
        Assert.Equal(failure.TechnicalDetails, viewModel.ConnectionErrorTechnicalDetails);
        Assert.False(viewModel.IsConnectionBusy);
    }

    [Fact]
    public async Task CancelConnectionCommandReturnsBusyRemoteOpenToIdleWithoutFreezing()
    {
        var manager = new CancellableSessionManager();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            manager);
        var request = new TerminalSessionRequest(
            Guid.NewGuid(),
            "Telnet",
            new TelnetConnectionOptions("unreachable.test"));

        Task pendingOpen = viewModel.OpenRemoteAsync(request);
        await manager.OpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.IsConnectionBusy);

        viewModel.CancelConnectionCommand.Execute(null);
        await pendingOpen.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(manager.WasCancelled);
        Assert.False(viewModel.IsConnectionBusy);
        Assert.Equal("Telnet connection cancelled", viewModel.StatusMessage);
        Assert.Empty(viewModel.TerminalTabs);
    }


    [Fact]
    public async Task SupportsSixIndependentSimultaneousSessions()
    {
        var manager = new RecordingSessionManager();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            manager);

        for (int index = 0; index < 6; index++)
        {
            await viewModel.NewPowerShellCommand.ExecuteAsync(null);
        }

        Assert.Equal(6, viewModel.TerminalTabs.Count);
        Assert.Equal(6, viewModel.WorkspaceTabs.Count);
        Assert.Equal(6, manager.Sessions.Select(session => session.Id).Distinct().Count());
    }

    [Fact]
    public async Task SplitPaneReceivesNextSessionAndClosesWithoutAffectingSibling()
    {
        var manager = new RecordingSessionManager();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            manager);
        await viewModel.NewPowerShellCommand.ExecuteAsync(null);
        WorkspaceTabViewModel workspace = Assert.Single(viewModel.WorkspaceTabs);
        TerminalPaneViewModel original = workspace.ActivePane;

        await viewModel.SplitPaneAsync(original, PaneSplitDirection.Right);
        await viewModel.NewCommandPromptCommand.ExecuteAsync(null);

        Assert.Equal(2, workspace.Panes.Count);
        Assert.Equal(2, viewModel.TerminalTabs.Count);
        Assert.IsType<SplitPaneViewModel>(workspace.Root);

        await viewModel.CloseSelectedTerminalAsync();

        Assert.Single(workspace.Panes);
        Assert.Same(original, workspace.ActivePane);
        Assert.Single(manager.Sessions);
        Assert.Single(viewModel.TerminalTabs);
    }

    [Fact]
    public async Task FocusNextPaneCyclesForwardAndBackward()
    {
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            new RecordingSessionManager());
        await viewModel.NewPowerShellCommand.ExecuteAsync(null);
        WorkspaceTabViewModel workspace = Assert.Single(viewModel.WorkspaceTabs);
        TerminalPaneViewModel first = workspace.ActivePane;
        await viewModel.SplitPaneAsync(first, PaneSplitDirection.Right);
        await viewModel.NewCommandPromptCommand.ExecuteAsync(null);
        TerminalPaneViewModel second = workspace.ActivePane;

        viewModel.FocusNextPane(reverse: false);

        Assert.Same(first, workspace.ActivePane);
        Assert.True(first.IsActive);
        Assert.False(second.IsActive);

        viewModel.FocusNextPane(reverse: true);

        Assert.Same(second, workspace.ActivePane);
        Assert.True(second.IsActive);
        Assert.False(first.IsActive);
    }

    [Fact]
    public async Task RestoresWorkspaceDisconnectedWithoutCreatingSessions()
    {
        Guid paneId = Guid.NewGuid();
        Guid tabId = Guid.NewGuid();
        var document = new WorkspaceDocument
        {
            ActiveTabId = tabId,
            Tabs =
            [
                new WorkspaceTabDocument
                {
                    TabId = tabId,
                    ActivePaneId = paneId,
                    Root = new PaneNodeDocument
                    {
                        NodeId = paneId,
                        Launch = new WorkspaceLaunchReference
                        {
                            Kind = WorkspaceLaunchKind.SavedDevice,
                            DisplayName = "Production core",
                            ConnectionType = ConnectionType.Ssh,
                            DeviceId = Guid.NewGuid(),
                            Environment = NetOpsTerminal.Core.Devices.DeviceEnvironment.Production,
                        },
                    },
                },
            ],
        };
        var manager = new RecordingSessionManager();
        var workspaceService = new InMemoryWorkspaceService(document);
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            manager,
            workspaceService);

        await viewModel.InitializeAsync();

        TerminalPaneViewModel pane = Assert.Single(viewModel.WorkspaceTabs).ActivePane;
        Assert.Empty(manager.Sessions);
        Assert.Empty(viewModel.TerminalTabs);
        Assert.True(pane.IsRestoredPlaceholder);
        Assert.True(pane.IsProduction);
        Assert.True(pane.CanReconnect);
    }

    [Fact]
    public async Task InputLockTogglesOnlyActiveTerminal()
    {
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()));
        await viewModel.NewPowerShellCommand.ExecuteAsync(null);

        viewModel.ToggleActivePaneInputLock();

        Assert.True(viewModel.SelectedTerminal!.IsInputLocked);
        Assert.Equal("UNLOCK INPUT", viewModel.SelectedTerminal.InputLockLabel);

        viewModel.ToggleActivePaneInputLock();

        Assert.False(viewModel.SelectedTerminal.IsInputLocked);
    }

    private static MainWindowViewModel CreateViewModel(
        IApplicationSettingsService settingsService,
        ISessionManager? sessionManager = null,
        IWorkspaceService? workspaceService = null)
    {
        return new MainWindowViewModel(
            settingsService,
            sessionManager ?? new RecordingSessionManager(),
            new DeviceExplorerViewModel(new EmptyDeviceCatalogService()),
            new UnusedSavedDeviceSessionService(),
            workspaceService ?? new InMemoryWorkspaceService(),
            NullLogger<MainWindowViewModel>.Instance);
    }

    private sealed class RecordingSettingsService(ApplicationSettings loaded)
        : IApplicationSettingsService
    {
        public ApplicationSettings? Saved { get; private set; }

        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(loaded);
        }

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }
    private sealed class InMemoryWorkspaceService(WorkspaceDocument? loaded = null) : IWorkspaceService
    {
        public WorkspaceDocument? Document { get; private set; } = loaded;

        public Task<WorkspaceDocument?> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Document);
        }

        public Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken)
        {
            Document = document;
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyDeviceCatalogService : IDeviceCatalogService
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<DeviceCatalogSnapshot> LoadAsync(
            string? searchText,
            DeviceCatalogFilter filter,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeviceCatalogSnapshot([], [], [], [], []));
        }

        public Task SaveAsync(DeviceEditRequest request, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RecordRecentAsync(Guid deviceId, DateTimeOffset connectedAtUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class UnusedSavedDeviceSessionService : ISavedDeviceSessionService
    {
        public Task<SavedDeviceSessionResult> OpenAsync(
            Guid deviceId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingSessionManager : ISessionManager
    {
        private readonly List<ITerminalSession> _sessions = [];

        public IReadOnlyCollection<ITerminalSession> Sessions => _sessions.ToArray();

        public async Task<ITerminalSession> OpenAsync(
            TerminalSessionRequest request,
            CancellationToken cancellationToken)
        {
            var session = new RecordingTerminalSession(request);
            await session.ConnectAsync(cancellationToken);
            _sessions.Add(session);
            return session;
        }

        public async Task CloseAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            ITerminalSession? session = _sessions.FirstOrDefault(candidate => candidate.Id == sessionId);
            if (session is null)
            {
                return;
            }

            _sessions.Remove(session);
            await session.DisconnectAsync(cancellationToken);
            await session.DisposeAsync();
        }

        public async Task CloseAllAsync(CancellationToken cancellationToken)
        {
            foreach (ITerminalSession session in _sessions.ToArray())
            {
                await CloseAsync(session.Id, cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await CloseAllAsync(CancellationToken.None);
        }
    }

    private sealed class CancellableSessionManager : ISessionManager
    {
        public TaskCompletionSource OpenStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCancelled { get; private set; }

        public IReadOnlyCollection<ITerminalSession> Sessions => [];

        public async Task<ITerminalSession> OpenAsync(
            TerminalSessionRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            OpenStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException exception)
            {
                WasCancelled = true;
                throw new TerminalConnectionException(
                    TerminalConnectionFailureKind.Cancelled,
                    "Connection was cancelled.",
                    exception.GetType().Name,
                    exception);
            }

            throw new InvalidOperationException("The cancellable test session unexpectedly completed.");
        }

        public Task CloseAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            _ = sessionId;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task CloseAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingSessionManager(Exception exception) : ISessionManager
    {
        public IReadOnlyCollection<ITerminalSession> Sessions => [];

        public Task<ITerminalSession> OpenAsync(
            TerminalSessionRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromException<ITerminalSession>(exception);
        }

        public Task CloseAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task CloseAllAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingTerminalSession : ITerminalSession
    {
        public RecordingTerminalSession(TerminalSessionRequest request)
        {
            Id = request.SessionId;
            DisplayName = request.DisplayName;
            ConnectionType = request.ConnectionType;
            ShellKind = request.ShellKind;
            SshOptions = request.SshOptions;
        }

        public Guid Id { get; }

        public string DisplayName { get; }

        public ConnectionType ConnectionType { get; }

        public string ConnectionLabel => ConnectionType switch
        {
            ConnectionType.Ssh => "SSH",
            ConnectionType.Telnet => "TELNET",
            ConnectionType.Serial => "SERIAL",
            ConnectionType.RawTcp => "RAW TCP",
            _ => "LOCAL",
        };

        public LocalShellKind? ShellKind { get; }

        private SshConnectionOptions? SshOptions { get; }

        public SessionState State { get; private set; } = SessionState.Created;

        public int FontSize { get; private set; } = TerminalFontSizePolicy.Default;

        public bool IsInputLocked { get; private set; }

        public event EventHandler<SessionState>? StateChanged;

        public event EventHandler<Exception>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetState(SessionState.Connected);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (State != SessionState.Disposed)
            {
                SetState(SessionState.Disconnected);
            }

            return Task.CompletedTask;
        }

        public Task ReconnectAsync(CancellationToken cancellationToken)
        {
            return ConnectAsync(cancellationToken);
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
        {
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
            SshOptions?.Dispose();
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
