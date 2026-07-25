using BazaarPlusPlus.Game.CombatReplay.Reports;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Infrastructure.Files;

const string BattleId = "11111111111111111111111111111111";
var failures = new List<string>();
var sandbox = Path.Combine(
    Path.GetTempPath(),
    "bpp-report-recovery-tests-" + Guid.NewGuid().ToString("N")
);

try
{
    Directory.CreateDirectory(sandbox);
    VerifyMissingReportQueuesPhysicalVideo();
    VerifyExistingReportSkipsPublication();
    VerifyStaleReportFailsClosed();
    VerifyIncompleteReportFailsClosed();
    VerifyHostileCandidatesFailIndependently();
    VerifyPublisherFailureDoesNotBlockLaterCandidate();
    VerifyPermanentPublisherFailureDoesNotBlockCompletion();
    VerifyDeferredCacheMissIsNotReportedAsFailure();
    VerifyFailedCandidateRetriesUntilQueued();
    VerifyDeferredCandidateRetriesUntilQueued();
    VerifyCursorPagesRemainStableWhenNewerRowsAppear();
    VerifyViewerGateRunsBeforeExistingReportsAreAccepted();
    VerifyViewerGateFailureRetriesWithBackoff();
    VerifyPermanentViewerGateFailureDoesNotRetry();
    VerifySourceFailureRetriesWithBackoffAndPreservesCursor();
    VerifyPersistentSourceFailureSelfHeals();
    VerifyVideoDirectoryLinkIsRejected();
}
finally
{
    if (Directory.Exists(sandbox))
        Directory.Delete(sandbox, recursive: true);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine("CombatReplayReportRecovery tests passed.");
}

