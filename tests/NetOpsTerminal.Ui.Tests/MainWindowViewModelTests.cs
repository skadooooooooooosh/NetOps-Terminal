using NetOpsTerminal.Core.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Sessions;
using NetOpsTerminal.Application.Settings;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Application.Credentials;
using NetOpsTerminal.Application.Workspaces;
using NetOpsTerminal.App.Views;
using NetOpsTerminal.App.Services;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedDeviceConnectionClosesOnlyUnpinnedExplorer(bool pinned)
    {
        DeviceProfile device = CreateSavedTelnetDevice();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            savedDeviceService: new StubSavedDeviceSessionService(device),
            catalogService: new EmptyDeviceCatalogService(device));
        await viewModel.InitializeAsync();
        await viewModel.ToggleSidebarCommand.ExecuteAsync(null);
        if (pinned)
            viewModel.ToggleDeviceExplorerPinCommand.Execute(null);
        DeviceExplorerNodeViewModel unassigned = Assert.Single(viewModel.DeviceExplorer.Nodes);
        unassigned.IsExpanded = true;
        viewModel.DeviceExplorer.SelectedNode = Assert.Single(unassigned.Children);

        await viewModel.OpenSelectedSavedDeviceAsync();

        Assert.Equal(pinned, viewModel.IsSidebarExpanded);
        Assert.True(Assert.Single(viewModel.DeviceExplorer.Nodes).IsExpanded);
        Assert.Single(viewModel.TerminalTabs);
    }

    [Fact]
    public async Task FailedSavedDeviceConnectionLeavesExplorerOpen()
    {
        DeviceProfile device = CreateSavedTelnetDevice();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            savedDeviceService: new StubSavedDeviceSessionService(device, fail: true),
            catalogService: new EmptyDeviceCatalogService(device));
        await viewModel.InitializeAsync();
        await viewModel.ToggleSidebarCommand.ExecuteAsync(null);
        viewModel.DeviceExplorer.SelectedNode = Assert.Single(
            Assert.Single(viewModel.DeviceExplorer.Nodes).Children);

        await viewModel.OpenSelectedSavedDeviceAsync();

        Assert.True(viewModel.IsSidebarExpanded);
        Assert.Empty(viewModel.TerminalTabs);
        Assert.NotNull(viewModel.ConnectionErrorMessage);
    }

    [Fact]
    public async Task WebOnlyDefaultOpensBrowserWithoutCreatingTerminalTab()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var web = new DeviceWebAccessSettings("https://switch.test/",
            WebBrowserPreference.WindowsDefault, DeviceOpenAction.Web);
        var device = new DeviceProfile(Guid.NewGuid(), "Web switch", string.Empty,
            new SavedDeviceConnectionSettings(ConnectionType.Ssh, "switch.test", 22),
            DeviceType.Switch, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Unknown, null, null, false, string.Empty,
            now, now, null, DeviceAccessMode.WebOnly, web);
        var browser = new RecordingWebAccessService();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            catalogService: new EmptyDeviceCatalogService(device),
            deviceWebAccessService: browser);
        await viewModel.InitializeAsync();
        viewModel.DeviceExplorer.SelectedNode = Assert.Single(
            Assert.Single(viewModel.DeviceExplorer.Nodes).Children);

        await viewModel.OpenSelectedSavedDeviceAsync();
        await viewModel.OpenSelectedSavedTerminalAsync();

        Assert.Equal(1, browser.OpenCount);
        Assert.Equal(web, browser.LastSettings);
        Assert.Empty(viewModel.TerminalTabs);
    }
    [Fact]
    public async Task DualAccessUsesChosenDefaultAndKeepsExplicitTerminalAction()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var device = new DeviceProfile(Guid.NewGuid(), "Dual switch", string.Empty,
            new SavedDeviceConnectionSettings(ConnectionType.Telnet, "dual.test", 23,
                telnetAcknowledged: true),
            DeviceType.Switch, string.Empty, string.Empty, null, null,
            DeviceEnvironment.Unknown, null, null, false, string.Empty,
            now, now, null, DeviceAccessMode.TerminalAndWeb,
            new DeviceWebAccessSettings("https://dual.test/",
                WebBrowserPreference.WindowsDefault, DeviceOpenAction.Web));
        var browser = new RecordingWebAccessService();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            savedDeviceService: new StubSavedDeviceSessionService(device),
            catalogService: new EmptyDeviceCatalogService(device),
            deviceWebAccessService: browser);
        await viewModel.InitializeAsync();
        viewModel.DeviceExplorer.SelectedNode = Assert.Single(
            Assert.Single(viewModel.DeviceExplorer.Nodes).Children);

        await viewModel.OpenSelectedSavedDeviceAsync();
        Assert.Equal(1, browser.OpenCount);
        Assert.Empty(viewModel.TerminalTabs);

        await viewModel.OpenSelectedSavedTerminalAsync();
        Assert.Single(viewModel.TerminalTabs);
    }
    private static DeviceProfile CreateSavedTelnetDevice()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new DeviceProfile(Guid.NewGuid(), "Test switch", string.Empty,
            new SavedDeviceConnectionSettings(ConnectionType.Telnet, "switch.test", 23,
                telnetAcknowledged: true), DeviceType.Switch, string.Empty, string.Empty,
            null, null, DeviceEnvironment.Unknown, null, null, false,
            string.Empty, now, now, null);
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


    [Theory]
    [InlineData(LocalShellKind.PowerShell, false)]
    [InlineData(LocalShellKind.PowerShell, true)]
    [InlineData(LocalShellKind.CommandPrompt, false)]
    [InlineData(LocalShellKind.CommandPrompt, true)]
    public async Task LocalShellClosesOnlyUnpinnedExplorerAfterOpening(
        LocalShellKind shellKind, bool pinned)
    {
        var viewModel = CreateViewModel(new RecordingSettingsService(new ApplicationSettings()));
        await viewModel.InitializeAsync();
        await viewModel.ToggleSidebarCommand.ExecuteAsync(null);
        if (pinned)
            viewModel.ToggleDeviceExplorerPinCommand.Execute(null);

        if (shellKind == LocalShellKind.PowerShell)
            await viewModel.NewPowerShellCommand.ExecuteAsync(null);
        else
            await viewModel.NewCommandPromptCommand.ExecuteAsync(null);

        Assert.Equal(pinned, viewModel.IsSidebarExpanded);
        Assert.Equal(pinned, viewModel.IsDeviceExplorerPinned);
        Assert.Equal(shellKind, Assert.Single(viewModel.TerminalTabs).Session.ShellKind);
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
        Assert.False(tab.BackspaceSendsControlH);
        Assert.False(tab.BackspaceSendsDelete);
        Assert.False(password.IsDisposed);
        Assert.Equal("Connected securely to server.test:22", viewModel.StatusMessage);

        await viewModel.CloseSelectedTerminalAsync();

        Assert.True(password.IsDisposed);
        Assert.Empty(viewModel.TerminalTabs);
    }

    [Fact]
    public async Task QuickConnectSshBackspaceModeReachesTerminalTab()
    {
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            new RecordingSessionManager());
        var password = EphemeralSecret.Create("test-only-password");
        var request = new TerminalSessionRequest(
            Guid.NewGuid(), "SSH", new SshConnectionOptions("server.test", 22, "operator", password));

        await viewModel.OpenRemoteAsync(request, backspaceSendsControlH: true);

        TerminalTabViewModel tab = Assert.Single(viewModel.TerminalTabs);
        Assert.True(tab.BackspaceSendsControlH);
        Assert.False(tab.BackspaceSendsDelete);
        await viewModel.CloseSelectedTerminalAsync();
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
        Assert.False(tab.BackspaceSendsControlH);
        Assert.False(tab.BackspaceSendsDelete);
        Assert.Equal($"Connected to {safeTarget}", viewModel.StatusMessage);
        Assert.False(viewModel.IsConnectionBusy);
    }

    [Theory]
    [MemberData(nameof(Phase5Requests))]
    public async Task QuickConnectDeleteModeReachesOtherRemoteTerminalTabs(
        TerminalSessionRequest request, ConnectionType expectedType, string safeTarget)
    {
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            new RecordingSessionManager());

        await viewModel.OpenRemoteAsync(request, backspaceSendsDelete: true);

        TerminalTabViewModel tab = Assert.Single(viewModel.TerminalTabs);
        Assert.Equal(expectedType, tab.Session.ConnectionType);
        Assert.False(tab.BackspaceSendsControlH);
        Assert.True(tab.BackspaceSendsDelete);
        Assert.Equal($"Connected to {safeTarget}", viewModel.StatusMessage);
        await viewModel.CloseSelectedTerminalAsync();
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
        Assert.True(viewModel.IsFooterError);
        Assert.Equal("Selected tab disconnected or failed", viewModel.ActiveTabStatusText);
        Assert.Empty(viewModel.TerminalTabs);
    }


    [Fact]
    public async Task PendingPaneHidesTerminalOnlyIndicatorsUntilSessionExists()
    {
        var launch = new WorkspaceLaunchReference
        {
            Kind = WorkspaceLaunchKind.TransientConnection,
            DisplayName = "Test switch",
            ConnectionType = ConnectionType.Telnet,
            Environment = DeviceEnvironment.Production,
        };
        var pane = new TerminalPaneViewModel(Guid.NewGuid());
        pane.BeginConnection(launch, "Connecting...");
        Assert.True(pane.StatusIsConnecting);
        Assert.False(pane.StatusIsConnected);
        Assert.False(pane.StatusIsDisconnected);
        Assert.False(pane.ShowLogging);
        Assert.False(pane.ShowInputLocked);
        Assert.False(pane.ShowFind);
        Assert.False(pane.ShowProductionBanner);
        pane.EndConnection("Connection failed");
        Assert.True(pane.StatusIsDisconnected);
        Assert.False(pane.IsRestoredPlaceholder);
        pane.BeginConnection(launch, "Connecting...");

        var session = new RecordingTerminalSession(new TerminalSessionRequest(
            Guid.NewGuid(), "Test switch", new TelnetConnectionOptions("switch.test")));
        await session.ConnectAsync(CancellationToken.None);
        var terminal = new TerminalTabViewModel(session, _ => Task.CompletedTask);
        pane.Attach(terminal, launch);
        Assert.True(pane.StatusIsConnected);
        Assert.False(pane.StatusIsConnecting);
        Assert.False(pane.StatusIsDisconnected);
        terminal.ToggleInputLock();
        terminal.OpenFind();

        Assert.True(pane.ShowInputLocked);
        Assert.True(pane.ShowFind);
        Assert.True(pane.ShowProductionBanner);
        pane.Detach();
        Assert.False(pane.ShowInputLocked);
        Assert.False(pane.ShowFind);
        Assert.False(pane.ShowProductionBanner);
        terminal.Dispose();

        var disconnectedSession = new RecordingTerminalSession(new TerminalSessionRequest(
            Guid.NewGuid(), "Test switch", new TelnetConnectionOptions("switch.test")));
        await disconnectedSession.ConnectAsync(CancellationToken.None);
        await disconnectedSession.DisconnectAsync(CancellationToken.None);
        var disconnectedTerminal = new TerminalTabViewModel(
            disconnectedSession, _ => Task.CompletedTask);
        pane.Attach(disconnectedTerminal, launch);
        Assert.True(pane.StatusIsDisconnected);
        Assert.False(pane.StatusIsConnected);
        disconnectedTerminal.Dispose();
    }

    [Fact]
    public async Task ConcurrentConnectionsKeepTheirOwnTabsAndDoNotStealFocus()
    {
        var manager = new ControlledSessionManager();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()), manager);
        var first = new TerminalSessionRequest(
            Guid.NewGuid(), "First switch", new TelnetConnectionOptions("first.test"));
        var second = new TerminalSessionRequest(
            Guid.NewGuid(), "Second switch", new TelnetConnectionOptions("second.test"));

        Assert.True(viewModel.IsFooterIdle);
        Task firstOpen = viewModel.OpenRemoteAsync(first);
        WorkspaceTabViewModel firstTab = Assert.Single(viewModel.WorkspaceTabs);
        Assert.True(firstTab.ActivePane.IsConnecting);
        Assert.True(viewModel.IsFooterWarning);
        Assert.Empty(viewModel.TerminalTabs);

        Task secondOpen = viewModel.OpenRemoteAsync(second);
        Assert.Equal(2, viewModel.WorkspaceTabs.Count);
        WorkspaceTabViewModel secondTab = viewModel.WorkspaceTabs[1];
        Assert.True(secondTab.ActivePane.IsConnecting);
        Assert.Same(secondTab, viewModel.SelectedWorkspace);

        await manager.CompleteAsync(first);
        await firstOpen;
        Assert.Same(firstTab.ActivePane.Terminal, Assert.Single(viewModel.TerminalTabs));
        Assert.Same(secondTab, viewModel.SelectedWorkspace);
        Assert.True(secondTab.ActivePane.IsConnecting);
        Assert.True(viewModel.IsFooterWarning);
        Assert.True(viewModel.IsConnectionBusy);

        viewModel.SelectedWorkspace = firstTab;
        Assert.True(viewModel.IsFooterConnected);
        viewModel.SelectedWorkspace = secondTab;
        Assert.True(viewModel.IsFooterWarning);

        await manager.CompleteAsync(second);
        await secondOpen;
        Assert.Equal(2, viewModel.TerminalTabs.Count);
        Assert.Same(secondTab.ActivePane.Terminal, viewModel.SelectedTerminal);
        Assert.True(viewModel.IsFooterConnected);
        Assert.Equal("Selected tab connected", viewModel.ActiveTabStatusText);
        Assert.False(viewModel.IsConnectionBusy);
    }

    [Fact]
    public async Task ClosingPendingTabCancelsOnlyItsConnection()
    {
        var manager = new ControlledSessionManager();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()), manager);
        var first = new TerminalSessionRequest(
            Guid.NewGuid(), "First switch", new TelnetConnectionOptions("first.test"));
        var second = new TerminalSessionRequest(
            Guid.NewGuid(), "Second switch", new TelnetConnectionOptions("second.test"));

        Task firstOpen = viewModel.OpenRemoteAsync(first);
        WorkspaceTabViewModel firstTab = Assert.Single(viewModel.WorkspaceTabs);
        Task secondOpen = viewModel.OpenRemoteAsync(second);
        WorkspaceTabViewModel secondTab = viewModel.WorkspaceTabs[1];

        await viewModel.ClosePaneAsync(firstTab.ActivePane);
        await firstOpen;
        Assert.Single(viewModel.WorkspaceTabs);
        Assert.Same(secondTab, viewModel.SelectedWorkspace);
        Assert.True(secondTab.ActivePane.IsConnecting);
        Assert.True(viewModel.IsConnectionBusy);

        await manager.CompleteAsync(second);
        await secondOpen;
        Assert.Single(viewModel.TerminalTabs);
        Assert.Same(secondTab.ActivePane.Terminal, viewModel.SelectedTerminal);
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
    public async Task ReorderedWorkspaceTabsKeepSessionsActiveAndRestoreInSavedOrder()
    {
        var manager = new RecordingSessionManager();
        var workspaceService = new InMemoryWorkspaceService();
        var viewModel = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            manager, workspaceService);
        for (int index = 0; index < 4; index++)
            await viewModel.NewPowerShellCommand.ExecuteAsync(null);

        WorkspaceTabViewModel[] original = viewModel.WorkspaceTabs.ToArray();
        Guid[] sessionIds = manager.Sessions.Select(session => session.Id).ToArray();
        Assert.True(viewModel.CanMoveWorkspaceTab(original[3], original[0], false));
        Assert.True(await viewModel.MoveWorkspaceTabAsync(original[3], original[0], false));
        Assert.Equal([original[3], original[0], original[1], original[2]],
            viewModel.WorkspaceTabs);
        Assert.False(viewModel.CanMoveWorkspaceTab(original[3], original[0], false));
        Assert.False(await viewModel.MoveWorkspaceTabAsync(original[3], original[0], false));

        Assert.True(await viewModel.MoveWorkspaceTabAsync(original[3], original[1], true));
        Assert.Equal([original[0], original[1], original[3], original[2]],
            viewModel.WorkspaceTabs);
        Assert.Same(original[3], viewModel.SelectedWorkspace);
        Assert.Same(original[3].ActivePane.Terminal, viewModel.SelectedTerminal);
        Assert.Equal(sessionIds, manager.Sessions.Select(session => session.Id));
        Assert.Equal(4, viewModel.TerminalTabs.Count);
        Assert.Equal(
            viewModel.WorkspaceTabs.Select(tab => tab.Id),
            workspaceService.Document!.Tabs.Select(tab => tab.TabId));

        var restored = CreateViewModel(
            new RecordingSettingsService(new ApplicationSettings()),
            new RecordingSessionManager(), workspaceService);
        await restored.InitializeAsync();
        Assert.Equal(
            viewModel.WorkspaceTabs.Select(tab => tab.Id),
            restored.WorkspaceTabs.Select(tab => tab.Id));
        Assert.Equal(original[3].Id, restored.SelectedWorkspace!.Id);
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
        IWorkspaceService? workspaceService = null,
        ISavedDeviceSessionService? savedDeviceService = null,
        IDeviceCatalogService? catalogService = null,
        IDeviceWebAccessService? deviceWebAccessService = null)
    {
        return new MainWindowViewModel(
            settingsService,
            sessionManager ?? new RecordingSessionManager(),
            new DeviceExplorerViewModel(catalogService ?? new EmptyDeviceCatalogService()),
            savedDeviceService ?? new UnusedSavedDeviceSessionService(),
            workspaceService ?? new InMemoryWorkspaceService(),
            NullLogger<MainWindowViewModel>.Instance,
            deviceWebAccessService: deviceWebAccessService);
    }

    private sealed class RecordingWebAccessService : IDeviceWebAccessService
    {
        public int OpenCount { get; private set; }
        public DeviceWebAccessSettings? LastSettings { get; private set; }

        public void Open(DeviceWebAccessSettings settings)
        {
            OpenCount++;
            LastSettings = settings;
        }

        public Task<bool> CheckPortAsync(DeviceWebAccessSettings settings,
            CancellationToken cancellationToken) => Task.FromResult(true);
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

    private sealed class EmptyDeviceCatalogService(DeviceProfile? device = null) : IDeviceCatalogService
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
            return Task.FromResult(new DeviceCatalogSnapshot(
                device is null ? [] : [device], [], [], [], []));
        }

        public Task SaveAsync(DeviceEditRequest request, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RenameSiteAsync(Guid id, string name, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task RenameFolderAsync(Guid id, string name, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteEmptySiteAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteEmptyFolderAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Site> CreateSiteAsync(string name, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<DeviceGroup> CreateFolderAsync(
            string name, Guid? siteId, Guid? parentGroupId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task MoveDeviceAsync(
            Guid deviceId, Guid? siteId, Guid? groupId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SaveExplorerOrderAsync(
            IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ResetExplorerOrderAsync(
            IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task RecordRecentAsync(Guid deviceId, DateTimeOffset connectedAtUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubSavedDeviceSessionService(DeviceProfile device, bool fail = false)
        : ISavedDeviceSessionService
    {
        public async Task<SavedDeviceSessionResult> OpenAsync(
            Guid deviceId, CancellationToken cancellationToken)
        {
            Assert.Equal(device.Id, deviceId);
            if (fail)
                throw new SavedDeviceConnectionException("Connection failed.");
            var session = new RecordingTerminalSession(new TerminalSessionRequest(
                Guid.NewGuid(), device.Name, new TelnetConnectionOptions("switch.test")));
            await session.ConnectAsync(cancellationToken);
            return new SavedDeviceSessionResult(device, session);
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

    private sealed class ControlledSessionManager : ISessionManager
    {
        private readonly Dictionary<Guid, TaskCompletionSource<ITerminalSession>> _pending = [];
        private readonly Dictionary<Guid, ITerminalSession> _sessions = [];

        public IReadOnlyCollection<ITerminalSession> Sessions => _sessions.Values.ToArray();

        public Task<ITerminalSession> OpenAsync(
            TerminalSessionRequest request, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<ITerminalSession>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(request.SessionId, completion);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        }

        public async Task CompleteAsync(TerminalSessionRequest request)
        {
            var session = new RecordingTerminalSession(request);
            await session.ConnectAsync(CancellationToken.None);
            _sessions.Add(session.Id, session);
            _pending[request.SessionId].TrySetResult(session);
        }

        public async Task CloseAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            if (_sessions.Remove(sessionId, out ITerminalSession? session))
            {
                await session.DisconnectAsync(cancellationToken);
                await session.DisposeAsync();
            }
        }

        public async Task CloseAllAsync(CancellationToken cancellationToken)
        {
            foreach (Guid id in _sessions.Keys.ToArray())
                await CloseAsync(id, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
