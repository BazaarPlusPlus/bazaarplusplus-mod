using System.Text;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Game.CombatReplay.ReportAssets;
using BazaarPlusPlus.Game.CombatReplay.ReportData;
using BazaarPlusPlus.Game.CombatReplay.Reports;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure.Files;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

var failures = new List<string>();
var sandbox = Path.Combine(
    Path.GetTempPath(),
    "bpp-report-publication-coordinator-" + Guid.NewGuid().ToString("N")
);

try
{
    Directory.CreateDirectory(sandbox);
    VerifySchemaV1GoldenFixture();
    await VerifyCpuAndFilesystemWorkUsesInjectedScheduler();
    VerifyRecordingGenerationIsolation();
    VerifyTransientPublicationRetries();
    VerifyTransientVideoAdmissionRetries();
    VerifyTransientArtifactBuildRetries();
    VerifyStructuredPublicationFailuresAreNonRetryable();
    VerifySchemaMismatchIsNonRetryable();
    VerifyConflictingGenerationIdentityIsNonRetryable();
    VerifyInvalidAssetDegradesIndependentlyAndClearsStaleReference();
    VerifyEventSemanticAssetBinding();
    VerifyResolvedAssetEnrichesEntityDisplayName();
    VerifyExactVideoSyncRequiresContiguousIdentityMatchedAnchors();
    VerifyScrubProxyUsabilityBoundaries();
    VerifyScrubProxyPublication();
    VerifyFailedScrubProxyRegenerationFallsBackToSourceVideo();
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
    Console.WriteLine("CombatReplayReportPublicationCoordinator tests passed.");
}

