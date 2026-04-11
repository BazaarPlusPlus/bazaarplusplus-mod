#nullable enable
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;

var syncServiceType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Ghost.GhostBattleSyncService");
var apiClientType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Ghost.GhostBattleApiClient");
var repositoryType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelRepository");
var battleRecordType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryBattleRecord");
var coordinatorType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelCoordinator");
var playerAccountResolverType = RequireType("BazaarPlusPlus.Game.Identity.PlayerAccountIdResolver");
var installationRecordType = RequireType("BazaarPlusPlus.Game.Identity.InstallationRecord");
var installationStoreType = RequireType("BazaarPlusPlus.Game.Identity.InstallationRecordStore");
var coordinatorOutcomeType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelCoordinator+GhostBattleOutcome"
);
var importRecordType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Ghost.GhostBattleImportRecord");
var routesType = RequireType("BazaarPlusPlus.Game.Online.V3Routes");
var artifactCodecType = RequireType("BazaarPlusPlus.Game.Online.V3RunBundleArtifactCodec");
var ghostPayloadStoreType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Ghost.GhostBattlePayloadStore");
var runArtifactType = RequireType("BazaarPlusPlus.Game.Online.Models.RunArtifactV3");
var runArtifactBattleType = RequireType("BazaarPlusPlus.Game.Online.Models.RunArtifactBattleV3");
var battleManifestArtifactType = RequireType(
    "BazaarPlusPlus.Game.Online.Models.BattleManifestArtifactV3"
);
var battleParticipantsArtifactType = RequireType(
    "BazaarPlusPlus.Game.Online.Models.BattleParticipantsArtifactV3"
);
var battleSnapshotsArtifactType = RequireType(
    "BazaarPlusPlus.Game.Online.Models.BattleSnapshotsArtifactV3"
);
var replayPayloadArtifactType = RequireType(
    "BazaarPlusPlus.Game.Online.Models.ReplayPayloadArtifactV3"
);
var cardSetCaptureArtifactType = RequireType(
    "BazaarPlusPlus.Game.Online.Models.CardSetCaptureArtifactV3"
);
var cardSnapshotType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayCardSnapshot");

var shouldAdvanceCheckpoint = syncServiceType.GetMethod(
    "ShouldAdvanceCheckpoint",
    BindingFlags.NonPublic | BindingFlags.Static
);
var tryExtractPayloadFromArtifact = apiClientType.GetMethod(
    "TryExtractPayloadFromArtifact",
    BindingFlags.NonPublic | BindingFlags.Static
);
var tryParseBattle = apiClientType.GetMethod(
    "TryParseBattle",
    BindingFlags.NonPublic | BindingFlags.Static
);
var resolvePlayerAccountId = playerAccountResolverType.GetMethod(
    "Resolve",
    BindingFlags.NonPublic | BindingFlags.Static
);
var serializeArtifact = artifactCodecType.GetMethod(
    "Serialize",
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
);
var resolveGhostBattleOutcome = coordinatorType.GetMethod(
    "ResolveGhostBattleOutcome",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    shouldAdvanceCheckpoint != null,
    "GhostBattleSyncService should expose checkpoint advancement logic."
);
Assert(
    tryExtractPayloadFromArtifact != null,
    "GhostBattleApiClient should expose artifact extraction logic for replay downloads."
);
Assert(serializeArtifact != null, "V3RunBundleArtifactCodec should expose a serialize helper.");
Assert(tryParseBattle != null, "GhostBattleApiClient should expose battle parsing logic.");
Assert(
    resolveGhostBattleOutcome != null,
    "HistoryPanelCoordinator should expose ghost-outcome resolution logic."
);
Assert(
    resolvePlayerAccountId != null,
    "PlayerAccountIdResolver should expose account-id fallback logic."
);

Assert(
    !(bool)shouldAdvanceCheckpoint!.Invoke(null, [200, 200])!,
    "Ghost sync should not advance the checkpoint when the returned batch hits the limit."
);
Assert(
    (bool)shouldAdvanceCheckpoint.Invoke(null, [12, 200])!,
    "Ghost sync should advance the checkpoint after a non-truncated incremental fetch."
);

var installationRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-player-account-resolver-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(installationRoot);
try
{
    var installationStore = Activator.CreateInstance(
        installationStoreType,
        Path.Combine(installationRoot, "installation.bpp"),
        Path.Combine(installationRoot, "installation.key")
    ) ?? throw new InvalidOperationException("InstallationRecordStore should be constructible.");
    var installationRecord = Activator.CreateInstance(installationRecordType)
        ?? throw new InvalidOperationException("InstallationRecord should be constructible.");
    installationRecordType.GetProperty("PlayerAccountId")!.SetValue(installationRecord, "player-installation-001");
    installationRecordType.GetProperty("InstallationId")!.SetValue(installationRecord, "installation-001");
    installationRecordType.GetProperty("ApiBaseUrl")!.SetValue(installationRecord, "https://mod-api-v3.bazaarplusplus.com");
    InvokeVoid(
        installationStoreType,
        installationStore,
        "Save",
        [installationRecord, new byte[] { 48, 130, 1, 0 }]
    );
    Assert(
        (string?)resolvePlayerAccountId!.Invoke(null, [null, installationStore])
            == "player-installation-001",
        "PlayerAccountIdResolver should fall back to the installation record when the runtime cache is unavailable."
    );
    Assert(
        (string?)resolvePlayerAccountId.Invoke(null, [" player-cache-001 ", installationStore])
            == "player-cache-001",
        "PlayerAccountIdResolver should prefer the trimmed runtime cache account id."
    );
}
finally
{
    if (Directory.Exists(installationRoot))
        Directory.Delete(installationRoot, recursive: true);
}

var artifact = Activator.CreateInstance(runArtifactType)
    ?? throw new InvalidOperationException("RunArtifactV3 should be constructible.");
runArtifactType.GetProperty("RunId")!.SetValue(artifact, "run-001");

var battleArtifact = Activator.CreateInstance(runArtifactBattleType)
    ?? throw new InvalidOperationException("RunArtifactBattleV3 should be constructible.");
runArtifactBattleType.GetProperty("BattleId")!.SetValue(battleArtifact, "battle-001");

var manifestArtifact = Activator.CreateInstance(battleManifestArtifactType)
    ?? throw new InvalidOperationException("BattleManifestArtifactV3 should be constructible.");
battleManifestArtifactType.GetProperty("BattleId")!.SetValue(manifestArtifact, "battle-001");
battleManifestArtifactType
    .GetProperty("RecordedAtUtc")!
    .SetValue(manifestArtifact, "2026-04-11T00:00:00.000Z");
battleManifestArtifactType.GetProperty("Day")!.SetValue(manifestArtifact, 7);
battleManifestArtifactType.GetProperty("Hour")!.SetValue(manifestArtifact, 2);
battleManifestArtifactType
    .GetProperty("EncounterId")!
    .SetValue(manifestArtifact, "encounter-001");
battleManifestArtifactType.GetProperty("CombatKind")!.SetValue(manifestArtifact, "PVPCombat");
battleManifestArtifactType.GetProperty("Result")!.SetValue(manifestArtifact, "Won");
battleManifestArtifactType
    .GetProperty("WinnerCombatantId")!
    .SetValue(manifestArtifact, "Player");
battleManifestArtifactType
    .GetProperty("LoserCombatantId")!
    .SetValue(manifestArtifact, "Opponent");
runArtifactBattleType.GetProperty("Manifest")!.SetValue(battleArtifact, manifestArtifact);

var participantsArtifact = Activator.CreateInstance(battleParticipantsArtifactType)
    ?? throw new InvalidOperationException("BattleParticipantsArtifactV3 should be constructible.");
battleParticipantsArtifactType.GetProperty("PlayerName")!.SetValue(participantsArtifact, "RemoteGhost");
battleParticipantsArtifactType
    .GetProperty("PlayerAccountId")!
    .SetValue(participantsArtifact, "remote-account-001");
battleParticipantsArtifactType.GetProperty("PlayerHero")!.SetValue(participantsArtifact, "Dooley");
battleParticipantsArtifactType.GetProperty("PlayerRank")!.SetValue(participantsArtifact, "Bronze");
battleParticipantsArtifactType.GetProperty("PlayerRating")!.SetValue(participantsArtifact, 1200);
battleParticipantsArtifactType.GetProperty("PlayerLevel")!.SetValue(participantsArtifact, 9);
battleParticipantsArtifactType
    .GetProperty("OpponentName")!
    .SetValue(participantsArtifact, "LocalPlayer");
