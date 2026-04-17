using System;
using System.IO;
using BazaarPlusPlus.Game.Identity;
using Microsoft.Data.Sqlite;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        RunTest("Open_CreatesSchema", Open_CreatesSchema);
        RunTest("Open_IsIdempotent", Open_IsIdempotent);
        RunTest("AuthStore_TryLoad_WhenEmpty", AuthStore_TryLoad_WhenEmpty);
        RunTest("AuthStore_Upsert_ThenLoad", AuthStore_Upsert_ThenLoad);
        RunTest("AuthStore_Delete_ClearsRow", AuthStore_Delete_ClearsRow);
        RunTest(
            "PlayerObservationStore_TryLoad_WhenEmpty",
            PlayerObservationStore_TryLoad_WhenEmpty
        );
        RunTest("PlayerObservationStore_SaveThenLoad", PlayerObservationStore_SaveThenLoad);
        RunTest("PlayerObservationStore_Save_Upserts", PlayerObservationStore_Save_Upserts);

        if (_failures > 0)
            Environment.Exit(1);
        Console.WriteLine("OK");
    }

    private static void RunTest(string name, Action body)
    {
        try
        {
            body();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}: {ex}");
        }
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"identity-test-{Guid.NewGuid():N}.db");

    private static void Open_CreatesSchema()
    {
        var path = TempDbPath();
        try
        {
            using var db = new IdentityDatabase(path);
            db.Open();
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            var names = new System.Collections.Generic.List<string>();
            while (reader.Read())
                names.Add(reader.GetString(0));
            if (!names.Contains("auth"))
                throw new Exception("auth table missing");
            if (!names.Contains("player_observation"))
                throw new Exception("player_observation missing");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void Open_IsIdempotent()
    {
        var path = TempDbPath();
        try
        {
            using (var db1 = new IdentityDatabase(path))
            {
                db1.Open();
            }
            using (var db2 = new IdentityDatabase(path))
            {
                db2.Open();
            } // should not throw
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void AuthStore_TryLoad_WhenEmpty()
    {
        var path = TempDbPath();
        try
        {
            using var db = new IdentityDatabase(path);
            db.Open();
            var store = new AuthStore(db);
            if (store.TryLoad(out _))
                throw new Exception("expected empty store to return false");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void AuthStore_Upsert_ThenLoad()
    {
        var path = TempDbPath();
        try
        {
            using var db = new IdentityDatabase(path);
            db.Open();
            var store = new AuthStore(db);
            var rec = new AuthRecord("tok_xyz", "player_123", "alice", "2026-04-17T00:00:00Z");
            store.Upsert(rec);
            if (!store.TryLoad(out var loaded))
                throw new Exception("load returned false");
            if (loaded!.Token != "tok_xyz")
                throw new Exception("token mismatch");
            if (loaded.PlayerAccountId != "player_123")
                throw new Exception("account id mismatch");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void AuthStore_Delete_ClearsRow()
    {
        var path = TempDbPath();
        try
        {
            using var db = new IdentityDatabase(path);
            db.Open();
            var store = new AuthStore(db);
            store.Upsert(new AuthRecord("t", "p", "u", "2026-04-17T00:00:00Z"));
            store.Delete();
            if (store.TryLoad(out _))
                throw new Exception("expected empty after delete");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void PlayerObservationStore_TryLoad_WhenEmpty()
    {
        var path = TempDbPath();
        try
        {
            using var db = new IdentityDatabase(path);
            db.Open();
            var store = new PlayerObservationStore(db);
            if (store.TryLoad(out _))
                throw new Exception("expected empty observation store");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void PlayerObservationStore_SaveThenLoad()
    {
        var path = TempDbPath();
        try
        {
            using var db = new IdentityDatabase(path);
            db.Open();
            var store = new PlayerObservationStore(db);
            var record = new PlayerObservationRecord("player_abc", "bob", "2026-04-17T10:00:00Z");
            store.Save(record);
            if (!store.TryLoad(out var loaded))
                throw new Exception("load returned false");
            if (loaded!.PlayerAccountId != "player_abc")
                throw new Exception("account id mismatch");
            if (loaded.PlayerUsername != "bob")
                throw new Exception("username mismatch");
            if (loaded.ObservedAtUtc != "2026-04-17T10:00:00Z")
                throw new Exception("timestamp mismatch");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void PlayerObservationStore_Save_Upserts()
    {
        var path = TempDbPath();
        try
        {
            using var db = new IdentityDatabase(path);
            db.Open();
            var store = new PlayerObservationStore(db);
            store.Save(new PlayerObservationRecord("p1", "u1", "2026-04-17T10:00:00Z"));
            store.Save(new PlayerObservationRecord("p2", "u2", "2026-04-17T11:00:00Z"));
            if (!store.TryLoad(out var loaded))
                throw new Exception("load returned false");
            if (loaded!.PlayerAccountId != "p2")
                throw new Exception("expected upsert to most recent");
            if (loaded.PlayerUsername != "u2")
                throw new Exception("username not updated");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
