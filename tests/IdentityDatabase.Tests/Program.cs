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

        if (_failures > 0) Environment.Exit(1);
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
            while (reader.Read()) names.Add(reader.GetString(0));
            if (!names.Contains("auth")) throw new Exception("auth table missing");
            if (!names.Contains("player_observation")) throw new Exception("player_observation missing");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void Open_IsIdempotent()
    {
        var path = TempDbPath();
        try
        {
            using (var db1 = new IdentityDatabase(path)) { db1.Open(); }
            using (var db2 = new IdentityDatabase(path)) { db2.Open(); } // should not throw
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
