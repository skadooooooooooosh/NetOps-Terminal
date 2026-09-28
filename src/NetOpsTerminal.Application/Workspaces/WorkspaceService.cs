using NetOpsTerminal.Application.Abstractions;

namespace NetOpsTerminal.Application.Workspaces;

public sealed class WorkspaceService(
    IApplicationDatabaseInitializer databaseInitializer,
    IWorkspaceRepository repository) : IWorkspaceService
{
    public async Task<WorkspaceDocument?> LoadAsync(CancellationToken cancellationToken)
    {
        await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        WorkspaceDocument? document = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        return document is null ? null : Validate(document);
    }

    public async Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await repository.SaveAsync(Validate(document), cancellationToken).ConfigureAwait(false);
    }

    private static WorkspaceDocument Validate(WorkspaceDocument document)
    {
        if (document.Version != WorkspaceDocument.CurrentVersion)
        {
            throw new InvalidOperationException($"Unsupported workspace document version {document.Version}.");
        }

        var tabIds = new HashSet<Guid>();
        var paneIds = new HashSet<Guid>();
        foreach (WorkspaceTabDocument tab in document.Tabs)
        {
            if (tab.TabId == Guid.Empty || !tabIds.Add(tab.TabId))
            {
                throw new InvalidOperationException("Workspace tab identifiers must be unique and non-empty.");
            }

            ValidateNode(tab.Root, paneIds);
            if (!ContainsPane(tab.Root, tab.ActivePaneId))
            {
                throw new InvalidOperationException("The active workspace pane does not exist in its tab.");
            }
        }

        if (document.ActiveTabId is { } activeTabId && !tabIds.Contains(activeTabId))
        {
            throw new InvalidOperationException("The active workspace tab does not exist.");
        }

        return document;
    }

    private static void ValidateNode(PaneNodeDocument node, HashSet<Guid> paneIds)
    {
        if (node.NodeId == Guid.Empty || !paneIds.Add(node.NodeId))
        {
            throw new InvalidOperationException("Workspace pane identifiers must be unique and non-empty.");
        }

        if (node.IsSplit)
        {
            if (node.First is null || node.Second is null || node.Launch is not null)
            {
                throw new InvalidOperationException("A split pane must contain exactly two child nodes.");
            }

            if (node.SplitRatio is < 0.15 or > 0.85)
            {
                throw new InvalidOperationException("Workspace split ratios must remain between 0.15 and 0.85.");
            }

            ValidateNode(node.First, paneIds);
            ValidateNode(node.Second, paneIds);
        }
        else if (node.First is not null || node.Second is not null)
        {
            throw new InvalidOperationException("A terminal pane cannot contain child nodes.");
        }
    }

    private static bool ContainsPane(PaneNodeDocument node, Guid paneId)
    {
        return node.NodeId == paneId
            || node.First is not null && ContainsPane(node.First, paneId)
            || node.Second is not null && ContainsPane(node.Second, paneId);
    }
}