battleParticipantsArtifactType
    .GetProperty("OpponentAccountId")!
    .SetValue(participantsArtifact, "local-account-001");
battleParticipantsArtifactType
    .GetProperty("OpponentHero")!
    .SetValue(participantsArtifact, "Vanessa");
battleParticipantsArtifactType
    .GetProperty("OpponentRank")!
    .SetValue(participantsArtifact, "Legendary");
battleParticipantsArtifactType
    .GetProperty("OpponentRating")!
    .SetValue(participantsArtifact, 1500);
battleParticipantsArtifactType.GetProperty("OpponentLevel")!.SetValue(participantsArtifact, 12);
runArtifactBattleType.GetProperty("Participants")!.SetValue(battleArtifact, participantsArtifact);

var snapshotsArtifact = Activator.CreateInstance(battleSnapshotsArtifactType)
    ?? throw new InvalidOperationException("BattleSnapshotsArtifactV3 should be constructible.");
var cardSetListType = typeof(List<>).MakeGenericType(cardSetCaptureArtifactType);
var cardSetList = (IList)(
    Activator.CreateInstance(cardSetListType)
    ?? throw new InvalidOperationException("Card set list should be constructible.")
);
var cardSetCapture = Activator.CreateInstance(cardSetCaptureArtifactType)
    ?? throw new InvalidOperationException("CardSetCaptureArtifactV3 should be constructible.");
cardSetCaptureArtifactType.GetProperty("Label")!.SetValue(cardSetCapture, "player_hand");
cardSetCaptureArtifactType.GetProperty("Status")!.SetValue(cardSetCapture, "Captured");
cardSetCaptureArtifactType.GetProperty("Source")!.SetValue(cardSetCapture, "LiveRetry");
var cardSnapshot = Activator.CreateInstance(cardSnapshotType)
    ?? throw new InvalidOperationException("CombatReplayCardSnapshot should be constructible.");
cardSnapshotType.GetProperty("InstanceId")!.SetValue(cardSnapshot, "card-instance-001");
cardSnapshotType.GetProperty("TemplateId")!.SetValue(cardSnapshot, "card-template-001");
cardSnapshotType.GetProperty("Name")!.SetValue(cardSnapshot, "Test Card");
var cardSnapshotListType = typeof(List<>).MakeGenericType(cardSnapshotType);
var cardSnapshotList = (IList)(
    Activator.CreateInstance(cardSnapshotListType)
    ?? throw new InvalidOperationException("Combat replay card snapshot list should be constructible.")
);
cardSnapshotList.Add(cardSnapshot);
cardSetCaptureArtifactType.GetProperty("Items")!.SetValue(cardSetCapture, cardSnapshotList);
cardSetList.Add(cardSetCapture);
battleSnapshotsArtifactType.GetProperty("CardSets")!.SetValue(snapshotsArtifact, cardSetList);
runArtifactBattleType.GetProperty("Snapshots")!.SetValue(battleArtifact, snapshotsArtifact);

var replayPayloadArtifact = Activator.CreateInstance(replayPayloadArtifactType)
    ?? throw new InvalidOperationException("ReplayPayloadArtifactV3 should be constructible.");
replayPayloadArtifactType.GetProperty("BattleId")!.SetValue(replayPayloadArtifact, "battle-001");
replayPayloadArtifactType.GetProperty("Version")!.SetValue(replayPayloadArtifact, 1);
replayPayloadArtifactType
    .GetProperty("SpawnMessageBytes")!
    .SetValue(replayPayloadArtifact, new byte[] { 1 });
replayPayloadArtifactType
    .GetProperty("CombatMessageBytes")!
    .SetValue(replayPayloadArtifact, new byte[] { 2 });
replayPayloadArtifactType
    .GetProperty("DespawnMessageBytes")!
    .SetValue(replayPayloadArtifact, new byte[] { 3 });
runArtifactBattleType.GetProperty("ReplayPayload")!.SetValue(battleArtifact, replayPayloadArtifact);