void VerifySchemaV1GoldenFixture()
{
    const string itemContentKey =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string iconContentKey =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    var envelope = new EmbeddedReportEnvelopeV1
    {
        SchemaVersion = 1,
        Locale = "zh-CN",
        BattleDocument = new CombatReportDocumentV1
        {
            SchemaVersion = 1,
            DocumentId = "document-1",
            BattleId = "battle-1",
            RecordedAtUtc = new DateTimeOffset(2026, 7, 25, 1, 2, 3, TimeSpan.Zero),
            Summary = new CombatReportSummaryV1
            {
                PlayerName = "pengx17",
                OpponentName = "Anaui",
                Outcome = "victory",
            },
            Player = new CombatReportParticipantV1 { Name = "pengx17", Hero = "Jules" },
            Opponent = new CombatReportParticipantV1 { Name = "Anaui", Hero = "Mak" },
            FrameDurationMs = 50,
            FrameCount = 3,
            DurationMs = 150,
            Winner = "player",
            Loser = "opponent",
            RawRecordCount = 7,
            // The golden payload predates optional native recap totals and remains a
            // backwards-compatibility fixture for schema-v1 reports that omit them.
            CardStats = null!,
            Entities =
            {
                new CombatReportEntityV1
                {
                    EntityId = "player-hero",
                    Owner = "player",
                    Type = "hero",
                    Name = "pengx17",
                    Order = 0,
                },
                new CombatReportEntityV1
                {
                    EntityId = "opponent-item",
                    TemplateId = "item-template",
                    Owner = "opponent",
                    Type = "item",
                    Name = "Cash Cannon",
                    Size = "Large",
                    Slot = 2,
                    Span = 3,
                    Tier = "Gold",
                    Enchant = "Fiery",
                    ContentKey = itemContentKey,
                    AssetRelativeUrl = "../report-assets/objects/aa/" + itemContentKey + ".png",
                    Order = 1,
                },
            },
            Events =
            {
                new CombatReportEventV1
                {
                    EventId = "event-0",
                    Frame = 1,
                    FrameSequence = 0,
                    CombatTimeMs = 50,
                    Kind = "effect-executed",
                    Action = "Damage",
                    SourceEntityId = "opponent-item",
                    TargetEntityIds = { "player-hero" },
                    Value = 42,
                    Unit = "points",
                    IsCritical = true,
                    Role = "applied",
                    AttributionConfidence = "exact",
                    IconSemanticKey = "damage",
                    IconContentKey = iconContentKey,
                    IconAssetRelativeUrl = "../report-assets/objects/bb/" + iconContentKey + ".png",
                    RawReference = new CombatReportRawReferenceV1
                    {
                        Category = "effect",
                        Type = "Damage",
                        Index = 4,
                    },
                },
                new CombatReportEventV1
                {
                    EventId = "event-1",
                    Frame = 2,
                    FrameSequence = 0,
                    CombatTimeMs = 100,
                    Kind = "player-attribute",
                    Action = "Health",
                    TargetEntityIds = { "player-hero" },
                    Role = "received",
                    AttributionConfidence = "target-exact-source-unknown",
                    RawReference = new CombatReportRawReferenceV1
                    {
                        Category = "player-update",
                        Type = "CombatSimPlayerAttributeUpdate",
                        Index = 0,
                    },
                },
            },
            FrameZeroState = new CombatReportFrameZeroStateV1
            {
                Player = new CombatReportCombatantStateV1
                {
                    Health = 1_000,
                    HealthRegen = 5,
                    Burn = 0,
                },
                Opponent = new CombatReportCombatantStateV1
                {
                    Health = 900,
                    Rage = 20,
                    Shield = 30,
                    Poison = 4,
                },
            },
            Metrics =
            {
                new CombatReportMetricSampleV1
                {
                    Frame = 1,
                    CombatTimeMs = 50,
                    Combatant = "player",
                    Metric = "Health",
                    Value = 958,
                    Unit = "points",
                },
            },
        },
        RecordingManifest = new RecordingReportManifestV1
        {
            SchemaVersion = 1,
            ArtifactId = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            RecordingId = "cccccccccccccccccccccccccccccccc",
            BattleId = "battle-1",
            VideoRelativeUrl = "../CombatReplayVideos/cccccccccccccccccccccccccccccccc/battle.mp4",
            SyncMetadataStatus = "ReadyExact",
            Width = 1920,
            Height = 1080,
            FramesPerSecond = 60,
            DurationMs = 150,
            SyncAnchors =
            {
                new RecordingReportSyncAnchorV1
                {
                    CombatFrame = 0,
                    CombatMs = 0,
                    MediaPtsMs = 0,
                    OutputOrdinal = 0,
                },
                new RecordingReportSyncAnchorV1
                {
                    CombatFrame = 3,
                    CombatMs = 150,
                    MediaPtsMs = 150,
                    OutputOrdinal = 3,
                },
            },
            Assets =
            {
                new RecordingReportAssetV1
                {
                    ContentKey = itemContentKey,
                    RelativeUrl = "../report-assets/objects/aa/" + itemContentKey + ".png",
                    SemanticRole = "entity:item",
                    NaturalWidth = 300,
                    NaturalHeight = 100,
                    Sha256 = itemContentKey,
                },
            },
        },
    };

    var actual = CombatReportJson.Serialize(envelope);
    var fixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "fixtures",
        "combat-report-envelope-v1.golden.json"
    );
    Check(File.Exists(fixturePath), "Schema-v1 golden fixture must be copied to test output.");
    if (!File.Exists(fixturePath))
        return;

    var expected = File.ReadAllText(fixturePath).TrimEnd('\r', '\n');
    Check(
        string.Equals(actual, expected, StringComparison.Ordinal),
        $"Schema-v1 JSON must match CombatReportJson semantics.{Environment.NewLine}Actual:{actual}"
    );
    Check(
        !actual.Contains("\"day\"", StringComparison.Ordinal)
            && !actual.Contains("\"result\"", StringComparison.Ordinal)
            && !actual.Contains("\"templateId\":null", StringComparison.Ordinal)
            && !actual.Contains("\"effectId\":null", StringComparison.Ordinal),
        "CombatReportJson must omit nullable schema-v1 fields instead of emitting null."
    );
}

async Task VerifyCpuAndFilesystemWorkUsesInjectedScheduler()
{
    var root = Path.Combine(sandbox, "scheduled-work");
    var scheduled = new System.Collections.Concurrent.ConcurrentQueue<Action>();
    var committed = false;
    var script = Encoding.UTF8.GetBytes("/* viewer */");
    var css = Encoding.UTF8.GetBytes("/* css */");
    var artifacts = new ViewerArtifactBundle(1, script, css);
    var coordinator = new CombatReplayReportPublicationCoordinator(
        root,
        artifacts,
        () => { },
        (_, _) => committed = true,
        scheduled.Enqueue,
        () => 0L
    );
    const string recordingId = "10101010101010101010101010101010";
    const string battleId = "scheduled-battle";

    var capture = coordinator.CaptureDraftAsync(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim()
    );
    Check(
        !capture.IsCompleted && scheduled.Count == 1,
        "Draft projection must be deferred through the background scheduler."
    );
    await RunNextScheduled();
    Check(await capture, "Scheduled draft projection must complete successfully.");

    var snapshot = coordinator.GetDraftSnapshotAsync(battleId);
    Check(
        !snapshot.IsCompleted && scheduled.Count == 1,
        "Draft serialization/cloning must be deferred through the background scheduler."
    );
    await RunNextScheduled();
    Check(await snapshot != null, "Scheduled draft snapshot creation must return the document.");

    coordinator.ObserveVideoStarted(Started(recordingId, battleId));
    coordinator.MarkAssetsReady(recordingId, battleId, Array.Empty<PostCombatReportAssetFile>());
    Check(
        !committed && scheduled.Count == 1,
        "Asset hashing/validation must be deferred through the background scheduler."
    );
    await RunNextScheduled();

    coordinator.ObserveVideoTerminal(Completed(root, recordingId, battleId), "en");
    Check(
        !committed && scheduled.Count == 1,
        "Report serialization and publication must be deferred through the background scheduler."
    );
    await RunNextScheduled();
    Check(committed, "The scheduled publication worker must commit the completed report.");

    async Task RunNextScheduled()
    {
        Check(scheduled.TryDequeue(out var action), "Expected one scheduled report work item.");
        if (action != null)
            await Task.Run(action);
    }
}

