#nullable enable
using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.Data.Sqlite;

var serviceType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbScreenshotUploadService"
);
var storeType = RequireType("BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbScreenshotUploadStore");
var routesType = RequireModApiType("BazaarPlusPlus.ModApi.ModApiRoutes");

var ctor = serviceType.GetConstructor(
    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic,
    binder: null,
    [storeType, routesType, typeof(HttpClient), typeof(Func<string?>)],
    modifiers: null
);
Assert(
    ctor != null,
    "BazaarDbScreenshotUploadService should take (store, routes, httpClient, playerAccountIdResolver)."
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
    SeedRunScreenshotWithFile(dbPath, screenshotsDir, "shot-1", "2026-04-08_20-30-25-000_final_run-r1.png");

    var storeCtor = storeType.GetConstructor([typeof(string), typeof(string)])!;
    var store = storeCtor.Invoke([dbPath, screenshotsDir]);
    var ensureBackfilled = storeType.GetMethod("EnsureBackfilled")!;
    ensureBackfilled.Invoke(store, []);

    var routes = routesType
        .GetMethod("TryCreate", BindingFlags.Public | BindingFlags.Static)!
        .Invoke(null, ["https://example.invalid"]);
    Assert(routes != null, "TryCreate should accept https://example.invalid.");

    // Test 1: happy path
    {
        var handler = new RecordingHandler(req =>
        {
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

        Assert(handler.Requests.Count == 1, "Service should POST exactly one request.");
        Assert(
            GetUploadStatus(dbPath, "shot-1") == "uploaded",
            "Happy-path row should be marked uploaded."
        );
        client.Dispose();
    }

    // Test 2: transient HTTP 503 keeps row pending and increments attempts
    SeedRunScreenshotWithFile(dbPath, screenshotsDir, "shot-2", "2026-04-08_20-31-25-000_final_run-r1.png");
    ensureBackfilled.Invoke(store, []);
    {
        var handler = new RecordingHandler(req =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("boom"),
            }
        );
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
        client.Dispose();
    }

    // Test 3: permanent HTTP 400 flips to permanent_failure
    {
        var handler = new RecordingHandler(req =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"reason\":\"schema_mismatch\"}"),
            }
        );
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-2") == "permanent_failure",
            "HTTP 400 should be classified as a permanent failure."
        );
        client.Dispose();
    }

    // Test 4: missing image file → permanent_failure
    SeedRunScreenshotMissingFile(dbPath, "shot-3");
    ensureBackfilled.Invoke(store, []);
    {
        var handler = new RecordingHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}"),
            }
        );
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
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch { }
}

Console.WriteLine("BazaarDbScreenshotUploadService checks passed.");

static void SeedRunScreenshotWithFile(
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

static void SeedRunScreenshotMissingFile(string dbPath, string id)
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
    command.CommandText =
        "SELECT status FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;";
    command.Parameters.AddWithValue("$id", id);
    return (string?)command.ExecuteScalar() ?? string.Empty;
}

static long GetUploadAttempts(string dbPath, string id)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
        "SELECT attempts FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;";
    command.Parameters.AddWithValue("$id", id);
    return (long)(command.ExecuteScalar() ?? 0L);
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

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}
