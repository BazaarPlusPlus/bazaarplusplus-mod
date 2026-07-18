#nullable enable
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using BepInEx.Logging;
using Microsoft.Data.Sqlite;
using static BazaarPlusPlus.Tests.Shared.ScreenshotUploadTestHelpers;

var uploadLogEventsType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.ScreenshotUploadLogEvents"
);
var uploadDefinitions = uploadLogEventsType
    .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
    .Where(field => field.FieldType.Name == "BppLogEventDefinition")
    .Select(field => field.GetValue(null)!)
    .ToDictionary(
        definition => GetStringProperty(definition, "EventId"),
        definition => DescribeEventDefinition(definition),
        StringComparer.Ordinal
    );
Assert(
    uploadDefinitions.Count == 4,
    $"Screenshot upload should declare exactly four structured events; got {uploadDefinitions.Count}."
);
AssertDefinition(
    uploadDefinitions,
    "screenshots.upload.initialization_degraded",
    "reason_code:Public:Low:None|endpoint:Public:Low:None",
    "endpoint|reason_code"
);
AssertDefinition(
    uploadDefinitions,
    "screenshots.upload.waiting",
    "reason_code:Public:Low:None|pending_count:Public:Low:None",
    null
);
AssertDefinition(
    uploadDefinitions,
    "screenshots.upload.degraded",
    "endpoint:Public:Low:None|reason_code:Public:Low:None|rtt_ms:Public:High:None",
    "reason_code"
);
AssertDefinition(
    uploadDefinitions,
    "screenshots.upload.recovered",
    "endpoint:Public:Low:None|reason_code:Public:Low:None|outage_duration_ms:Public:High:None",
    null
);

var uploadLogStateType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.ScreenshotUploadLogState"
);
var uploadLogState = Activator.CreateInstance(uploadLogStateType, nonPublic: true)!;
var reportInitializationDegraded = uploadLogStateType.GetMethod(
    "ReportInitializationDegraded",
    BindingFlags.Instance | BindingFlags.NonPublic
)!;
using (var capture = new LogCapture())
{
    reportInitializationDegraded.Invoke(uploadLogState, ["invalid_local_paths", null]);
    reportInitializationDegraded.Invoke(
        uploadLogState,
        [
            "initialization_exception",
            new InvalidOperationException(
                "path=/Users/private/game/data.db url=https://private.invalid/bootstrap?token=private-query account_id=private-account response_body=private-body"
            ),
        ]
    );

    var initialization = capture.Events("screenshots.upload.initialization_degraded");
    Assert(initialization.Count == 2, "Each failed activation should emit one structured warning.");
    Assert(
        initialization.All(entry => entry.Level == LogLevel.Warning),
        "Optional screenshot upload initialization failure should be Warning, not Error."
    );
    Assert(
        initialization.All(entry =>
            entry.Data?.ToString()?.Contains("endpoint=bazaardb_snapshot") == true
        ),
        "Initialization degradation should use the governed endpoint."
    );
    Assert(
        initialization[0].Data?.ToString()?.Contains("reason_code=invalid_local_paths") == true
            && initialization[1].Data?.ToString()?.Contains("reason_code=initialization_exception")
                == true,
        "Initialization degradation should use fixed reason codes."
    );
    Assert(
        initialization.All(entry =>
            entry.Data?.ToString()?.Contains("/Users/private", StringComparison.Ordinal) != true
            && entry.Data?.ToString()?.Contains("private-query", StringComparison.Ordinal) != true
            && entry.Data?.ToString()?.Contains("private-account", StringComparison.Ordinal) != true
            && entry.Data?.ToString()?.Contains("private-body", StringComparison.Ordinal) != true
        ),
        "Initialization diagnostics must redact paths, query secrets, accounts, and bodies."
    );
}

