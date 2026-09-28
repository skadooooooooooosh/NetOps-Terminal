using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Sessions;
using NetOpsTerminal.Terminal.Abstractions;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal sealed partial class RoyalTerminalSession : ITerminalSession, IRoyalTerminalSession
{
    private const int InitialColumns = 120;
    private const int InitialRows = 36;
    private const int InitialWidthPixels = 1200;
    private const int InitialHeightPixels = 720;
    private const int DefaultScrollbackLines = 10_000;

    private readonly ShellProfile _profile;
    private readonly LocalShellKind _shellKind;
    private readonly string _workingDirectory;
    private readonly ITerminalPasteConfirmationService _pasteConfirmationService;
    private readonly ILogger<RoyalTerminalSession> _logger;
    private readonly object _stateLock = new();
    private SessionState _state = SessionState.Created;
    private bool _isInputLocked;
    private bool _disposed;

    public RoyalTerminalSession(
        Guid id,
        string displayName,
        LocalShellKind shellKind,
        ShellProfile profile,
        string workingDirectory,
        ITerminalPasteConfirmationService pasteConfirmationService,
        ILogger<RoyalTerminalSession> logger)
    {
        Id = id;
        DisplayName = displayName;
        _shellKind = shellKind;
        _profile = profile;
        _workingDirectory = workingDirectory;
        _pasteConfirmationService = pasteConfirmationService;
        _logger = logger;

        Control = TerminalControlConfiguration.Apply(new TerminalControl
        {
            FontFamilyName = "Consolas",
            TerminalFontSize = TerminalFontSizePolicy.Default,
            Columns = InitialColumns,
            Rows = InitialRows,
            ScrollbackLimit = DefaultScrollbackLines,
            VtProcessorPreference = VtProcessorPreference.Managed,
            DefaultForeground = Color.Parse("#D3DFEC"),
            DefaultBackground = Color.Parse("#080C12"),
            Background = new SolidColorBrush(Color.Parse("#080C12")),
            Padding = new Thickness(10, 8),
        }, _pasteConfirmationService);
        Control.ProcessExited += OnProcessExited;
        Control.DataReceived += OnDataReceived;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public ConnectionType ConnectionType => ConnectionType.LocalShell;

    public string ConnectionLabel => _shellKind == LocalShellKind.PowerShell
        ? "POWERSHELL · LOCAL"
        : "COMMAND PROMPT · LOCAL";

    public LocalShellKind? ShellKind => _shellKind;

    public SessionState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public int FontSize => checked((int)Math.Round(Control.TerminalFontSize));

    public bool IsInputLocked => _isInputLocked;

    public TerminalControl Control { get; }

    public event EventHandler<SessionState>? StateChanged;

    public event EventHandler<Exception>? ErrorOccurred;

    public event EventHandler<TerminalOutputReceivedEventArgs>? OutputReceived;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        SessionState current = State;
        if (current == SessionState.Connected)
        {
            return;
        }

        if (current is not (SessionState.Created or SessionState.Disconnected or SessionState.Faulted))
        {
            throw new InvalidOperationException($"Cannot connect a terminal session from state {current}.");
        }

        SetState(SessionState.Connecting);
        try
        {
            await StartShellAsync(cancellationToken).ConfigureAwait(false);
            SetState(SessionState.Connected);
            LogShellStarted(_logger, Id, _shellKind);
        }
        catch (Exception exception)
        {
            SetState(SessionState.Faulted);
            ErrorOccurred?.Invoke(this, exception);
            LogShellStartFailed(_logger, exception, Id, _shellKind);
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed || State is SessionState.Disconnected or SessionState.Disposed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        SetState(SessionState.Disconnecting);
        try
        {
            await InvokeOnUiThreadAsync(
                () =>
                {
                    if (Control.HasActiveSession)
                    {
                        Control.StopPty();
                    }

                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);
            SetState(SessionState.Disconnected);
        }
        catch (Exception exception)
        {
            SetState(SessionState.Faulted);
            ErrorOccurred?.Invoke(this, exception);
            LogShellStopFailed(_logger, exception, Id);
            throw;
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        SetState(SessionState.Reconnecting);
        try
        {
            await StartShellAsync(cancellationToken).ConfigureAwait(false);
            SetState(SessionState.Connected);
        }
        catch (Exception exception)
        {
            SetState(SessionState.Faulted);
            ErrorOccurred?.Invoke(this, exception);
            throw;
        }
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ThrowIfDisposed();
        EnsureConnected();
        EnsureInputUnlocked();
        return InvokeOnUiThreadAsync(
            () =>
            {
                Control.SendInput(text);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureConnected();
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);

        return InvokeOnUiThreadAsync(
            () =>
            {
                Control.Columns = columns;
                Control.Rows = rows;
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    public void SetFontSize(int fontSize)
    {
        ThrowIfDisposed();
        int normalized = TerminalFontSizePolicy.Normalize(fontSize);
        if (Dispatcher.UIThread.CheckAccess())
        {
            TerminalFontRedraw.Apply(Control, normalized);
        }
        else
        {
            Dispatcher.UIThread.Post(() => TerminalFontRedraw.Apply(Control, normalized));
        }
    }

    public void SetInputLocked(bool isLocked)
    {
        ThrowIfDisposed();
        _isInputLocked = isLocked;
    }

    public TerminalSearchResult StartSearch(string? searchText) =>
        RoyalTerminalSearch.Start(Control, searchText);

    public TerminalSearchResult SelectNextSearchMatch() =>
        RoyalTerminalSearch.Next(Control);

    public TerminalSearchResult SelectPreviousSearchMatch() =>
        RoyalTerminalSearch.Previous(Control);

    public void EndSearch() => Control.EndSearch();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            Control.ProcessExited -= OnProcessExited;
            Control.DataReceived -= OnDataReceived;
            SetState(SessionState.Disposed);
        }
    }

    private Task StartShellAsync(CancellationToken cancellationToken)
    {
        PtyTransportOptions options = new(
            Command: _profile.Command,
            WorkingDirectory: _workingDirectory,
            Environment: null,
            Dimensions: new TerminalSessionDimensions(
                InitialColumns,
                InitialRows,
                InitialWidthPixels,
                InitialHeightPixels));

        return InvokeOnUiThreadAsync(
            () => Control.StartSessionAsync(options, cancellationToken).AsTask(),
            cancellationToken);
    }

    private void OnProcessExited(object? sender, int exitCode)
    {
        if (!_disposed && State is SessionState.Connected or SessionState.Connecting)
        {
            SetState(SessionState.Disconnected);
        }

        LogShellExited(_logger, Id, exitCode);
    }

    private void OnDataReceived(object? sender, TerminalDataEventArgs e)
    {
        _ = sender;
        OutputReceived?.Invoke(
            this,
            new TerminalOutputReceivedEventArgs(e.DataSpan.ToArray()));
    }

    private void SetState(SessionState nextState)
    {
        bool changed;
        lock (_stateLock)
        {
            changed = _state != nextState;
            _state = nextState;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, nextState);
        }
    }

    private void EnsureConnected()
    {
        if (State != SessionState.Connected)
        {
            throw new InvalidOperationException("The terminal session is not connected.");
        }
    }

    private void EnsureInputUnlocked()
    {
        if (_isInputLocked)
        {
            throw new InvalidOperationException("Terminal input is locked.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static Task InvokeOnUiThreadAsync(
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(
            async () =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });

        return completion.Task.WaitAsync(cancellationToken);
    }

    [LoggerMessage(
        EventId = 301,
        Level = LogLevel.Information,
        Message = "Started local shell for session {SessionId} using {ShellKind}")]
    private static partial void LogShellStarted(
        ILogger logger,
        Guid sessionId,
        LocalShellKind shellKind);

    [LoggerMessage(
        EventId = 302,
        Level = LogLevel.Information,
        Message = "Local shell exited for session {SessionId} with code {ExitCode}")]
    private static partial void LogShellExited(ILogger logger, Guid sessionId, int exitCode);

    [LoggerMessage(
        EventId = 303,
        Level = LogLevel.Error,
        Message = "Unable to start local shell for session {SessionId} using {ShellKind}")]
    private static partial void LogShellStartFailed(
        ILogger logger,
        Exception exception,
        Guid sessionId,
        LocalShellKind shellKind);

    [LoggerMessage(
        EventId = 304,
        Level = LogLevel.Error,
        Message = "Unable to stop local shell for session {SessionId}")]
    private static partial void LogShellStopFailed(
        ILogger logger,
        Exception exception,
        Guid sessionId);
}
