#nullable enable
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.Sqlite;

var schemaType = RequireType("BazaarPlusPlus.Storage.RunLog.RunLogSchema");
var storeType = RequireType("BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotUploadStore");
var uploadImageType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotUploadImage"
);
var uploadLimitsType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotUploadLimits"
);
var imagePreparerType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotImagePreparer"
);
var prepareDelegateType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.PrepareSnapshotImage"
);

var ctor = storeType.GetConstructor([typeof(string), typeof(string)]);
Assert(
    ctor != null,
    "BazaarDbSnapshotUploadStore should expose a constructor taking the database path and screenshots directory."
);
var testCtor = storeType.GetConstructor(
    BindingFlags.NonPublic | BindingFlags.Instance,
    binder: null,
    [typeof(string), typeof(string), prepareDelegateType],
    modifiers: null
);
Assert(
    testCtor != null,
    "BazaarDbSnapshotUploadStore should expose an internal constructor with an image preparer delegate for tests."
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
        "BazaarPlusPlus.Storage.RunScreenshot.RunScreenshotSqliteStore"
    );
    Activator.CreateInstance(screenshotStoreType, dbPath);

    SeedRunSnapshotRow(dbPath, "shot-A", capturedAtUtc: "2026-04-08T20:30:25.000Z");
    SeedRunSnapshotRow(dbPath, "shot-B", capturedAtUtc: "2026-04-08T20:31:25.000Z");
    WriteScreenshotFile(screenshotsDir, "shot-A", [1]);
    WriteScreenshotFile(screenshotsDir, "shot-B", [1]);
    SeedRunSnapshotRow(
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
    Assert(ensureBackfilled != null, "BazaarDbSnapshotUploadStore should expose EnsureBackfilled.");
    ensureBackfilled!.Invoke(store, []);

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            CountRows(connection, "bazaardb_snapshot_uploads") == 2,
            "EnsureBackfilled should insert exactly one pending row per end-of-run screenshot."
        );
        Assert(
            GetString(
                connection,
                "SELECT status FROM bazaardb_snapshot_uploads WHERE snapshot_id = $id;",
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
            CountRows(connection, "bazaardb_snapshot_uploads") == 2,
            "EnsureBackfilled should be idempotent (INSERT OR IGNORE)."
        );
    }

    var getPending = storeType.GetMethod(
        "GetPendingSnapshotIds",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(getPending != null, "BazaarDbSnapshotUploadStore should expose GetPendingSnapshotIds.");
    var pending = (System.Collections.Generic.IReadOnlyList<string>)
        getPending!.Invoke(store, [10])!;
    Assert(pending.Count == 2, "GetPendingSnapshotIds should return both backfilled rows.");
    Assert(pending[0] == "shot-A", "Pending ordering should be by captured_at_utc ASC.");
    Assert(pending[1] == "shot-B", "Pending ordering should be by captured_at_utc ASC.");

    var pendingLimited = (System.Collections.Generic.IReadOnlyList<string>)
        getPending.Invoke(store, [1])!;
    Assert(pendingLimited.Count == 1, "GetPendingSnapshotIds should respect the limit argument.");

    var tryBuildSnapshot = storeType.GetMethod(
        "TryBuildSnapshot",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(tryBuildSnapshot != null, "BazaarDbSnapshotUploadStore should expose TryBuildSnapshot.");
    var maxUploadImageBytes = GetStaticInt(uploadLimitsType, "MaxUploadImageBytes");
    var originalPngRecord = tryBuildSnapshot!.Invoke(
        store,
        ["shot-A", "acct-1", CancellationToken.None]
    );
    Assert(
        originalPngRecord != null,
        "A small original PNG should build through the production preparer fast path."
    );

    var invalidOversizedPath = Path.Combine(tempRoot, "invalid-oversized.png");
    File.WriteAllBytes(invalidOversizedPath, new byte[maxUploadImageBytes + 1]);
    var imagePreparer = Activator.CreateInstance(
        imagePreparerType,
        Path.Combine(tempRoot, "upload-cache")
    );
    Assert(imagePreparer != null, "BazaarDbSnapshotImagePreparer should be constructible.");
    var prepareImage = imagePreparerType.GetMethod(
        "Prepare",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(prepareImage != null, "BazaarDbSnapshotImagePreparer should expose Prepare.");
    Assert(
        prepareImage!.Invoke(
            imagePreparer,
            ["bad-image", invalidOversizedPath, CancellationToken.None]
        ) == null,
        "An oversized image that cannot be decoded should return null instead of throwing."
    );

    var base64Length = uploadLimitsType.GetMethod(
        "Base64Length",
        BindingFlags.Public | BindingFlags.Static
    );
    Assert(base64Length != null, "BazaarDbSnapshotUploadLimits should expose Base64Length.");

    var exactLimitBytes = new byte[maxUploadImageBytes];
    exactLimitBytes[0] = 1;
    FakePreparedImage.NextImage = CreateUploadImage(
        uploadImageType,
        exactLimitBytes,
        "image/png",
        "fake-cache/shot-A.png"
    );
    var fakeStore = testCtor!.Invoke([
        dbPath,
        screenshotsDir,
        CreatePrepareSnapshotImageDelegate(prepareDelegateType),
    ]);
    var pngRecord = tryBuildSnapshot!.Invoke(
        fakeStore,
        ["shot-A", "acct-1", CancellationToken.None]
    );
    Assert(pngRecord != null, "A 2 MiB prepared PNG should build an upload record.");
    var pngImage = GetProperty(GetProperty(pngRecord!, "Payload")!, "Image")!;
    var pngBase64 = (string)GetProperty(pngImage, "DataBase64")!;
    Assert(
        (string)GetProperty(pngImage, "ContentType")! == "image/png",
        "Prepared PNG upload should keep image/png content_type."
    );
    Assert(
        pngBase64.Length == (int)base64Length!.Invoke(null, [exactLimitBytes.Length])!,
        "Prepared image base64 length should match the limit helper."
    );

    FakePreparedImage.NextImage = CreateUploadImage(
        uploadImageType,
        [1, 2, 3, 4],
        "image/jpeg",
        "fake-cache/shot-B.jpg"
    );
    var jpegStore = testCtor.Invoke([
        dbPath,
        screenshotsDir,
        CreatePrepareSnapshotImageDelegate(prepareDelegateType),
    ]);
    var jpegRecord = tryBuildSnapshot.Invoke(
        jpegStore,
        ["shot-B", "acct-1", CancellationToken.None]
    );
    Assert(jpegRecord != null, "A prepared JPEG should build an upload record.");
    var jpegImage = GetProperty(GetProperty(jpegRecord!, "Payload")!, "Image")!;
    Assert(
        (string)GetProperty(jpegImage, "ContentType")! == "image/jpeg",
        "Prepared JPEG upload should set image/jpeg content_type."
    );

    SeedRunSnapshotRow(dbPath, "shot-too-large", capturedAtUtc: "2026-04-08T20:33:25.000Z");
    WriteScreenshotFile(screenshotsDir, "shot-too-large", [1]);
    FakePreparedImage.NextImage = null;
    var nullStore = testCtor.Invoke([
        dbPath,
        screenshotsDir,
        CreatePrepareSnapshotImageDelegate(prepareDelegateType),
    ]);
    var nullRecord = tryBuildSnapshot.Invoke(
        nullStore,
        ["shot-too-large", "acct-1", CancellationToken.None]
    );
    Assert(nullRecord == null, "A null prepared image should not build an upload record.");
    Assert(
        (string?)GetProperty(nullStore, "LastBuildFailureReason") == "image_too_large_after_resize",
        "A null prepared image should record image_too_large_after_resize."
    );

    // MarkUploaded
    var markUploaded = storeType.GetMethod(
        "MarkUploaded",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(markUploaded != null, "BazaarDbSnapshotUploadStore should expose MarkUploaded.");
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
                "SELECT status FROM bazaardb_snapshot_uploads WHERE snapshot_id = $id;",
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
        "BazaarDbSnapshotUploadStore should expose MarkTransientFailure."
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
                "SELECT status FROM bazaardb_snapshot_uploads WHERE snapshot_id = $id;",
                "shot-B"
            ) == "pending",
            "MarkTransientFailure should keep status as 'pending'."
        );
        Assert(
            GetInt64(
                connection,
                "SELECT attempts FROM bazaardb_snapshot_uploads WHERE snapshot_id = $id;",
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
        "BazaarDbSnapshotUploadStore should expose MarkPermanentFailure."
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
                "SELECT status FROM bazaardb_snapshot_uploads WHERE snapshot_id = $id;",
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

Console.WriteLine("BazaarDbSnapshotUploadStore checks passed.");

static void SeedRunSnapshotRow(
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

static void WriteScreenshotFile(string screenshotsDir, string screenshotId, byte[] bytes)
{
    var path = Path.Combine(screenshotsDir, "test", $"{screenshotId}.png");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, bytes);
}

static object CreateUploadImage(
    Type uploadImageType,
    byte[] bytes,
    string contentType,
    string sourcePath
)
{
    var image =
        Activator.CreateInstance(uploadImageType)
        ?? throw new InvalidOperationException("Upload image should be constructible.");
    SetProperty(image, "Bytes", bytes);
    SetProperty(image, "ContentType", contentType);
    SetProperty(image, "SourcePath", sourcePath);
    return image;
}

static Delegate CreatePrepareSnapshotImageDelegate(Type delegateType)
{
    var invoke =
        delegateType.GetMethod("Invoke")
        ?? throw new InvalidOperationException("Delegate Invoke not found.");
    var parametersInfo = invoke.GetParameters();
    var parameters = new ParameterExpression[parametersInfo.Length];
    for (var index = 0; index < parameters.Length; index++)
        parameters[index] = Expression.Parameter(
            parametersInfo[index].ParameterType,
            parametersInfo[index].Name
        );

    var nextImageProperty = typeof(FakePreparedImage).GetProperty(
        nameof(FakePreparedImage.NextImage)
    )!;
    var body = Expression.Convert(Expression.Property(null, nextImageProperty), invoke.ReturnType);
    return Expression.Lambda(delegateType, body, parameters).Compile();
}

static int GetStaticInt(Type type, string name)
{
    return (int)(
        type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
        ?? throw new InvalidOperationException($"Static int not found: {name}")
    );
}

static object? GetProperty(object instance, string name)
{
    return instance
        .GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?.GetValue(instance);
}

static void SetProperty(object instance, string name, object? value)
{
    instance
        .GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?.SetValue(instance, value);
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

internal static class FakePreparedImage
{
    public static object? NextImage { get; set; }
}