var uploadFeedType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotUploadFeed"
);
var runAttempt = uploadFeedType.GetMethod(
    "RunAttemptAsync",
    BindingFlags.Static | BindingFlags.NonPublic
);
Assert(runAttempt != null, "Screenshot upload feed should expose its attempt boundary for tests.");
var attemptLogState = Activator.CreateInstance(uploadLogStateType, nonPublic: true)!;
using (var capture = new LogCapture())
{
    Func<CancellationToken, Task> throwingUpload = _ =>
        Task.FromException(new InvalidOperationException("private service detail"));
    var firstResult = InvokeTaskWithResult(
        runAttempt!,
        null,
        [throwingUpload, attemptLogState, CancellationToken.None]
    );
    var secondResult = InvokeTaskWithResult(
        runAttempt!,
        null,
        [throwingUpload, attemptLogState, CancellationToken.None]
    );

    AssertNoHealthSignal(firstResult);
    AssertNoHealthSignal(secondResult);
    var serviceDegraded = capture.Events("screenshots.upload.degraded");
    Assert(
        serviceDegraded.Count == 1,
        "Repeated non-cancellation service exceptions should stay inside one screenshot-owned degradation episode."
    );
    Assert(
        serviceDegraded[0].Data?.ToString()?.Contains("reason_code=service_exception") == true,
        "A caught service exception should use a fixed low-cardinality reason."
    );
    Assert(
        serviceDegraded[0]
            .Data?.ToString()
            ?.Contains("private service detail", StringComparison.Ordinal) != true,
        "Caught service exceptions should not expose service text."
    );
}

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
    var preflightRecord = tryBuildSnapshot.Invoke(
        store,
        ["shot-1", "acct-9", null, CancellationToken.None]
    );
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

    // Test 5: health succeeds, then missing image file -> permanent_failure without POST
    SeedRunSnapshotMissingFile(dbPath, "shot-3");
    ensureBackfilled.Invoke(store, []);
    {
        var handler = new RecordingHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"status\":\"ok\",\"server_time_utc\":\"2026-06-03T00:00:00.000Z\"}"
            ),
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
            handler.Requests.Count == 1
                && handler.Requests[0].Method == HttpMethod.Get
                && handler.Requests[0].RequestUri?.AbsolutePath == "/health",
            "Missing image should happen after the health probe and before upload POST."
        );
        client.Dispose();
    }

    // Test 6: prepared image failure flips to permanent_failure after health succeeds, without POST
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
            Content = new StringContent(
                "{\"status\":\"ok\",\"server_time_utc\":\"2026-06-03T00:00:00.000Z\"}"
            ),
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
            handler.Requests.Count == 1
                && handler.Requests[0].Method == HttpMethod.Get
                && handler.Requests[0].RequestUri?.AbsolutePath == "/health",
            "Prepared image failure should happen after the health probe and before upload POST."
        );
        client.Dispose();
    }
    FakePreparedImage.NextException = null;
    FakePreparedImage.NextImage = CreateUploadImage(
        uploadImageType,
        [0x89, 0x50, 0x4E, 0x47],
        "image/png",
        "fake-cache/snapshot.png"
    );

    // Test 7: prepared image timeout is transient after health succeeds, without POST
    SeedRunSnapshotWithFile(
        dbPath,
        screenshotsDir,
        "shot-image-timeout",
        "2026-04-08_20-32-10-000_final_run-r1.png"
    );
    FakePreparedImage.NextImage = null;
    FakePreparedImage.NextException = new TimeoutException("resize timed out");
    var timeoutStore = fakeStoreCtor.Invoke([
        dbPath,
        screenshotsDir,
        CreatePrepareSnapshotImageDelegate(prepareDelegateType),
    ]);
    ensureBackfilled.Invoke(timeoutStore, []);
    {
        var handler = new RecordingHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"status\":\"ok\",\"server_time_utc\":\"2026-06-03T00:00:00.000Z\"}"
            ),
        });
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([
            timeoutStore,
            routes,
            client,
            new Func<string?>(() => "acct-9"),
        ]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-image-timeout") == "pending",
            "Prepared image timeout should be a transient failure."
        );
        Assert(
            GetUploadAttempts(dbPath, "shot-image-timeout") == 1,
            "Prepared image timeout should increment attempts."
        );
        Assert(
            GetUploadLastError(dbPath, "shot-image-timeout") == "image_prepare_timeout",
            "Prepared image timeout should record image_prepare_timeout."
        );
        Assert(
            handler.Requests.Count == 1
                && handler.Requests[0].Method == HttpMethod.Get
                && handler.Requests[0].RequestUri?.AbsolutePath == "/health",
            "Prepared image timeout should happen after the health probe and before upload POST."
        );
        client.Dispose();
    }
    FakePreparedImage.NextException = null;
    FakePreparedImage.NextImage = CreateUploadImage(
        uploadImageType,
        [0x89, 0x50, 0x4E, 0x47],
        "image/png",
        "fake-cache/snapshot.png"
    );

    // Test 8: health state logs one warning per episode and one recovery after success
    SeedRunSnapshotWithFile(
        dbPath,
        screenshotsDir,
        "shot-health-fail",
        "2026-04-08_20-32-25-000_final_run-r1.png"
    );
    ensureBackfilled.Invoke(store, []);
    {
        FakePreparedImage.PrepareCallCount = 0;
        var healthRequestCount = 0;
        var handler = new RecordingHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri?.AbsolutePath == "/health")
            {
                healthRequestCount++;
                if (healthRequestCount is 1 or 2 or 3 or 5)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("private-health-response-body"),
                    };

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
        var service = ctor!.Invoke([
            store,
            routes,
            client,
            new Func<string?>(() => "private-account-id"),
        ]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        using var capture = new LogCapture();

        for (var attempt = 0; attempt < 4; attempt++)
            InvokeTask(uploadMethod, service);

        Assert(
            capture.Events("screenshots.upload.degraded").Count == 1,
            "Three failed health polls should emit one degradation warning."
        );
        Assert(
            capture.Events("screenshots.upload.recovered").Count == 1,
            "The first later health success should emit one recovery."
        );

        SeedRunSnapshotWithFile(
            dbPath,
            screenshotsDir,
            "shot-health-second-episode",
            "2026-04-08_20-32-35-000_final_run-r1.png"
        );
        ensureBackfilled.Invoke(store, []);
        InvokeTask(uploadMethod, service);
        InvokeTask(uploadMethod, service);

        var degraded = capture.Events("screenshots.upload.degraded");
        var recovered = capture.Events("screenshots.upload.recovered");
        Assert(degraded.Count == 2, "A later health outage should emit a new warning.");
        Assert(recovered.Count == 2, "A later recovered outage should emit a new recovery.");
        Assert(
            degraded.All(entry => entry.Level == LogLevel.Warning),
            "Health degradation should be Warning."
        );
        Assert(
            recovered.All(entry => entry.Level == LogLevel.Info),
            "Health recovery should be Info."
        );
        Assert(
            degraded.All(entry =>
                entry.Data?.ToString()?.Contains("endpoint=bazaardb_snapshot") == true
                && entry.Data?.ToString()?.Contains("reason_code=health_probe_failed") == true
                && entry.Data?.ToString()?.Contains("rtt_ms=") == true
            ),
            "Health degradation should use governed endpoint, reason, and RTT fields."
        );
        Assert(
            recovered.All(entry =>
                entry.Data?.ToString()?.Contains("endpoint=bazaardb_snapshot") == true
                && entry.Data?.ToString()?.Contains("reason_code=health_probe_failed") == true
                && entry.Data?.ToString()?.Contains("outage_duration_ms=") == true
            ),
            "Health recovery should carry the recovered reason and outage duration."
        );
        Assert(
            capture.All.All(entry =>
                entry.Data?.ToString()?.Contains("private-account-id", StringComparison.Ordinal)
                    != true
                && entry
                    .Data?.ToString()
                    ?.Contains("private-health-response-body", StringComparison.Ordinal) != true
                && entry.Data?.ToString()?.Contains("example.invalid", StringComparison.Ordinal)
                    != true
                && entry.Data?.ToString()?.Contains("server_time", StringComparison.Ordinal) != true
            ),
            "Screenshot upload events must omit account, response body, URL, and server text."
        );

        Assert(
            GetUploadStatus(dbPath, "shot-health-fail") == "uploaded",
            "The later healthy attempt should preserve normal screenshot upload behavior."
        );
        client.Dispose();
    }

    // Test 9: missing account id should not probe health or count attempts
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
        using var capture = new LogCapture();
        InvokeTask(uploadMethod, service);

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
#if DEBUG
        var waiting = capture.Events("screenshots.upload.waiting");
        Assert(waiting.Count == 1, "Missing account context should emit one Debug waiting event.");
        Assert(waiting[0].Level == LogLevel.Debug, "Upload waiting should be Debug.");
        Assert(
            waiting[0].Data?.ToString()?.Contains("reason_code=account_context_unavailable") == true
                && waiting[0].Data?.ToString()?.Contains("pending_count=") == true,
            "Upload waiting should contain only governed reason and pending-count fields."
        );
