using System;
using System.IO;
using BazaarPlusPlus.Game.Identity;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        RunTest("AuthStore_TryLoad_WhenEmpty", AuthStore_TryLoad_WhenEmpty);
        RunTest("AuthStore_Upsert_WritesJson", AuthStore_Upsert_WritesJson);
        RunTest("AuthStore_Delete_ClearsJsonAndLegacyFiles", AuthStore_Delete_ClearsJsonAndLegacyFiles);
        RunTest(
            "PlayerObservationStore_TryLoad_WhenEmpty",
            PlayerObservationStore_TryLoad_WhenEmpty
        );
        RunTest("PlayerObservationStore_SaveThenLoad", PlayerObservationStore_SaveThenLoad);
        RunTest("PlayerObservationStore_Save_UpsertsJson", PlayerObservationStore_Save_UpsertsJson);

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

    private static string TempIdentityDirectory() =>
        Path.Combine(Path.GetTempPath(), $"identity-json-test-{Guid.NewGuid():N}");

    private static void WithTempIdentityDirectory(Action<string> body)
    {
        var directory = TempIdentityDirectory();
        try
        {
            body(directory);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void AuthStore_TryLoad_WhenEmpty()
    {
        WithTempIdentityDirectory(directory =>
        {
            var store = new AuthStore(directory);
            if (store.TryLoad(out _))
                throw new Exception("expected empty store to return false");
        });
    }

    private static void AuthStore_Upsert_WritesJson()
    {
        WithTempIdentityDirectory(directory =>
        {
            var store = new AuthStore(directory);
            var rec = new AuthRecord("tok_xyz", "player_123", "alice", "2026-04-17T00:00:00Z");
            store.Upsert(rec);

            var path = Path.Combine(directory, "auth.v1.json");
            var json = File.ReadAllText(path);
            if (!json.Contains("\"token\":\"tok_xyz\""))
                throw new Exception("auth JSON token missing");

            if (!store.TryLoad(out var loaded))
                throw new Exception("load returned false");
            if (loaded!.Token != "tok_xyz")
                throw new Exception("token mismatch");
            if (loaded.PlayerAccountId != "player_123")
                throw new Exception("account id mismatch");
        });
    }

    private static void AuthStore_Delete_ClearsJsonAndLegacyFiles()
    {
        WithTempIdentityDirectory(directory =>
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "identity.db"), "legacy");
            File.WriteAllText(Path.Combine(directory, "identity.db-wal"), "legacy");
            File.WriteAllText(Path.Combine(directory, "identity.db-shm"), "legacy");

            var store = new AuthStore(directory);
            store.Upsert(new AuthRecord("t", "p", "u", "2026-04-17T00:00:00Z"));
            store.Delete();

            if (store.TryLoad(out _))
                throw new Exception("expected empty after delete");
            if (File.Exists(Path.Combine(directory, "auth.v1.json")))
                throw new Exception("auth JSON should be deleted");
            if (File.Exists(Path.Combine(directory, "identity.db")))
                throw new Exception("legacy identity.db should be deleted");
            if (File.Exists(Path.Combine(directory, "identity.db-wal")))
                throw new Exception("legacy identity.db-wal should be deleted");
            if (File.Exists(Path.Combine(directory, "identity.db-shm")))
                throw new Exception("legacy identity.db-shm should be deleted");
        });
    }

    private static void PlayerObservationStore_TryLoad_WhenEmpty()
    {
        WithTempIdentityDirectory(directory =>
        {
            var store = new PlayerObservationStore(directory);
            if (store.TryLoad(out _))
                throw new Exception("expected empty observation store");
        });
    }

    private static void PlayerObservationStore_SaveThenLoad()
    {
        WithTempIdentityDirectory(directory =>
        {
            var store = new PlayerObservationStore(directory);
            var record = new PlayerObservationRecord("player_abc", "bob", "2026-04-17T10:00:00Z");
            store.Save(record);

            var path = Path.Combine(directory, "observation.v1.json");
            var json = File.ReadAllText(path);
            if (!json.Contains("\"player_account_id\":\"player_abc\""))
                throw new Exception("observation JSON account id missing");

            if (!store.TryLoad(out var loaded))
                throw new Exception("load returned false");
            if (loaded!.PlayerAccountId != "player_abc")
                throw new Exception("account id mismatch");
            if (loaded.PlayerUsername != "bob")
                throw new Exception("username mismatch");
            if (loaded.ObservedAtUtc != "2026-04-17T10:00:00Z")
                throw new Exception("timestamp mismatch");
        });
    }

    private static void PlayerObservationStore_Save_UpsertsJson()
    {
        WithTempIdentityDirectory(directory =>
        {
            var store = new PlayerObservationStore(directory);
            store.Save(new PlayerObservationRecord("p1", "u1", "2026-04-17T10:00:00Z"));
            store.Save(new PlayerObservationRecord("p2", "u2", "2026-04-17T11:00:00Z"));

            if (!store.TryLoad(out var loaded))
                throw new Exception("load returned false");
            if (loaded!.PlayerAccountId != "p2")
                throw new Exception("expected upsert to most recent");
            if (loaded.PlayerUsername != "u2")
                throw new Exception("username not updated");
        });
    }
}