var battlesListType = typeof(List<>).MakeGenericType(runArtifactBattleType);
var battlesList = (IList)(
    Activator.CreateInstance(battlesListType)
    ?? throw new InvalidOperationException("Run artifact battle list should be constructible.")
);
battlesList.Add(battleArtifact);
runArtifactType.GetProperty("Battles")!.SetValue(artifact, battlesList);

var artifactBytes = (byte[])(
    serializeArtifact!.Invoke(null, [artifact])
    ?? throw new InvalidOperationException("Serialize should return artifact bytes.")
);

var extractedPayload =
    tryExtractPayloadFromArtifact!.Invoke(null, ["battle-001", artifactBytes])
    ?? throw new InvalidOperationException(
        "TryExtractPayloadFromArtifact should return a payload for a matching battle."
    );
var extractedPayloadType = extractedPayload.GetType();
Assert(
    (string?)extractedPayloadType.GetProperty("BattleId")?.GetValue(extractedPayload) == "battle-001",
    "Artifact extraction should preserve the selected battle id."
);
var extractedReplayPayload =
    extractedPayloadType.GetProperty("ReplayPayload")?.GetValue(extractedPayload)
    ?? throw new InvalidOperationException("Extracted payload should include replay payload.");
var replayPayloadType = extractedReplayPayload.GetType();
Assert(
    (string?)replayPayloadType.GetProperty("BattleId")?.GetValue(extractedReplayPayload)
        == "battle-001",
    "Artifact extraction should deserialize the replay payload for the selected battle."
);
var extractedManifest =
    extractedPayloadType.GetProperty("BattleManifest")?.GetValue(extractedPayload)
    ?? throw new InvalidOperationException("Extracted payload should include battle manifest.");
var extractedManifestType = extractedManifest.GetType();
var extractedParticipants =
    extractedManifestType.GetProperty("Participants")?.GetValue(extractedManifest)
    ?? throw new InvalidOperationException("Extracted manifest should include participants.");
var extractedParticipantsType = extractedParticipants.GetType();
Assert(
    (string?)extractedParticipantsType.GetProperty("PlayerHero")?.GetValue(extractedParticipants)
        == "Dooley",
    "Artifact extraction should preserve the remote player hero."
);
Assert(
    (string?)extractedParticipantsType.GetProperty("OpponentHero")?.GetValue(extractedParticipants)
        == "Vanessa",
    "Artifact extraction should preserve the local opponent hero."
);
Assert(
    (string?)extractedParticipantsType.GetProperty("OpponentName")?.GetValue(extractedParticipants)
        == "LocalPlayer",
    "Artifact extraction should preserve the local opponent name."
);
Assert(
    (string?)extractedParticipantsType.GetProperty("OpponentRank")?.GetValue(extractedParticipants)
        == "Legendary",
    "Artifact extraction should preserve the local opponent rank."
);
Assert(
    (int?)extractedParticipantsType.GetProperty("OpponentRating")?.GetValue(extractedParticipants)
        == 1500,
    "Artifact extraction should preserve the local opponent rating."
);

