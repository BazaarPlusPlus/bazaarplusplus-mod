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
    await VerifyCpuAndFilesystemWorkUsesInjectedScheduler();
    VerifyRecordingGenerationIsolation();
    VerifyTransientPublicationRetries();
    VerifySchemaMismatchIsNonRetryable();
    VerifyConflictingGenerationIdentityIsNonRetryable();
    VerifyInvalidAssetDegradesIndependentlyAndClearsStaleReference();
    VerifyEventSemanticAssetBinding();
    VerifyResolvedAssetEnrichesEntityDisplayName();
    VerifyExactVideoSyncRequiresContiguousIdentityMatchedAnchors();
    VerifyReplaceablePublishRecoversConcurrentCreate();
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

async Task VerifyCpuAndFilesystemWorkUsesInjectedScheduler()
{
    var root = Path.Combine(sandbox, "scheduled-work");
    var scheduled = new System.Collections.Concurrent.ConcurrentQueue<Action>();
    var committed = false;
    var script = Encoding.UTF8.GetBytes("/* viewer */");
    var css = Encoding.UTF8.GetBytes("/* css */");
    var artifacts = new ViewerArtifactBundle(
        1,
        script,
        css,
        script.Length,
        StaticReportIntegrity.Sha256(script),
        css.Length,
        StaticReportIntegrity.Sha256(css)
    );
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

void VerifyReplaceablePublishRecoversConcurrentCreate()
{
    var root = Path.Combine(sandbox, "replaceable-concurrent-create");
    var destination = Path.Combine(root, "objects", "viewer.js");
    var expected = Encoding.UTF8.GetBytes("current viewer bytes");

    var sameWinnerPublisher = new ReplaceableArtifactPublisher(
        (source, target) =>
        {
            File.WriteAllBytes(target, expected);
            File.Move(source, target);
        }
    );
    var reused = sameWinnerPublisher.PublishBelowRoot(root, destination, expected);
    Check(
        reused == ReplaceableArtifactPublishResult.Reused
            && File.ReadAllBytes(destination).SequenceEqual(expected),
        "A concurrent process that creates identical bytes before File.Move must be reused."
    );

    File.Delete(destination);
    var stale = Encoding.UTF8.GetBytes("stale viewer bytes");
    var differentWinnerPublisher = new ReplaceableArtifactPublisher(
        (source, target) =>
        {
            File.WriteAllBytes(target, stale);
            File.Move(source, target);
        }
    );
    var replaced = differentWinnerPublisher.PublishBelowRoot(root, destination, expected);
    Check(
        replaced == ReplaceableArtifactPublishResult.Replaced
            && File.ReadAllBytes(destination).SequenceEqual(expected),
        "A concurrent process that creates different bytes before File.Move must be safely replaced."
    );
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
    coordinator.RetryPendingPublications();
    Check(attempts == 1, "Retry backoff must prevent a same-tick publication loop.");

    now = 250;
    coordinator.RetryPendingPublications();
    terminals = Drain(coordinator);
    Check(
        attempts == 2 && published && terminals.Any(terminal => terminal.Succeeded),
        "A transient failure must retain all work and publish successfully after backoff."
    );
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
        "invalid",
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
    var artifacts = new ViewerArtifactBundle(
        1,
        script,
        css,
        script.Length,
        StaticReportIntegrity.Sha256(script),
        css.Length,
        StaticReportIntegrity.Sha256(css)
    );
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
        instanceId,
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
