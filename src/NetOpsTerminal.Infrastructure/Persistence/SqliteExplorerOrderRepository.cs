using Microsoft.Data.Sqlite;
using NetOpsTerminal.Application.Devices;

namespace NetOpsTerminal.Infrastructure.Persistence;

public sealed class SqliteExplorerOrderRepository(SqliteConnectionFactory connectionFactory)
    : IExplorerOrderRepository
{
    public async Task<IReadOnlyDictionary<Guid, int>> LoadAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EntityId, SortPosition FROM ExplorerOrder;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var order = new Dictionary<Guid, int>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            order.Add(Guid.Parse(reader.GetString(0)), reader.GetInt32(1));
        }

        return order;
    }

    public async Task ClearAsync(IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siblingIds);
        if (siblingIds.Any(id => id == Guid.Empty) || siblingIds.Distinct().Count() != siblingIds.Count)
            throw new ArgumentException("Explorer order requires distinct saved IDs.", nameof(siblingIds));
        if (siblingIds.Count == 0)
            return;

        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (Guid id in siblingIds)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM ExplorerOrder WHERE EntityId = @id;";
            command.Parameters.AddWithValue("@id", id.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siblingIds);
        if (siblingIds.Any(id => id == Guid.Empty) || siblingIds.Distinct().Count() != siblingIds.Count)
            throw new ArgumentException("Explorer order requires distinct saved IDs.", nameof(siblingIds));

        await using SqliteConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        for (int position = 0; position < siblingIds.Count; position++)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ExplorerOrder (EntityId, SortPosition) VALUES (@id, @position)
                ON CONFLICT(EntityId) DO UPDATE SET SortPosition = excluded.SortPosition;
                """;
            command.Parameters.AddWithValue("@id", siblingIds[position].ToString("D"));
            command.Parameters.AddWithValue("@position", position);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
