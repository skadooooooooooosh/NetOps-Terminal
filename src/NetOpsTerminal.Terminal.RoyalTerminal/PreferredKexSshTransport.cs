using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.Terminal.Abstractions;
using Renci.SshNet;
using Renci.SshNet.Common;
using RoyalTerminal.Terminal;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

// This transport is limited to the application's existing password/PTY SSH path.
// It keeps SSH.NET behind the same RoyalTerminal transport contract while making
// the per-connection key-exchange order configurable before ConnectAsync.
internal sealed class PreferredKexSshTransportProvider(EphemeralSecret password, ILogger logger)
    : ITerminalTransportProvider
{
    public string TransportId => "ssh";

    public bool CanHandle(ITerminalTransportOptions options) => options is SshTransportOptions;

    public ITerminalTransport Create() => new PreferredKexSshTransport(password, logger);
}

internal sealed partial class PreferredKexSshTransport(EphemeralSecret password, ILogger logger)
    : ITerminalTransport
{
    private const int BufferSize = 16 * 1024;
    private readonly object _gate = new();
    private SshClient? _client;
    private ShellStream? _shell;
    private PasswordAuthenticationMethod? _authentication;
    private byte[]? _passwordBytes;
    private bool _stopping;
    private bool _disposed;
    private bool _exitReported;
    private bool _hostKeyMismatch;

    public event Action<byte[], int>? DataReceived;
    public event Action<int>? ProcessExited;

    public bool IsRunning { get; private set; }

    public async ValueTask StartAsync(
        ITerminalTransportOptions transportOptions,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (transportOptions is not SshTransportOptions options)
        {
            throw new ArgumentException("SSH transport options are required.", nameof(transportOptions));
        }

        if (_client is not null || IsRunning)
        {
            throw new InvalidOperationException("The SSH transport has already started.");
        }

        ValidateSupportedOptions(options);
        byte[] expectedHash = ParseExpectedFingerprint(options.ExpectedHostKeyFingerprintSha256);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password.Reveal());
        PasswordAuthenticationMethod? authentication = null;
        SshClient? client = null;
        ConnectionInfo connectionInfo;
        try
        {
            authentication = new PasswordAuthenticationMethod(options.Endpoint.Username, passwordBytes);
            connectionInfo = new ConnectionInfo(
                options.Endpoint.Host,
                options.Endpoint.Port,
                options.Endpoint.Username,
                authentication)
            {
                Timeout = TimeSpan.FromSeconds(options.Policy?.ConnectTimeoutSeconds ?? 15),
            };
            SshKeyExchangePreference.Apply(connectionInfo);
            client = new SshClient(connectionInfo)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(options.Policy?.KeepAliveIntervalSeconds ?? 30),
            };
        }
        catch
        {
            client?.Dispose();
            authentication?.Dispose();
            CryptographicOperations.ZeroMemory(passwordBytes);
            throw;
        }

        client.HostKeyReceived += ValidateHostKey;

        lock (_gate)
        {
            _client = client;
            _authentication = authentication;
            _passwordBytes = passwordBytes;
            _stopping = false;
            _exitReported = false;
            _hostKeyMismatch = false;
        }

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            LogNegotiatedKeyExchange(logger, connectionInfo.CurrentKeyExchangeAlgorithm ?? "unknown");

            TerminalSessionDimensions dimensions = options.Dimensions;
            ShellStream shell = client.CreateShellStream(
                options.TerminalType,
                checked((uint)dimensions.Columns),
                checked((uint)dimensions.Rows),
                checked((uint)dimensions.WidthPixels),
                checked((uint)dimensions.HeightPixels),
                BufferSize);
            shell.DataReceived += OnShellDataReceived;
            shell.Closed += OnShellClosed;
            bool stopped;
            lock (_gate)
            {
                stopped = _stopping;
                if (!stopped)
                {
                    _shell = shell;
                    IsRunning = true;
                }
            }

            if (stopped)
            {
                shell.DataReceived -= OnShellDataReceived;
                shell.Closed -= OnShellClosed;
                shell.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }

            DrainOutput(shell);
        }
        catch (Exception exception)
        {
            bool hostKeyMismatch = _hostKeyMismatch;
            await StopAsync().ConfigureAwait(false);
            if (hostKeyMismatch)
            {
                throw new TerminalConnectionException(
                    TerminalConnectionFailureKind.HostKeyChanged,
                    "The SSH server host key changed before authentication. Connection blocked.",
                    "Pinned SSH host key mismatch.",
                    exception);
            }

            throw;
        }

        void ValidateHostKey(object? sender, HostKeyEventArgs eventArgs)
        {
            _ = sender;
            eventArgs.CanTrust = IsExpectedHostKey(eventArgs.HostKey, expectedHash);
            if (!eventArgs.CanTrust)
            {
                _hostKeyMismatch = true;
            }
        }
    }

    public void SendInput(ReadOnlySpan<byte> input)
    {
        lock (_gate)
        {
            ShellStream shell = _shell
                ?? throw new InvalidOperationException("The SSH shell is not connected.");
            shell.Write(input);
            shell.Flush();
        }
    }

    public void Resize(TerminalSessionDimensions dimensions)
    {
        lock (_gate)
        {
            _shell?.ChangeWindowSize(
                checked((uint)dimensions.Columns),
                checked((uint)dimensions.Rows),
                checked((uint)dimensions.WidthPixels),
                checked((uint)dimensions.HeightPixels));
        }
    }

    public async ValueTask StopAsync()
    {
        Resources resources = TakeResources();
        await Task.Run(() => ReleaseResources(resources)).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseResources(TakeResources());
    }

    private static void ValidateSupportedOptions(SshTransportOptions options)
    {
        if (!options.RequestPty || !string.IsNullOrEmpty(options.InitialCommand)
            || options.Authentication is null || !options.Authentication.UsePassword
            || options.Authentication.UseAgent
            || options.Authentication.PrivateKeySecretIds.Count != 0
            || options.Proxy is not null || options.X11 is not null
            || options.PortForwardings is { Count: > 0 }
            || options.EnvironmentVariables is { Count: > 0 })
        {
            throw new NotSupportedException(
                "This SSH transport supports password-authenticated interactive shells only.");
        }
    }

    internal static byte[] ParseExpectedFingerprint(string? fingerprint)
    {
        const string prefix = "SHA256:";
        if (fingerprint is null || !fingerprint.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("A pinned SHA256 SSH host key is required.", nameof(fingerprint));
        }

        string encoded = fingerprint[prefix.Length..];
        byte[] hash = Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='));
        if (hash.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("The pinned SSH host-key fingerprint is invalid.", nameof(fingerprint));
        }

        return hash;
    }

    internal static bool IsExpectedHostKey(byte[] hostKey, ReadOnlySpan<byte> expectedHash)
    {
        byte[] actualHash = SHA256.HashData(hostKey);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private Resources TakeResources()
    {
        lock (_gate)
        {
            _stopping = true;
            IsRunning = false;
            Resources resources = new(_shell, _client, _authentication, _passwordBytes);
            _shell = null;
            _client = null;
            _authentication = null;
            _passwordBytes = null;
            if (resources.Shell is not null)
            {
                resources.Shell.DataReceived -= OnShellDataReceived;
                resources.Shell.Closed -= OnShellClosed;
            }

            return resources;
        }
    }

    private static void ReleaseResources(Resources resources)
    {
        try
        {
            try
            {
                resources.Shell?.Dispose();
            }
            finally
            {
                try
                {
                    resources.Client?.Dispose();
                }
                finally
                {
                    resources.Authentication?.Dispose();
                }
            }
        }
        finally
        {
            if (resources.PasswordBytes is not null)
            {
                CryptographicOperations.ZeroMemory(resources.PasswordBytes);
            }
        }
    }

    private void OnShellDataReceived(object? sender, ShellDataEventArgs eventArgs)
    {
        _ = eventArgs;
        if (sender is ShellStream shell)
        {
            DrainOutput(shell);
        }
    }

    private void DrainOutput(ShellStream shell)
    {
        List<(byte[] Buffer, int Count)> pending = [];
        lock (_gate)
        {
            if (_stopping || !ReferenceEquals(_shell, shell))
            {
                return;
            }

            while (shell.DataAvailable)
            {
                byte[] buffer = new byte[BufferSize];
                int count = shell.Read(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    break;
                }

                pending.Add((buffer, count));
            }
        }

        foreach ((byte[] buffer, int count) in pending)
        {
            DataReceived?.Invoke(buffer, count);
        }
    }

    private void OnShellClosed(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        lock (_gate)
        {
            if (_stopping || _exitReported)
            {
                return;
            }

            _exitReported = true;
            IsRunning = false;
        }

        ProcessExited?.Invoke(0);
    }

    private sealed record Resources(
        ShellStream? Shell,
        SshClient? Client,
        PasswordAuthenticationMethod? Authentication,
        byte[]? PasswordBytes);

    [LoggerMessage(EventId = 317, Level = LogLevel.Information,
        Message = "SSH negotiated key exchange {KeyExchangeAlgorithm}")]
    private static partial void LogNegotiatedKeyExchange(ILogger logger, string keyExchangeAlgorithm);
}
