#nullable enable
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.Data.Sqlite;

var serviceType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotUploadService"
);
var storeType = RequireType("BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotUploadStore");
var uploadImageType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotUploadImage"
);
var routesType = RequireModApiType("BazaarPlusPlus.ModApi.ModApiRoutes");
var prepareDelegateType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.PrepareSnapshotImage"
);

var ctor = serviceType.GetConstructor(
    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic,
    binder: null,
    [storeType, routesType, typeof(HttpClient), typeof(Func<string?>)],
    modifiers: null
);
Assert(
    ctor != null,
    "BazaarDbSnapshotUploadService should take (store, routes, httpClient, playerAccountIdResolver)."
);

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-bazaardb-screenshot-upload-service-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "test.db");
var screenshotsDir = Path.Combine(tempRoot, "screenshots");
Directory.CreateDirectory(screenshotsDir);

try
{
    var screenshotStoreType = RequireType(
        "BazaarPlusPlus.Storage.RunScreenshot.RunScreenshotSqliteStore"
    );
    Activator.CreateInstance(screenshotStoreType, dbPath);
    SeedRunSnapshotWithFile(
        dbPath,
        screenshotsDir,
        "shot-1",
        "2026-04-08_20-30-25-000_final_run-r1.png"
    );

    var fakeStoreCtor = storeType.GetConstructor(
        BindingFlags.NonPublic | BindingFlags.Instance,
        binder: null,
        [typeof(string), typeof(string), prepareDelegateType],
        modifiers: null
    );
    Assert(
        fakeStoreCtor != null,
        "BazaarDbSnapshotUploadStore should expose an internal constructor with an image preparer delegate for tests."
    );
    FakePreparedImage.NextImage = CreateUploadImage(
        uploadImageType,
        [0x89, 0x50, 0x4E, 0x47],
        "image/png",
        "fake-cache/snapshot.png"
    );
    var store = fakeStoreCtor!.Invoke([
        dbPath,
        screenshotsDir,
        CreatePrepareSnapshotImageDelegate(prepareDelegateType),
    ]);
    var ensureBackfilled = storeType.GetMethod("EnsureBackfilled")!;
    ensureBackfilled.Invoke(store, []);
    var tryBuildSnapshot = storeType.GetMethod("TryBuildSnapshot")!;
    var preflightRecord = tryBuildSnapshot.Invoke(store, ["shot-1", "acct-9"]);
    Assert(
        preflightRecord != null,
        $"Preflight snapshot build should succeed; failure={GetStoreBuildFailure(storeType, store)}."
    );

    var routes = routesType
        .GetMethod("TryCreate", BindingFlags.Public | BindingFlags.Static)!
        .Invoke(null, ["https://example.invalid"]);
    Assert(routes != null, "TryCreate should accept https://example.invalid.");

    // Test 1: happy path
    {
        var handler = new RecordingHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri?.AbsolutePath == "/health")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"status\":\"ok\",\"server_time_utc\":\"2026-06-03T00:00:00.000Z\"}"
                    ),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}"),
            };
        });
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            handler.Requests.Count == 2,
            $"Service should GET health then POST one upload request; got {handler.Requests.Count}."
        );
        Assert(
            handler.Requests[0].Method == HttpMethod.Get
                && handler.Requests[0].RequestUri?.AbsolutePath == "/health",
            "First request should be the health probe."
        );
        Assert(
            handler.Requests[1].Method == HttpMethod.Post
                && handler.Requests[1].RequestUri?.AbsolutePath == "/bazaardb/snapshots/shot-1",
            "Second request should POST to the snapshot id path."
        );
        Assert(
            handler.ContentTypes[1] == "application/json",
            "Snapshot upload should use application/json."
        );
        var uploadJson = handler.Bodies[1];
        Assert(
            uploadJson.Contains("\"schema_version\":2", StringComparison.Ordinal),
            "Snapshot upload DTO should use schema_version 2."
        );
        Assert(
            uploadJson.Contains("\"snapshot\":{\"id\":\"shot-1\"", StringComparison.Ordinal),
            "Snapshot upload DTO should include snapshot.id."
        );
        Assert(
            !uploadJson.Contains("\"screenshot_id\"", StringComparison.Ordinal),
            "Snapshot upload DTO should not emit old screenshot_id."
        );
        Assert(
            GetUploadStatus(dbPath, "shot-1") == "uploaded",
            "Happy-path row should be marked uploaded."
        );
        client.Dispose();
    }

    // Test 2: no pending rows should not probe health
    {
        var handler = new RecordingHandler(req => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            handler.Requests.Count == 0,
            "Service should not probe health when there are no pending uploads."
        );
        client.Dispose();
    }

    // Test 3: transient HTTP 503 keeps row pending and increments attempts
    SeedRunSnapshotWithFile(
        dbPath,
        screenshotsDir,
        "shot-2",
        "2026-04-08_20-31-25-000_final_run-r1.png"
    );
    ensureBackfilled.Invoke(store, []);
    {
        var handler = new RecordingHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri?.AbsolutePath == "/health")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"status\":\"ok\",\"server_time_utc\":\"2026-06-03T00:00:00.000Z\"}"
                    ),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("boom"),
            };
        });
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-2") == "pending",
            "Transient failure should leave row pending."
        );
        Assert(
            GetUploadAttempts(dbPath, "shot-2") == 1,
            "Transient failure should increment attempts."
        );
        Assert(handler.Requests.Count == 2, "Transient upload should GET health then POST once.");
        client.Dispose();
    }

    // Test 4: permanent HTTP 400 flips to permanent_failure
    {
        var handler = new RecordingHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri?.AbsolutePath == "/health")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"status\":\"ok\",\"server_time_utc\":\"2026-06-03T00:00:00.000Z\"}"
                    ),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"reason\":\"schema_mismatch\"}"),
            };
        });
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-2") == "permanent_failure",
            "HTTP 400 should be classified as a permanent failure."
        );
        Assert(handler.Requests.Count == 2, "Permanent upload should GET health then POST once.");
        client.Dispose();
    }

    // Test 5: missing image file → permanent_failure
    SeedRunSnapshotMissingFile(dbPath, "shot-3");
    ensureBackfilled.Invoke(store, []);
    {
        var handler = new RecordingHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\"}"),
        });
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-3") == "permanent_failure",
            "Missing image file should be a permanent failure."
        );
        Assert(
            handler.Requests.Count == 0,
            "Service should not POST when the image file is missing."
        );
        client.Dispose();
    }

    // Test 6: prepared image failure flips to permanent_failure without probing health
    SeedRunSnapshotWithFile(
        dbPath,
        screenshotsDir,
        "shot-image-too-large",
        "2026-04-08_20-32-00-000_final_run-r1.png"
    );
    FakePreparedImage.NextImage = null;
    var fakeStore = fakeStoreCtor!.Invoke([
        dbPath,
        screenshotsDir,
        CreatePrepareSnapshotImageDelegate(prepareDelegateType),
    ]);
    ensureBackfilled.Invoke(fakeStore, []);
    {
        var handler = new RecordingHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\"}"),
        });
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([fakeStore, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-image-too-large") == "permanent_failure",
            "Prepared image failure should be a permanent failure."
        );
        Assert(
            GetUploadLastError(dbPath, "shot-image-too-large") == "image_too_large_after_resize",
            "Prepared image failure should record image_too_large_after_resize."
        );
        Assert(
            handler.Requests.Count == 0,
            "Service should not probe health or POST when the upload image cannot be prepared."
        );
        client.Dispose();
    }
    FakePreparedImage.NextImage = CreateUploadImage(
        uploadImageType,
        [0x89, 0x50, 0x4E, 0x47],
        "image/png",
        "fake-cache/snapshot.png"
    );

    // Test 7: health failure leaves pending rows untouched for the next retry
    SeedRunSnapshotWithFile(
        dbPath,
        screenshotsDir,
        "shot-health-fail",
        "2026-04-08_20-32-25-000_final_run-r1.png"
    );
    ensureBackfilled.Invoke(store, []);
    {
        var handler = new RecordingHandler(req => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable
        ));
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-health-fail") == "pending",
            "Health failure should leave pending snapshots pending for retry."
        );
        Assert(
            GetUploadAttempts(dbPath, "shot-health-fail") == 0,
            "Health failure should not count as a screenshot upload attempt."
        );
        Assert(handler.Requests.Count == 1, "Health failure should not continue into upload POST.");
        client.Dispose();
    }

    // Test 8: missing account id should not probe health or count attempts
    SeedRunSnapshotWithFile(
        dbPath,
        screenshotsDir,
        "shot-no-account",
        "2026-04-08_20-33-25-000_final_run-r1.png"
    );
    ensureBackfilled.Invoke(store, []);
    {
        var handler = new RecordingHandler(req => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => " ")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-no-account") == "pending",
            "Missing account id should leave pending snapshots pending for retry."
        );
        Assert(
            GetUploadAttempts(dbPath, "shot-no-account") == 0,
            "Missing account id should not count as a screenshot upload attempt."
        );
        Assert(
            handler.Requests.Count == 0,
            "Service should not probe health before the player account id is available."
        );
        client.Dispose();
    }
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch { }
}

