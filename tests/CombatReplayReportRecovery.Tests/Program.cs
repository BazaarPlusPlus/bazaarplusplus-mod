using BazaarPlusPlus.Game.CombatReplay.ReportAssets;
using BazaarPlusPlus.Game.CombatReplay.ReportData;
using BazaarPlusPlus.Game.CombatReplay.Reports;
using BazaarPlusPlus.Game.CombatReplay.Video;

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
    VerifyStaleReportQueuesReplacementPublication();
    VerifyHostileCandidatesFailIndependently();
    VerifyPublisherFailureDoesNotBlockLaterCandidate();
    VerifyDeferredCacheMissIsNotReportedAsFailure();
    VerifyAssetGateReportsEveryRequiredCacheMiss();
    VerifyRecoveryDrainsEveryAdmissionBatchInOneStartup();
    VerifyRecoveryPagesRemainBoundedAndReachEveryCandidate();
    VerifyViewerGateRunsBeforeExistingReportsAreAccepted();
    VerifyViewerGateFailureStopsRecovery();
    VerifyCandidateSourceFailureIsContained();
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
        _ => [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher
    );

    var summary = recovery.Recover(256, 16, "zh-CN");
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
        _ => [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher
    );

    var summary = recovery.Recover(32, 16, "en");
    Check(
        summary.ExistingReportCount == 1
            && summary.QueuedCount == 0
            && summary.FailedCount == 0
            && publisher.Calls.Count == 0,
        "An existing physical immutable report must never be regenerated at startup."
    );
}

void VerifyStaleReportQueuesReplacementPublication()
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
        _ => [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher
    );

    var summary = recovery.Recover(32, 16, "en");
    Check(
        summary.ExistingReportCount == 0
            && summary.QueuedCount == 1
            && summary.FailedCount == 0
            && publisher.Calls.Count == 1,
        "An existing report without the current Viewer marker and references must be regenerated."
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
    var recovery = fixture.CreateRecovery(_ => candidates, publisher);

    var summary = recovery.Recover(64, 16, "en");
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
        _ =>
            [
                new(firstId, BattleId, "CurrentNative", firstRelative),
                new(secondId, BattleId, "CurrentNative", secondRelative),
            ],
        publisher
    );

    var summary = recovery.Recover(16, 16, "en");
    Check(
        summary.FailedCount == 1
            && summary.QueuedCount == 1
            && publisher.Calls.Count == 2
            && publisher.Calls[1].Candidate.RecordingId == secondId,
        "A manifest/payload/publication failure for one recording must not block later recordings."
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
        _ => [new(recordingId, BattleId, "CurrentNative", relativeVideo)],
        publisher
    );

    var summary = recovery.Recover(16, 4, "en");
    Check(
        summary.ExaminedCount == 1
            && summary.DeferredCount == 1
            && summary.QueuedCount == 0
            && summary.FailedCount == 0,
        "A cache miss must safely defer immutable report publication instead of becoming a placeholder report or a terminal failure."
    );
}

void VerifyAssetGateReportsEveryRequiredCacheMiss()
{
    var document = new CombatReportDocumentV1
    {
        Entities =
        [
            new() { EntityId = "hero", Type = "hero" },
            new() { EntityId = "item", Type = "item" },
            new() { EntityId = "skill", Type = "skill" },
        ],
        Events = [new() { IconSemanticKey = "status:burn" }],
    };
    var item = new PostCombatReportAssetFile("item", "item.png");
    var cardOnlyMissing = CombatReplayReportRecoveryAssetGate.FindMissingBindings(
        document,
        [item],
        requireNativeBindings: false
    );
    Check(
        cardOnlyMissing.SequenceEqual(["entity:skill"]),
        "The recovery asset gate must require every item/skill binding while leaving native bindings optional when their materializer is unavailable."
    );

    var nativeMissing = CombatReplayReportRecoveryAssetGate.FindMissingBindings(
        document,
        [item],
        requireNativeBindings: true
    );
    Check(
        nativeMissing.SequenceEqual(["entity:hero", "entity:skill", "event-semantic:status:burn"]),
        "The recovery asset gate must report missing hero and event-semantic cache entries without scheduling Unity work."
    );

    var complete = CombatReplayReportRecoveryAssetGate.FindMissingBindings(
        document,
        [
            item,
            new PostCombatReportAssetFile("skill", "skill.png"),
            new PostCombatReportAssetFile(
                PostCombatReportAssetBindingKind.Entity,
                "hero",
                "hero-portrait",
                "hero.png"
            ),
            new PostCombatReportAssetFile(
                PostCombatReportAssetBindingKind.EventSemantic,
                "status:burn",
                "status-icon",
                "burn.png"
            ),
        ],
        requireNativeBindings: true
    );
    Check(
        complete.Count == 0,
        "A complete cache binding set must remain eligible for immutable report publication."
    );
}

void VerifyRecoveryDrainsEveryAdmissionBatchInOneStartup()
{
    var fixture = CreateFixture("all-batches");
    var candidates = new List<CompletedVideoReportRecoveryCandidate>();
    for (var index = 0; index < 21; index++)
    {
        var recordingId = (index + 32).ToString("x32");
        var relativeVideo = Path.Combine("date", recordingId + ".mp4");
        _ = WriteVideo(fixture.VideoRoot, relativeVideo);
        candidates.Add(new(recordingId, BattleId, "CurrentNative", relativeVideo));
    }

    var publisher = new RecordingPublisher();
    var recovery = fixture.CreateRecovery(_ => candidates, publisher);
    var summary = recovery.Recover(64, 4, "en");
    Check(
        summary.ExaminedCount == candidates.Count
            && summary.QueuedCount == candidates.Count
            && summary.DeferredCount == 0
            && summary.FailedCount == 0
            && publisher.Calls.Count == candidates.Count,
        "Startup recovery must continue through every bounded admission batch instead of stranding candidates after the first batch."
    );
}

