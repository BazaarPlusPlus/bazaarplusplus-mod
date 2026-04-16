using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.Identity
{
    public sealed class IdentityDatabase : IDisposable
    {
        private const int CurrentSchemaVersion = 1;
        private readonly string _filePath;
        private SqliteConnection? _connection;

        public IdentityDatabase(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        public SqliteConnection Connection
        {
            get
            {
                if (_connection == null) Open();
                return _connection!;
            }
        }

        public void Open()
        {
            if (_connection != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            _connection = new SqliteConnection($"Data Source={_filePath};Cache=Shared");
            _connection.Open();

            using (var pragma = _connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
                pragma.ExecuteNonQuery();
            }

            EnsureSchema();
        }

        private void EnsureSchema()
        {
            int version;
            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version;";
                version = Convert.ToInt32(cmd.ExecuteScalar());
            }

            if (version == 0) MigrateTo1();
            // Future: if (version < 2) MigrateTo2(); etc.
        }

        private void MigrateTo1()
        {
            using var tx = _connection!.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                CREATE TABLE auth (
                  id                INTEGER PRIMARY KEY CHECK (id = 1),
                  token             TEXT    NOT NULL,
                  player_account_id TEXT    NOT NULL,
                  player_username   TEXT    NOT NULL,
                  issued_at_utc     TEXT    NOT NULL
                );
                CREATE TABLE player_observation (
                  id                INTEGER PRIMARY KEY CHECK (id = 1),
                  player_account_id TEXT    NOT NULL,
                  player_username   TEXT    NOT NULL,
                  observed_at_utc   TEXT    NOT NULL
                );
                PRAGMA user_version = 1;
            ";
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        public void Dispose()
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