void VerifyRecordingGenerationIsolation()
{
    var root = Path.Combine(sandbox, "generation-isolation");
    var published = new Dictionary<string, string>(StringComparer.Ordinal);
    var coordinator = CreateCoordinator(root, (path, bytes) => published[path] = Utf8(bytes));
    const string battleId = "same-battle";
    const string firstRecording = "11111111111111111111111111111111";
    const string secondRecording = "22222222222222222222222222222222";

    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim { EntityIds = new[] { "item" } },
        out _
    );
    coordinator.ObserveVideoStarted(Started(firstRecording, battleId));
    var firstAsset = CreatePngCacheObject(root, "item", width: 3, height: 2);
    coordinator.MarkAssetsReady(firstRecording, battleId, new[] { firstAsset });
    coordinator.ObserveVideoTerminal(Completed(root, firstRecording, battleId), "en");

    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim { EntityIds = new[] { "item" }, SeedStaleAssetReference = true },
        out _
    );
    coordinator.ObserveVideoStarted(Started(secondRecording, battleId));
    coordinator.MarkAssetsReady(
        secondRecording,
        battleId,
        Array.Empty<PostCombatReportAssetFile>()
    );
    coordinator.ObserveVideoTerminal(Completed(root, secondRecording, battleId), "en");

    var firstHtml = published[Path.Combine(root, "reports", firstRecording + ".html")];
    var secondHtml = published[Path.Combine(root, "reports", secondRecording + ".html")];
    Check(
        firstHtml.Contains(firstAsset.ContentHash, StringComparison.Ordinal),
        "First recording must contain its own asset content key."
    );
    Check(
        !secondHtml.Contains(firstAsset.ContentHash, StringComparison.Ordinal)
            && !secondHtml.Contains(new string('f', 64), StringComparison.Ordinal),
        "Re-recording the same battle must not inherit first/stale asset references."
    );
    VerifyEmbeddedDocumentIdentity(firstHtml);
    VerifyEmbeddedDocumentIdentity(secondHtml);
}

void VerifyScrubProxyPublication()
{
    var root = Path.Combine(sandbox, "scrub-proxy");
    string? publishedHtml = null;
    var coordinator = CreateCoordinator(root, (_, bytes) => publishedHtml = Utf8(bytes));
    const string recordingId = "abababababababababababababababab";
    const string battleId = "scrub-proxy-battle";

    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim(),
        out _
    );
    coordinator.ObserveVideoStarted(Started(recordingId, battleId));
    coordinator.MarkAssetsReady(recordingId, battleId, Array.Empty<PostCombatReportAssetFile>());
    var completed = Completed(root, recordingId, battleId);
    var scrubProxyPath = ReplayVideoScrubProxy.BuildFilePath(completed.FinalFilePath);
    File.WriteAllBytes(scrubProxyPath, new byte[1024]);
    coordinator.ObserveVideoTerminal(completed, "en");

    var envelope = publishedHtml == null ? null : ParseEmbeddedEnvelope(publishedHtml);
    Check(envelope != null, "A report with a scrub proxy must be published.");
    Check(
        envelope?.RecordingManifest.ScrubVideoRelativeUrl
            == "../CombatReplayVideos/" + recordingId + ".scrub.mp4",
        "The report manifest must reference the committed scrub proxy next to the source video."
    );
}

