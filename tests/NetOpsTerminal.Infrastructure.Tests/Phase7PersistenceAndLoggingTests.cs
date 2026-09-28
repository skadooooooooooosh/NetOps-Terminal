using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Sessions;
using NetOpsTerminal.Infrastructure.Logging;
using NetOpsTerminal.Infrastructure.Platform;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Infrastructure.Tests;

public sealed class Phase7PersistenceAndLoggingTests
{
    [Fact]
    public async Task SessionLogCapturesOutputButNeverTypedInput()
    {
        using var storage = new TemporaryStorage();
        var service = new FileSessionLogService(
            storage,
            NullLogger<FileSessionLogService>.Instance);
        var session = new RecordingSession();

        var handle = await service.StartAsync(session);
        await session.SendTextAsync("typed-password", CancellationToken.None);
        session.EmitOutput("router# show version\r\nNetwork OS\r\n");
        await service.StopAsync(session.Id);

        string content = await File.ReadAllTextAsync(handle.FilePath);
        Assert.Contains("router# show version", content, StringComparison.Ordinal);
        Assert.DoesNotContain("typed-password", content, StringComparison.Ordinal);
        await service.DisposeAsync();
    }

    private sealed class TemporaryStorage : IApplicationDataPathProvider, IDisposable
    {
        public TemporaryStorage()
        {
            Root = Path.Combine(Path.GetTempPath(), "NetOpsTerminal-Phase7-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string SettingsFilePath => Path.Combine(Root, "settings.json");

        public string KnownHostsFilePath => Path.Combine(Root, "ssh", "known_hosts");

        public string SessionLogsDirectoryPath => Path.Combine(Root, "session-logs");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class RecordingSession : ITerminalSession
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string DisplayName => "QA Session";
        public ConnectionType ConnectionType => ConnectionType.LocalShell;
        public string ConnectionLabel => "QA";
        public LocalShellKind? ShellKind => LocalShellKind.PowerShell;
        public SessionState State => SessionState.Connected;
        public int FontSize => 14;
        public bool IsInputLocked => false;
        public event EventHandler<SessionState>? StateChanged { add { } remove { } }
        public event EventHandler<Exception>? ErrorOccurred { add { } remove { } }
        public event EventHandler<TerminalOutputReceivedEventArgs>? OutputReceived;

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken) => Task.CompletedTask;
        public void SetFontSize(int fontSize) { }
        public void SetInputLocked(bool isLocked) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void EmitOutput(string text) => OutputReceived?.Invoke(
            this,
            new TerminalOutputReceivedEventArgs(Encoding.UTF8.GetBytes(text)));
    }
}