Console.WriteLine("BazaarDbSnapshotUploadService checks passed.");

static void SeedRunSnapshotWithFile(
    string dbPath,
    string screenshotsDir,
    string id,
    string relativePath
)
{
    var absolutePath = Path.Combine(screenshotsDir, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
    File.WriteAllBytes(absolutePath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
        INSERT INTO run_screenshots (
            screenshot_id, run_id, hero_name, battle_id, capture_source, is_primary,
            image_relative_path, captured_at_local, captured_at_utc, day, player_rank,
            player_rating, player_position, victories_at_capture
        ) VALUES (
            $id, NULL, 'TestHero', NULL, 'end_of_run_auto', 0,
            $path, '2026-04-08T20:30:25.000Z', '2026-04-08T20:30:25.000Z', 14, 'Diamond', 1942, 1, 10
        );
        """;
    cmd.Parameters.AddWithValue("$id", id);
    cmd.Parameters.AddWithValue("$path", relativePath);
    cmd.ExecuteNonQuery();
}

static void SeedRunSnapshotMissingFile(string dbPath, string id)
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
            $id, NULL, NULL, NULL, 'end_of_run_auto', 0,
            'does/not/exist.png', '2026-04-08T20:30:25.000Z', '2026-04-08T20:30:25.000Z', NULL, NULL, NULL, NULL, NULL
        );
        """;
    cmd.Parameters.AddWithValue("$id", id);
    cmd.ExecuteNonQuery();
}

static string GetUploadStatus(string dbPath, string id)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT status FROM bazaardb_snapshot_uploads WHERE snapshot_id = $id;";
    command.Parameters.AddWithValue("$id", id);
    return (string?)command.ExecuteScalar() ?? string.Empty;
}

static long GetUploadAttempts(string dbPath, string id)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT attempts FROM bazaardb_snapshot_uploads WHERE snapshot_id = $id;";
    command.Parameters.AddWithValue("$id", id);
    return (long)(command.ExecuteScalar() ?? 0L);
}

static string GetUploadLastError(string dbPath, string id)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
        "SELECT last_error FROM bazaardb_snapshot_uploads WHERE snapshot_id = $id;";
    command.Parameters.AddWithValue("$id", id);
    return (string?)command.ExecuteScalar() ?? string.Empty;
}

static string? GetStoreBuildFailure(Type storeType, object store)
{
    return (string?)
        storeType
            .GetProperty("LastBuildFailureReason", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(store);
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

static void SetProperty(object instance, string name, object? value)
{
    instance
        .GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?.SetValue(instance, value);
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

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus.Storage")
        ?? Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static Type RequireModApiType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus.ModApi")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string> Bodies { get; } = new();

    public List<string?> ContentTypes { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request);
        Bodies.Add(
            request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult()
                ?? string.Empty
        );
        ContentTypes.Add(request.Content?.Headers.ContentType?.MediaType);
        return Task.FromResult(_responder(request));
    }
}

internal static class FakePreparedImage
{
    public static object? NextImage { get; set; }
}