void VerifyScrubProxyUsabilityBoundaries()
{
    var root = Path.Combine(sandbox, "scrub-proxy-boundaries");
    Directory.CreateDirectory(root);
    var sourceVideoPath = Path.Combine(root, "boundary.mp4");
    File.WriteAllBytes(sourceVideoPath, new byte[1024]);
    var proxyPath = ReplayVideoScrubProxy.BuildFilePath(sourceVideoPath);

    foreach (
        var (length, expectedUsable) in new[]
        {
            (0, false),
            (1, false),
            (1023, false),
            (1024, true),
        }
    )
    {
        File.WriteAllBytes(proxyPath, new byte[length]);
        Check(
            ReplayVideoScrubProxy.IsUsable(proxyPath) == expectedUsable,
            $"A {length}-byte scrub proxy returned the wrong usability result."
        );
        Check(
            ReplayVideoScrubProxy.TryGetExistingFilePath(sourceVideoPath, out _) == expectedUsable,
            $"A {length}-byte scrub proxy returned the wrong publication result."
        );
    }
}

void VerifyFailedScrubProxyRegenerationFallsBackToSourceVideo()
{
    var root = Path.Combine(sandbox, "scrub-proxy-failed-regeneration");
    string? publishedHtml = null;
    var coordinator = CreateCoordinator(root, (_, bytes) => publishedHtml = Utf8(bytes));
    const string recordingId = "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd";
    const string battleId = "scrub-proxy-failed-regeneration-battle";

    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim(),
        out _
    );
    coordinator.ObserveVideoStarted(Started(recordingId, battleId));
    coordinator.MarkAssetsReady(recordingId, battleId, Array.Empty<PostCombatReportAssetFile>());
    var completed = Completed(root, recordingId, battleId);
    var scrubProxyPath = ReplayVideoScrubProxy.BuildFilePath(completed.FinalFilePath);
    File.WriteAllBytes(scrubProxyPath, new byte[1023]);

    var regeneration = ReplayVideoScrubProxyGenerator.Create(null, completed.FinalFilePath);
    Check(
        !regeneration.Available
            && regeneration.ReasonCode == ReplayVideoScrubProxyReasonCode.FfmpegUnavailable,
        "An unusable old scrub stub must remain unavailable when regeneration cannot start."
    );
    coordinator.ObserveVideoTerminal(completed, "en");

    var envelope = publishedHtml == null ? null : ParseEmbeddedEnvelope(publishedHtml);
    Check(envelope != null, "A failed scrub regeneration must not block report publication.");
    Check(
        envelope?.RecordingManifest.ScrubVideoRelativeUrl == null,
        "A failed scrub regeneration must omit the unusable proxy URL."
    );
    Check(
        envelope?.RecordingManifest.VideoRelativeUrl
            == "../CombatReplayVideos/" + recordingId + ".mp4",
        "A failed scrub regeneration must retain the original video URL."
    );
}

void VerifyTransientPublicationRetries()
{
    var root = Path.Combine(sandbox, "retry");
    var now = 0L;
    var attempts = 0;
    var published = false;
    var coordinator = CreateCoordinator(
        root,
        (_, _) =>
        {
            attempts++;
            if (attempts == 1)
                throw new IOException("temporary sharing violation");
            published = true;
        },
        () => now
    );
    const string recordingId = "33333333333333333333333333333333";
    const string battleId = "retry-battle";
    SupplyCompleteInputs(coordinator, root, recordingId, battleId, schemaVersion: 1);

    var terminals = Drain(coordinator);
    Check(
        attempts == 1
            && terminals.Count == 1
            && terminals[0].FailureKind == CombatReplayReportPublicationFailureKind.Retryable,
        "A transient write failure must remain retryable and observable."
    );
    var sameTick = Drain(coordinator);
    Check(
        attempts == 1 && sameTick.Count == 0,
        "Retry backoff must prevent a same-tick publication loop."
    );

    now = 250;
    terminals = Drain(coordinator);
    Check(
        attempts == 2 && published && terminals.Any(terminal => terminal.Succeeded),
        "A transient failure must retain all work and publish successfully after backoff."
    );
}

void VerifyTransientVideoAdmissionRetries()
{
    var root = Path.Combine(sandbox, "video-admission-retry");
    var now = 0L;
    var commits = 0;
    var coordinator = CreateCoordinator(root, (_, _) => commits++, () => now);
    const string recordingId = "34343434343434343434343434343434";
    const string battleId = "video-admission-retry-battle";
    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim(),
        out _
    );
    coordinator.ObserveVideoStarted(Started(recordingId, battleId));
    coordinator.MarkAssetsReady(recordingId, battleId, Array.Empty<PostCombatReportAssetFile>());

    var videoDirectory = Path.Combine(root, "CombatReplayVideos");
    var videoPath = Path.Combine(videoDirectory, recordingId + ".mp4");
    coordinator.ObserveVideoTerminal(
        new CombatReplayVideoRecordingCompleted
        {
            RecordingId = recordingId,
            BattleId = battleId,
            FinalFilePath = videoPath,
            ArtifactUsable = true,
            SyncAnchors = Array.Empty<ReplayVideoSyncAnchor>(),
        },
        "en"
    );

    var terminals = Drain(coordinator);
    Check(
        commits == 0
            && terminals.Count == 1
            && terminals[0].FailureKind == CombatReplayReportPublicationFailureKind.Retryable,
        "A completed video that is temporarily missing during terminal admission must remain retryable."
    );

    Directory.CreateDirectory(videoDirectory);
    File.WriteAllBytes(videoPath, new byte[] { 0, 0, 0, 0 });
    now = 250;
    terminals = Drain(coordinator);
    Check(
        commits == 1 && terminals.Any(terminal => terminal.Succeeded),
        "Terminal admission must retain the video generation and publish after the transient file appears."
    );
}

