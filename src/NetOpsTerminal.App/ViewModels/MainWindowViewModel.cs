using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Sessions;
using NetOpsTerminal.Application.Settings;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Application.Workspaces;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    public const double CollapsedSidebarWidth = 52;

    private readonly IApplicationSettingsService _settingsService;
    private readonly ISessionManager _sessionManager;
    private readonly ISavedDeviceSessionService _savedDeviceSessionService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ISessionLogService? _sessionLogService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IDeviceWebAccessService? _deviceWebAccessService;
    private bool _isSidebarCollapsed;
    private bool _isDeviceExplorerPinned;
    private double _sidebarWidth = ApplicationSettingsPolicy.DefaultSidebarWidth;
    private int _terminalFontSize = TerminalFontSizePolicy.Default;
    private bool _isSettingsVisible;
    private string _statusMessage = "Ready · No active sessions";
    private string? _connectionErrorMessage;
    private string? _connectionErrorTechnicalDetails;
    private bool _isConnectionBusy;
    private CancellationTokenSource? _connectionCancellation;
    private readonly Dictionary<TerminalPaneViewModel, CancellationTokenSource> _pendingConnections = [];
    private bool _isShuttingDown;
    private TerminalTabViewModel? _selectedTerminal;
    private WorkspaceTabViewModel? _selectedWorkspace;
    private int _powerShellSequence;
    private int _commandPromptSequence;

    public MainWindowViewModel(
        IApplicationSettingsService settingsService,
        ISessionManager sessionManager,
        DeviceExplorerViewModel deviceExplorer,
        ISavedDeviceSessionService savedDeviceSessionService,
        IWorkspaceService workspaceService,
        ILogger<MainWindowViewModel> logger,
        ISessionLogService? sessionLogService = null,
        IDeviceWebAccessService? deviceWebAccessService = null)
    {
        _settingsService = settingsService;
        _sessionManager = sessionManager;
        DeviceExplorer = deviceExplorer;
        _savedDeviceSessionService = savedDeviceSessionService;
        _workspaceService = workspaceService;
        _logger = logger;
        _sessionLogService = sessionLogService;
        _deviceWebAccessService = deviceWebAccessService;

        ToggleSidebarCommand = new AsyncRelayCommand(ToggleSidebarAsync);
        ToggleDeviceExplorerPinCommand = new RelayCommand(ToggleDeviceExplorerPin);
        ShowSettingsCommand = new RelayCommand(ShowSettings);
        ShowWorkspaceCommand = new RelayCommand(() => IsSettingsVisible = false);
        NewPowerShellCommand = new AsyncRelayCommand(
            () => OpenLocalTerminalAsync(LocalShellKind.PowerShell));
        NewCommandPromptCommand = new AsyncRelayCommand(
            () => OpenLocalTerminalAsync(LocalShellKind.CommandPrompt));
        CancelConnectionCommand = new RelayCommand(
            CancelPendingConnection,
            () => IsConnectionBusy);
        DismissConnectionErrorCommand = new RelayCommand(ClearConnectionError);
    }

    public ObservableCollection<TerminalTabViewModel> TerminalTabs { get; } = [];

    public ObservableCollection<WorkspaceTabViewModel> WorkspaceTabs { get; } = [];

    public DeviceExplorerViewModel DeviceExplorer { get; }

    public IAsyncRelayCommand ToggleSidebarCommand { get; }

    public IRelayCommand ToggleDeviceExplorerPinCommand { get; }

    public IRelayCommand ShowSettingsCommand { get; }

    public IRelayCommand ShowWorkspaceCommand { get; }

    public IAsyncRelayCommand NewPowerShellCommand { get; }

    public IAsyncRelayCommand NewCommandPromptCommand { get; }

    public IRelayCommand CancelConnectionCommand { get; }

    public IRelayCommand DismissConnectionErrorCommand { get; }

    public TerminalTabViewModel? SelectedTerminal
    {
        get => _selectedTerminal;
        set
        {
            if (ReferenceEquals(_selectedTerminal, value))
            {
                return;
            }

            if (_selectedTerminal is not null)
            {
                _selectedTerminal.PropertyChanged -= SelectedTerminal_OnPropertyChanged;
            }

            if (SetProperty(ref _selectedTerminal, value) && _selectedTerminal is not null)
            {
                _selectedTerminal.PropertyChanged += SelectedTerminal_OnPropertyChanged;
            }

            NotifyFooterChanged();
        }
    }

    public WorkspaceTabViewModel? SelectedWorkspace
    {
        get => _selectedWorkspace;
        set
        {
            if (ReferenceEquals(_selectedWorkspace, value))
                return;

            if (_selectedWorkspace is not null)
                _selectedWorkspace.PropertyChanged -= SelectedWorkspace_OnPropertyChanged;
            if (SetProperty(ref _selectedWorkspace, value))
            {
                if (_selectedWorkspace is not null)
                    _selectedWorkspace.PropertyChanged += SelectedWorkspace_OnPropertyChanged;
                SelectedTerminal = value?.ActivePane.Terminal;
                NotifyFooterChanged();
            }
        }
    }

    public bool HasTerminalSessions => WorkspaceTabs.Count > 0;

    public bool HasNoTerminalSessions => !HasTerminalSessions;

    public bool HasSelectedTerminal => SelectedTerminal is not null;

    public bool IsFooterConnected => SelectedWorkspace?.StatusIsConnected == true;

    public bool IsFooterWarning => SelectedWorkspace?.StatusIsConnecting == true;

    public bool IsFooterError => SelectedWorkspace?.StatusIsDisconnected == true;

    public bool IsFooterIdle => !IsFooterConnected && !IsFooterWarning && !IsFooterError;

    public string ActiveTabStatusText => IsFooterConnected ? "Selected tab connected"
        : IsFooterWarning ? "Selected tab connecting"
        : IsFooterError ? "Selected tab disconnected or failed"
        : "No active connection";

    public string FooterSessionCountText => TerminalTabs.Count == 1
        ? "1 session"
        : $"{TerminalTabs.Count} sessions";

    public string FooterConnectionText => SelectedTerminal?.ShellLabel ?? string.Empty;

    public string FooterLoggingText => SelectedTerminal?.IsLogging == true
        ? "Logging On"
        : "Logging Off";

    public bool CanStartConnection => !_isShuttingDown;

    public bool IsConnectionBusy
    {
        get => _isConnectionBusy;
        private set
        {
            if (SetProperty(ref _isConnectionBusy, value))
            {
                CancelConnectionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasConnectionError => ConnectionErrorMessage is not null;

    public string? ConnectionErrorMessage
    {
        get => _connectionErrorMessage;
        private set
        {
            if (SetProperty(ref _connectionErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasConnectionError));
            }
        }
    }

    public string? ConnectionErrorTechnicalDetails
    {
        get => _connectionErrorTechnicalDetails;
        private set => SetProperty(ref _connectionErrorTechnicalDetails, value);
    }

    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        private set
        {
            if (SetProperty(ref _isSidebarCollapsed, value))
            {
                OnPropertyChanged(nameof(IsSidebarExpanded));
                OnPropertyChanged(nameof(SidebarDisplayWidth));
            }
        }
    }

    public bool IsSidebarExpanded => !IsSidebarCollapsed;

    public bool IsDeviceExplorerPinned
    {
        get => _isDeviceExplorerPinned;
        private set => SetProperty(ref _isDeviceExplorerPinned, value);
    }

    public double SidebarWidth
    {
        get => _sidebarWidth;
        private set
        {
            if (SetProperty(ref _sidebarWidth, value))
            {
                OnPropertyChanged(nameof(SidebarDisplayWidth));
            }
        }
    }

    public double SidebarDisplayWidth => Math.Min(SidebarWidth, CollapsedSidebarWidth);

    public int TerminalFontSize
    {
        get => _terminalFontSize;
        private set => SetProperty(ref _terminalFontSize, value);
    }

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        private set
        {
            if (SetProperty(ref _isSettingsVisible, value))
            {
                OnPropertyChanged(nameof(IsWorkspaceVisible));
            }
        }
    }

    public bool IsWorkspaceVisible => !IsSettingsVisible;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await _settingsService.LoadAsync(cancellationToken);
            IsSidebarCollapsed = true;
            SidebarWidth = settings.SidebarWidth;
            TerminalFontSize = settings.TerminalFontSize;
            LogSettingsLoaded(_logger, IsSidebarCollapsed, SidebarWidth);
        }
        catch (Exception exception)
        {
            LogSettingsLoadFailed(_logger, exception);
            StatusMessage = "Settings could not be loaded; defaults are active";
        }

        await DeviceExplorer.InitializeAsync(cancellationToken);
        await RestoreWorkspaceAsync(cancellationToken);
    }

    public void PreviewSidebarWidth(double width)
    {
        if (IsSidebarCollapsed)
        {
            return;
        }

        SidebarWidth = Math.Clamp(
            width,
            ApplicationSettingsPolicy.MinimumSidebarWidth,
            ApplicationSettingsPolicy.MaximumSidebarWidth);
    }

    public Task CommitSidebarWidthAsync(CancellationToken cancellationToken = default)
    {
        return PersistAsync(cancellationToken);
    }

    public void CloseDeviceExplorer()
    {
        IsSidebarCollapsed = true;
    }

    public void CloseDeviceExplorerUnlessPinned()
    {
        if (!IsDeviceExplorerPinned)
        {
            CloseDeviceExplorer();
        }
    }

    public void SelectNextTerminal(bool reverse)
    {
        if (WorkspaceTabs.Count < 2)
        {
            return;
        }

        int currentIndex = SelectedWorkspace is null
            ? 0
            : WorkspaceTabs.IndexOf(SelectedWorkspace);
        int offset = reverse ? -1 : 1;
        int nextIndex = (currentIndex + offset + WorkspaceTabs.Count) % WorkspaceTabs.Count;
        SelectedWorkspace = WorkspaceTabs[nextIndex];
    }

    public bool CanMoveWorkspaceTab(
        WorkspaceTabViewModel source, WorkspaceTabViewModel target, bool insertAfter)
    {
        int sourceIndex = WorkspaceTabs.IndexOf(source);
        int targetIndex = WorkspaceTabs.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0)
            return false;

        int destination = targetIndex + (insertAfter ? 1 : 0);
        if (sourceIndex < destination)
            destination--;
        return destination != sourceIndex;
    }

    public async Task<bool> MoveWorkspaceTabAsync(
        WorkspaceTabViewModel source, WorkspaceTabViewModel target,
        bool insertAfter, CancellationToken cancellationToken = default)
    {
        if (!CanMoveWorkspaceTab(source, target, insertAfter))
            return false;

        int sourceIndex = WorkspaceTabs.IndexOf(source);
        int destination = WorkspaceTabs.IndexOf(target) + (insertAfter ? 1 : 0);
        if (sourceIndex < destination)
            destination--;
        WorkspaceTabs.Move(sourceIndex, destination);
        SelectedWorkspace = source;
        await PersistWorkspaceAsync(cancellationToken);
        return true;
    }

    public Task CloseSelectedTerminalAsync()
    {
        return SelectedWorkspace?.ActivePane is { } pane
            ? ClosePaneAsync(pane)
            : Task.CompletedTask;
    }

    public async Task ChangeTerminalFontSizeAsync(int requestedFontSize)
    {
        int normalized = TerminalFontSizePolicy.Normalize(requestedFontSize);
        TerminalFontSize = normalized;
        SelectedTerminal?.SetFontSize(normalized);
        StatusMessage = $"Terminal font size: {normalized} pt";
        await PersistAsync(CancellationToken.None);
    }

    public async Task ShutdownAsync()
    {
        _isShuttingDown = true;
        foreach (CancellationTokenSource pending in _pendingConnections.Values)
            pending.Cancel();
        _connectionCancellation?.Cancel();
        await PersistWorkspaceAsync(CancellationToken.None);
        try
        {
            await _sessionManager.CloseAllAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogSessionShutdownFailed(_logger, exception);
        }
        finally
        {
            foreach (TerminalTabViewModel terminal in TerminalTabs)
            {
                terminal.Dispose();
            }

            TerminalTabs.Clear();
            WorkspaceTabs.Clear();
            SelectedWorkspace = null;
            SelectedTerminal = null;
            NotifyTerminalCollectionChanged();
        }
    }

    public void Dispose()
    {
        _isShuttingDown = true;
        foreach (CancellationTokenSource pending in _pendingConnections.Values)
            pending.Cancel();
        _connectionCancellation?.Cancel();
        _connectionCancellation?.Dispose();
        _connectionCancellation = null;

        foreach (TerminalTabViewModel terminal in TerminalTabs)
        {
            terminal.Dispose();
        }
    }

    public async Task OpenSshAsync(
        TerminalSessionRequest request,
        bool backspaceSendsControlH = false,
        bool backspaceSendsDelete = false,
        TerminalPaneViewModel? targetPane = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ConnectionType != ConnectionType.Ssh || request.SshOptions is null)
            throw new ArgumentException("An SSH terminal request is required.", nameof(request));

        SshConnectionOptions options = request.SshOptions;
        string target = $"{options.Host}:{options.Port}";
        WorkspaceLaunchReference launch = new()
        {
            Kind = WorkspaceLaunchKind.TransientConnection,
            DisplayName = request.DisplayName,
            ConnectionType = request.ConnectionType,
        };
        TerminalPaneViewModel pane = StartConnectionPane(
            launch, $"Verifying SSH host key for {target}...", targetPane);
        CancellationTokenSource cancellation = _pendingConnections[pane];
        bool attached = false;
        try
        {
            ITerminalSession session = await _sessionManager.OpenAsync(request, cancellation.Token);
            if (CanAttachConnection(pane, cancellation))
            {
                AddTerminal(session, launch, pane, backspaceSendsControlH, backspaceSendsDelete,
                    activate: false);
                attached = true;
                StatusMessage = $"Connected securely to {target}";
            }
            else
            {
                await _sessionManager.CloseAsync(session.Id, CancellationToken.None);
            }
        }
        catch (TerminalConnectionException exception)
        {
            string? message = exception.FailureKind == TerminalConnectionFailureKind.Cancelled
                ? null : exception.UserMessage;
            FinishConnectionError(pane, cancellation, message,
                exception.TechnicalDetails, "SSH connection cancelled");
            LogSshOpenFailed(_logger, request.SessionId, options.Host, options.Port,
                exception.FailureKind, exception.GetType().Name);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            FinishConnectionError(pane, cancellation, null, null, "SSH connection cancelled");
        }
        catch (Exception exception)
        {
            FinishConnectionError(pane, cancellation,
                $"SSH connection to {target} failed.", exception.GetType().Name,
                "SSH connection cancelled");
            LogSshOpenFailed(_logger, request.SessionId, options.Host, options.Port,
                TerminalConnectionFailureKind.Unknown, exception.GetType().Name);
        }
        finally
        {
            if (!attached)
                options.Dispose();
            EndPendingConnection(pane, cancellation);
        }
    }

    public async Task OpenRemoteAsync(
        TerminalSessionRequest request,
        bool backspaceSendsControlH = false,
        bool backspaceSendsDelete = false,
        TerminalPaneViewModel? targetPane = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ConnectionType == ConnectionType.Ssh)
        {
            await OpenSshAsync(request, backspaceSendsControlH, backspaceSendsDelete, targetPane);
            return;
        }

        if (request.ConnectionType == ConnectionType.LocalShell)
            throw new ArgumentException("A remote terminal request is required.", nameof(request));

        string target = GetSafeRemoteTarget(request);
        WorkspaceLaunchReference launch = new()
        {
            Kind = WorkspaceLaunchKind.TransientConnection,
            DisplayName = request.DisplayName,
            ConnectionType = request.ConnectionType,
        };
        TerminalPaneViewModel pane = StartConnectionPane(
            launch, $"Connecting to {target}...", targetPane);
        CancellationTokenSource cancellation = _pendingConnections[pane];
        try
        {
            ITerminalSession session = await _sessionManager.OpenAsync(request, cancellation.Token);
            if (CanAttachConnection(pane, cancellation))
            {
                AddTerminal(session, launch, pane, backspaceSendsControlH, backspaceSendsDelete,
                    activate: false);
                StatusMessage = $"Connected to {target}";
            }
            else
            {
                await _sessionManager.CloseAsync(session.Id, CancellationToken.None);
            }
        }
        catch (TerminalConnectionException exception)
        {
            FinishConnectionError(pane, cancellation,
                exception.FailureKind == TerminalConnectionFailureKind.Cancelled
                    ? null : exception.UserMessage,
                exception.TechnicalDetails,
                $"{request.ConnectionType} connection cancelled");
            LogRemoteOpenFailed(_logger, request.SessionId, request.ConnectionType, target,
                exception.FailureKind, exception.GetType().Name);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            FinishConnectionError(pane, cancellation, null, null,
                $"{request.ConnectionType} connection cancelled");
        }
        catch (Exception exception)
        {
            FinishConnectionError(pane, cancellation,
                $"{request.ConnectionType} connection to {target} failed.",
                exception.GetType().Name, $"{request.ConnectionType} connection cancelled");
            LogRemoteOpenFailed(_logger, request.SessionId, request.ConnectionType, target,
                TerminalConnectionFailureKind.Unknown, exception.GetType().Name);
        }
        finally
        {
            EndPendingConnection(pane, cancellation);
        }
    }

    public Task OpenSelectedSavedDeviceAsync()
    {
        DeviceProfile? device = DeviceExplorer.SelectedDevice;
        return device?.WebAccess?.DefaultAction == DeviceOpenAction.Web
            ? OpenSelectedWebInterfaceAsync()
            : OpenSelectedSavedTerminalAsync();
    }

    public Task OpenSelectedSavedTerminalAsync()
    {
        return DeviceExplorer.SelectedDevice is { AccessMode: not DeviceAccessMode.WebOnly } device
            ? OpenSavedDeviceAsync(device.Id, device.Name, connectionType: device.ConnectionType,
                environment: device.Environment)
            : Task.CompletedTask;
    }

    public Task OpenSelectedWebInterfaceAsync()
    {
        if (DeviceExplorer.SelectedDevice is not { WebAccess: { } webAccess } device)
            return Task.CompletedTask;

        try
        {
            if (_deviceWebAccessService is null)
                throw new InvalidOperationException("Web browser integration is unavailable.");
            _deviceWebAccessService.Open(webAccess);
            ClearConnectionError();
            StatusMessage = $"Opened {device.Name} in a web browser";
            CloseDeviceExplorerUnlessPinned();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or System.IO.FileNotFoundException)
        {
            ConnectionErrorMessage = $"Could not open the web interface for {device.Name}. Check the browser setting.";
            ConnectionErrorTechnicalDetails = exception.GetType().Name;
            StatusMessage = ConnectionErrorMessage;
        }

        return Task.CompletedTask;
    }

    private async Task OpenSavedDeviceAsync(
        Guid deviceId, string displayName, TerminalPaneViewModel? targetPane = null,
        ConnectionType connectionType = ConnectionType.Ssh,
        DeviceEnvironment environment = DeviceEnvironment.Unknown)
    {
        WorkspaceLaunchReference launch = new()
        {
            Kind = WorkspaceLaunchKind.SavedDevice,
            DisplayName = displayName,
            DeviceId = deviceId,
            ConnectionType = connectionType,
            Environment = environment,
        };
        TerminalPaneViewModel pane = StartConnectionPane(
            launch, $"Connecting to saved device {displayName}...", targetPane);
        CancellationTokenSource cancellation = _pendingConnections[pane];
        try
        {
            SavedDeviceSessionResult result = await _savedDeviceSessionService
                .OpenAsync(deviceId, cancellation.Token);
            WorkspaceLaunchReference connectedLaunch = launch with
            {
                DisplayName = result.Device.Name,
                ConnectionType = result.Device.ConnectionType,
                Environment = result.Device.Environment,
            };
            if (CanAttachConnection(pane, cancellation))
            {
                AddTerminal(result.Session, connectedLaunch, pane,
                    result.Device.Connection.BackspaceSendsControlH,
                    result.Device.Connection.BackspaceSendsDelete, activate: false);
                StatusMessage = result.Device.ConnectionType == ConnectionType.Ssh
                    ? $"Connected securely to {result.Device.Name}"
                    : $"Connected to {result.Device.Name} via {result.Device.ConnectionType}";
                await DeviceExplorer.RefreshAsync();
                CloseDeviceExplorerUnlessPinned();
            }
            else
            {
                await _sessionManager.CloseAsync(result.Session.Id, CancellationToken.None);
            }
        }
        catch (SavedDeviceConnectionException exception)
        {
            FinishConnectionError(pane, cancellation, exception.Message,
                exception.GetType().Name, "Saved-device connection cancelled");
        }
        catch (TerminalConnectionException exception)
        {
            FinishConnectionError(pane, cancellation,
                exception.FailureKind == TerminalConnectionFailureKind.Cancelled
                    ? null : exception.UserMessage,
                exception.TechnicalDetails, "Saved-device connection cancelled");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            FinishConnectionError(pane, cancellation, null, null,
                "Saved-device connection cancelled");
        }
        catch (Exception exception)
        {
            FinishConnectionError(pane, cancellation,
                $"Connection to saved device {displayName} failed.",
                exception.GetType().Name, "Saved-device connection cancelled");
            LogSavedDeviceOpenFailed(_logger, deviceId, exception.GetType().Name);
        }
        finally
        {
            EndPendingConnection(pane, cancellation);
        }
    }

    private TerminalPaneViewModel StartConnectionPane(
        WorkspaceLaunchReference launch, string status, TerminalPaneViewModel? targetPane)
    {
        if (targetPane is not null && _pendingConnections.ContainsKey(targetPane))
            throw new InvalidOperationException("This pane is already connecting.");

        ClearConnectionError();
        IsSettingsVisible = false;
        TerminalPaneViewModel pane = targetPane ?? new TerminalPaneViewModel(Guid.NewGuid());
        if (targetPane is null)
        {
            var workspace = new WorkspaceTabViewModel(Guid.NewGuid(), pane, pane);
            WorkspaceTabs.Add(workspace);
            SelectedWorkspace = workspace;
            NotifyTerminalCollectionChanged();
        }
        else
        {
            ActivatePane(pane);
        }

        pane.BeginConnection(launch, status);
        FindWorkspace(pane).NotifyActiveContentChanged();
        _pendingConnections.Add(pane, new CancellationTokenSource());
        IsConnectionBusy = true;
        StatusMessage = status;
        return pane;
    }

    private bool CanAttachConnection(TerminalPaneViewModel pane, CancellationTokenSource cancellation)
    {
        return !_isShuttingDown && !cancellation.IsCancellationRequested
            && _pendingConnections.TryGetValue(pane, out CancellationTokenSource? current)
            && ReferenceEquals(current, cancellation)
            && WorkspaceTabs.Any(tab => tab.Panes.Contains(pane));
    }

    private void FinishConnectionError(
        TerminalPaneViewModel pane, CancellationTokenSource cancellation,
        string? message, string? details, string cancellationMessage)
    {
        if (_isShuttingDown
            || !_pendingConnections.TryGetValue(pane, out CancellationTokenSource? current)
            || !ReferenceEquals(current, cancellation)
            || !WorkspaceTabs.Any(tab => tab.Panes.Contains(pane)))
            return;
        bool cancelled = cancellation.IsCancellationRequested;
        string display = cancelled ? cancellationMessage : message ?? cancellationMessage;
        pane.EndConnection(display);
        ConnectionErrorMessage = cancelled ? null : message;
        ConnectionErrorTechnicalDetails = cancelled ? null : details;
        StatusMessage = display;
    }

    private void EndPendingConnection(TerminalPaneViewModel pane, CancellationTokenSource cancellation)
    {
        if (_pendingConnections.TryGetValue(pane, out CancellationTokenSource? current)
            && ReferenceEquals(current, cancellation))
            _pendingConnections.Remove(pane);
        cancellation.Dispose();
        IsConnectionBusy = _pendingConnections.Count > 0;
    }

    public void CancelPaneConnection(TerminalPaneViewModel pane)
    {
        if (_pendingConnections.TryGetValue(pane, out CancellationTokenSource? cancellation))
            cancellation.Cancel();
    }

    public Task RetrySavedPaneAsync(TerminalPaneViewModel pane)
    {
        return pane.Launch is { Kind: WorkspaceLaunchKind.SavedDevice, DeviceId: { } id } launch
            ? OpenSavedDeviceAsync(id, launch.DisplayName, pane, launch.ConnectionType,
                launch.Environment)
            : Task.CompletedTask;
    }

    private async Task ToggleSidebarAsync()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        await Task.CompletedTask;
    }

    private void ToggleDeviceExplorerPin()
    {
        IsDeviceExplorerPinned = !IsDeviceExplorerPinned;
        CloseDeviceExplorerUnlessPinned();
    }

    private void ShowSettings()
    {
        CloseDeviceExplorer();
        IsSettingsVisible = true;
    }

    private async Task OpenLocalTerminalAsync(LocalShellKind shellKind)
    {
        IsSettingsVisible = false;
        string displayName = CreateDisplayName(shellKind);
        StatusMessage = $"Starting {displayName}…";

        try
        {
            ITerminalSession session = await _sessionManager.OpenAsync(
                new TerminalSessionRequest(Guid.NewGuid(), displayName, shellKind),
                CancellationToken.None);
            AddTerminal(session, new WorkspaceLaunchReference
            {
                Kind = WorkspaceLaunchKind.LocalShell,
                DisplayName = displayName,
                ConnectionType = ConnectionType.LocalShell,
                ShellKind = shellKind,
            });
            StatusMessage = $"{displayName} is running locally";
            CloseDeviceExplorerUnlessPinned();
        }
        catch (Exception exception)
        {
            LogLocalShellOpenFailed(_logger, exception, shellKind);
            StatusMessage = $"{displayName} could not be started; see application log";
        }
    }

    private void AddTerminal(
        ITerminalSession session,
        WorkspaceLaunchReference launch,
        TerminalPaneViewModel? targetPane = null,
        bool backspaceSendsControlH = false,
        bool backspaceSendsDelete = false,
        bool activate = true)
    {
        var terminal = new TerminalTabViewModel(
            session,
            CloseTerminalAsync,
            launch.Environment,
            _sessionLogService,
            backspaceSendsControlH,
            backspaceSendsDelete);
        terminal.SetFontSize(TerminalFontSize);
        TerminalTabs.Add(terminal);
        targetPane ??= SelectedWorkspace?.ActivePane is { IsEmpty: true } emptyPane
            ? emptyPane
            : null;
        if (targetPane is null)
        {
            targetPane = new TerminalPaneViewModel(Guid.NewGuid());
            var workspace = new WorkspaceTabViewModel(Guid.NewGuid(), targetPane, targetPane);
            WorkspaceTabs.Add(workspace);
            SelectedWorkspace = workspace;
        }

        targetPane.Attach(terminal, launch);
        WorkspaceTabViewModel attachedWorkspace = FindWorkspace(targetPane);
        if (activate)
        {
            SelectedWorkspace = attachedWorkspace;
            attachedWorkspace.Activate(targetPane);
        }
        attachedWorkspace.NotifyActiveContentChanged();
        if (ReferenceEquals(SelectedWorkspace?.ActivePane, targetPane))
            SelectedTerminal = terminal;
        NotifyTerminalCollectionChanged();
        _ = PersistWorkspaceAsync(CancellationToken.None);
    }

    private void CancelPendingConnection()
    {
        if (SelectedWorkspace?.ActivePane is { IsConnecting: true } pane)
            CancelPaneConnection(pane);
        else
            _connectionCancellation?.Cancel();
    }

    private void ClearConnectionError()
    {
        ConnectionErrorMessage = null;
        ConnectionErrorTechnicalDetails = null;
    }

    private async Task CloseTerminalAsync(TerminalTabViewModel terminal)
    {
        TerminalPaneViewModel? pane = WorkspaceTabs
            .SelectMany(workspace => workspace.Panes)
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Terminal, terminal));
        if (pane is null)
        {
            return;
        }

        await ClosePaneAsync(pane);
    }

    public void ActivatePane(TerminalPaneViewModel pane)
    {
        WorkspaceTabViewModel workspace = FindWorkspace(pane);
        workspace.Activate(pane);
        SelectedWorkspace = workspace;
        SelectedTerminal = pane.Terminal;
    }

    public async Task SplitPaneAsync(
        TerminalPaneViewModel pane,
        PaneSplitDirection direction)
    {
        WorkspaceTabViewModel workspace = FindWorkspace(pane);
        SelectedWorkspace = workspace;
        workspace.Split(pane, direction);
        SelectedTerminal = null;
        StatusMessage = direction == PaneSplitDirection.Right
            ? "Pane split right - choose a connection for the active pane"
            : "Pane split down - choose a connection for the active pane";
        NotifyTerminalCollectionChanged();
        await PersistWorkspaceAsync(CancellationToken.None);
    }

    public async Task ClosePaneAsync(TerminalPaneViewModel pane)
    {
        WorkspaceTabViewModel workspace = FindWorkspace(pane);
        CancelPaneConnection(pane);
        TerminalTabViewModel? terminal = pane.Terminal;
        if (terminal is not null)
        {
            StatusMessage = $"Closing {terminal.Title}...";
            try
            {
                await _sessionManager.CloseAsync(terminal.Id, CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogLocalShellCloseFailed(_logger, exception, terminal.Id);
                StatusMessage = $"{terminal.Title} did not close cleanly; see application log";
                return;
            }

            pane.Detach();
            terminal.Dispose();
            TerminalTabs.Remove(terminal);
        }

        if (!workspace.Remove(pane))
        {
            int workspaceIndex = WorkspaceTabs.IndexOf(workspace);
            WorkspaceTabs.Remove(workspace);
            SelectedWorkspace = WorkspaceTabs.Count == 0
                ? null
                : WorkspaceTabs[Math.Min(workspaceIndex, WorkspaceTabs.Count - 1)];
        }
        else
        {
            SelectedWorkspace = workspace;
        }

        SelectedTerminal = SelectedWorkspace?.ActivePane.Terminal;
        NotifyTerminalCollectionChanged();
        StatusMessage = WorkspaceTabs.Count == 0
            ? "Ready - No active sessions"
            : $"{TerminalTabs.Count} terminal session(s) open";
        await PersistWorkspaceAsync(CancellationToken.None);
    }

    public async Task RestorePaneAsync(TerminalPaneViewModel pane)
    {
        if (pane.Terminal is not null || pane.Launch is not { CanReconnect: true } launch)
        {
            return;
        }

        ActivatePane(pane);
        if (launch.Kind == WorkspaceLaunchKind.LocalShell && launch.ShellKind is { } shellKind)
        {
            try
            {
                ITerminalSession session = await _sessionManager.OpenAsync(
                    new TerminalSessionRequest(Guid.NewGuid(), launch.DisplayName, shellKind),
                    CancellationToken.None);
                AddTerminal(session, launch, pane, activate: false);
                StatusMessage = $"{launch.DisplayName} restored";
            }
            catch (Exception exception)
            {
                LogLocalShellOpenFailed(_logger, exception, shellKind);
                StatusMessage = $"{launch.DisplayName} could not be restored";
            }
        }
        else if (launch.Kind == WorkspaceLaunchKind.SavedDevice && launch.DeviceId is { } deviceId)
        {
            await OpenSavedDeviceAsync(deviceId, launch.DisplayName, pane,
                launch.ConnectionType, launch.Environment);
        }
    }

    public void ToggleActivePaneInputLock()
    {
        if (SelectedWorkspace?.ActivePane.Terminal is not { } terminal)
        {
            return;
        }

        terminal.ToggleInputLock();
        StatusMessage = terminal.IsInputLocked
            ? $"Input locked for {terminal.Title}"
            : $"Input unlocked for {terminal.Title}";
    }

    public void FocusNextPane(bool reverse)
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        SelectedWorkspace.FocusNext(reverse);
        SelectedTerminal = SelectedWorkspace.ActivePane.Terminal;
    }

    public async Task UpdateSplitRatioAsync(SplitPaneViewModel split, double ratio)
    {
        split.Ratio = ratio;
        await PersistWorkspaceAsync(CancellationToken.None);
    }

    private WorkspaceTabViewModel FindWorkspace(TerminalPaneViewModel pane)
    {
        return WorkspaceTabs.FirstOrDefault(workspace => workspace.Panes.Contains(pane))
            ?? throw new InvalidOperationException("The pane is not part of the active workspace.");
    }

    private async Task RestoreWorkspaceAsync(CancellationToken cancellationToken)
    {
        try
        {
            WorkspaceDocument? document = await _workspaceService.LoadAsync(cancellationToken);
            if (document is null)
            {
                return;
            }

            foreach (WorkspaceTabDocument tabDocument in document.Tabs)
            {
                WorkspaceTabs.Add(WorkspaceTabViewModel.FromDocument(tabDocument));
            }

            SelectedWorkspace = document.ActiveTabId is { } activeId
                ? WorkspaceTabs.FirstOrDefault(tab => tab.Id == activeId)
                : WorkspaceTabs.FirstOrDefault();
            SelectedWorkspace ??= WorkspaceTabs.FirstOrDefault();
            NotifyTerminalCollectionChanged();
            if (WorkspaceTabs.Count > 0)
            {
                StatusMessage = "Workspace restored disconnected - reconnect intentionally";
            }
        }
        catch (Exception exception)
        {
            LogWorkspaceLoadFailed(_logger, exception.GetType().Name);
            StatusMessage = "Workspace layout could not be restored";
        }
    }

    private async Task PersistWorkspaceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _workspaceService.SaveAsync(
                new WorkspaceDocument
                {
                    ActiveTabId = SelectedWorkspace?.Id,
                    Tabs = WorkspaceTabs.Select(tab => tab.ToDocument()).ToArray(),
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            LogWorkspaceSaveFailed(_logger, exception.GetType().Name);
            StatusMessage = "Workspace layout could not be saved";
        }
    }

    private string CreateDisplayName(LocalShellKind shellKind)
    {
        int sequence = shellKind switch
        {
            LocalShellKind.PowerShell => ++_powerShellSequence,
            LocalShellKind.CommandPrompt => ++_commandPromptSequence,
            _ => throw new ArgumentOutOfRangeException(nameof(shellKind), shellKind, null),
        };
        string baseName = shellKind == LocalShellKind.PowerShell ? "PowerShell" : "Command Prompt";
        return sequence == 1 ? baseName : $"{baseName} {sequence}";
    }

    private static string GetSafeRemoteTarget(TerminalSessionRequest request)
    {
        return request.ConnectionType switch
        {
            ConnectionType.Telnet when request.TelnetOptions is { } options =>
                $"{options.Host}:{options.Port} (Telnet)",
            ConnectionType.Serial when request.SerialOptions is { } options =>
                $"{options.PortName} (Serial)",
            ConnectionType.RawTcp when request.RawTcpOptions is { } options =>
                $"{options.Host}:{options.Port} (Raw TCP)",
            _ => request.ConnectionType.ToString(),
        };
    }

    private void NotifyTerminalCollectionChanged()
    {
        OnPropertyChanged(nameof(HasTerminalSessions));
        OnPropertyChanged(nameof(HasNoTerminalSessions));
        NotifyFooterChanged();
    }

    private void SelectedWorkspace_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceTabViewModel.ActivePane))
            SelectedTerminal = SelectedWorkspace?.ActivePane.Terminal;
        if (e.PropertyName is nameof(WorkspaceTabViewModel.ActivePane)
            or nameof(WorkspaceTabViewModel.StatusIsConnected)
            or nameof(WorkspaceTabViewModel.StatusIsConnecting)
            or nameof(WorkspaceTabViewModel.StatusIsDisconnected)
            or nameof(WorkspaceTabViewModel.StatusIsIdle))
            NotifyFooterChanged();
    }

    private void SelectedTerminal_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TerminalTabViewModel.State)
            or nameof(TerminalTabViewModel.IsLogging))
        {
            NotifyFooterChanged();
        }
    }

    private void NotifyFooterChanged()
    {
        OnPropertyChanged(nameof(HasSelectedTerminal));
        OnPropertyChanged(nameof(IsFooterConnected));
        OnPropertyChanged(nameof(IsFooterWarning));
        OnPropertyChanged(nameof(IsFooterError));
        OnPropertyChanged(nameof(IsFooterIdle));
        OnPropertyChanged(nameof(ActiveTabStatusText));
        OnPropertyChanged(nameof(FooterSessionCountText));
        OnPropertyChanged(nameof(FooterConnectionText));
        OnPropertyChanged(nameof(FooterLoggingText));
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _settingsService.SaveAsync(
                new ApplicationSettings
                {
                    IsSidebarCollapsed = true,
                    SidebarWidth = SidebarWidth,
                    TerminalFontSize = TerminalFontSize,
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            LogSettingsSaveFailed(_logger, exception);
            StatusMessage = "UI settings could not be saved";
        }
    }

    [LoggerMessage(
        EventId = 101,
        Level = LogLevel.Information,
        Message = "Loaded UI settings with sidebar collapsed {SidebarCollapsed}, width {SidebarWidth}")]
    private static partial void LogSettingsLoaded(
        ILogger logger,
        bool sidebarCollapsed,
        double sidebarWidth);

    [LoggerMessage(
        EventId = 102,
        Level = LogLevel.Error,
        Message = "Unable to load application UI settings")]
    private static partial void LogSettingsLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 103,
        Level = LogLevel.Error,
        Message = "Unable to save application UI settings")]
    private static partial void LogSettingsSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 104,
        Level = LogLevel.Error,
        Message = "Unable to open local shell {ShellKind}")]
    private static partial void LogLocalShellOpenFailed(
        ILogger logger,
        Exception exception,
        LocalShellKind shellKind);

    [LoggerMessage(
        EventId = 105,
        Level = LogLevel.Error,
        Message = "Unable to close local terminal session {SessionId}")]
    private static partial void LogLocalShellCloseFailed(
        ILogger logger,
        Exception exception,
        Guid sessionId);

    [LoggerMessage(
        EventId = 106,
        Level = LogLevel.Error,
        Message = "Unable to close all terminal sessions during shutdown")]
    private static partial void LogSessionShutdownFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 107,
        Level = LogLevel.Warning,
        Message = "SSH open failed for session {SessionId} to {Host}:{Port}; kind {FailureKind}; exception {ExceptionType}")]
    private static partial void LogSshOpenFailed(
        ILogger logger,
        Guid sessionId,
        string host,
        int port,
        TerminalConnectionFailureKind failureKind,
        string exceptionType);

    [LoggerMessage(
        EventId = 108,
        Level = LogLevel.Warning,
        Message = "Saved-device connection failed for device {DeviceId}; exception {ExceptionType}")]
    private static partial void LogSavedDeviceOpenFailed(
        ILogger logger,
        Guid deviceId,
        string exceptionType);

    [LoggerMessage(
        EventId = 109,
        Level = LogLevel.Warning,
        Message = "Remote open failed for session {SessionId}; type {ConnectionType}; target {Target}; kind {FailureKind}; exception {ExceptionType}")]
    private static partial void LogRemoteOpenFailed(
        ILogger logger,
        Guid sessionId,
        ConnectionType connectionType,
        string target,
        TerminalConnectionFailureKind failureKind,
        string exceptionType);

    [LoggerMessage(
        EventId = 110,
        Level = LogLevel.Warning,
        Message = "Workspace restore failed; exception {ExceptionType}")]
    private static partial void LogWorkspaceLoadFailed(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 111,
        Level = LogLevel.Warning,
        Message = "Workspace save failed; exception {ExceptionType}")]
    private static partial void LogWorkspaceSaveFailed(ILogger logger, string exceptionType);
}
