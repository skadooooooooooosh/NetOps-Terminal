using Microsoft.Data.Sqlite;
using NetOpsTerminal.Infrastructure.Platform;

namespace NetOpsTerminal.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string _databaseFilePath;

    public SqliteConnectionFactory(IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        string applicationDataDirectory = Path.GetDirectoryName(pathProvider.SettingsFilePath)
            ?? throw new InvalidOperationException("The application data directory is unavailable.");
        _databaseFilePath = Path.Combine(
            applicationDataDirectory,
            "data",
            "netops-terminal.db");
    }

    public string DatabaseFilePath => _databaseFilePath;

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_databaseFilePath)
            ?? throw new InvalidOperationException("The database directory is unavailable.");
        Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