void VerifyTransientArtifactBuildRetries()
{
    var root = Path.Combine(sandbox, "artifact-build-retry");
    var now = 0L;
    var commits = 0;
    var scheduled = new Queue<Action>();
    var script = Encoding.UTF8.GetBytes("/* viewer */");
    var css = Encoding.UTF8.GetBytes("/* css */");
    var coordinator = new CombatReplayReportPublicationCoordinator(
        root,
        new ViewerArtifactBundle(1, script, css),
        () => { },
        (_, _) => commits++,
        scheduled.Enqueue,
        () => now
    );
    const string recordingId = "35353535353535353535353535353535";
    const string battleId = "artifact-build-retry-battle";
    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim(),
        out _
    );
    coordinator.ObserveVideoStarted(Started(recordingId, battleId));
    coordinator.MarkAssetsReady(recordingId, battleId, Array.Empty<PostCombatReportAssetFile>());
    Check(
        scheduled.Count == 1,
        "Asset resolution must be queued before the artifact-build retry test."
    );
    scheduled.Dequeue()();

    var completed = Completed(root, recordingId, battleId);
    coordinator.ObserveVideoTerminal(completed, "en");
    Check(scheduled.Count == 1, "A complete generation must queue one background artifact build.");
    File.Delete(completed.FinalFilePath);
    scheduled.Dequeue()();

    var terminals = Drain(coordinator);
    Check(
        commits == 0
            && terminals.Count == 1
            && terminals[0].FailureKind == CombatReplayReportPublicationFailureKind.Retryable,
        "A video that disappears during background artifact build must remain retryable."
    );

    File.WriteAllBytes(completed.FinalFilePath, new byte[] { 0, 0, 0, 0 });
    now = 250;
    terminals = Drain(coordinator);
    Check(
        terminals.Count == 0 && scheduled.Count == 1,
        "Artifact-build retry backoff must queue one later worker without prematurely completing."
    );
    scheduled.Dequeue()();
    terminals = Drain(coordinator);
    Check(
        commits == 1 && terminals.Any(terminal => terminal.Succeeded),
        "The retained generation must publish after its transient artifact-build input returns."
    );
}

void VerifyStructuredPublicationFailuresAreNonRetryable()
{
    var cases = new (string Name, Func<Exception> CreateFailure)[]
    {
        (
            "traversal",
            () =>
                new ArtifactPublicationException(
                    ArtifactPublicationFailureKind.PathEscapesRoot,
                    "destination rejected"
                )
        ),
        (
            "symlink",
            () =>
                new ArtifactPublicationException(
                    ArtifactPublicationFailureKind.SymbolicLinkOrReparsePoint,
                    "destination rejected"
                )
        ),
        ("permission", () => new UnauthorizedAccessException("destination rejected")),
    };

    foreach (var testCase in cases)
    {
        var root = Path.Combine(sandbox, "structured-" + testCase.Name);
        var attempts = 0;
        var now = 0L;
        var coordinator = CreateCoordinator(
            root,
            (_, _) =>
            {
                attempts++;
                throw testCase.CreateFailure();
            },
            () => now
        );
        var recordingId =
            testCase.Name == "traversal" ? "71717171717171717171717171717171"
            : testCase.Name == "symlink" ? "72727272727272727272727272727272"
            : "73737373737373737373737373737373";
        SupplyCompleteInputs(
            coordinator,
            root,
            recordingId,
            testCase.Name + "-battle",
            schemaVersion: 1
        );

        var terminals = Drain(coordinator);
        now = 30_000;
        _ = Drain(coordinator);
        Check(
            attempts == 1
                && terminals.Count == 1
                && terminals[0].FailureKind
                    == CombatReplayReportPublicationFailureKind.NonRetryable,
            $"Structured {testCase.Name} publication failures must terminate without retries."
        );
    }
}