void VerifyMissingReportQueuesPhysicalVideo()
{
    var fixture = CreateFixture("queues");
    const string recordingId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    var relativeVideo = Path.Combine("2026-07-22", recordingId + ".mp4");
    var video = WriteVideo(fixture.VideoRoot, relativeVideo);
    var publisher = new RecordingPublisher();
    var recovery = fixture.CreateRecovery(
        [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher
    );

    var summary = recovery.RecoverNextBatch(256, "zh-CN");
    Check(
        summary.ExaminedCount == 1
            && summary.QueuedCount == 1
            && summary.ExistingReportCount == 0
            && summary.FailedCount == 0,
        "A valid completed recording without HTML must be queued exactly once."
    );
    Check(
        publisher.Calls.Count == 1
            && publisher.Calls[0].VideoFilePath == Path.GetFullPath(video)
            && publisher.Calls[0].Locale == "zh-CN",
        "Recovery must pass the validated physical MP4 and locale to publication."
    );
}

void VerifyExistingReportSkipsPublication()
{
    var fixture = CreateFixture("existing");
    const string recordingId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    var relativeVideo = Path.Combine("2026-07-22", recordingId + ".mp4");
    _ = WriteVideo(fixture.VideoRoot, relativeVideo);
    Directory.CreateDirectory(fixture.ReportRoot);
    WriteCurrentReport(fixture.ReportRoot, recordingId);
    var publisher = new RecordingPublisher();
    var recovery = fixture.CreateRecovery(
        [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher
    );

    var summary = recovery.RecoverNextBatch(32, "en");
    Check(
        summary.ExistingReportCount == 1
            && summary.QueuedCount == 0
            && summary.FailedCount == 0
            && publisher.Calls.Count == 0,
        "An existing physical immutable report must never be regenerated at startup."
    );
}

void VerifyStaleReportFailsClosed()
{
    var fixture = CreateFixture("stale-existing");
    const string recordingId = "abababababababababababababababab";
    var relativeVideo = Path.Combine("2026-07-22", recordingId + ".mp4");
    _ = WriteVideo(fixture.VideoRoot, relativeVideo);
    Directory.CreateDirectory(fixture.ReportRoot);
    File.WriteAllText(
        Path.Combine(fixture.ReportRoot, recordingId + ".html"),
        "<!doctype html><html><body>old viewer</body></html>"
    );
    var publisher = new RecordingPublisher();
    var recovery = fixture.CreateRecovery(
        [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher
    );

    var summary = recovery.RecoverNextBatch(32, "en");
    Check(
        summary.ExistingReportCount == 0
            && summary.QueuedCount == 0
            && summary.FailedCount == 1
            && publisher.Calls.Count == 0,
        "An invalid existing immutable report must fail closed instead of being regenerated."
    );
}

void VerifyIncompleteReportFailsClosed()
{
    var fixture = CreateFixture("incomplete-existing");
    Directory.CreateDirectory(fixture.ReportRoot);
    var validReport = BuildCurrentReport();
    var incompleteReports = new[]
    {
        ReplaceEmbeddedReportJson(validReport, "{\"events\":1"),
        ReplaceEmbeddedReportJson(validReport, "{\"events\":[1,2"),
        ReplaceEmbeddedReportJson(validReport, "{\"name\":\"unfinished"),
        ReplaceEmbeddedReportJson(validReport, "{\"nested\":{}"),
        ReplaceEmbeddedReportJson(validReport, "{\"nested\":[]"),
        validReport.Replace(
            "</script>\n<script defer",
            "\n<script defer",
            StringComparison.Ordinal
        ),
        validReport.Replace("</body>\n", string.Empty, StringComparison.Ordinal),
        validReport.Replace("</html>\n", string.Empty, StringComparison.Ordinal),
    };
    var candidates = new List<CompletedVideoReportRecoveryCandidate>();
    for (var index = 0; index < incompleteReports.Length; index++)
    {
        var recordingId = (index + 180).ToString("x32");
        var relativeVideo = Path.Combine("date", recordingId + ".mp4");
        _ = WriteVideo(fixture.VideoRoot, relativeVideo);
        File.WriteAllText(
            Path.Combine(fixture.ReportRoot, recordingId + ".html"),
            incompleteReports[index]
        );
        candidates.Add(new(recordingId, BattleId, "CurrentNative", relativeVideo));
    }

    var publisher = new RecordingPublisher();
    var recovery = fixture.CreateRecovery(candidates, publisher);

    var summary = recovery.RecoverNextBatch(32, "en");
    Check(
        summary.ExaminedCount == incompleteReports.Length
            && summary.ExistingReportCount == 0
            && summary.QueuedCount == 0
            && summary.FailedCount == incompleteReports.Length
            && publisher.Calls.Count == 0,
        "Truncated immutable reports must fail closed instead of being replaced."
    );
}

void VerifyHostileCandidatesFailIndependently()
{
    var fixture = CreateFixture("hostile");
    const string validRecordingId = "cccccccccccccccccccccccccccccccc";
    var validRelative = Path.Combine("2026-07-22", validRecordingId + ".mp4");
    _ = WriteVideo(fixture.VideoRoot, validRelative);
    var external = Path.Combine(fixture.Root, "external.mp4");
    File.WriteAllBytes(external, [1, 2, 3]);
    var emptyRelative = Path.Combine("2026-07-22", "empty.mp4");
    var emptyPath = Path.Combine(fixture.VideoRoot, emptyRelative);
    Directory.CreateDirectory(Path.GetDirectoryName(emptyPath)!);
    File.WriteAllBytes(emptyPath, []);

    var candidates = new CompletedVideoReportRecoveryCandidate[]
    {
        new("NOT-A-RECORDING-ID", BattleId, "CurrentNative", validRelative),
        new("dddddddddddddddddddddddddddddddd", "not-a-battle", "CurrentNative", validRelative),
        new("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", BattleId, "CurrentNative", "../external.mp4"),
        new("ffffffffffffffffffffffffffffffff", BattleId, "CurrentNative", external),
        new("12121212121212121212121212121212", BattleId, "CurrentNative", emptyRelative),
        new(validRecordingId, BattleId, "CurrentNative", validRelative),
    };
    var publisher = new RecordingPublisher();
    var recovery = fixture.CreateRecovery(candidates, publisher);

    var summary = recovery.RecoverNextBatch(64, "en");
    Check(
        summary.ExaminedCount == candidates.Length
            && summary.FailedCount == candidates.Length - 1
            && summary.QueuedCount == 1
            && publisher.Calls.Single().Candidate.RecordingId == validRecordingId,
        "Invalid identities, traversal, absolute paths, and empty videos must fail per row without blocking a later valid row."
    );
}

void VerifyPublisherFailureDoesNotBlockLaterCandidate()
{
    var fixture = CreateFixture("publisher-failure");
    const string firstId = "13131313131313131313131313131313";
    const string secondId = "14141414141414141414141414141414";
    var firstRelative = Path.Combine("date", firstId + ".mp4");
    var secondRelative = Path.Combine("date", secondId + ".mp4");
    _ = WriteVideo(fixture.VideoRoot, firstRelative);
    _ = WriteVideo(fixture.VideoRoot, secondRelative);
    var publisher = new RecordingPublisher { FailRecordingId = firstId };
    var recovery = fixture.CreateRecovery(
        [
            new(firstId, BattleId, "CurrentNative", firstRelative),
            new(secondId, BattleId, "CurrentNative", secondRelative),
        ],
        publisher
    );

    var summary = recovery.RecoverNextBatch(16, "en");
    Check(
        summary.FailedCount == 1
            && summary.QueuedCount == 1
            && publisher.Calls.Count == 2
            && publisher.Calls[1].Candidate.RecordingId == secondId,
        "A manifest/payload/publication failure for one recording must not block later recordings."
    );
}

void VerifyPermanentPublisherFailureDoesNotBlockCompletion()
{
    var fixture = CreateFixture("permanent-publisher-failure");
    const string recordingId = "15151515151515151515151515151515";
    var relativeVideo = Path.Combine("date", recordingId + ".mp4");
    _ = WriteVideo(fixture.VideoRoot, relativeVideo);
    var publisher = new RecordingPublisher { FailRecordingId = recordingId };
    var recovery = fixture.CreateRecovery(
        [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher
    );

    var failed = recovery.RecoverNextBatch(1, "en");
    _ = recovery.RecoverNextBatch(1, "en");
    Check(
        recovery.IsCompleted && failed.FailedCount == 1 && publisher.Calls.Count == 1,
        "A permanent publisher failure must be reported once without keeping startup recovery alive forever."
    );
}

void VerifyDeferredCacheMissIsNotReportedAsFailure()
{
    var fixture = CreateFixture("deferred-cache-miss");
    const string recordingId = "16161616161616161616161616161616";
    var relativeVideo = Path.Combine("date", recordingId + ".mp4");
    _ = WriteVideo(fixture.VideoRoot, relativeVideo);
    var publisher = new RecordingPublisher { DeferredRecordingId = recordingId };
    var recovery = fixture.CreateRecovery(
        [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher
    );

    var summary = recovery.RecoverNextBatch(16, "en");
    Check(
        summary.ExaminedCount == 1
            && summary.DeferredCount == 1
            && summary.QueuedCount == 0
            && summary.FailedCount == 0,
        "A cache miss must safely defer immutable report publication instead of becoming a placeholder report or a terminal failure."
    );
}

void VerifyFailedCandidateRetriesUntilQueued()
{
    var fixture = CreateFixture("failed-retry");
    const string recordingId = "17171717171717171717171717171717";
    var relativeVideo = Path.Combine("date", recordingId + ".mp4");
    _ = WriteVideo(fixture.VideoRoot, relativeVideo);
    long now = 0;
    var publisher = new RecordingPublisher();
    publisher.FailAttemptsByRecordingId[recordingId] = 1;
    var recovery = fixture.CreateRecovery(
        [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher,
        () => now
    );

    var failed = recovery.RecoverNextBatch(1, "en");
    var sameTick = recovery.RecoverNextBatch(1, "en");
    Check(
        !recovery.IsCompleted
            && failed.FailedCount == 1
            && failed.QueuedCount == 0
            && sameTick.ExaminedCount == 0
            && publisher.Calls.Count == 1,
        "A failed candidate must remain pending without retrying in a same-tick busy loop."
    );

    now = CombatReplayReportStartupRecovery.InitialRetryDelayMilliseconds;
    var recovered = recovery.RecoverNextBatch(1, "en");
    _ = recovery.RecoverNextBatch(1, "en");
    Check(
        recovery.IsCompleted && recovered.QueuedCount == 1 && publisher.Calls.Count == 2,
        "A transient candidate failure must retry after backoff until publication is queued."
    );
}

void VerifyDeferredCandidateRetriesUntilQueued()
{
    var fixture = CreateFixture("deferred-retry");
    const string recordingId = "18181818181818181818181818181818";
    var relativeVideo = Path.Combine("date", recordingId + ".mp4");
    _ = WriteVideo(fixture.VideoRoot, relativeVideo);
    long now = 0;
    var publisher = new RecordingPublisher();
    publisher.DeferredAttemptsByRecordingId[recordingId] = 1;
    var recovery = fixture.CreateRecovery(
        [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher,
        () => now
    );

    var deferred = recovery.RecoverNextBatch(1, "en");
    Check(
        !recovery.IsCompleted
            && deferred.DeferredCount == 1
            && deferred.FailedCount == 0
            && publisher.Calls.Count == 1,
        "A deferred candidate must remain pending without being reported as a terminal failure."
    );

    now = CombatReplayReportStartupRecovery.InitialRetryDelayMilliseconds;
    var recovered = recovery.RecoverNextBatch(1, "en");
    _ = recovery.RecoverNextBatch(1, "en");
    Check(
        recovery.IsCompleted && recovered.QueuedCount == 1 && publisher.Calls.Count == 2,
        "A deferred candidate must be admitted again after backoff when its dependency becomes available."
    );
}

void VerifyCursorPagesRemainStableWhenNewerRowsAppear()
{
    var fixture = CreateFixture("stable-cursor-pages");
    var candidates = new List<CompletedVideoReportRecoveryCandidate>();
    for (var index = 0; index < 4; index++)
    {
        var recordingId = (index + 80).ToString("x32");
        var relativeVideo = Path.Combine("date", recordingId + ".mp4");
        _ = WriteVideo(fixture.VideoRoot, relativeVideo);
        candidates.Add(new(recordingId, BattleId, "CurrentNative", relativeVideo));
    }

    var pageCalls = new List<(int Limit, string? AfterRecordingId)>();
    var publisher = new RecordingPublisher();
    var recovery = new CombatReplayReportStartupRecovery(
        fixture.DataRoot,
        fixture.VideoRoot,
        (limit, cursor) =>
        {
            pageCalls.Add((limit, cursor?.RecordingId));
            return RecoveryPages.PageAfter(candidates, limit, cursor);
        },
        publisher
    );

    _ = recovery.RecoverNextBatch(2, "en");
    const string newerRecordingId = "ffffffffffffffffffffffffffffffff";
    candidates.Insert(
        0,
        new(
            newerRecordingId,
            BattleId,
            "CurrentNative",
            Path.Combine("date", newerRecordingId + ".mp4")
        )
    );
    while (!recovery.IsCompleted)
        _ = recovery.RecoverNextBatch(2, "en");

    Check(
        pageCalls.SequenceEqual([
            (2, (string?)null),
            (2, candidates[2].RecordingId),
            (2, candidates[4].RecordingId),
        ])
            && publisher
                .Calls.Select(call => call.Candidate.RecordingId)
                .SequenceEqual(candidates.Skip(1).Select(candidate => candidate.RecordingId)),
        "Stable keyset pagination must reach the original startup candidates exactly once when a newer completed row appears between pages."
    );
}

void VerifyViewerGateRunsBeforeExistingReportsAreAccepted()
{
    var fixture = CreateFixture("viewer-gate-existing");
    const string recordingId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    Directory.CreateDirectory(fixture.ReportRoot);
    WriteCurrentReport(fixture.ReportRoot, recordingId);
    var ensureCalls = 0;
    var recovery = new CombatReplayReportStartupRecovery(
        fixture.DataRoot,
        fixture.VideoRoot,
        (limit, cursor) =>
            RecoveryPages.PageAfter(
                [new(recordingId, BattleId, "CurrentNative", recordingId + ".mp4")],
                limit,
                cursor
            ),
        new RecordingPublisher(),
        () =>
        {
            ensureCalls++;
            return TestValues.ViewerBundleId;
        }
    );

    var summary = recovery.RecoverNextBatch(16, "en");
    Check(
        ensureCalls == 1 && summary.ExistingReportCount == 1 && summary.SourceFailure == null,
        "Startup recovery must install or verify the shared Viewer before accepting existing HTML."
    );
}

void VerifyViewerGateFailureRetriesWithBackoff()
{
    var fixture = CreateFixture("viewer-gate-retry");
    long now = 0;
    var viewerCalls = 0;
    var sourceCalls = 0;
    var recovery = new CombatReplayReportStartupRecovery(
        fixture.DataRoot,
        fixture.VideoRoot,
        (_, _) =>
        {
            sourceCalls++;
            return RecoveryPages.Empty();
        },
        new RecordingPublisher(),
        () =>
        {
            viewerCalls++;
            if (viewerCalls == 1)
                throw new IOException("viewer bundle temporarily unavailable");
            return TestValues.ViewerBundleId;
        },
        () => now
    );

    var first = recovery.RecoverNextBatch(16, "en");
    var sameTick = recovery.RecoverNextBatch(16, "en");
    Check(
        !recovery.IsCompleted
            && viewerCalls == 1
            && sourceCalls == 0
            && first.SourceFailure == "viewer bundle temporarily unavailable"
            && sameTick.SourceFailure == null,
        "A transient Viewer install failure must remain pending while same-tick calls respect backoff."
    );

    now = CombatReplayReportStartupRecovery.InitialRetryDelayMilliseconds;
    var recovered = recovery.RecoverNextBatch(16, "en");
    Check(
        recovery.IsCompleted
            && viewerCalls == 2
            && sourceCalls == 1
            && recovered.SourceFailure == null,
        "Viewer installation must retry after backoff and resume candidate discovery."
    );
}

void VerifyPermanentViewerGateFailureDoesNotRetry()
{
    var fixture = CreateFixture("viewer-gate-permanent-failure");
    var viewerCalls = 0;
    var sourceCalls = 0;
    var recovery = new CombatReplayReportStartupRecovery(
        fixture.DataRoot,
        fixture.VideoRoot,
        (_, _) =>
        {
            sourceCalls++;
            return RecoveryPages.Empty();
        },
        new RecordingPublisher(),
        () =>
        {
            viewerCalls++;
            throw new ArtifactPublicationException(
                ArtifactPublicationFailureKind.InvalidArtifactIdentity,
                "viewer identity conflict"
            );
        }
    );

    var first = recovery.RecoverNextBatch(16, "en");
    var second = recovery.RecoverNextBatch(16, "en");
    Check(
        recovery.IsCompleted
            && viewerCalls == 1
            && sourceCalls == 0
            && first.SourceFailure == "viewer identity conflict"
            && second.SourceFailure == null,
        "A deterministic Viewer identity conflict must fail once without entering the retry queue."
    );
}

void VerifySourceFailureRetriesWithBackoffAndPreservesCursor()
{
    var fixture = CreateFixture("source-failure-retry");
    long now = 0;
    var candidates = new List<CompletedVideoReportRecoveryCandidate>();
    for (var index = 0; index < 2; index++)
    {
        var recordingId = (index + 160).ToString("x32");
        var relativeVideo = Path.Combine("date", recordingId + ".mp4");
        _ = WriteVideo(fixture.VideoRoot, relativeVideo);
        candidates.Add(new(recordingId, BattleId, "CurrentNative", relativeVideo));
    }

    var calls = new List<(int Limit, string? AfterRecordingId)>();
    var secondPageAttempts = 0;
    var publisher = new RecordingPublisher();
    var recovery = new CombatReplayReportStartupRecovery(
        fixture.DataRoot,
        fixture.VideoRoot,
        (limit, cursor) =>
        {
            calls.Add((limit, cursor?.RecordingId));
            if (cursor != null && secondPageAttempts++ == 0)
                throw new IOException("database temporarily unavailable");
            return RecoveryPages.PageAfter(candidates, limit, cursor);
        },
        publisher,
        () => TestValues.ViewerBundleId,
        () => now
    );

    _ = recovery.RecoverNextBatch(1, "en");
    var failed = recovery.RecoverNextBatch(1, "en");
    _ = recovery.RecoverNextBatch(1, "en");
    Check(
        !recovery.IsCompleted
            && failed.SourceFailure == "database temporarily unavailable"
            && calls.SequenceEqual([(1, (string?)null), (1, candidates[0].RecordingId)])
            && publisher.Calls.Count == 1,
        "A transient source failure must preserve its continuation cursor and suppress same-tick retries."
    );

    now = CombatReplayReportStartupRecovery.InitialRetryDelayMilliseconds;
    _ = recovery.RecoverNextBatch(1, "en");
    _ = recovery.RecoverNextBatch(1, "en");
    Check(
        recovery.IsCompleted
            && calls.SequenceEqual([
                (1, (string?)null),
                (1, candidates[0].RecordingId),
                (1, candidates[0].RecordingId),
                (1, candidates[1].RecordingId),
            ])
            && publisher.Calls.Count == 2,
        "Recovery must retry the failed cursor after backoff and continue to the next page without skipping candidates."
    );
}

void VerifyPersistentSourceFailureSelfHeals()
{
    var fixture = CreateFixture("persistent-source-failure");
    long now = 0;
    var sourceCalls = 0;
    var sourceAvailable = false;
    var recovery = new CombatReplayReportStartupRecovery(
        fixture.DataRoot,
        fixture.VideoRoot,
        (_, _) =>
        {
            sourceCalls++;
            if (!sourceAvailable)
                throw new IOException("database unavailable");
            return RecoveryPages.Empty();
        },
        new RecordingPublisher(),
        () => TestValues.ViewerBundleId,
        () => now
    );

    const int attemptsBeyondFormerLimit = 7;
    for (var attempt = 1; attempt <= attemptsBeyondFormerLimit; attempt++)
    {
        var summary = recovery.RecoverNextBatch(16, "en");
        Check(
            summary.SourceFailure == "database unavailable",
            "Every actual source retry must expose its failure."
        );
        Check(
            !recovery.IsCompleted,
            "A persistently failing source must remain recoverable instead of becoming permanently completed."
        );
        now += CombatReplayReportStartupRecovery.MaximumRetryDelayMilliseconds;
    }

    sourceAvailable = true;
    var recovered = recovery.RecoverNextBatch(16, "en");
    Check(
        recovery.IsCompleted
            && recovered.SourceFailure == null
            && sourceCalls == attemptsBeyondFormerLimit + 1,
        "Source discovery must self-heal after any number of capped-backoff failures."
    );
}

void VerifyVideoDirectoryLinkIsRejected()
{
    var fixture = CreateFixture("linked-video");
    var outside = Path.Combine(fixture.Root, "outside");
    Directory.CreateDirectory(outside);
    var target = WriteVideo(outside, "linked.mp4");
    var linkedDirectory = Path.Combine(fixture.VideoRoot, "linked");
    Directory.CreateDirectory(fixture.VideoRoot);
    try
    {
        Directory.CreateSymbolicLink(linkedDirectory, outside);
    }
    catch (Exception ex)
        when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
    {
        return;
    }

    const string recordingId = "15151515151515151515151515151515";
    var publisher = new RecordingPublisher();
    var recovery = fixture.CreateRecovery(
        [
            new(
                recordingId,
                BattleId,
                "CurrentNative",
                Path.Combine("linked", Path.GetFileName(target))
            ),
        ],
        publisher
    );

    var summary = recovery.RecoverNextBatch(16, "en");
    Check(
        summary.FailedCount == 1 && publisher.Calls.Count == 0,
        "A video reached through a symlink/reparse directory must be rejected before publication."
    );
}

Fixture CreateFixture(string name)
{
    var root = Path.Combine(sandbox, name);
    var dataRoot = Path.Combine(root, "BazaarPlusPlusV4");
    return new Fixture(
        root,
        dataRoot,
        Path.Combine(dataRoot, "CombatReplayVideos"),
        Path.Combine(dataRoot, "reports")
    );
}

static string WriteVideo(string videoRoot, string relativePath)
{
    var path = Path.Combine(videoRoot, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, [0, 0, 0, 24, 102, 116, 121, 112]);
    return path;
}

static void WriteCurrentReport(string reportRoot, string recordingId)
{
    Directory.CreateDirectory(reportRoot);
    File.WriteAllText(Path.Combine(reportRoot, recordingId + ".html"), BuildCurrentReport());
}

static string BuildCurrentReport(string embeddedJson = "{}") =>
    "<!doctype html>\n"
    + "<html lang=\"en\">\n"
    + "<head>\n"
    + "<meta name=\"bpp-viewer-bundle\" content=\""
    + TestValues.ViewerBundleId
    + "\">\n"
    + "<meta name=\"bpp-viewer-script-sha256\" content=\""
    + TestValues.ViewerScriptSha256
    + "\">\n"
    + "<meta name=\"bpp-viewer-stylesheet-sha256\" content=\""
    + TestValues.ViewerStylesheetSha256
    + "\">\n"
    + "<link rel=\"stylesheet\" href=\"../report-viewer/objects/"
    + TestValues.ViewerBundleId
    + "/viewer.css\">\n"
    + "</head>\n"
    + "<body>\n"
    + "<main data-bpp-test-id=\"report-root\"></main>\n"
    + "<script type=\"application/json\" id=\""
    + StaticReportPaths.EmbeddedReportElementId
    + "\">"
    + embeddedJson
    + "</script>\n"
    + "<script defer src=\"../report-viewer/objects/"
    + TestValues.ViewerBundleId
    + "/viewer.js\"></script>\n"
    + "</body>\n"
    + "</html>\n";

static string ReplaceEmbeddedReportJson(string html, string replacement)
{
    var startToken =
        "<script type=\"application/json\" id=\""
        + StaticReportPaths.EmbeddedReportElementId
        + "\">";
    var payloadStart = html.IndexOf(startToken, StringComparison.Ordinal) + startToken.Length;
    var payloadEnd = html.IndexOf("</script>", payloadStart, StringComparison.Ordinal);
    return html.Substring(0, payloadStart) + replacement + html.Substring(payloadEnd);
}

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

internal sealed record Fixture(string Root, string DataRoot, string VideoRoot, string ReportRoot)
{
    internal CombatReplayReportStartupRecovery CreateRecovery(
        IReadOnlyList<CompletedVideoReportRecoveryCandidate> candidates,
        ICombatReplayReportRecoveryPublisher publisher,
        Func<long>? monotonicMilliseconds = null
    ) =>
        new(
            DataRoot,
            VideoRoot,
            (limit, cursor) => RecoveryPages.PageAfter(candidates, limit, cursor),
            publisher,
            () => TestValues.ViewerBundleId,
            monotonicMilliseconds
        );
}

internal static class TestValues
{
    internal const string ViewerBundleId =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string ViewerScriptSha256 =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    internal const string ViewerStylesheetSha256 =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
}

internal sealed class RecordingPublisher : ICombatReplayReportRecoveryPublisher
{
    internal List<PublicationCall> Calls { get; } = new();
    internal string? FailRecordingId { get; init; }
    internal string? DeferredRecordingId { get; init; }
    internal Dictionary<string, int> FailAttemptsByRecordingId { get; } =
        new(StringComparer.Ordinal);
    internal Dictionary<string, int> DeferredAttemptsByRecordingId { get; } =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _attemptsByRecordingId = new(StringComparer.Ordinal);

    public CombatReplayReportRecoveryQueueOutcome TryQueue(
        CompletedVideoReportRecoveryCandidate candidate,
        string physicalVideoFilePath,
        string locale,
        bool useTraditionalChinese,
        out string reason
    )
    {
        Calls.Add(new PublicationCall(candidate, physicalVideoFilePath, locale));
        _attemptsByRecordingId.TryGetValue(candidate.RecordingId, out var previousAttempts);
        var attempt = previousAttempts + 1;
        _attemptsByRecordingId[candidate.RecordingId] = attempt;
        if (string.Equals(candidate.RecordingId, FailRecordingId, StringComparison.Ordinal))
        {
            reason = "payload missing";
            return CombatReplayReportRecoveryQueueOutcome.Failed;
        }
        if (string.Equals(candidate.RecordingId, DeferredRecordingId, StringComparison.Ordinal))
        {
            reason = "asset cache miss";
            return CombatReplayReportRecoveryQueueOutcome.Deferred;
        }
        if (
            FailAttemptsByRecordingId.TryGetValue(candidate.RecordingId, out var failAttempts)
            && attempt <= failAttempts
        )
        {
            reason = "payload temporarily unavailable";
            return CombatReplayReportRecoveryQueueOutcome.RetryableFailure;
        }
        if (
            DeferredAttemptsByRecordingId.TryGetValue(
                candidate.RecordingId,
                out var deferredAttempts
            )
            && attempt <= deferredAttempts
        )
        {
            reason = "asset cache temporarily unavailable";
            return CombatReplayReportRecoveryQueueOutcome.Deferred;
        }

        reason = string.Empty;
        return CombatReplayReportRecoveryQueueOutcome.Queued;
    }
}

internal sealed record PublicationCall(
    CompletedVideoReportRecoveryCandidate Candidate,
    string VideoFilePath,
    string Locale
);

internal static class RecoveryPages
{
    internal static CompletedVideoReportRecoveryPage PageAfter(
        IReadOnlyList<CompletedVideoReportRecoveryCandidate> candidates,
        int limit,
        CompletedVideoReportRecoveryCursor? cursor
    )
    {
        var startIndex = 0;
        if (cursor != null)
        {
            startIndex = candidates
                .Select((candidate, index) => (candidate, index))
                .Where(entry =>
                    string.Equals(
                        entry.candidate.RecordingId,
                        cursor.RecordingId,
                        StringComparison.Ordinal
                    )
                )
                .Select(entry => entry.index + 1)
                .DefaultIfEmpty(candidates.Count)
                .Single();
        }

        var page = candidates.Skip(startIndex).Take(limit).ToArray();
        return new CompletedVideoReportRecoveryPage(
            page,
            page.Length == 0 ? null : CursorAfter(page[^1])
        );
    }

    internal static CompletedVideoReportRecoveryPage Empty() =>
        new(Array.Empty<CompletedVideoReportRecoveryCandidate>(), null);

    private static CompletedVideoReportRecoveryCursor CursorAfter(
        CompletedVideoReportRecoveryCandidate candidate
    ) => new("2026-07-22T00:00:00.0000000Z", "2026-07-22T00:00:00.0000000Z", candidate.RecordingId);
}
