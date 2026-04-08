#nullable enable
using System.Reflection;
using Microsoft.Data.Sqlite;

var recordType = RequireType("BazaarPlusPlus.Game.Screenshots.RunScreenshotRecord");
var sourceType = RequireType("BazaarPlusPlus.Game.Screenshots.RunScreenshotCaptureSource");
var storeType = RequireType("BazaarPlusPlus.Game.Screenshots.Persistence.RunScreenshotSqliteStore");

var ctor = storeType.GetConstructor([typeof(string)]);
Assert(ctor != null, "RunScreenshotSqliteStore should expose a constructor taking the database path.");

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-run-screenshot-sqlite-store-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "run-screenshots.db");

try
{
    var store = ctor!.Invoke([dbPath]);

    var saveMethod = storeType.GetMethod("Save", BindingFlags.Public | BindingFlags.Instance);
    Assert(saveMethod != null, "RunScreenshotSqliteStore should expose Save.");

    var localCapturedAt = new DateTimeOffset(2026, 4, 8, 21, 30, 15, TimeSpan.FromHours(8));
    var utcCapturedAt = localCapturedAt.ToUniversalTime();

    saveMethod!.Invoke(
        store,
        [
            CreateRecord(
                recordType,
                sourceType,
                screenshotId: "shot-manual-001",
                runId: "run-001",
                battleId: null,
                captureSource: "ManualF9",
                isPrimary: false,
                relativePath: Path.Combine("2026-04-08", "run-001-manual_f9-213015000-shot-manual-001.png"),
                localCapturedAt,
                utcCapturedAt,
                day: 5,
                playerRank: "Gold 2",
                playerRating: 1420,
                victoriesAtCapture: 4
            ),
        ]
    );

    saveMethod.Invoke(
        store,
        [
            CreateRecord(
                recordType,
                sourceType,
                screenshotId: "shot-primary-001",
                runId: "run-001",
                battleId: null,
                captureSource: "EndOfRunAuto",
                isPrimary: true,
                relativePath: Path.Combine("2026-04-08", "run-001-end_of_run_auto-213015000-shot-primary-001.png"),
                localCapturedAt.AddSeconds(10),
                utcCapturedAt.AddSeconds(10),
                day: 10,
                playerRank: "Legendary",
                playerRating: 1533,
                victoriesAtCapture: 10
            ),
        ]
    );

    saveMethod.Invoke(
        store,
        [
            CreateRecord(
                recordType,
                sourceType,
                screenshotId: "shot-battle-001",
                runId: "run-001",
                battleId: "battle-001",
                captureSource: "PvpBattleNextDay",
                isPrimary: false,
                relativePath: Path.Combine("2026-04-08", "run-001-pvp_battle_nextday-battle-001-213015000-shot-battle-001.png"),
                localCapturedAt.AddSeconds(20),
                utcCapturedAt.AddSeconds(20),
                day: 4,
                playerRank: "Gold 1",
                playerRating: 1468,
                victoriesAtCapture: 3
            ),
        ]
    );

    saveMethod.Invoke(
        store,
        [
            CreateRecord(
                recordType,
                sourceType,
                screenshotId: "shot-dock-camera-001",
                runId: "run-001",
                battleId: null,
                captureSource: "SettingsDockCameraButton",
                isPrimary: false,
                relativePath: Path.Combine("2026-04-08", "run-001-settings_dock_camera_button-213015000-shot-dock-camera-001.png"),
                localCapturedAt.AddSeconds(40),
                utcCapturedAt.AddSeconds(40),
                day: 6,
                playerRank: "Gold 1",
                playerRating: 1450,
                victoriesAtCapture: 5
            ),
        ]
    );

    Assert(File.Exists(dbPath), "RunScreenshotSqliteStore should initialize the SQLite database file.");

    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    Assert(
        CountRows(connection, "run_screenshots") == 4,
        "run_screenshots should persist all inserted screenshot rows."
    );
    Assert(
        GetString(
            connection,
            "SELECT capture_source FROM run_screenshots WHERE screenshot_id = $id;",
            "shot-primary-001"
        ) == "end_of_run_auto",
        "run_screenshots should serialize capture_source in snake_case."
    );
    Assert(
        GetInt64(
            connection,
            "SELECT is_primary FROM run_screenshots WHERE screenshot_id = $id;",
            "shot-primary-001"
        ) == 1,
        "run_screenshots should mark end-of-run primary screenshots."
    );
    Assert(
        GetString(
            connection,
            "SELECT battle_id FROM run_screenshots WHERE screenshot_id = $id;",
            "shot-battle-001"
        ) == "battle-001",
        "run_screenshots should associate battle screenshots with battle ids."
    );
    Assert(
        GetInt64(
            connection,
            "SELECT victories_at_capture FROM run_screenshots WHERE screenshot_id = $id;",
            "shot-battle-001"
        ) == 3,
        "run_screenshots should persist victories_at_capture."
    );
    Assert(
        GetString(
            connection,
            "SELECT capture_source FROM run_screenshots WHERE screenshot_id = $id;",
            "shot-dock-camera-001"
        ) == "settings_dock_camera_button",
        "run_screenshots should serialize settings dock camera screenshots with a dedicated source."
    );

    ExpectSqliteConstraint(
        () =>
            saveMethod.Invoke(
                store,
                [
                    CreateRecord(
                        recordType,
                        sourceType,
                        screenshotId: "shot-battle-002",
                        runId: "run-001",
                        battleId: "battle-001",
                        captureSource: "PvpBattleNextDay",
                        isPrimary: false,
                        relativePath: Path.Combine("2026-04-08", "duplicate-battle.png"),
                        localCapturedAt.AddSeconds(25),
                        utcCapturedAt.AddSeconds(25),
                        day: 4,
                        playerRank: "Gold 1",
                        playerRating: 1468,
                        victoriesAtCapture: 3
                    ),
                ]
            ),
        "battle screenshots should be unique per battle id."
    );

    ExpectSqliteConstraint(
        () =>
            saveMethod.Invoke(
                store,
                [
                    CreateRecord(
                        recordType,
                        sourceType,
                        screenshotId: "shot-primary-002",
                        runId: "run-001",
                        battleId: null,
                        captureSource: "EndOfRunAuto",
                        isPrimary: true,
                        relativePath: Path.Combine("2026-04-08", "duplicate-primary.png"),
                        localCapturedAt.AddSeconds(30),
                        utcCapturedAt.AddSeconds(30),
                        day: 10,
                        playerRank: "Legendary",
                        playerRating: 1539,
                        victoriesAtCapture: 10
                    ),
                ]
            ),
        "only one primary screenshot should exist per run."
    );
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch
    {
    }
}