void VerifySchemaMismatchIsNonRetryable()
{
    var root = Path.Combine(sandbox, "schema");
    var commits = 0;
    var coordinator = CreateCoordinator(root, (_, _) => commits++);
    SupplyCompleteInputs(
        coordinator,
        root,
        "44444444444444444444444444444444",
        "schema-battle",
        schemaVersion: 2
    );
    var terminals = Drain(coordinator);
    Check(
        commits == 0
            && terminals.Count == 1
            && terminals[0].FailureKind == CombatReplayReportPublicationFailureKind.NonRetryable
            && terminals[0].Reason!.Contains("schema", StringComparison.OrdinalIgnoreCase),
        "Viewer/document schema mismatch must fail explicitly without retrying publication."
    );
}

void VerifyConflictingGenerationIdentityIsNonRetryable()
{
    var root = Path.Combine(sandbox, "identity");
    var commits = 0;
    var coordinator = CreateCoordinator(root, (_, _) => commits++);
    const string recordingId = "66666666666666666666666666666666";
    coordinator.ObserveVideoStarted(Started(recordingId, "first-battle"));
    coordinator.MarkAssetsReady(
        recordingId,
        "different-battle",
        Array.Empty<PostCombatReportAssetFile>()
    );
    var terminals = Drain(coordinator);
    Check(
        commits == 0
            && terminals.Count == 1
            && terminals[0].FailureKind == CombatReplayReportPublicationFailureKind.NonRetryable
            && terminals[0].Reason!.Contains("conflicting", StringComparison.OrdinalIgnoreCase),
        "A recording ID reused for a different battle must terminate as an identity failure."
    );
}

void VerifyInvalidAssetDegradesIndependentlyAndClearsStaleReference()
{
    var root = Path.Combine(sandbox, "asset-degradation");
    string? html = null;
    var coordinator = CreateCoordinator(root, (_, bytes) => html = Utf8(bytes));
    const string recordingId = "55555555555555555555555555555555";
    const string battleId = "asset-battle";
    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim
        {
            EntityIds = new[] { "valid", "invalid" },
            SeedStaleAssetReference = true,
        },
        out _
    );
    coordinator.ObserveVideoStarted(Started(recordingId, battleId));
    var valid = CreatePngCacheObject(root, "valid", 7, 5);
    var invalid = new PostCombatReportAssetFile(
        PostCombatReportAssetBindingKind.Entity,
        "invalid",
        "card-preview",
        Path.Combine(root, "report-assets", "objects", "00", "missing.png")
    );
    coordinator.MarkAssetsReady(recordingId, battleId, new[] { invalid, valid });
    coordinator.ObserveVideoTerminal(Completed(root, recordingId, battleId), "en");

    Check(
        html != null
            && html.Contains("sha256-" + valid.ContentHash, StringComparison.Ordinal)
            && html.Contains(valid.ContentHash + ".png", StringComparison.Ordinal)
            && !html.Contains(new string('f', 64), StringComparison.Ordinal),
        "A valid asset must keep matching contentKey/manifest URL while an invalid peer degrades alone and stale references are cleared."
    );
}

void VerifyEventSemanticAssetBinding()
{
    var root = Path.Combine(sandbox, "event-semantic-asset");
    string? html = null;
    var coordinator = CreateCoordinator(root, (_, bytes) => html = Utf8(bytes));
    const string recordingId = "77777777777777777777777777777777";
    const string battleId = "event-semantic-battle";
    const string semanticKey = "status.freeze";
    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim
        {
            EventIconSemanticKey = semanticKey,
            SeedStaleAssetReference = true,
        },
        out _
    );
    coordinator.ObserveVideoStarted(Started(recordingId, battleId));
    var cached = CreatePngCacheObject(root, "unused-entity", 9, 9);
    var semantic = new PostCombatReportAssetFile(
        PostCombatReportAssetBindingKind.EventSemantic,
        semanticKey,
        "status-effect-icon",
        cached.FilePath,
        ContentHash: cached.ContentHash,
        PixelWidth: 9,
        PixelHeight: 9
    );
    coordinator.MarkAssetsReady(recordingId, battleId, new[] { semantic });
    coordinator.ObserveVideoTerminal(Completed(root, recordingId, battleId), "en");

    Check(html != null, "Event-semantic report must publish.");
    if (html == null)
        return;
    var envelope = ParseEmbeddedEnvelope(html);
    var reportEvent = envelope?.BattleDocument.Events.SingleOrDefault();
    Check(
        reportEvent != null
            && reportEvent.IconContentKey == "sha256-" + cached.ContentHash
            && reportEvent.IconAssetRelativeUrl != null
            && reportEvent.IconAssetRelativeUrl.EndsWith(
                cached.ContentHash + ".png",
                StringComparison.Ordinal
            )
            && envelope!.RecordingManifest.Assets.Any(asset =>
                asset.SemanticRole == "status-effect-icon"
            ),
        "EventSemantic assets must bind every matching event and remain present in the manifest."
    );
    VerifyEmbeddedDocumentIdentity(html);
}

