using System.Text.Json;
using Microsoft.Data.Sqlite;
using NetOpsTerminal.Application.Abstractions;
using NetOpsTerminal.Application.Workspaces;

namespace NetOpsTerminal.Infrastructure.Persistence;

public sealed class SqliteWorkspaceRepository(
    IApplicationDatabaseInitializer databaseInitializer,
    SqliteConnectionFactory connectionFactory) : IWorkspaceRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);

    public async Task<WorkspaceDocument?> LoadAsync(CancellationToken cancellationToken)
    {
        await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT DocumentJson FROM WorkspaceState WHERE Id = 1;";
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string json
            ? JsonSerializer.Deserialize<WorkspaceDocument>(json, SerializerOptions)
            : null;
    }

    public async Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        string json = JsonSerializer.Serialize(document, SerializerOptions);
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO WorkspaceState (Id, DocumentJson, UpdatedAtUtc)
            VALUES (1, $document, $updated)
            ON CONFLICT(Id) DO UPDATE SET
                DocumentJson = excluded.DocumentJson,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$document", json);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
