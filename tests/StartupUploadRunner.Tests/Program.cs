#nullable enable
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BazaarPlusPlus.Game.RunLogging.Upload;
using BazaarPlusPlus.Game.Screenshots.Upload;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Models;
using BepInEx.Logging;

AssertMetadataTypeMissing(
    typeof(StartupUploadAttemptRunner).Assembly.Location,
    "BazaarPlusPlus.Game.RunLogging.Upload.RunUploadController"
);
AssertMetadataTypeMissing(
    typeof(StartupUploadAttemptRunner).Assembly.Location,
    "BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotUploadController"
);
Assert(
    typeof(IUploadFeed).IsAssignableFrom(typeof(RunBundleUploadFeed)),
    "Run feed contract drifted."
);
Assert(
    typeof(IUploadFeed).IsAssignableFrom(typeof(BazaarDbSnapshotUploadFeed)),
    "Screenshot feed contract drifted."
);
AssertUploadEventCatalog();

SchedulingRetainsCompletedTaskUntilObservation();
LiveRunDeferralDoesNotConsumeAttempt();
OrderedFailureAndSuccessEmitOneEpisode();
RepeatedFailuresStaySilentUntilRecovery();
SynchronousThrowEntersDegradedState();
ShutdownDrainAndCleanupRemainBounded();
DetailedPayloadOutcomeAndFailureGateAreStable();
await HttpBackedRunBundleFailuresAndRecoveryStayPrivate();
await PermanentRunBundleFailureStopsRetrying();

Console.WriteLine("Startup upload runner tests passed.");
return;

static void SchedulingRetainsCompletedTaskUntilObservation()
{
    using var capture = new LogCapture();
    var state = new UploadFeedLogState(UploadFeedKind.RunBundle);
    var runner = new StartupUploadAttemptRunner(UploadFeedKind.RunBundle, state);
    var gate = new StartupUploadAttemptGate(5f, 10f);
    var starts = 0;
    Task<UploadAttemptResult> Start(CancellationToken _)
    {
        starts++;
        return Task.FromResult(UploadAttemptResult.NoWork());
    }

    runner.Tick(gate, 3f, false, Start, CancellationToken.None);
    Assert(starts == 0, "Runner should wait for its startup gate.");
    runner.Tick(gate, 5f, false, Start, CancellationToken.None);
    Assert(starts == 1 && runner.HasPendingTask, "Eligible attempt should remain observable.");
    runner.Tick(gate, 6f, false, Start, CancellationToken.None);
    Assert(!runner.HasPendingTask, "Completion tick should clear the observed task.");
    runner.Tick(gate, 15f, false, Start, CancellationToken.None);
    Assert(starts == 2, "Retry interval should schedule the next attempt.");
}

static void AssertUploadEventCatalog()
{
    var actual = typeof(UploadLogEvents)
        .GetFields(
            System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly
        )
        .Where(field => field.FieldType == typeof(BppLogEventDefinition))
        .Select(field => ((BppLogEventDefinition)field.GetValue(null)!).EventId)
        .OrderBy(eventId => eventId, StringComparer.Ordinal)
        .ToArray();
    var expected = new[]
    {
        "upload.account_probe.failed",
        "upload.attempt.deferred",
        "upload.attempt.started",
        "upload.bundle.build_failed",
        "upload.cleanup.degraded",
        "upload.feed.armed",
        "upload.feed.degraded",
        "upload.feed.recovered",
        "upload.feed.skipped",
        "upload.shutdown_drain.degraded",
    };
    Assert(actual.SequenceEqual(expected), "Upload event catalog must match the locked manifest.");
}

