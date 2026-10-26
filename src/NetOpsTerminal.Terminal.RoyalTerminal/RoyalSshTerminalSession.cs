using System.Net.Sockets;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Sessions;
using NetOpsTerminal.Security.Ssh;
using NetOpsTerminal.Terminal.Abstractions;
using Renci.SshNet.Common;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Ssh;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal sealed partial class RoyalSshTerminalSession : ITerminalSession, IRoyalTerminalSession
{
    private const int InitialColumns = 120;
    private const int InitialRows = 36;
    private const int InitialWidthPixels = 1200;
    private const int InitialHeightPixels = 720;
    private const int DefaultScrollbackLines = 10_000;
    private const int KeepAliveIntervalSeconds = 30;

    private readonly SshConnectionOptions _options;
    private readonly ISshHostKeyProbe _hostKeyProbe;
    private readonly ISshHostKeyTrustService _hostKeyTrustService;
    private readonly ITerminalPasteConfirmationService _pasteConfirmationService;
    private readonly ILogger<RoyalSshTerminalSession> _logger;
    private readonly object _stateLock = new();
    private SessionState _state = SessionState.Created;
    private TerminalControl? _control;
    private int _fontSize = TerminalFontSizePolicy.Default;
    private bool _isInputLocked;
    private bool _disposed;

    public RoyalSshTerminalSession(
        Guid id,
        string displayName,
        SshConnectionOptions options,
        ISshHostKeyProbe hostKeyProbe,
        ISshHostKeyTrustService hostKeyTrustService,
        ITerminalPasteConfirmationService pasteConfirmationService,
        ILogger<RoyalSshTerminalSession> logger)
    {
        Id = id;
        DisplayName = displayName;
        _options = options;
        _hostKeyProbe = hostKeyProbe;
        _hostKeyTrustService = hostKeyTrustService;
        _pasteConfirmationService = pasteConfirmationService;
        _logger = logger;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public ConnectionType ConnectionType => ConnectionType.Ssh;

    public string ConnectionLabel => $"SSH · {_options.Username}@{_options.Host}:{_options.Port}";

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

    public TerminalControl Control => _control
        ?? throw new InvalidOperationException("The SSH terminal control is not initialized.");

    public int FontSize => _fontSize;

    public bool IsInputLocked => _isInputLocked;

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
            throw new InvalidOperationException($"Cannot connect an SSH session from state {State}.");
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
        SetState(SessionState.Disconnecting);
        try
        {
            if (_control is not null)
            {
                await InvokeOnUiThreadAsync(
                    () =>
                    {
                        if (_control.HasActiveSession)
                        {
                            _control.StopPty();
                        }

                        return Task.CompletedTask;
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            SetState(SessionState.Disconnected);
            LogSshDisconnected(_logger, Id, _options.Host, _options.Port);
        }
        catch (Exception exception)
        {
            TerminalConnectionException mapped = MapConnectionException(exception);
            SetState(SessionState.Faulted);
            ErrorOccurred?.Invoke(this, mapped);
            LogSshOperationFailed(_logger, Id, _options.Host, _options.Port, mapped.FailureKind, exception.GetType().FullName ?? exception.GetType().Name);
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
        _fontSize = TerminalFontSizePolicy.Normalize(fontSize);
        if (_control is null)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            TerminalFontRedraw.Apply(_control, _fontSize);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_control is not null)
                {
                    TerminalFontRedraw.Apply(_control, _fontSize);
                }
            });
        }
    }

    public void SetInputLocked(bool isLocked)
    {
        ThrowIfDisposed();
        _isInputLocked = isLocked;
    }

    public TerminalSearchResult StartSearch(string? searchText) => _control is null
        ? TerminalSearchResult.Empty
        : RoyalTerminalSearch.Start(_control, searchText);

    public TerminalSearchResult SelectNextSearchMatch() => _control is null
        ? TerminalSearchResult.Empty
        : RoyalTerminalSearch.Next(_control);

    public TerminalSearchResult SelectPreviousSearchMatch() => _control is null
        ? TerminalSearchResult.Empty
        : RoyalTerminalSearch.Previous(_control);

    public void EndSearch() => _control?.EndSearch();

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
            _options.Dispose();
            if (_control is not null)
            {
                _control.ProcessExited -= OnProcessExited;
                _control.DataReceived -= OnDataReceived;
            }

            SetState(SessionState.Disposed);
        }
    }

    private async Task ConnectCoreAsync(
        SessionState connectingState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetState(connectingState);
        long connectionStarted = Stopwatch.GetTimestamp();
        string connectionStage = "trusted-host-key lookup";
        try
        {
            var endpoint = new SshHostEndpoint(_options.Host, _options.Port);
            SshHostKey? presentedKey = await _hostKeyTrustService
                .GetTrustedKeyAsync(endpoint, cancellationToken)
                .ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                double elapsedMilliseconds = Stopwatch.GetElapsedTime(connectionStarted).TotalMilliseconds;
                LogSshConnectionStage(
                    _logger,
                    Id,
                    presentedKey is null ? "Trusted key unavailable" : "Trusted key loaded",
                    elapsedMilliseconds);
            }

            if (presentedKey is null)
            {
                connectionStage = "host-key probe";
                long probeStarted = Stopwatch.GetTimestamp();
                presentedKey = await _hostKeyProbe
                    .ProbeAsync(_options, cancellationToken)
                    .ConfigureAwait(false);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    double elapsedMilliseconds = Stopwatch.GetElapsedTime(probeStarted).TotalMilliseconds;
                    LogSshConnectionStage(
                        _logger,
                        Id,
                        "Host key probed",
                        elapsedMilliseconds);
                }
                SshHostKeyVerificationResult verification = await _hostKeyTrustService
                    .VerifyAsync(endpoint, presentedKey, cancellationToken)
                    .ConfigureAwait(false);

                if (verification == SshHostKeyVerificationResult.Changed)
                {
                    throw new TerminalConnectionException(
                        TerminalConnectionFailureKind.HostKeyChanged,
                        "The SSH host key changed since the previous connection. The connection was blocked.",
                        "Host-key verification result: changed. No trust record was modified.");
                }

                if (verification == SshHostKeyVerificationResult.Rejected)
                {
                    throw new TerminalConnectionException(
                        TerminalConnectionFailureKind.HostKeyRejected,
                        "The SSH host key was not trusted. No connection was opened.",
                        "Host-key verification result: rejected by user.");
                }
            }

            connectionStage = "SSH login and terminal startup";
            long transportStarted = Stopwatch.GetTimestamp();
            try
            {
                await InvokeOnUiThreadAsync(
                async () =>
                {
                    if (_control is null)
                    {
                        long controlStarted = Stopwatch.GetTimestamp();
                        _control = CreateControl(presentedKey.FingerprintSha256);
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            double elapsedMilliseconds = Stopwatch.GetElapsedTime(controlStarted).TotalMilliseconds;
                            LogSshConnectionStage(_logger, Id, "Terminal control created", elapsedMilliseconds);
                        }
                    }

                    SshTransportOptions transportOptions = CreateTransportOptions(
                        presentedKey.FingerprintSha256);
                    long sshStarted = Stopwatch.GetTimestamp();
                    await _control.StartSshAsync(transportOptions, cancellationToken);
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        double elapsedMilliseconds = Stopwatch.GetElapsedTime(sshStarted).TotalMilliseconds;
                        LogSshConnectionStage(_logger, Id, "SSH login and shell opened", elapsedMilliseconds);
                    }
                },
                cancellationToken).ConfigureAwait(false);
            }
            catch (TerminalConnectionException exception)
                when (exception.FailureKind == TerminalConnectionFailureKind.HostKeyChanged)
            {
                connectionStage = "changed host-key probe";
                SshHostKey replacementKey = await _hostKeyProbe
                    .ProbeAsync(_options, cancellationToken).ConfigureAwait(false);
                if (replacementKey.FingerprintSha256 == presentedKey.FingerprintSha256)
                {
                    throw;
                }

                SshHostKeyVerificationResult verification = await _hostKeyTrustService
                    .VerifyAsync(endpoint, replacementKey, cancellationToken)
                    .ConfigureAwait(false);
                if (verification == SshHostKeyVerificationResult.Trusted)
                {
                    throw new TerminalConnectionException(
                        TerminalConnectionFailureKind.HostKeyChanged,
                        "The new SSH host key was saved. Connect again to open a session.",
                        "Replacement host key explicitly verified and saved; no login was attempted.");
                }

                throw;
            }
            if (_logger.IsEnabled(LogLevel.Information))
            {
                double elapsedMilliseconds = Stopwatch.GetElapsedTime(transportStarted).TotalMilliseconds;
                LogSshConnectionStage(
                    _logger,
                    Id,
                    "SSH transport started",
                    elapsedMilliseconds);
            }
            SetState(SessionState.Connected);
            LogSshConnected(_logger, Id, _options.Host, _options.Port, _options.Username, presentedKey.FingerprintSha256);
        }
        catch (Exception exception)
        {
            TerminalConnectionException mapped = MapConnectionException(exception, connectionStage);
            SetState(mapped.FailureKind == TerminalConnectionFailureKind.Cancelled
                ? SessionState.Disconnected
                : SessionState.Faulted);
            ErrorOccurred?.Invoke(this, mapped);
            LogSshOperationFailed(_logger, Id, _options.Host, _options.Port, mapped.FailureKind, exception.GetType().FullName ?? exception.GetType().Name);
            throw mapped;
        }
    }

    private TerminalControl CreateControl(string expectedFingerprintSha256)
    {
        var credentialProvider = new EphemeralSshCredentialProvider(_options.Password);
        var hostKeyValidator = new ExpectedFingerprintSshHostKeyValidator(expectedFingerprintSha256);
        var sshProvider = new PreferredKexSshTransportProvider(_options.Password, _logger);
        var transportFactory = new CompositeTerminalTransportFactory([sshProvider]);
        var control = TerminalControlConfiguration.Apply(new TerminalControl(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new CleanTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory(),
            new DefaultPtyFactory(),
            credentialProvider,
            hostKeyValidator,
            transportFactory)
        {
            FontFamilyName = "Consolas",
            TerminalFontSize = _fontSize,
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
        control.DataReceived += OnDataReceived;
        return control;
    }

    private SshTransportOptions CreateTransportOptions(string expectedFingerprintSha256)
    {
        return new SshTransportOptions(
            new SshEndpointOptions(_options.Host, _options.Port, _options.Username),
            RequestPty: true,
            TerminalType: "xterm-256color",
            InitialCommand: string.Empty,
            new SshAuthenticationOptions(
                UsePassword: true,
                PasswordSecretId: "phase3-session-password",
                PrivateKeySecretIds: [],
                UseAgent: false),
            new TerminalSessionDimensions(
                InitialColumns,
                InitialRows,
                InitialWidthPixels,
                InitialHeightPixels))
        {
            ExpectedHostKeyFingerprintSha256 = expectedFingerprintSha256,
            Policy = new SshPolicyOptions(
                KeepAliveIntervalSeconds,
                _options.ConnectTimeoutSeconds),
        };
    }

    private TerminalConnectionException MapConnectionException(Exception exception, string connectionStage = "disconnect")
    {
        if (exception is TerminalConnectionException terminalException)
        {
            return terminalException;
        }

        string target = $"{_options.Host}:{_options.Port}";
        if (exception is OperationCanceledException)
        {
            return new TerminalConnectionException(
                TerminalConnectionFailureKind.Cancelled,
                $"SSH connection to {target} was cancelled.",
                exception.GetType().Name,
                exception);
        }

        if (exception is SshAuthenticationException)
        {
            return new TerminalConnectionException(
                TerminalConnectionFailureKind.AuthenticationFailed,
                $"Authentication failed for user {_options.Username}.",
                exception.GetType().Name,
                exception);
        }

        if (exception is SshOperationTimeoutException or TimeoutException)
        {
            return new TerminalConnectionException(
                TerminalConnectionFailureKind.TimedOut,
                $"Connection timed out while connecting to {target}.",
                exception.GetType().Name,
                exception);
        }

        SocketException? socketException = FindSocketException(exception);
        if (socketException is not null)
        {
            TerminalConnectionFailureKind kind = socketException.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => TerminalConnectionFailureKind.DnsFailure,
                SocketError.ConnectionRefused => TerminalConnectionFailureKind.ConnectionRefused,
                SocketError.TimedOut => TerminalConnectionFailureKind.TimedOut,
                _ => TerminalConnectionFailureKind.NetworkFailure,
            };
            string message = kind switch
            {
                TerminalConnectionFailureKind.DnsFailure => $"The SSH host {_options.Host} could not be resolved.",
                TerminalConnectionFailureKind.ConnectionRefused => $"SSH connection to {target} was refused.",
                TerminalConnectionFailureKind.TimedOut => $"Connection timed out while connecting to {target}.",
                _ => $"A network error occurred while connecting to {target}.",
            };
            return new TerminalConnectionException(
                kind,
                message,
                $"Socket error: {socketException.SocketErrorCode}",
                exception);
        }

        if (exception is SshConnectionException sshException)
        {
            string category = sshException.Message.Contains("key exchange", StringComparison.OrdinalIgnoreCase)
                || sshException.Message.Contains("negotiate", StringComparison.OrdinalIgnoreCase)
                ? "algorithm negotiation"
                : "SSH handshake or server disconnect";
            return new TerminalConnectionException(
                TerminalConnectionFailureKind.Unknown,
                $"SSH connection to {target} failed during {connectionStage}. Check the device's SSH settings and logs.",
                $"SshConnectionException; stage: {connectionStage}; category: {category}; disconnect reason: {sshException.DisconnectReason}",
                exception);
        }

        return new TerminalConnectionException(
            TerminalConnectionFailureKind.Unknown,
            $"SSH connection to {target} failed during {connectionStage}.",
            $"{exception.GetType().Name}; stage: {connectionStage}",
            exception);
    }

    private static SocketException? FindSocketException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socketException)
            {
                return socketException;
            }
        }

        return null;
    }

    private void OnProcessExited(object? sender, int exitCode)
    {
        _ = sender;
        if (!_disposed && State is SessionState.Connected or SessionState.Connecting or SessionState.Reconnecting)
        {
            SetState(SessionState.Disconnected);
        }

        LogSshExited(_logger, Id, _options.Host, _options.Port, exitCode);
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
            throw new InvalidOperationException("The SSH terminal session is not connected.");
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
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
        return completion.Task.WaitAsync(cancellationToken);
    }

    [LoggerMessage(EventId = 311, Level = LogLevel.Information, Message = "Connected SSH session {SessionId} to {Host}:{Port} as {Username} with host key {FingerprintSha256}")]
    private static partial void LogSshConnected(ILogger logger, Guid sessionId, string host, int port, string username, string fingerprintSha256);

    [LoggerMessage(EventId = 312, Level = LogLevel.Information, Message = "Disconnected SSH session {SessionId} from {Host}:{Port}")]
    private static partial void LogSshDisconnected(ILogger logger, Guid sessionId, string host, int port);

    [LoggerMessage(EventId = 313, Level = LogLevel.Information, Message = "SSH session {SessionId} for {Host}:{Port} exited with code {ExitCode}")]
    private static partial void LogSshExited(ILogger logger, Guid sessionId, string host, int port, int exitCode);

    [LoggerMessage(EventId = 314, Level = LogLevel.Error, Message = "SSH operation failed for session {SessionId} to {Host}:{Port}; kind {FailureKind}; exception {ExceptionType}")]
    private static partial void LogSshOperationFailed(ILogger logger, Guid sessionId, string host, int port, TerminalConnectionFailureKind failureKind, string exceptionType);

    [LoggerMessage(EventId = 315, Level = LogLevel.Information, Message = "SSH connection stage for session {SessionId}: {Stage} in {ElapsedMilliseconds:F0} ms")]
    private static partial void LogSshConnectionStage(ILogger logger, Guid sessionId, string stage, double elapsedMilliseconds);

}
