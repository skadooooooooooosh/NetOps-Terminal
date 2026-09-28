using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NetOpsTerminal.Application.Sessions;
using NetOpsTerminal.Infrastructure.Platform;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Infrastructure.Logging;

public sealed partial class FileSessionLogService(
    IApplicationDataPathProvider pathProvider,
    ILogger<FileSessionLogService> logger)
    : ISessionLogService
{
    private readonly ConcurrentDictionary<Guid, ActiveSessionLog> _activeLogs = new();
    private bool _disposed;

    public bool IsLogging(Guid sessionId) => _activeLogs.ContainsKey(sessionId);

    public string? GetLogFilePath(Guid sessionId) =>
        _activeLogs.TryGetValue(sessionId, out ActiveSessionLog? active)
            ? active.Handle.FilePath
            : null;

    public async Task<SessionLogHandle> StartAsync(
        ITerminalSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeLogs.TryGetValue(session.Id, out ActiveSessionLog? existing))
        {
            return existing.Handle;
        }

        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        string datedDirectory = Path.Combine(
            pathProvider.SessionLogsDirectoryPath,
            startedAtUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(datedDirectory);
        string safeName = CreateSafeFileName(session.DisplayName);
        string filePath = Path.Combine(
            datedDirectory,
            $"{startedAtUtc:HHmmss}-{safeName}-{session.Id:N}.log");
        var stream = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var handle = new SessionLogHandle(session.Id, filePath, startedAtUtc);
        var active = new ActiveSessionLog(session, handle, stream);
        try
        {
            await active.WriteHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (!_activeLogs.TryAdd(session.Id, active))
            {
                await active.DisposeAsync().ConfigureAwait(false);
                return _activeLogs[session.Id].Handle;
            }

            active.Start();
            LogSessionLoggingStarted(logger, session.Id, filePath);
            return handle;
        }
        catch
        {
            await active.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_activeLogs.TryRemove(sessionId, out ActiveSessionLog? active))
        {
            return;
        }

        await active.StopAsync(cancellationToken).ConfigureAwait(false);
        LogSessionLoggingStopped(logger, sessionId, active.Handle.FilePath);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Guid[] sessionIds = _activeLogs.Keys.ToArray();
        foreach (Guid sessionId in sessionIds)
        {
            await StopAsync(sessionId).ConfigureAwait(false);
        }
    }

    private static string CreateSafeFileName(string displayName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string normalized = new(displayName
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray());
        normalized = normalized.Trim().Replace(' ', '-');
        if (normalized.Length == 0)
        {
            normalized = "session";
        }

        return normalized.Length <= 60 ? normalized : normalized[..60];
    }

    [LoggerMessage(EventId = 551, Level = LogLevel.Information, Message = "Started output-only logging for terminal session {SessionId} at {LogFilePath}")]
    private static partial void LogSessionLoggingStarted(ILogger logger, Guid sessionId, string logFilePath);

    [LoggerMessage(EventId = 552, Level = LogLevel.Information, Message = "Stopped output-only logging for terminal session {SessionId} at {LogFilePath}")]
    private static partial void LogSessionLoggingStopped(ILogger logger, Guid sessionId, string logFilePath);

    private sealed class ActiveSessionLog(
        ITerminalSession session,
        SessionLogHandle handle,
        FileStream stream)
        : IAsyncDisposable
    {
        private readonly Channel<byte[]> _output = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        private Task? _writerTask;
        private bool _subscribed;

        public SessionLogHandle Handle { get; } = handle;

        public async Task WriteHeaderAsync(CancellationToken cancellationToken)
        {
            string header = $"# NetOps Terminal output log{Environment.NewLine}"
                + $"# Started (UTC): {Handle.StartedAtUtc:O}{Environment.NewLine}"
                + $"# Session: {session.Id:D}{Environment.NewLine}"
                + $"# Type: {session.ConnectionType}{Environment.NewLine}"
                + "# Security: output only; keyboard input is never intentionally recorded."
                + Environment.NewLine + Environment.NewLine;
            await stream.WriteAsync(Encoding.UTF8.GetBytes(header), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Start()
        {
            session.OutputReceived += OnOutputReceived;
            _subscribed = true;
            _writerTask = WriteOutputAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_subscribed)
            {
                session.OutputReceived -= OnOutputReceived;
                _subscribed = false;
            }

            _output.Writer.TryComplete();
            if (_writerTask is not null)
            {
                await _writerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void OnOutputReceived(object? sender, TerminalOutputReceivedEventArgs e)
        {
            _ = sender;
            if (!e.Data.IsEmpty)
            {
                _output.Writer.TryWrite(e.Data.ToArray());
            }
        }

        private async Task WriteOutputAsync()
        {
            await foreach (byte[] payload in _output.Reader.ReadAllAsync())
            {
                await stream.WriteAsync(payload).ConfigureAwait(false);
            }
        }
    }
}
