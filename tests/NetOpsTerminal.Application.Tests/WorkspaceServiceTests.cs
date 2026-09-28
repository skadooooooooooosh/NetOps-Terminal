using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Workspaces;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Application.Tests;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public async Task SavesValidNestedPaneTree()
    {
        var repository = new RecordingRepository();
        var service = new WorkspaceService(new RecordingInitializer(), repository);
        WorkspaceDocument document = CreateDocument();

        await service.SaveAsync(document, CancellationToken.None);

        Assert.Same(document, repository.Saved);
    }

    [Fact]
    public async Task RejectsDuplicatePaneIdentifiers()
    {
        Guid duplicate = Guid.NewGuid();
        WorkspaceDocument document = CreateDocument(duplicate, duplicate);
        var service = new WorkspaceService(new RecordingInitializer(), new RecordingRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveAsync(document, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsActivePaneOutsideTree()
    {
        WorkspaceDocument original = CreateDocument();
        WorkspaceDocument invalid = original with
        {
            Tabs =
            [
                original.Tabs[0] with { ActivePaneId = Guid.NewGuid() },
            ],
        };
        var service = new WorkspaceService(new RecordingInitializer(), new RecordingRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveAsync(invalid, CancellationToken.None));
    }

    private static WorkspaceDocument CreateDocument(Guid? firstId = null, Guid? secondId = null)
    {
        Guid first = firstId ?? Guid.NewGuid();
        Guid second = secondId ?? Guid.NewGuid();
        return new WorkspaceDocument
        {
            ActiveTabId = Guid.Parse("71f482d4-9941-4ce9-9e77-7a68b1e68f29"),
            Tabs =
            [
                new WorkspaceTabDocument
                {
                    TabId = Guid.Parse("71f482d4-9941-4ce9-9e77-7a68b1e68f29"),
                    ActivePaneId = second,
                    Root = new PaneNodeDocument
                    {
                        NodeId = Guid.NewGuid(),
                        IsSplit = true,
                        SplitDirection = PaneSplitDirection.Right,
                        SplitRatio = 0.4,
                        First = new PaneNodeDocument
                        {
                            NodeId = first,
                            Launch = new WorkspaceLaunchReference
                            {
                                Kind = WorkspaceLaunchKind.LocalShell,
                                DisplayName = "PowerShell",
                                ConnectionType = ConnectionType.LocalShell,
                                ShellKind = LocalShellKind.PowerShell,
                            },
                        },
                        Second = new PaneNodeDocument { NodeId = second },
                    },
                },
            ],
        };
    }

    private sealed class RecordingInitializer : IApplicationDatabaseInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingRepository : IWorkspaceRepository
    {
        public WorkspaceDocument? Saved { get; private set; }

        public Task<WorkspaceDocument?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Saved);

        public Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken)
        {
            Saved = document;
            return Task.CompletedTask;
        }
    }
}