static void LiveRunDeferralDoesNotConsumeAttempt()
{
    using var capture = new LogCapture();
    var state = new UploadFeedLogState(UploadFeedKind.RunBundle);
    var runner = new StartupUploadAttemptRunner(UploadFeedKind.RunBundle, state);
    var gate = new StartupUploadAttemptGate(0f, 10f);
    var starts = 0;
    Task<UploadAttemptResult> Start(CancellationToken _)
    {
        starts++;
        return Task.FromResult(UploadAttemptResult.NoWork());
    }

    runner.Tick(gate, 0f, true, Start, CancellationToken.None);
    runner.Tick(gate, 0.5f, true, Start, CancellationToken.None);
    runner.Tick(gate, 1f, false, Start, CancellationToken.None);

    Assert(starts == 1, "Live-run deferral must preserve the startup opportunity.");
    Assert(
        capture.Count("event=upload.attempt.deferred") == 1,
        "A live-run deferral episode should emit one structured Debug event."
    );
    Assert(capture.Contains("reason_code=live_run_active"), "Deferral reason should be typed.");
}

static void OrderedFailureAndSuccessEmitOneEpisode()
{
    using var capture = new LogCapture();
    var state = new UploadFeedLogState(UploadFeedKind.RunBundle);
    state.Observe(
        UploadAttemptResult.From(
            UploadAttemptObservation.Degraded(
                "run-00000001",
                UploadLogReasonCode.RemoteUploadFailed
            ),
            UploadAttemptObservation.Degraded(
                "run-00000002",
                UploadLogReasonCode.RemoteUploadFailed
            ),
            UploadAttemptObservation.Succeeded("run-00000003")
        )
    );

    Assert(
        capture.Count("event=upload.feed.degraded") == 1,
        "Ordered failures should enter one degradation episode."
    );
    Assert(
        capture.Count("event=upload.feed.recovered") == 1,
        "A later semantic success in the same attempt should recover exactly once."
    );
    Assert(capture.Contains("run_id=run-0000"), "Run correlation should render Short.");
}

static void RepeatedFailuresStaySilentUntilRecovery()
{
    using var capture = new LogCapture();
    var state = new UploadFeedLogState(UploadFeedKind.RunBundle);
    for (var index = 0; index < 10; index++)
    {
        state.Observe(
            UploadAttemptObservation.Degraded(
                $"run-{index:00000000}",
                UploadLogReasonCode.AttemptException,
                new InvalidOperationException("response_body=secret token=top-secret")
            )
        );
    }

    state.Observe(UploadAttemptObservation.Succeeded("run-recovered"));
    state.Observe(UploadAttemptObservation.Succeeded("run-ordinary"));

    Assert(capture.Count("event=upload.feed.degraded") == 1, "Retries must stay silent.");
    Assert(capture.Count("event=upload.feed.recovered") == 1, "Recovery must be one-shot.");
    Assert(!capture.Contains("top-secret"), "Projected exceptions must redact credentials.");
}

static void SynchronousThrowEntersDegradedState()
{
    using var capture = new LogCapture();
    var state = new UploadFeedLogState(UploadFeedKind.RunBundle);
    var runner = new StartupUploadAttemptRunner(UploadFeedKind.RunBundle, state);
    var gate = new StartupUploadAttemptGate(0f, 10f);

    runner.Tick(
        gate,
        0f,
        false,
        _ => throw new InvalidOperationException("sync failure"),
        CancellationToken.None
    );

    Assert(!runner.HasPendingTask, "Synchronous failure should not leave a phantom task.");
    Assert(
        capture.Count("event=upload.feed.degraded") == 1
            && capture.Contains("reason_code=attempt_exception"),
        "Synchronous failure should enter the same typed degradation state."
    );
}

