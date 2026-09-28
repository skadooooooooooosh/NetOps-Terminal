using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NetOpsTerminal.Application.Workspaces;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Infrastructure.Persistence;
using NetOpsTerminal.Infrastructure.Platform;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Infrastructure.Tests;

public sealed class SqliteWorkspacePersistenceTests
{
    [Fact]
    public async Task VersionTwoWorkspaceRoundTripsMetadataWithoutSecrets()
    {
        using var storage = new TemporaryStorage();
        var factory = new SqliteConnectionFactory(storage);
        using var initializer = new SqliteDatabaseInitializer(
            factory,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        var repository = new SqliteWorkspaceRepository(initializer, factory);
        Guid tabId = Guid.NewGuid();
        Guid paneId = Guid.NewGuid();
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
                            Kind = WorkspaceLaunchKind.LocalShell,
                            DisplayName = "PowerShell",
                            ConnectionType = ConnectionType.LocalShell,
                            ShellKind = LocalShellKind.PowerShell,
                        },
                    },
                },
            ],
        };

        await repository.SaveAsync(document, CancellationToken.None);
        WorkspaceDocument? loaded = await repository.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(tabId, loaded.ActiveTabId);
        Assert.Equal(paneId, Assert.Single(loaded.Tabs).ActivePaneId);
        await using SqliteConnection connection = await factory.OpenAsync(CancellationToken.None);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT DocumentJson FROM WorkspaceState WHERE Id = 1;";
        string json = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryStorage : IApplicationDataPathProvider, IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"netops-terminal-workspace-tests-{Guid.NewGuid():N}");

        public string SettingsFilePath => Path.Combine(_directory, "settings.json");

        public string KnownHostsFilePath => Path.Combine(_directory, "ssh", "known_hosts");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
