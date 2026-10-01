using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NetOpsTerminal.Application.Workspaces;
using Avalonia.Controls;
using NetOpsTerminal.Core.Sessions;

namespace NetOpsTerminal.App.ViewModels;

public abstract class WorkspacePaneNodeViewModel(Guid id) : ObservableObject
{
    public Guid Id { get; } = id == Guid.Empty
        ? throw new ArgumentException("A pane ID is required.", nameof(id))
        : id;

    public SplitPaneViewModel? Parent { get; internal set; }

    public abstract IEnumerable<TerminalPaneViewModel> EnumerateLeaves();

    public abstract PaneNodeDocument ToDocument();
}

public sealed partial class TerminalPaneViewModel : WorkspacePaneNodeViewModel
{
    private TerminalTabViewModel? _terminal;
    private WorkspaceLaunchReference? _launch;
    private bool _isActive;
    private bool _isConnecting;
    private string? _connectionStatus;
    private string? _connectionError;

    public TerminalPaneViewModel(
        Guid id,
        WorkspaceLaunchReference? launch = null,
        TerminalTabViewModel? terminal = null)
        : base(id)
    {
        _launch = launch;
        _terminal = terminal;
    }

    public TerminalTabViewModel? Terminal
    {
        get => _terminal;
        private set
        {
            if (ReferenceEquals(_terminal, value))
                return;
            if (_terminal is not null)
                _terminal.PropertyChanged -= Terminal_OnPropertyChanged;
            if (SetProperty(ref _terminal, value))
            {
                if (_terminal is not null)
                    _terminal.PropertyChanged += Terminal_OnPropertyChanged;
                NotifyContentChanged();
            }
        }
    }

    public WorkspaceLaunchReference? Launch
    {
        get => _launch;
        private set
        {
            if (SetProperty(ref _launch, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }

    public bool HasTerminal => Terminal is not null;

    public bool HasNoTerminal => Terminal is null;
    public bool ShowLogging => Terminal?.IsLogging == true;

    public bool ShowInputLocked => Terminal?.IsInputLocked == true;

    public bool ShowFind => Terminal?.IsFindVisible == true;

    public bool ShowProductionBanner => HasTerminal && IsProduction;

    public bool StatusIsConnected => Terminal?.State == SessionState.Connected;

    public bool StatusIsConnecting => IsConnecting || Terminal?.State is
        SessionState.Connecting or SessionState.Reconnecting or SessionState.Disconnecting;

    public bool StatusIsDisconnected => HasConnectionError || Terminal?.State is
        SessionState.Disconnected or SessionState.Faulted or SessionState.Disposed;

    public bool StatusIsIdle => Terminal is not null
        && !StatusIsConnected && !StatusIsConnecting && !StatusIsDisconnected;

    private void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(StatusIsConnected));
        OnPropertyChanged(nameof(StatusIsConnecting));
        OnPropertyChanged(nameof(StatusIsDisconnected));
        OnPropertyChanged(nameof(StatusIsIdle));
    }


    private void Terminal_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalTabViewModel.IsLogging))
            OnPropertyChanged(nameof(ShowLogging));
        else if (e.PropertyName == nameof(TerminalTabViewModel.IsInputLocked))
            OnPropertyChanged(nameof(ShowInputLocked));
        else if (e.PropertyName == nameof(TerminalTabViewModel.IsFindVisible))
            OnPropertyChanged(nameof(ShowFind));
        else if (e.PropertyName == nameof(TerminalTabViewModel.State))
            NotifyStatusChanged();
    }


    public bool IsConnecting
    {
        get => _isConnecting;
        private set
        {
            if (SetProperty(ref _isConnecting, value))
                NotifyContentChanged();
        }
    }

    public string? ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string? ConnectionError
    {
        get => _connectionError;
        private set
        {
            if (SetProperty(ref _connectionError, value))
                NotifyContentChanged();
        }
    }

    public bool HasConnectionError => ConnectionError is not null;

    public void BeginConnection(WorkspaceLaunchReference launch, string status)
    {
        Launch = launch;
        ConnectionError = null;
        ConnectionStatus = status;
        IsConnecting = true;
    }

    public void EndConnection(string? error = null)
    {
        IsConnecting = false;
        ConnectionStatus = null;
        ConnectionError = error;
    }

    public bool IsRestoredPlaceholder => Terminal is null && Launch is not null && !IsConnecting && !HasConnectionError;

    public bool IsEmpty => Terminal is null && Launch is null && !IsConnecting;

    public bool CanReconnect => Terminal is null && !IsConnecting && !HasConnectionError && Launch?.CanReconnect == true;

    public bool IsProduction => Launch?.Environment == Core.Devices.DeviceEnvironment.Production;

    public string Title => Terminal?.Title ?? Launch?.DisplayName ?? "Empty pane";

    public string PlaceholderMessage => Launch switch
    {
        { Kind: WorkspaceLaunchKind.TransientConnection } =>
            "Transient Quick Connect details and credentials are not persisted. Open a new Quick Connect session.",
        { Environment: Core.Devices.DeviceEnvironment.Production } =>
            "Production session restored disconnected. Reconnect intentionally when authorized.",
        not null => "Session restored disconnected. Select Reconnect when ready.",
        _ => "This pane is ready. Start PowerShell, CMD, Quick Connect, or a saved device.",
    };

    public void Attach(TerminalTabViewModel terminal, WorkspaceLaunchReference launch)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(launch);
        Launch = launch;
        Terminal = terminal;
        EndConnection();
    }

    public TerminalTabViewModel? Detach()
    {
        TerminalTabViewModel? terminal = Terminal;
        Terminal = null;
        return terminal;
    }

    public override IEnumerable<TerminalPaneViewModel> EnumerateLeaves()
    {
        yield return this;
    }

    public override PaneNodeDocument ToDocument()
    {
        return new PaneNodeDocument
        {
            NodeId = Id,
            Launch = Launch,
        };
    }

    private void NotifyContentChanged()
    {
        OnPropertyChanged(nameof(HasConnectionError));
        OnPropertyChanged(nameof(HasTerminal));
        OnPropertyChanged(nameof(ShowLogging));
        OnPropertyChanged(nameof(ShowInputLocked));
        OnPropertyChanged(nameof(ShowFind));
        OnPropertyChanged(nameof(ShowProductionBanner));
        NotifyStatusChanged();
        OnPropertyChanged(nameof(HasNoTerminal));
        OnPropertyChanged(nameof(IsRestoredPlaceholder));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanReconnect));
        OnPropertyChanged(nameof(IsProduction));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PlaceholderMessage));
    }
}

