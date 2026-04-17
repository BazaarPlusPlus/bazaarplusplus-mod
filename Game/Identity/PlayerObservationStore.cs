#nullable enable
using System;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.Identity;

public sealed class PlayerObservationStore
{
    private readonly IdentityDatabase _database;

    public PlayerObservationStore(IdentityDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public bool TryLoad(out PlayerObservationRecord? record)
    {
        using var cmd = _database.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT player_account_id, player_username, observed_at_utc FROM player_observation WHERE id = 1";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            record = null;
            return false;
        }
        record = new PlayerObservationRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2)
        );
        return true;
    }

    public void Save(PlayerObservationRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        using var cmd = _database.Connection.CreateCommand();
        cmd.CommandText =
            @"
            INSERT INTO player_observation (id, player_account_id, player_username, observed_at_utc)
            VALUES (1, $p, $u, $t)
            ON CONFLICT(id) DO UPDATE SET
              player_account_id = excluded.player_account_id,
              player_username   = excluded.player_username,
              observed_at_utc   = excluded.observed_at_utc";
        cmd.Parameters.AddWithValue("$p", record.PlayerAccountId);
        cmd.Parameters.AddWithValue("$u", record.PlayerUsername);
        cmd.Parameters.AddWithValue("$t", record.ObservedAtUtc);
        cmd.ExecuteNonQuery();
    }
}