var ghostPayloadStorePath = Path.Combine(
    Path.GetTempPath(),
    "bpp-ghost-payload-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(ghostPayloadStorePath);
try
{
    var ghostPayloadStore = Activator.CreateInstance(ghostPayloadStoreType, ghostPayloadStorePath)
        ?? throw new InvalidOperationException("GhostBattlePayloadStore should be constructible.");
    InvokeVoid(ghostPayloadStoreType, ghostPayloadStore, "Save", [extractedPayload]);
    var reloadedGhostPayload =
        Invoke<object?>(ghostPayloadStoreType, ghostPayloadStore, "Load", ["battle-001"])
        ?? throw new InvalidOperationException("GhostBattlePayloadStore should reload saved payloads.");
    var reloadedReplayPayload =
        reloadedPayloadType(reloadedGhostPayload);
    var reloadedManifest =
        reloadedGhostPayload.GetType().GetProperty("BattleManifest")?.GetValue(reloadedGhostPayload)
        ?? throw new InvalidOperationException("Reloaded ghost payload should include battle manifest.");
    Assert(
        ((byte[]?)replayPayloadType.GetProperty("CombatMessageBytes")?.GetValue(reloadedReplayPayload))
            ?.SequenceEqual(new byte[] { 2 }) == true,
        "Ghost payload store should preserve binary replay payload bytes."
    );
    Assert(
        (string?)extractedManifestType.GetProperty("EncounterId")?.GetValue(reloadedManifest)
            == "encounter-001",
        "Ghost payload store should preserve manifest encounter metadata."
    );
    Assert(
        (DateTimeOffset?)extractedManifestType.GetProperty("RecordedAtUtc")?.GetValue(reloadedManifest)
            == DateTimeOffset.Parse("2026-04-11T00:00:00.000Z"),
        "Ghost payload store should preserve manifest timestamps."
    );
    var reloadedSnapshots =
        extractedManifestType.GetProperty("Snapshots")?.GetValue(reloadedManifest)
        ?? throw new InvalidOperationException("Reloaded ghost payload should include snapshots.");
    var playerHandCapture =
        reloadedSnapshots.GetType().GetProperty("PlayerHand")?.GetValue(reloadedSnapshots)
        ?? throw new InvalidOperationException("Reloaded ghost snapshots should include player hand.");
    var playerHandItems =
        (IEnumerable?)playerHandCapture.GetType().GetProperty("Items")?.GetValue(playerHandCapture);
    var reloadedCardSnapshot =
        playerHandItems?.Cast<object>().SingleOrDefault()
        ?? throw new InvalidOperationException("Reloaded ghost player hand should preserve card snapshots.");
    Assert(
        (string?)cardSnapshotType.GetProperty("InstanceId")?.GetValue(reloadedCardSnapshot)
            == "card-instance-001",
        "Ghost payload store should preserve snapshot card identities."
    );
}
finally
{
    if (Directory.Exists(ghostPayloadStorePath))
        Directory.Delete(ghostPayloadStorePath, recursive: true);
}

Assert(
    importRecordType.GetProperty("PlayerName") != null,
    "GhostBattleImportRecord should preserve the raw remote player_name field."
);
Assert(
    importRecordType.GetProperty("PlayerAccountId") != null,
    "GhostBattleImportRecord should preserve the raw remote player_account_id field."
);

var rawBattlePayload = JObject.Parse(
    """
    {
      "battle_id": "ghost-battle-001",
      "recorded_at_utc": "2026-04-11T00:00:00.000Z",
      "day": 7,
      "hour": 2,
      "encounter_id": "encounter-001",
      "player_name": "RemoteGhost",
      "player_account_id": "remote-account-001",
      "player_hero": "Dooley",
      "player_rank": "Bronze",
      "player_rating": 1200,
      "player_level": 9,
      "opponent_hero": "Vanessa",
      "opponent_rank": "Legendary",
      "opponent_rating": 1500,
      "opponent_level": 12,
      "opponent_account_id": "local-account-001",
      "combat_kind": "PVPCombat",
      "result": "Won",
      "winner_combatant_id": "Player",
      "loser_combatant_id": "Opponent",
      "replay": {
        "available": true
      }
    }
    """
);

var importRecord =
    tryParseBattle!.Invoke(null, [rawBattlePayload])
    ?? throw new InvalidOperationException("TryParseBattle should return an import record.");
Assert(
    (string?)importRecordType.GetProperty("PlayerName")?.GetValue(importRecord) == "RemoteGhost",
    "Ghost import should preserve remote player_name without flipping."
);
Assert(
    (string?)importRecordType.GetProperty("PlayerAccountId")?.GetValue(importRecord)
        == "remote-account-001",
    "Ghost import should preserve remote player_account_id without flipping."
);
Assert(
    (string?)importRecordType.GetProperty("PlayerHero")?.GetValue(importRecord) == "Dooley",
    "Ghost import should preserve the remote player hero without flipping."
);
Assert(
    (string?)importRecordType.GetProperty("OpponentAccountId")?.GetValue(importRecord)
        == "local-account-001",
    "Ghost import should preserve the local opponent account id without flipping."
);
Assert(
    (string?)importRecordType.GetProperty("Result")?.GetValue(importRecord) == "Won",
    "Ghost import should preserve the remote result without flipping."
);
Assert(
    (string?)importRecordType.GetProperty("WinnerCombatantId")?.GetValue(importRecord)
        == "Player",
    "Ghost import should preserve winner_combatant_id without flipping."
);

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-ghost-battle-sync-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);

