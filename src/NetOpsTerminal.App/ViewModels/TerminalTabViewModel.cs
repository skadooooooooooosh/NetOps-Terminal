using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetOpsTerminal.Core.Sessions;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Terminal.Abstractions;
using NetOpsTerminal.Application.Sessions;

namespace NetOpsTerminal.App.ViewModels;

public sealed partial class TerminalTabViewModel : ObservableObject, IDisposable
{
    private readonly Func<TerminalTabViewModel, Task> _closeAsync;
    private readonly ISessionLogService? _sessionLogService;
    private SessionState _state;
    private string? _errorMessage;
    private bool _isFindVisible;
    private string _findText = string.Empty;
    private TerminalSearchResult _searchResult;
    private bool _isLogging;
    private string? _logFilePath;
    private bool _disposed;

    public TerminalTabViewModel(
        ITerminalSession session,
        Func<TerminalTabViewModel, Task> closeAsync,
        DeviceEnvironment environment = DeviceEnvironment.Unknown,
        ISessionLogService? sessionLogService = null)
    {
        Session = session;
        _closeAsync = closeAsync;
        _sessionLogService = sessionLogService;
        _state = session.State;
        Environment = environment;
        CloseCommand = new AsyncRelayCommand(() => _closeAsync(this));
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, CanDisconnect);
        ReconnectCommand = new AsyncRelayCommand(ReconnectAsync, CanReconnect);
        ToggleLoggingCommand = new AsyncRelayCommand(ToggleLoggingAsync);
        session.StateChanged += OnStateChanged;
        session.ErrorOccurred += OnErrorOccurred;
    }

    public Guid Id => Session.Id;

    public string Title => Session.DisplayName;

    public string ShellLabel => Session.ConnectionLabel;

    public ITerminalSession Session { get; }

    public DeviceEnvironment Environment { get; }

    public bool IsProduction => Environment == DeviceEnvironment.Production;

    public string EnvironmentLabel => Environment.ToString().ToUpperInvariant();

    public bool IsInputLocked => Session.IsInputLocked;

    public string InputLockLabel => IsInputLocked ? "UNLOCK INPUT" : "LOCK INPUT";

    public IAsyncRelayCommand CloseCommand { get; }

    public IAsyncRelayCommand DisconnectCommand { get; }

    public IAsyncRelayCommand ReconnectCommand { get; }

    public IAsyncRelayCommand ToggleLoggingCommand { get; }

    public bool IsFindVisible
    {
        get => _isFindVisible;
        private set => SetProperty(ref _isFindVisible, value);
    }

    public string FindText
    {
        get => _findText;
        set
        {
            if (SetProperty(ref _findText, value ?? string.Empty))
            {
                _searchResult = Session.StartSearch(_findText);
                OnPropertyChanged(nameof(FindStatusText));
            }
        }
    }

    public string FindStatusText => string.IsNullOrWhiteSpace(FindText)
        ? "Type to search"
        : _searchResult.TotalMatches == 0
            ? "No matches"
            : $"{_searchResult.SelectedMatchNumber}/{_searchResult.TotalMatches}";

    public bool IsLogging
    {
        get => _isLogging;
        private set
        {
            if (SetProperty(ref _isLogging, value))
            {
                OnPropertyChanged(nameof(LoggingLabel));
                OnPropertyChanged(nameof(LoggingToolTip));
            }
        }
    }

    public string LoggingLabel => IsLogging ? "STOP LOG" : "START LOG";

    public string LoggingToolTip => IsLogging
        ? $"Stop output-only logging{(_logFilePath is null ? string.Empty : $" — {_logFilePath}")}"
        : "Start output-only session logging (keyboard input is not recorded)";

    public SessionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusGlyph));
                DisconnectCommand.NotifyCanExecuteChanged();
                ReconnectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int FontSize => Session.FontSize;

    public void SetFontSize(int fontSize)
    {
        Session.SetFontSize(fontSize);
        OnPropertyChanged(nameof(FontSize));
    }

    public void ToggleInputLock()
    {
        Session.SetInputLocked(!Session.IsInputLocked);
        OnPropertyChanged(nameof(IsInputLocked));
        OnPropertyChanged(nameof(InputLockLabel));
    }

    public void OpenFind()
    {
        IsFindVisible = true;
        _searchResult = Session.StartSearch(FindText);
        OnPropertyChanged(nameof(FindStatusText));
    }

    public void CloseFind()
    {
        Session.EndSearch();
        IsFindVisible = false;
        _searchResult = TerminalSearchResult.Empty;
        OnPropertyChanged(nameof(FindStatusText));
    }

    public void FindNext()
    {
        _searchResult = Session.SelectNextSearchMatch();
        OnPropertyChanged(nameof(FindStatusText));
    }

    public void FindPrevious()
    {
        _searchResult = Session.SelectPreviousSearchMatch();
        OnPropertyChanged(nameof(FindStatusText));
    }

    public string StatusText => _errorMessage ?? State switch
    {
        SessionState.Connecting => "Starting",
        SessionState.Connected => "Running",
        SessionState.Disconnecting => "Closing",
        SessionState.Disconnected => "Exited",
        SessionState.Faulted => "Error",
        SessionState.Disposed => "Closed",
        _ => State.ToString(),
    };

    public string StatusGlyph => State switch
    {
        SessionState.Connected => "●",
        SessionState.Connecting or SessionState.Reconnecting => "◐",
        SessionState.Faulted => "▲",
        _ => "○",
    };

    private bool CanDisconnect()
    {
        return State == SessionState.Connected;
    }

    private bool CanReconnect()
    {
        return State is SessionState.Disconnected or SessionState.Faulted;
    }

    private async Task DisconnectAsync()
    {
        try
        {
            await Session.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            OnErrorOccurred(this, exception);
        }
    }

    private async Task ReconnectAsync()
    {
        try
        {
            await Session.ReconnectAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            OnErrorOccurred(this, exception);
        }
    }

    private async Task ToggleLoggingAsync()
    {
        if (_sessionLogService is null)
        {
            _errorMessage = "Session logging is unavailable";
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        try
        {
            if (IsLogging)
            {
                await _sessionLogService.StopAsync(Id);
                _logFilePath = null;
                IsLogging = false;
            }
            else
            {
                SessionLogHandle handle = await _sessionLogService.StartAsync(Session);
                _logFilePath = handle.FilePath;
                IsLogging = true;
            }
        }
        catch (Exception)
        {
            _errorMessage = "Session log operation failed — see application log";
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private async Task StopLoggingSafelyAsync()
    {
        if (!IsLogging || _sessionLogService is null)
        {
            return;
        }

        try
        {
            await _sessionLogService.StopAsync(Id);
        }
        catch (Exception)
        {
        }
        finally
        {
            _logFilePath = null;
            RunOnUiThread(() => IsLogging = false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Session.StateChanged -= OnStateChanged;
        Session.ErrorOccurred -= OnErrorOccurred;
        Session.EndSearch();
        _ = StopLoggingSafelyAsync();
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        RunOnUiThread(() =>
        {
            _errorMessage = null;
            State = state;
            if (state is SessionState.Disconnected or SessionState.Faulted or SessionState.Disposed)
            {
                _ = StopLoggingSafelyAsync();
            }
        });
    }

    private void OnErrorOccurred(object? sender, Exception exception)
    {
        RunOnUiThread(() =>
        {
            _errorMessage = exception is TerminalConnectionException terminalException
                ? terminalException.UserMessage
                : "Session error — see application log";
            OnPropertyChanged(nameof(StatusText));
        });
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