Console.WriteLine("Run screenshot SQLite store checks passed.");

static object CreateRecord(
    Type recordType,
    Type sourceType,
    string screenshotId,
    string? runId,
    string? battleId,
    string captureSource,
    bool isPrimary,
    string relativePath,
    DateTimeOffset localCapturedAt,
    DateTimeOffset utcCapturedAt,
    int? day,
    string? playerRank,
    int? playerRating,
    int? victoriesAtCapture
)
{
    var record =
        Activator.CreateInstance(recordType)
        ?? throw new InvalidOperationException("RunScreenshotRecord should be constructible.");

    SetProperty(recordType, record, "ScreenshotId", screenshotId);
    SetProperty(recordType, record, "RunId", runId);
    SetProperty(recordType, record, "BattleId", battleId);
    SetProperty(recordType, record, "CaptureSource", Enum.Parse(sourceType, captureSource));
    SetProperty(recordType, record, "IsPrimary", isPrimary);
    SetProperty(recordType, record, "ImageRelativePath", relativePath);
    SetProperty(recordType, record, "CapturedAtLocal", localCapturedAt);
    SetProperty(recordType, record, "CapturedAtUtc", utcCapturedAt);
    SetProperty(recordType, record, "Day", day);
    SetProperty(recordType, record, "PlayerRank", playerRank);
    SetProperty(recordType, record, "PlayerRating", playerRating);
    SetProperty(recordType, record, "VictoriesAtCapture", victoriesAtCapture);
    return record;
}

static void SetProperty(Type type, object instance, string name, object? value)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    property.SetValue(instance, value);
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

static void ExpectSqliteConstraint(Action action, string message)
{
    try
    {
        action();
    }
    catch (TargetInvocationException ex) when (ex.InnerException is SqliteException)
    {
        return;
    }
    catch (SqliteException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