#else
        Assert(
            capture.Events("screenshots.upload.waiting").Count == 0,
            "Release should compile out upload waiting Debug events."
        );
#endif
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

static void AssertDefinition(
    IReadOnlyDictionary<string, EventDefinitionDescription> definitions,
    string eventId,
    string expectedFields,
    string? expectedStormKey
)
{
    Assert(definitions.TryGetValue(eventId, out var definition), $"Missing event {eventId}.");
    Assert(
        string.Equals(definition.Fields, expectedFields, StringComparison.Ordinal),
        $"{eventId} fields should be '{expectedFields}', got '{definition.Fields}'."
    );
    Assert(
        string.Equals(definition.Scope, "Screenshots", StringComparison.Ordinal),
        $"{eventId} should use the fixed Screenshots scope, got '{definition.Scope}'."
    );
    Assert(
        string.Equals(definition.StormKey, expectedStormKey, StringComparison.Ordinal),
        $"{eventId} storm key should be '{expectedStormKey ?? "<none>"}', got '{definition.StormKey ?? "<none>"}'."
    );
}

static EventDefinitionDescription DescribeEventDefinition(object definition)
{
    var fields = ((System.Collections.IEnumerable)GetProperty(definition, "Fields"))
        .Cast<object>()
        .Select(field =>
            $"{GetStringProperty(field, "Name")}:{GetProperty(field, "Privacy")}:{GetProperty(field, "Cardinality")}:{GetProperty(field, "Correlation")}"
        );
    var stormPolicy = GetNullableProperty(definition, "StormPolicy");
    var scope = GetProperty(definition, "Scope");
    var stormKey =
        stormPolicy == null
            ? null
            : string.Join(
                "|",
                ((System.Collections.IEnumerable)GetProperty(stormPolicy, "KeyFields"))
                    .Cast<object>()
                    .Select(field => GetStringProperty(field, "Name"))
            );
    return new EventDefinitionDescription(
        GetStringProperty(scope, "PrefixName"),
        string.Join("|", fields),
        stormKey
    );
}

