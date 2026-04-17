#nullable enable
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.Identity
{
    public sealed class AuthStore
    {
        private readonly IdentityDatabase _database;

        public AuthStore(IdentityDatabase database) => _database = database;

        public bool TryLoad(out AuthRecord? record)
        {
            using var cmd = _database.Connection.CreateCommand();
            cmd.CommandText =
                "SELECT token, player_account_id, player_username, issued_at_utc FROM auth WHERE id = 1";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                record = null;
                return false;
            }
            record = new AuthRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)
            );
            return true;
        }

        public void Upsert(AuthRecord record)
        {
            using var cmd = _database.Connection.CreateCommand();
            cmd.CommandText =
                @"
                INSERT INTO auth (id, token, player_account_id, player_username, issued_at_utc)
                VALUES (1, $t, $p, $u, $i)
                ON CONFLICT(id) DO UPDATE SET
                  token = excluded.token,
                  player_account_id = excluded.player_account_id,
                  player_username = excluded.player_username,
                  issued_at_utc = excluded.issued_at_utc";
            cmd.Parameters.AddWithValue("$t", record.Token);
            cmd.Parameters.AddWithValue("$p", record.PlayerAccountId);
            cmd.Parameters.AddWithValue("$u", record.PlayerUsername);
            cmd.Parameters.AddWithValue("$i", record.IssuedAtUtc);
            cmd.ExecuteNonQuery();
        }

        public void Delete()
        {
            using var cmd = _database.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM auth WHERE id = 1";
            cmd.ExecuteNonQuery();
        }
    }
}
