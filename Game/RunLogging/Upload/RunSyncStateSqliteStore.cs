#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunSyncStateSqliteStore
{
    private readonly string _databasePath;

    public RunSyncStateSqliteStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        _databasePath = databasePath;
        EnsureSchema();
    }

    public void MarkRunDirty(string runId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.RunSyncStateTableName} (
                run_id,
                dirty,
                retry_count
            ) VALUES (
                $runId,
                1,
                0
            )
            ON CONFLICT(run_id) DO UPDATE SET
                dirty = 1,
                last_error = NULL;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = RunLogSqliteSchema.BootstrapSql;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandTimeout = 2;
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 2000;
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

}