try
{
    var repository =
        Activator.CreateInstance(repositoryType, Path.Combine(tempRoot, "history.db"))
        ?? throw new InvalidOperationException("Failed to create HistoryPanelRepository.");
    var importRecords = Array.CreateInstance(importRecordType, 1);
    importRecords.SetValue(importRecord, 0);
    InvokeVoid(
        repositoryType,
        repository,
        "UpsertGhostBattles",
        ["local-account-001", importRecords]
    );

    var ghostBattles =
        (System.Collections.IEnumerable)
            Invoke<object>(
                repositoryType,
                repository,
                "ListRecentGhostBattles",
                ["local-account-001", 20]
            );
    var projectedBattle =
        ghostBattles.Cast<object>().SingleOrDefault()
        ?? throw new InvalidOperationException("Expected one projected ghost battle.");

    Assert(
        (string?)battleRecordType.GetProperty("OpponentName")?.GetValue(projectedBattle)
            == "RemoteGhost",
        "Ghost repository reads should project the remote uploader into OpponentName."
    );
    Assert(
        (string?)battleRecordType.GetProperty("OpponentAccountId")?.GetValue(projectedBattle)
            == "remote-account-001",
        "Ghost repository reads should project the remote uploader account into OpponentAccountId."
    );
    Assert(
        (string?)battleRecordType.GetProperty("PlayerHero")?.GetValue(projectedBattle)
            == "Vanessa",
        "Ghost repository reads should project the local hero from the raw opponent_hero field."
    );
    Assert(
        (string?)battleRecordType.GetProperty("Result")?.GetValue(projectedBattle) == "Lost",
        "Ghost repository reads should project the result into local-player perspective."
    );

    var resolvedOutcome = resolveGhostBattleOutcome!.Invoke(null, [projectedBattle]);
    var lostOutcome = Enum.Parse(coordinatorOutcomeType, "Lost");
    Assert(
        Equals(resolvedOutcome, lostOutcome),
        "Ghost battle filtering should use the projected local-player outcome."
    );
}
finally
{
    if (Directory.Exists(tempRoot))
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        catch (IOException)
        {
            try
            {
                await Task.Delay(200);
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup on Windows when SQLite releases the file handle late.
            }
        }
    }
}

var tryCreateRoutes = routesType.GetMethod("TryCreate", BindingFlags.Public | BindingFlags.Static);
Assert(tryCreateRoutes != null, "V3Routes should expose a static TryCreate factory.");
var routes =
    tryCreateRoutes!.Invoke(null, ["https://mod-api-v3.bazaarplusplus.com"])
    ?? throw new InvalidOperationException("Failed to create V3Routes.");
var queryGhostBattles = (string)(
    routesType.GetProperty("QueryGhostBattles")!.GetValue(routes)
    ?? throw new InvalidOperationException("QueryGhostBattles should be populated.")
);
var replayLink = (string)(
    routesType.GetMethod("CreateReplayLink")!.Invoke(routes, ["battle-001"])
    ?? throw new InvalidOperationException("CreateReplayLink should return a route.")
);
Assert(
    queryGhostBattles == "https://mod-api-v3.bazaarplusplus.com/ghost-battles",
    "V3Routes should publish the /ghost-battles query route."
);
Assert(
    replayLink == "https://mod-api-v3.bazaarplusplus.com/ghost-battles/battle-001/replay-link",
    "V3Routes should publish replay links under /ghost-battles/{battle_id}/replay-link."
);

Console.WriteLine("Ghost battle sync checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static T Invoke<T>(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    return (T)(
        method.Invoke(instance, args)
        ?? throw new InvalidOperationException($"Method returned null: {type.FullName}.{name}")
    );
}

static object reloadedPayloadType(object reloadedGhostPayload)
{
    return reloadedGhostPayload.GetType().GetProperty("ReplayPayload")?.GetValue(reloadedGhostPayload)
        ?? throw new InvalidOperationException("Reloaded ghost payload should include replay payload.");
}

static void InvokeVoid(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    method.Invoke(instance, args);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