public sealed class SplitPaneViewModel : WorkspacePaneNodeViewModel
{
    private double _ratio;

    public SplitPaneViewModel(
        Guid id,
        PaneSplitDirection direction,
        double ratio,
        WorkspacePaneNodeViewModel first,
        WorkspacePaneNodeViewModel second)
        : base(id)
    {
        Direction = direction;
        First = first;
        Second = second;
        First.Parent = this;
        Second.Parent = this;
        Ratio = ratio;
    }

    public PaneSplitDirection Direction { get; }

    public bool IsSideBySide => Direction == PaneSplitDirection.Right;

    public bool IsStacked => Direction == PaneSplitDirection.Down;

    public WorkspacePaneNodeViewModel First { get; private set; }

    public WorkspacePaneNodeViewModel Second { get; private set; }

    public double Ratio
    {
        get => _ratio;
        set
        {
            if (SetProperty(ref _ratio, Math.Clamp(value, 0.15, 0.85)))
            {
                OnPropertyChanged(nameof(FirstLength));
                OnPropertyChanged(nameof(SecondLength));
            }
        }
    }

    public GridLength FirstLength => new(Ratio, GridUnitType.Star);

    public GridLength SecondLength => new(1 - Ratio, GridUnitType.Star);

    internal void ReplaceChild(
        WorkspacePaneNodeViewModel existing,
        WorkspacePaneNodeViewModel replacement)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(replacement);
        if (ReferenceEquals(First, existing))
        {
            First.Parent = null;
            First = replacement;
            replacement.Parent = this;
            OnPropertyChanged(nameof(First));
        }
        else if (ReferenceEquals(Second, existing))
        {
            Second.Parent = null;
            Second = replacement;
            replacement.Parent = this;
            OnPropertyChanged(nameof(Second));
        }
        else
        {
            throw new InvalidOperationException("The pane is not a child of this split.");
        }
    }

    public override IEnumerable<TerminalPaneViewModel> EnumerateLeaves()
    {
        return First.EnumerateLeaves().Concat(Second.EnumerateLeaves());
    }

    public override PaneNodeDocument ToDocument()
    {
        return new PaneNodeDocument
        {
            NodeId = Id,
            IsSplit = true,
            SplitDirection = Direction,
            SplitRatio = Ratio,
            First = First.ToDocument(),
            Second = Second.ToDocument(),
        };
    }
}