void VerifyResolvedAssetEnrichesEntityDisplayName()
{
    var root = Path.Combine(sandbox, "entity-display-name");
    string? html = null;
    var coordinator = CreateCoordinator(root, (_, bytes) => html = Utf8(bytes));
    const string recordingId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string battleId = "entity-name-battle";
    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim { EntityIds = new[] { "item" } },
        out _
    );
    coordinator.ObserveVideoStarted(Started(recordingId, battleId));
    var asset = CreatePngCacheObject(
        root,
        "item",
        width: 7,
        height: 5,
        displayName: "Resolved Localized Item"
    );
    coordinator.MarkAssetsReady(recordingId, battleId, new[] { asset });
    coordinator.ObserveVideoTerminal(Completed(root, recordingId, battleId), "en");

    var envelope = ParseEmbeddedEnvelope(html!);
    var entity = envelope?.BattleDocument.Entities.SingleOrDefault(item => item.EntityId == "item");
    Check(
        entity?.Name == "Resolved Localized Item",
        "A successfully resolved native asset must enrich the report entity with its localized display name."
    );
}

void VerifyExactVideoSyncRequiresContiguousIdentityMatchedAnchors()
{
    var root = Path.Combine(sandbox, "video-sync");
    string? exactHtml = null;
    var coordinator = CreateCoordinator(root, (_, bytes) => exactHtml = Utf8(bytes));
    const string recordingId = "88888888888888888888888888888888";
    const string battleId = "sync-battle";
    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim(),
        out _
    );
    coordinator.ObserveVideoStarted(Started(recordingId, battleId));
    coordinator.MarkAssetsReady(recordingId, battleId, Array.Empty<PostCombatReportAssetFile>());
    coordinator.ObserveVideoTerminal(
        Completed(
            root,
            recordingId,
            battleId,
            new[]
            {
                new ReplayVideoSyncAnchor(recordingId, battleId, 0, 0, 0, 0),
                new ReplayVideoSyncAnchor(recordingId, battleId, 1, 50, 17, 1),
            }
        ),
        "en"
    );
    var exact = ParseEmbeddedEnvelope(exactHtml!);
    Check(
        exact?.RecordingManifest.SyncMetadataStatus == "ReadyExact"
            && exact.RecordingManifest.SyncAnchors.Count == 2,
        "Two contiguous identity-matched output anchors must publish exact video sync."
    );

    string? unsyncedHtml = null;
    coordinator = CreateCoordinator(root, (_, bytes) => unsyncedHtml = Utf8(bytes));
    const string unsyncedRecordingId = "99999999999999999999999999999999";
    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim(),
        out _
    );
    coordinator.ObserveVideoStarted(Started(unsyncedRecordingId, battleId));
    coordinator.MarkAssetsReady(
        unsyncedRecordingId,
        battleId,
        Array.Empty<PostCombatReportAssetFile>()
    );
    coordinator.ObserveVideoTerminal(
        Completed(
            root,
            unsyncedRecordingId,
            battleId,
            new[]
            {
                new ReplayVideoSyncAnchor(unsyncedRecordingId, battleId, 0, 0, 0, 5),
                new ReplayVideoSyncAnchor(unsyncedRecordingId, battleId, 1, 50, 17, 7),
            }
        ),
        "en"
    );
    var unsynced = ParseEmbeddedEnvelope(unsyncedHtml!);
    Check(
        unsynced?.RecordingManifest.SyncMetadataStatus == "ReadyUnsynced"
            && unsynced.RecordingManifest.SyncAnchors.Count == 0,
        "Output ordinals [5, 7] must degrade the report to ReadyUnsynced."
    );

    var gapped = ReplayVideoSyncMetadata.SelectExactAnchors(
        "gapped-recording",
        battleId,
        new[]
        {
            new ReplayVideoSyncAnchor("gapped-recording", battleId, 0, 0, 0, 0),
            new ReplayVideoSyncAnchor("gapped-recording", battleId, 1, 50, 34, 2),
        }
    );
    Check(
        gapped.Count == 0,
        "An output-ordinal gap after zero must not qualify as exact video sync."
    );

    var mismatched = ReplayVideoSyncMetadata.SelectExactAnchors(
        "matched-recording",
        battleId,
        new[]
        {
            new ReplayVideoSyncAnchor("matched-recording", battleId, 0, 0, 0, 0),
            new ReplayVideoSyncAnchor("wrong-recording", battleId, 1, 50, 17, 1),
        }
    );
    Check(
        mismatched.Count == 0,
        "An identity-mismatched anchor must not qualify as exact video sync."
    );
}

