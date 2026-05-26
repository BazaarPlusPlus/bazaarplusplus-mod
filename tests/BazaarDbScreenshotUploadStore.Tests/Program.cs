#nullable enable
using System.Reflection;
using Microsoft.Data.Sqlite;

var schemaType = RequireType(
    "BazaarPlusPlus.Storage.RunLog.RunLogSchema"
);
var storeType = RequireType("BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbScreenshotUploadStore");

var ctor = storeType.GetConstructor([typeof(string), typeof(string)]);
Assert(
    ctor != null,
    "BazaarDbScreenshotUploadStore should expose a constructor taking the database path and screenshots directory."
);

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-bazaardb-screenshot-upload-store-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "test.db");
var screenshotsDir = Path.Combine(tempRoot, "screenshots");
Directory.CreateDirectory(screenshotsDir);

try
{
    // Bootstrap the schema by touching the existing RunScreenshotSqliteStore (it inherits SqliteStoreBase
    // which runs RunLogSchema.EnsureInitialized in the ctor).
    var screenshotStoreType = RequireType(
        "BazaarPlusPlus.Game.Screenshots.Persistence.RunScreenshotSqliteStore"
    );
    Activator.CreateInstance(screenshotStoreType, dbPath);

    SeedRunScreenshotRow(dbPath, "shot-A", capturedAtUtc: "2026-04-08T20:30:25.000Z");
    SeedRunScreenshotRow(dbPath, "shot-B", capturedAtUtc: "2026-04-08T20:31:25.000Z");
    SeedRunScreenshotRow(
        dbPath,
        "shot-other-source",
        capturedAtUtc: "2026-04-08T20:32:25.000Z",
        captureSource: "manual"
    );

    var store = ctor!.Invoke([dbPath, screenshotsDir]);
    var ensureBackfilled = storeType.GetMethod(
        "EnsureBackfilled",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(ensureBackfilled != null, "BazaarDbScreenshotUploadStore should expose EnsureBackfilled.");
    ensureBackfilled!.Invoke(store, []);

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            CountRows(connection, "bazaardb_screenshot_uploads") == 2,
            "EnsureBackfilled should insert exactly one pending row per end-of-run screenshot."
        );
        Assert(
            GetString(
                connection,
                "SELECT status FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;",
                "shot-A"
            ) == "pending",
            "Backfilled rows should start in 'pending' status."
        );
    }

    // Idempotency: calling EnsureBackfilled twice must not duplicate.
    ensureBackfilled.Invoke(store, []);
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            CountRows(connection, "bazaardb_screenshot_uploads") == 2,
            "EnsureBackfilled should be idempotent (INSERT OR IGNORE)."
        );
    }

    var getPending = storeType.GetMethod(
        "GetPendingScreenshotIds",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(getPending != null, "BazaarDbScreenshotUploadStore should expose GetPendingScreenshotIds.");
    var pending = (System.Collections.Generic.IReadOnlyList<string>)
        getPending!.Invoke(store, [10])!;
    Assert(pending.Count == 2, "GetPendingScreenshotIds should return both backfilled rows.");
    Assert(pending[0] == "shot-A", "Pending ordering should be by captured_at_utc ASC.");
    Assert(pending[1] == "shot-B", "Pending ordering should be by captured_at_utc ASC.");

    var pendingLimited = (System.Collections.Generic.IReadOnlyList<string>)
        getPending.Invoke(store, [1])!;
    Assert(pendingLimited.Count == 1, "GetPendingScreenshotIds should respect the limit argument.");

    // MarkUploaded
    var markUploaded = storeType.GetMethod(
        "MarkUploaded",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(markUploaded != null, "BazaarDbScreenshotUploadStore should expose MarkUploaded.");
    markUploaded!.Invoke(
        store,
        ["shot-A", DateTimeOffset.Parse("2026-04-08T21:00:00.000Z").UtcDateTime]
    );
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetString(
                connection,
                "SELECT status FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;",
                "shot-A"
            ) == "uploaded",
            "MarkUploaded should set status to 'uploaded'."
        );
    }

    // MarkTransientFailure
    var markTransient = storeType.GetMethod(
        "MarkTransientFailure",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(
        markTransient != null,
        "BazaarDbScreenshotUploadStore should expose MarkTransientFailure."
    );
    markTransient!.Invoke(
        store,
        ["shot-B", DateTimeOffset.Parse("2026-04-08T21:00:00.000Z").UtcDateTime, "net_timeout"]
    );
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetString(
                connection,
                "SELECT status FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;",
                "shot-B"
            ) == "pending",
            "MarkTransientFailure should keep status as 'pending'."
        );
        Assert(
            GetInt64(
                connection,
                "SELECT attempts FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;",
                "shot-B"
            ) == 1,
            "MarkTransientFailure should increment attempts."
        );
    }

    // MarkPermanentFailure
    var markPermanent = storeType.GetMethod(
        "MarkPermanentFailure",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(
        markPermanent != null,
        "BazaarDbScreenshotUploadStore should expose MarkPermanentFailure."
    );
    markPermanent!.Invoke(
        store,
        ["shot-B", DateTimeOffset.Parse("2026-04-08T21:00:00.000Z").UtcDateTime, "schema_mismatch"]
    );
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetString(
                connection,
                "SELECT status FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;",
                "shot-B"
            ) == "permanent_failure",
            "MarkPermanentFailure should flip status to 'permanent_failure'."
        );
    }

    // GetPending after marking: only pending rows should be returned.
    var pendingAfter = (System.Collections.Generic.IReadOnlyList<string>)
        getPending.Invoke(store, [10])!;
    Assert(pendingAfter.Count == 0, "After marks, no rows should remain pending.");
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch { }
}

Console.WriteLine("BazaarDbScreenshotUploadStore checks passed.");

static void SeedRunScreenshotRow(
    string dbPath,
    string screenshotId,
    string capturedAtUtc,
    string captureSource = "end_of_run_auto"
)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
        INSERT INTO run_screenshots (
            screenshot_id, run_id, hero_name, battle_id, capture_source, is_primary,
            image_relative_path, captured_at_local, captured_at_utc, day, player_rank,
            player_rating, player_position, victories_at_capture
        ) VALUES (
            $id, NULL, NULL, NULL, $src, 0,
            'test/' || $id || '.png', $ts, $ts, NULL, NULL, NULL, NULL, NULL
        );
        """;
    cmd.Parameters.AddWithValue("$id", screenshotId);
    cmd.Parameters.AddWithValue("$src", captureSource);
    cmd.Parameters.AddWithValue("$ts", capturedAtUtc);
    cmd.ExecuteNonQuery();
}

static long CountRows(SqliteConnection connection, string tableName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
    return (long)(command.ExecuteScalar() ?? 0L);
}

static string GetString(SqliteConnection connection, string sql, string id)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$id", id);
    return (string)(command.ExecuteScalar() ?? throw new InvalidOperationException(sql));
}

static long GetInt64(SqliteConnection connection, string sql, string id)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$id", id);
    return (long)(command.ExecuteScalar() ?? throw new InvalidOperationException(sql));
}

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus.Storage")
        ?? Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
