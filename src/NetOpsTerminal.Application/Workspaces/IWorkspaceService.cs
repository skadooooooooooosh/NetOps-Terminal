namespace NetOpsTerminal.Application.Workspaces;

public interface IWorkspaceRepository
{
    Task<WorkspaceDocument?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken);
}

public interface IWorkspaceService
{
    Task<WorkspaceDocument?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken);
}