CombatReplayReportPublicationCoordinator CreateCoordinator(
    string root,
    Action<string, byte[]> commit,
    Func<long>? clock = null
)
{
    var script = Encoding.UTF8.GetBytes("/* viewer */");
    var css = Encoding.UTF8.GetBytes("/* css */");
    var artifacts = new ViewerArtifactBundle(1, script, css);
    return new CombatReplayReportPublicationCoordinator(
        root,
        artifacts,
        () => { },
        commit,
        action => action(),
        clock ?? (() => 0L)
    );
}

void SupplyCompleteInputs(
    CombatReplayReportPublicationCoordinator coordinator,
    string root,
    string recordingId,
    string battleId,
    int schemaVersion
)
{
    coordinator.TryCaptureDraft(
        new PvpBattleManifest { BattleId = battleId },
        new NetMessageCombatSim { ReportSchemaVersion = schemaVersion },
        out _
    );
    coordinator.ObserveVideoStarted(Started(recordingId, battleId));
    coordinator.MarkAssetsReady(recordingId, battleId, Array.Empty<PostCombatReportAssetFile>());
    coordinator.ObserveVideoTerminal(Completed(root, recordingId, battleId), "en");
}

CombatReplayVideoRecordingStarted Started(string recordingId, string battleId) =>
    new() { RecordingId = recordingId, BattleId = battleId };

CombatReplayVideoRecordingCompleted Completed(
    string root,
    string recordingId,
    string battleId,
    IReadOnlyList<ReplayVideoSyncAnchor>? syncAnchors = null
)
{
    var directory = Path.Combine(root, "CombatReplayVideos");
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, recordingId + ".mp4");
    File.WriteAllBytes(path, new byte[] { 0, 0, 0, 0 });
    return new CombatReplayVideoRecordingCompleted
    {
        RecordingId = recordingId,
        BattleId = battleId,
        FinalFilePath = path,
        ArtifactUsable = true,
        SyncAnchors = syncAnchors ?? Array.Empty<ReplayVideoSyncAnchor>(),
    };
}

PostCombatReportAssetFile CreatePngCacheObject(
    string root,
    string instanceId,
    int width,
    int height,
    string displayName = ""
)
{
    byte[] bytes;
    using (var image = new Image<Rgba32>(width, height, new Rgba32(20, 40, 60, 255)))
    using (var buffer = new MemoryStream())
    {
        image.Save(buffer, new PngEncoder());
        bytes = buffer.ToArray();
    }
    var digest = StaticReportIntegrity.Sha256(bytes);
    var directory = Path.Combine(root, "report-assets", "objects", digest[..2]);
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, digest + ".png");
    File.WriteAllBytes(path, bytes);
    return new PostCombatReportAssetFile(
        PostCombatReportAssetBindingKind.Entity,
        instanceId,
        "card-preview",
        path,
        ContentHash: digest,
        DisplayName: displayName
    );
}

List<CombatReplayReportPublicationTerminal> Drain(
    CombatReplayReportPublicationCoordinator coordinator
)
{
    var terminals = new List<CombatReplayReportPublicationTerminal>();
    coordinator.DrainTerminals(terminals.Add);
    return terminals;
}

string Utf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

void VerifyEmbeddedDocumentIdentity(string html)
{
    var envelope = ParseEmbeddedEnvelope(html);
    Check(envelope != null, "Published report envelope must remain valid JSON.");
    if (envelope == null)
        return;

    var document = envelope.BattleDocument;
    var publishedId = document.DocumentId;
    document.DocumentId = string.Empty;
    var expectedId = StaticReportIntegrity.Sha256Utf8(CombatReportJson.Serialize(document));
    Check(
        string.Equals(publishedId, expectedId, StringComparison.Ordinal),
        "DocumentId must identify the final document after asset references are applied."
    );
}

EmbeddedReportEnvelopeV1? ParseEmbeddedEnvelope(string html)
{
    const string marker = "<script type=\"application/json\" id=\"bpp-report-data\">";
    var start = html.IndexOf(marker, StringComparison.Ordinal);
    var end =
        start < 0 ? -1 : html.IndexOf("</script>", start + marker.Length, StringComparison.Ordinal);
    Check(start >= 0 && end > start, "Published HTML must contain one embedded report envelope.");
    if (start < 0 || end <= start)
        return null;
    return JsonConvert.DeserializeObject<EmbeddedReportEnvelopeV1>(
        html.Substring(start + marker.Length, end - start - marker.Length)
    );
}