void VerifyRecoveryPagesRemainBoundedAndReachEveryCandidate()
{
    var fixture = CreateFixture("bounded-pages");
    var candidates = new List<CompletedVideoReportRecoveryCandidate>();
    for (var index = 0; index < 9; index++)
    {
        var recordingId = (index + 80).ToString("x32");
        var relativeVideo = Path.Combine("date", recordingId + ".mp4");
        _ = WriteVideo(fixture.VideoRoot, relativeVideo);
        candidates.Add(new(recordingId, BattleId, "CurrentNative", relativeVideo));
    }

    var pageCalls = new List<(int Limit, int Offset)>();
    var publisher = new RecordingPublisher();
    var recovery = new CombatReplayReportStartupRecovery(
        fixture.DataRoot,
        fixture.VideoRoot,
        (limit, offset) =>
        {
            pageCalls.Add((limit, offset));
            return candidates.Skip(offset).Take(limit).ToArray();
        },
        publisher
    );

    while (!recovery.IsCompleted)
        _ = recovery.RecoverNextBatch(4, "en");

    Check(
        pageCalls.SequenceEqual([(4, 0), (4, 4), (4, 8)])
            && publisher.Calls.Count == candidates.Count,
        "Startup recovery must query bounded pages and advance its offset until every completed recording is reachable."
    );
}

void VerifyCandidateSourceFailureIsContained()
{
    var fixture = CreateFixture("source-failure");
    var recovery = fixture.CreateRecovery(
        _ => throw new InvalidOperationException("database unavailable"),
        new RecordingPublisher()
    );

    var summary = recovery.Recover(16, 16, "en");
    Check(
        summary.ExaminedCount == 0
            && summary.QueuedCount == 0
            && summary.SourceFailure == "database unavailable",
        "A database recovery query failure must be contained instead of breaking plugin startup."
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
        _ => [new(recordingId, BattleId, "CurrentNative", recordingId + ".mp4")],
        new RecordingPublisher(),
        () =>
        {
            ensureCalls++;
            return TestValues.ViewerBundleId;
        }
    );

    var summary = recovery.Recover(16, 16, "en");
    Check(
        ensureCalls == 1 && summary.ExistingReportCount == 1 && summary.SourceFailure == null,
        "Startup recovery must validate or repair the shared Viewer before accepting existing HTML."
    );
}

void VerifyViewerGateFailureStopsRecovery()
{
    var fixture = CreateFixture("viewer-gate-failure");
    var sourceCalls = 0;
    var publisher = new RecordingPublisher();
    var recovery = new CombatReplayReportStartupRecovery(
        fixture.DataRoot,
        fixture.VideoRoot,
        _ =>
        {
            sourceCalls++;
            return Array.Empty<CompletedVideoReportRecoveryCandidate>();
        },
        publisher,
        () => throw new InvalidDataException("viewer bundle unavailable")
    );

    var summary = recovery.Recover(16, 16, "en");
    Check(
        sourceCalls == 0
            && publisher.Calls.Count == 0
            && summary.SourceFailure == "viewer bundle unavailable",
        "A broken shared Viewer must fail recovery before reports can be treated as usable."
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
        _ =>
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

    var summary = recovery.Recover(16, 16, "en");
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
    File.WriteAllText(
        Path.Combine(reportRoot, recordingId + ".html"),
        "<!doctype html><html><head>"
            + "<meta name=\"bpp-viewer-bundle\" content=\""
            + TestValues.ViewerBundleId
            + "\">"
            + "<link rel=\"stylesheet\" href=\"../report-viewer/objects/"
            + TestValues.ViewerBundleId
            + "/viewer.css\">"
            + "</head><body><script defer src=\"../report-viewer/objects/"
            + TestValues.ViewerBundleId
            + "/viewer.js\"></script></body></html>"
    );
}

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

internal sealed record Fixture(string Root, string DataRoot, string VideoRoot, string ReportRoot)
{
    internal CombatReplayReportStartupRecovery CreateRecovery(
        Func<int, IReadOnlyList<CompletedVideoReportRecoveryCandidate>> list,
        ICombatReplayReportRecoveryPublisher publisher
    ) => new(DataRoot, VideoRoot, list, publisher, () => TestValues.ViewerBundleId);
}

internal static class TestValues
{
    internal const string ViewerBundleId =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
}

internal sealed class RecordingPublisher : ICombatReplayReportRecoveryPublisher
{
    internal List<PublicationCall> Calls { get; } = new();
    internal string? FailRecordingId { get; init; }
    internal string? DeferredRecordingId { get; init; }

    public CombatReplayReportRecoveryQueueOutcome TryQueue(
        CompletedVideoReportRecoveryCandidate candidate,
        string physicalVideoFilePath,
        string locale,
        bool useTraditionalChinese,
        out string reason
    )
    {
        Calls.Add(new PublicationCall(candidate, physicalVideoFilePath, locale));
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

        reason = string.Empty;
        return CombatReplayReportRecoveryQueueOutcome.Queued;
    }
}

internal sealed record PublicationCall(
    CompletedVideoReportRecoveryCandidate Candidate,
    string VideoFilePath,
    string Locale
);
