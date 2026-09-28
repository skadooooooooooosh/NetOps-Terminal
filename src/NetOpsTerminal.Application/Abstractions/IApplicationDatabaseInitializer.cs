namespace NetOpsTerminal.Application.Abstractions;

public interface IApplicationDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