static void ShutdownDrainAndCleanupRemainBounded()
{
    using var capture = new LogCapture();
    var state = new UploadFeedLogState(UploadFeedKind.RunBundle);
    var runner = new StartupUploadAttemptRunner(UploadFeedKind.RunBundle, state);
    var gate = new StartupUploadAttemptGate(0f, 10f);
    var pending = new TaskCompletionSource<UploadAttemptResult>(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    runner.Tick(gate, 0f, false, _ => pending.Task, CancellationToken.None);

    var cleanupCount = 0;
    var stopwatch = Stopwatch.StartNew();
    var drained = runner.TryDrainPendingTaskOnShutdown(
        TimeSpan.FromMilliseconds(20),
        () => cleanupCount++
    );
    stopwatch.Stop();
    Assert(!drained && stopwatch.Elapsed < TimeSpan.FromSeconds(1), "Drain must remain bounded.");
    Assert(cleanupCount == 0, "Timed-out work must finish before cleanup.");

    pending.SetResult(UploadAttemptResult.NoWork());
    Assert(
        SpinWait.SpinUntil(() => cleanupCount == 1, TimeSpan.FromSeconds(2)),
        "Asynchronous cleanup should run after completion."
    );

    var cleanupRunner = new StartupUploadAttemptRunner(
        UploadFeedKind.RunBundle,
        new UploadFeedLogState(UploadFeedKind.RunBundle)
    );
    Assert(
        cleanupRunner.TryDrainPendingTaskOnShutdown(
            TimeSpan.Zero,
            () => throw new InvalidOperationException("dispose failure")
        ),
        "Cleanup failure should not change the drain result."
    );
    Assert(
        capture.Count("event=upload.cleanup.degraded") == 1,
        "Disposable failure should emit one structured shutdown Warning."
    );
}

static void DetailedPayloadOutcomeAndFailureGateAreStable()
{
    using var capture = new LogCapture();
    var root = Path.Combine(
        Path.GetTempPath(),
        "bpp-upload-payload-" + Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(root);
    try
    {
        var store = new FileBackedPayloadStore<TestPayload>(
            root,
            ".payload",
            payload => payload.Bytes,
            TryDeserialize
        );
        Assert(
            store.LoadDetailed("battle-missing").Status == FileBackedPayloadLoadStatus.Missing,
            "A missing payload must remain distinct from corruption."
        );

        var missingRoot = Path.Combine(root, "removed");
        Directory.CreateDirectory(missingRoot);
        var missingStore = new FileBackedPayloadStore<TestPayload>(
            missingRoot,
            ".payload",
            payload => payload.Bytes,
            TryDeserialize
        );
        Directory.Delete(missingRoot);
        Assert(
            missingStore.LoadDetailed("battle-raced").Status == FileBackedPayloadLoadStatus.Missing,
            "A payload removed before the read should remain a silent missing outcome."
        );

        var unreadablePath = Path.Combine(root, "battle-unreadable.payload");
        Directory.CreateDirectory(unreadablePath);
        Assert(
            store.LoadDetailed("battle-unreadable").Status
                == FileBackedPayloadLoadStatus.Unreadable,
            "A present path that cannot be read as a file should be an integrity outcome."
        );

        var path = Path.Combine(root, "battle-corrupt.payload");
        File.WriteAllBytes(path, [1, 2, 3]);
        var first = store.LoadDetailed("battle-corrupt");
        var repeated = store.LoadDetailed("battle-corrupt");
        Assert(
            first.Status == FileBackedPayloadLoadStatus.Invalid
                && string.Equals(first.Fingerprint, repeated.Fingerprint, StringComparison.Ordinal),
            "Unchanged corrupt bytes should retain one fingerprint."
        );
        Assert(
            store.Load("battle-corrupt") == null,
            "The compatibility load facade should preserve its null fallback."
        );
        File.WriteAllBytes(path, [3, 2, 1]);
        var changed = store.LoadDetailed("battle-corrupt");
        Assert(
            changed.Status == FileBackedPayloadLoadStatus.Invalid
                && !string.Equals(first.Fingerprint, changed.Fingerprint, StringComparison.Ordinal),
            "Changed corrupt bytes must reopen the integrity gate."
        );
        Assert(
            capture.Count("event=upload.bundle.build_failed") == 0,
            "The typed payload helper must not log before its owner decides."
        );
        Assert(
            !capture.Contains(root),
            "The compatibility load facade must remain silent and omit local paths."
        );

        var gate = new UploadPayloadFailureLogGate();
        gate.Report(
            "run-12345678",
            "battle-12345678",
            first.Fingerprint!,
            UploadLogReasonCode.PayloadInvalid,
            null
        );
        gate.Report(
            "run-12345678",
            "battle-12345678",
            first.Fingerprint!,
            UploadLogReasonCode.PayloadInvalid,
            null
        );
        gate.Report(
            "run-12345678",
            "battle-12345678",
            changed.Fingerprint!,
            UploadLogReasonCode.PayloadInvalid,
            null
        );
        gate.Clear("run-12345678", "battle-12345678");
        gate.Report(
            "run-12345678",
            "battle-12345678",
            changed.Fingerprint!,
            UploadLogReasonCode.PayloadInvalid,
            null
        );
        Assert(
            capture.Count("event=upload.bundle.build_failed") == 3,
            "Unchanged corruption should log once; changed or cleared state should reopen it."
        );
        for (var index = 0; index < 300; index++)
        {
            gate.Report(
                $"run-{index:00000000}",
                $"battle-{index:00000000}",
                $"fingerprint-{index}",
                UploadLogReasonCode.PayloadInvalid,
                null
            );
        }
        Assert(gate.Count <= 256, "Payload integrity correlation state must remain bounded.");
        Assert(!capture.Contains(root), "Payload integrity events must omit local paths.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    static bool TryDeserialize(byte[]? bytes, out TestPayload? payload, out string? error)
    {
        payload = null;
        error = "response_body=private server text";
        return false;
    }
}

static async Task HttpBackedRunBundleFailuresAndRecoveryStayPrivate()
{
    const string accountId = "account-secret-id";
    const string failureBody =
        "response_body=private-body token=private-token link_code=private-code";
    using var capture = new LogCapture();
    using var httpClient = new HttpClient(
        new SequenceHttpHandler(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK,
            failureBody
        )
    );
    var routes = ModApiRoutes.TryCreate("https://example.test")!;
    var store = new FakeRunBundleUploadStore();
    using var service = new RunBundleUploadService(store, routes, httpClient, () => accountId);
    var state = new UploadFeedLogState(UploadFeedKind.RunBundle);

    for (var index = 0; index < 4; index++)
    {
        state.Observe(await service.UploadPendingRunBundlesAsync(CancellationToken.None));
    }

    Assert(store.FailureCount == 3, "Every non-success response should remain retryable.");
    Assert(store.Uploaded, "A later successful HTTP result should mark the run uploaded.");
    Assert(
        capture.Count("event=upload.feed.degraded") == 1,
        "Repeated non-success HTTP results should emit one degradation Warning."
    );
    Assert(
        capture.Count("event=upload.feed.recovered") == 1,
        "The first later HTTP success should emit one recovery Info."
    );
    Assert(!capture.Contains(failureBody), "The HTTP failure body must not enter diagnostics.");
    Assert(!capture.Contains(accountId), "The account identifier must not enter diagnostics.");
    Assert(!capture.Contains("private-token"), "Response credentials must remain private.");
}

static async Task PermanentRunBundleFailureStopsRetrying()
{
    using var httpClient = new HttpClient(
        new SequenceHttpHandler(
            HttpStatusCode.Conflict,
            HttpStatusCode.Conflict,
            HttpStatusCode.Conflict,
            HttpStatusCode.Conflict,
            "{\"error\":\"run_bundle_conflict\"}"
        )
    );
    var routes = ModApiRoutes.TryCreate("https://example.test")!;
    var store = new FakeRunBundleUploadStore();
    using var service = new RunBundleUploadService(store, routes, httpClient, () => "account-id");

    await service.UploadPendingRunBundlesAsync(CancellationToken.None);
    await service.UploadPendingRunBundlesAsync(CancellationToken.None);

    Assert(
        store.PermanentFailureCount == 1,
        "A permanent HTTP rejection should be recorded exactly once."
    );
    Assert(store.FailureCount == 0, "A permanent HTTP rejection must not enter retry state.");
}

static void AssertMetadataTypeMissing(string assemblyPath, string fullName)
{
    if (MetadataContainsType(assemblyPath, fullName))
        throw new InvalidOperationException($"{fullName} should not be compiled.");
}

static bool MetadataContainsType(string assemblyPath, string fullName)
{
    using var stream = File.OpenRead(assemblyPath);
    using var peReader = new PEReader(stream);
    var metadataReader = peReader.GetMetadataReader();
    foreach (var handle in metadataReader.TypeDefinitions)
    {
        var type = metadataReader.GetTypeDefinition(handle);
        var @namespace = metadataReader.GetString(type.Namespace);
        var name = metadataReader.GetString(type.Name);
        if (string.Equals($"{@namespace}.{name}", fullName, StringComparison.Ordinal))
            return true;
    }
    return false;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class LogCapture : IDisposable
{
    private readonly ManualLogSource _source = new("StartupUploadRunner.Tests");
    private readonly List<LogEventArgs> _events = new();

    internal LogCapture()
    {
        _source.LogEvent += OnLogEvent;
        BppLog.Install(_source);
    }

    internal int Count(string text) =>
        _events.Count(entry =>
            entry.Data?.ToString()?.Contains(text, StringComparison.Ordinal) == true
        );

    internal bool Contains(string text) =>
        _events.Any(entry =>
            entry.Data?.ToString()?.Contains(text, StringComparison.Ordinal) == true
        );

    public void Dispose()
    {
        _source.LogEvent -= OnLogEvent;
        _source.Dispose();
    }

    private void OnLogEvent(object? sender, LogEventArgs args)
    {
        _events.Add(args);
    }
}

internal sealed class TestPayload
{
    internal byte[] Bytes { get; init; } = [];
}

internal sealed class FakeRunBundleUploadStore : IRunBundleUploadStore
{
    private const string RunId = "run-http-12345678";

    internal int FailureCount { get; private set; }
    internal int PermanentFailureCount { get; private set; }
    internal bool Uploaded { get; private set; }

    public IReadOnlyList<string> GetPendingCompletedRunIds(int limit) =>
        Uploaded || PermanentFailureCount > 0 ? Array.Empty<string>() : new[] { RunId };

    public RunBundleBuildResult BuildRunBundleSnapshot(string runId, string playerAccountId) =>
        RunBundleBuildResult.Ready(
            new RunBundleUploadSnapshot
            {
                RunId = runId,
                LastSeq = 7,
                UploadedStatus = "COMPLETED",
                BattleIds = Array.Empty<string>(),
                ArtifactBytes = new byte[] { 1, 2, 3 },
                Metadata = new RunBundleUploadRequest
                {
                    SchemaVersion = 1,
                    PlayerAccountId = playerAccountId,
                    SubmittedAtUtc = DateTimeOffset.UnixEpoch.ToString("o"),
                    ArtifactCodec = "application/octet-stream",
                    RunProjection = new RunProjection
                    {
                        RunId = runId,
                        Status = "COMPLETED",
                        StartedAtUtc = DateTimeOffset.UnixEpoch.ToString("o"),
                        EndedAtUtc = DateTimeOffset.UnixEpoch.ToString("o"),
                    },
                    BattleProjections = new List<BattleProjection>(),
                },
            }
        );

    public void MarkRunUploadFailed(string runId, DateTimeOffset attemptedAtUtc, string error)
    {
        FailureCount++;
    }

    public void MarkRunUploadPermanentlyFailed(
        string runId,
        DateTimeOffset attemptedAtUtc,
        string error
    )
    {
        PermanentFailureCount++;
    }

    public void MarkRunUploaded(
        string runId,
        long uploadedSeq,
        string? uploadedStatus,
        IReadOnlyList<string> battleIds,
        DateTimeOffset uploadedAtUtc
    )
    {
        Uploaded = true;
    }
}

internal sealed class SequenceHttpHandler : HttpMessageHandler
{
    private readonly Queue<HttpStatusCode> _statuses;
    private readonly string _failureBody;
    private int _callCount;

    internal SequenceHttpHandler(
        HttpStatusCode first,
        HttpStatusCode second,
        HttpStatusCode third,
        HttpStatusCode fourth,
        string failureBody
    )
    {
        _statuses = new Queue<HttpStatusCode>([first, second, third, fourth]);
        _failureBody = failureBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var status = _statuses.Dequeue();
        _callCount++;
        if (_callCount == 3)
            throw new HttpRequestException(_failureBody);
        return Task.FromResult(
            new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? "{}" : _failureBody),
            }
        );
    }
}
