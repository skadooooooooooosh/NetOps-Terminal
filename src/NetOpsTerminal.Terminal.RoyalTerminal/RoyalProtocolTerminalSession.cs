using System.IO.Ports;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Sessions;
using NetOpsTerminal.Terminal.Abstractions;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Raw;
using RoyalTerminal.Terminal.Transport.Serial;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Telnet;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal sealed partial class RoyalProtocolTerminalSession : ITerminalSession, IRoyalTerminalSession
{
    private const int InitialColumns = 120;
    private const int InitialRows = 36;
    private const int InitialWidthPixels = 1200;
    private const int InitialHeightPixels = 720;
    private const int DefaultScrollbackLines = 10_000;
    private const int SerialOpenTimeoutSeconds = 15;

    private readonly TelnetConnectionOptions? _telnetOptions;
    private readonly SerialConnectionOptions? _serialOptions;
    private readonly RawTcpConnectionOptions? _rawTcpOptions;
    private readonly ITerminalPasteConfirmationService _pasteConfirmationService;
    private readonly ILogger<RoyalProtocolTerminalSession> _logger;
    private readonly object _stateLock = new();
    private SessionState _state = SessionState.Created;
    private bool _isInputLocked;
    private bool _disposed;

    public RoyalProtocolTerminalSession(
        Guid id,
        string displayName,
        TelnetConnectionOptions options,
        ITerminalPasteConfirmationService pasteConfirmationService,
        ILogger<RoyalProtocolTerminalSession> logger)
    {
        Id = id;
        DisplayName = displayName;
        ConnectionType = ConnectionType.Telnet;
        _telnetOptions = options;
        _pasteConfirmationService = pasteConfirmationService;
        _logger = logger;
        Control = CreateControl(
            new TelnetTerminalTransportProvider(),
            options.Encoding,
            lineEnding: null);
        Control.DataReceived += OnDataReceived;
    }

    public RoyalProtocolTerminalSession(
        Guid id,
        string displayName,
        SerialConnectionOptions options,
        ITerminalPasteConfirmationService pasteConfirmationService,
        ILogger<RoyalProtocolTerminalSession> logger)
    {
        Id = id;
        DisplayName = displayName;
        ConnectionType = ConnectionType.Serial;
        _serialOptions = options;
        _pasteConfirmationService = pasteConfirmationService;
        _logger = logger;
        Control = CreateControl(
            new SerialTerminalTransportProvider(),
            options.Encoding,
            lineEnding: null);
        Control.DataReceived += OnDataReceived;
    }

    public RoyalProtocolTerminalSession(
        Guid id,
        string displayName,
        RawTcpConnectionOptions options,
        ITerminalPasteConfirmationService pasteConfirmationService,
        ILogger<RoyalProtocolTerminalSession> logger)
    {
        Id = id;
        DisplayName = displayName;
        ConnectionType = ConnectionType.RawTcp;
        _rawTcpOptions = options;
        _pasteConfirmationService = pasteConfirmationService;
        _logger = logger;
        Control = CreateControl(
            new RawTcpTerminalTransportProvider(),
            options.Encoding,
            options.LineEnding);
        Control.DataReceived += OnDataReceived;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public ConnectionType ConnectionType { get; }

    public string ConnectionLabel => ConnectionType switch
    {
        ConnectionType.Telnet => $"TELNET · {_telnetOptions!.Host}:{_telnetOptions.Port} · PLAINTEXT",
        ConnectionType.Serial => $"SERIAL · {_serialOptions!.PortName} · {_serialOptions.BaudRate}",
        ConnectionType.RawTcp => $"RAW TCP · {_rawTcpOptions!.Host}:{_rawTcpOptions.Port}",
        _ => throw new InvalidOperationException("Unsupported Phase 5 connection type."),
    };

    public LocalShellKind? ShellKind => null;

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

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (State == SessionState.Connected)
        {
            return Task.CompletedTask;
        }

        if (State is not (SessionState.Created or SessionState.Disconnected or SessionState.Faulted))
        {
            throw new InvalidOperationException($"Cannot connect a {ConnectionType} session from state {State}.");
        }

        return ConnectCoreAsync(SessionState.Connecting, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed || State is SessionState.Disconnected or SessionState.Disposed or SessionState.Created)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string target = GetSafeTarget();
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
            LogProtocolDisconnected(_logger, Id, ConnectionType, target);
        }
        catch (Exception exception)
        {
            TerminalConnectionException mapped = MapConnectionException(exception, timedOut: false);
            SetState(SessionState.Faulted);
            ErrorOccurred?.Invoke(this, mapped);
            throw mapped;
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await ConnectCoreAsync(SessionState.Reconnecting, cancellationToken).ConfigureAwait(false);
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

    private async Task ConnectCoreAsync(
        SessionState connectingState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string target = GetSafeTarget();
        SetState(connectingState);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(GetConnectTimeoutSeconds()));
        try
        {
            await InvokeOnUiThreadAsync(
                () => StartTransportAsync(timeoutSource.Token),
                timeoutSource.Token).ConfigureAwait(false);
            SetState(SessionState.Connected);
            LogProtocolConnected(_logger, Id, ConnectionType, target);
        }
        catch (Exception exception)
        {
            bool timedOut = exception is OperationCanceledException
                && !cancellationToken.IsCancellationRequested
                && timeoutSource.IsCancellationRequested;
            TerminalConnectionException mapped = MapConnectionException(exception, timedOut);
            SetState(mapped.FailureKind == TerminalConnectionFailureKind.Cancelled
                ? SessionState.Disconnected
                : SessionState.Faulted);
            ErrorOccurred?.Invoke(this, mapped);
            LogProtocolOperationFailed(
                _logger,
                Id,
                ConnectionType,
                target,
                mapped.FailureKind,
                exception.GetType().FullName ?? exception.GetType().Name);
            throw mapped;
        }
    }

    private async Task StartTransportAsync(CancellationToken cancellationToken)
    {
        TerminalSessionDimensions dimensions = CreateDimensions();
        if (_telnetOptions is not null)
        {
            await Control.StartTelnetAsync(
                new TelnetTransportOptions(
                    _telnetOptions.Host,
                    _telnetOptions.Port,
                    _telnetOptions.TerminalType,
                    dimensions),
                cancellationToken);
            return;
        }

        if (_serialOptions is not null)
        {
            var options = new SerialTransportOptions(
                _serialOptions.PortName,
                _serialOptions.BaudRate,
                _serialOptions.DataBits,
                MapParity(_serialOptions.Parity),
                MapStopBits(_serialOptions.StopBits),
                MapHandshake(_serialOptions.FlowControl),
                dimensions)
            {
                NewLine = "\r",
            };
            await Control.StartSerialAsync(options, cancellationToken);
            return;
        }

        await Control.StartRawTcpAsync(
            new RawTcpTransportOptions(
                _rawTcpOptions!.Host,
                _rawTcpOptions.Port,
                dimensions),
            cancellationToken);
    }

    private TerminalControl CreateControl(
        ITerminalTransportProvider provider,
        TerminalTextEncoding encoding,
        TerminalLineEnding? lineEnding)
    {
        var transportFactory = new CompositeTerminalTransportFactory(
            [new TranscodingTerminalTransportProvider(provider, encoding, lineEnding)]);
        var control = TerminalControlConfiguration.Apply(new TerminalControl(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new CleanTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory(),
            new DefaultPtyFactory(),
            new UnavailableSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            transportFactory)
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
        control.ProcessExited += OnProcessExited;
        return control;
    }

    private TerminalConnectionException MapConnectionException(Exception exception, bool timedOut)
    {
        if (exception is TerminalConnectionException terminalException)
        {
            return terminalException;
        }

        string target = GetSafeTarget();
        if (timedOut || exception is TimeoutException)
        {
            return new TerminalConnectionException(
                TerminalConnectionFailureKind.TimedOut,
                $"Connection to {target} timed out.",
                exception.GetType().Name,
                exception);
        }

        if (exception is OperationCanceledException)
        {
            return new TerminalConnectionException(
                TerminalConnectionFailureKind.Cancelled,
                $"Connection to {target} was cancelled.",
                exception.GetType().Name,
                exception);
        }

        if (ConnectionType == ConnectionType.Serial)
        {
            if (FindException<UnauthorizedAccessException>(exception) is not null)
            {
                return new TerminalConnectionException(
                    TerminalConnectionFailureKind.SerialPortInUse,
                    $"Serial port {target} is already in use or access was denied.",
                    exception.GetType().Name,
                    exception);
            }

            if (FindException<IOException>(exception) is not null
                || FindException<ArgumentException>(exception) is not null)
            {
                return new TerminalConnectionException(
                    TerminalConnectionFailureKind.SerialPortUnavailable,
                    $"Serial port {target} is unavailable. Refresh the port list and verify the cable.",
                    exception.GetType().Name,
                    exception);
            }
        }

        SocketException? socketException = FindException<SocketException>(exception);
        if (socketException is not null)
        {
            TerminalConnectionFailureKind kind = socketException.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain
                    => TerminalConnectionFailureKind.DnsFailure,
                SocketError.ConnectionRefused => TerminalConnectionFailureKind.ConnectionRefused,
                SocketError.TimedOut => TerminalConnectionFailureKind.TimedOut,
                _ => TerminalConnectionFailureKind.NetworkFailure,
            };
            string message = kind switch
            {
                TerminalConnectionFailureKind.DnsFailure => $"The host for {target} could not be resolved.",
                TerminalConnectionFailureKind.ConnectionRefused => $"Connection to {target} was refused.",
                TerminalConnectionFailureKind.TimedOut => $"Connection to {target} timed out.",
                _ => $"A network error occurred while connecting to {target}.",
            };
            return new TerminalConnectionException(
                kind,
                message,
                $"Socket error: {socketException.SocketErrorCode}",
                exception);
        }

        return new TerminalConnectionException(
            TerminalConnectionFailureKind.Unknown,
            $"{ConnectionType} connection to {target} failed.",
            exception.GetType().Name,
            exception);
    }

    private string GetSafeTarget()
    {
        return ConnectionType switch
        {
            ConnectionType.Telnet => $"{_telnetOptions!.Host}:{_telnetOptions.Port}",
            ConnectionType.Serial => _serialOptions!.PortName,
            ConnectionType.RawTcp => $"{_rawTcpOptions!.Host}:{_rawTcpOptions.Port}",
            _ => "unknown target",
        };
    }

    private int GetConnectTimeoutSeconds()
    {
        return _telnetOptions?.ConnectTimeoutSeconds
            ?? _rawTcpOptions?.ConnectTimeoutSeconds
            ?? SerialOpenTimeoutSeconds;
    }

    private void OnProcessExited(object? sender, int exitCode)
    {
        _ = sender;
        if (!_disposed && State is SessionState.Connected or SessionState.Connecting or SessionState.Reconnecting)
        {
            SetState(SessionState.Disconnected);
        }

        string target = GetSafeTarget();
        LogProtocolExited(_logger, Id, ConnectionType, target, exitCode);
    }

    private void OnDataReceived(object? sender, TerminalDataEventArgs e)
    {
        _ = sender;
        OutputReceived?.Invoke(
            this,
            new TerminalOutputReceivedEventArgs(e.DataSpan.ToArray()));
    }

    private void SetState(SessionState state)
    {
        bool changed;
        lock (_stateLock)
        {
            changed = _state != state;
            _state = state;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, state);
        }
    }

    private void EnsureConnected()
    {
        if (State != SessionState.Connected)
        {
            throw new InvalidOperationException($"The {ConnectionType} terminal session is not connected.");
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

    private static TException? FindException<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }

    private static TerminalSessionDimensions CreateDimensions()
    {
        return new TerminalSessionDimensions(
            InitialColumns,
            InitialRows,
            InitialWidthPixels,
            InitialHeightPixels);
    }

    private static TerminalSerialParity MapParity(SerialParity parity) => parity switch
    {
        SerialParity.None => TerminalSerialParity.None,
        SerialParity.Odd => TerminalSerialParity.Odd,
        SerialParity.Even => TerminalSerialParity.Even,
        SerialParity.Mark => TerminalSerialParity.Mark,
        SerialParity.Space => TerminalSerialParity.Space,
        _ => throw new ArgumentOutOfRangeException(nameof(parity)),
    };

    private static TerminalSerialStopBits MapStopBits(SerialStopBits stopBits) => stopBits switch
    {
        SerialStopBits.One => TerminalSerialStopBits.One,
        SerialStopBits.OnePointFive => TerminalSerialStopBits.OnePointFive,
        SerialStopBits.Two => TerminalSerialStopBits.Two,
        _ => throw new ArgumentOutOfRangeException(nameof(stopBits)),
    };

    private static TerminalSerialHandshake MapHandshake(SerialFlowControl flowControl) => flowControl switch
    {
        SerialFlowControl.None => TerminalSerialHandshake.None,
        SerialFlowControl.XOnXOff => TerminalSerialHandshake.XOnXOff,
        SerialFlowControl.RequestToSend => TerminalSerialHandshake.RequestToSend,
        SerialFlowControl.RequestToSendXOnXOff => TerminalSerialHandshake.RequestToSendXOnXOff,
        _ => throw new ArgumentOutOfRangeException(nameof(flowControl)),
    };

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
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
        return completion.Task.WaitAsync(cancellationToken);
    }

    [LoggerMessage(EventId = 321, Level = LogLevel.Information, Message = "Connected {ConnectionType} session {SessionId} to {Target}")]
    private static partial void LogProtocolConnected(ILogger logger, Guid sessionId, ConnectionType connectionType, string target);

    [LoggerMessage(EventId = 322, Level = LogLevel.Information, Message = "Disconnected {ConnectionType} session {SessionId} from {Target}")]
    private static partial void LogProtocolDisconnected(ILogger logger, Guid sessionId, ConnectionType connectionType, string target);

    [LoggerMessage(EventId = 323, Level = LogLevel.Information, Message = "{ConnectionType} session {SessionId} for {Target} exited with code {ExitCode}")]
    private static partial void LogProtocolExited(ILogger logger, Guid sessionId, ConnectionType connectionType, string target, int exitCode);

    [LoggerMessage(EventId = 324, Level = LogLevel.Error, Message = "{ConnectionType} operation failed for session {SessionId} to {Target}; kind {FailureKind}; exception {ExceptionType}")]
    private static partial void LogProtocolOperationFailed(ILogger logger, Guid sessionId, ConnectionType connectionType, string target, TerminalConnectionFailureKind failureKind, string exceptionType);
}