static object GetProperty(object instance, string name) =>
    instance
        .GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
        .GetValue(instance)!;

static object? GetNullableProperty(object instance, string name) =>
    instance
        .GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
        .GetValue(instance);

static string GetStringProperty(object instance, string name) =>
    (string)GetProperty(instance, name);

static void InvokeTask(MethodInfo method, object instance)
{
    var task = (Task)method.Invoke(instance, [CancellationToken.None])!;
    task.GetAwaiter().GetResult();
}

static object InvokeTaskWithResult(MethodInfo method, object? instance, object?[] arguments)
{
    var task = (Task)method.Invoke(instance, arguments)!;
    task.GetAwaiter().GetResult();
    return task.GetType().GetProperty("Result")!.GetValue(task)!;
}

static void AssertNoHealthSignal(object result)
{
    var observations = ((System.Collections.IEnumerable)GetProperty(result, "Observations"))
        .Cast<object>()
        .ToArray();
    Assert(observations.Length == 1, "Attempt result should contain one observation.");
    Assert(
        string.Equals(
            GetProperty(observations[0], "Kind").ToString(),
            "NoHealthSignal",
            StringComparison.Ordinal
        ),
        "Screenshot feed should prevent the generic upload state from observing its owned health."
    );
}

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

    var prepareMethod = typeof(FakePreparedImage).GetMethod(nameof(FakePreparedImage.Prepare))!;
    var body = Expression.Convert(Expression.Call(prepareMethod, parameters), invoke.ReturnType);
    return Expression.Lambda(delegateType, body, parameters).Compile();
}

static Type RequireModApiType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus.ModApi")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
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

internal sealed class LogCapture : IDisposable
{
    private readonly ManualLogSource _source = new("BazaarDbScreenshotUploadService.Tests");
    private readonly List<LogEventArgs> _events = [];

    internal LogCapture()
    {
        _source.LogEvent += OnLogEvent;
        var bppLogType = RequireType("BazaarPlusPlus.Infrastructure.BppLog");
        bppLogType
            .GetMethod("Install", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [_source]);
    }

    internal IReadOnlyList<LogEventArgs> All => _events;

    internal IReadOnlyList<LogEventArgs> Events(string eventId) =>
        _events
            .Where(entry =>
                entry.Data?.ToString()?.Contains("event=" + eventId, StringComparison.Ordinal)
                == true
            )
            .ToArray();

    public void Dispose()
    {
        _source.LogEvent -= OnLogEvent;
        _source.Dispose();
    }

    private void OnLogEvent(object? sender, LogEventArgs args) => _events.Add(args);
}

internal static class FakePreparedImage
{
    public static object? NextImage { get; set; }

    public static Exception? NextException { get; set; }

    public static int PrepareCallCount { get; set; }

    public static object? Prepare(
        string snapshotId,
        string absolutePath,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        PrepareCallCount++;
        if (NextException != null)
            throw NextException;

        return NextImage;
    }
}

internal readonly record struct EventDefinitionDescription(
    string Scope,
    string Fields,
    string? StormKey
);
