#nullable enable
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Newtonsoft.Json.Linq;

var syncServiceType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Ghost.GhostBattleSyncService");
var apiClientType = RequireModApiType("BazaarPlusPlus.ModApi.Clients.GhostBattleClient");
var repositoryType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryPanelRepository"
);
var dataServiceType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryPanelDataService"
);
var battleRecordType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Data.HistoryBattleRecord");
var formatterType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelFormatter");
var coordinatorType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelCoordinator");
var coordinatorStateType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelState");
var coordinatorDependenciesType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelDependencies"
);
var coordinatorOutcomeType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelCoordinator+GhostBattleOutcome"
);
var importRecordType = RequireModApiType("BazaarPlusPlus.ModApi.Models.GhostBattleImportRecord");
var routesType = RequireModApiType("BazaarPlusPlus.ModApi.ModApiRoutes");
var artifactCodecType = RequireModApiType("BazaarPlusPlus.ModApi.RunBundleArtifactCodec");
var ghostPayloadStoreType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.Ghost.GhostBattlePayloadStore"
);
var runArtifactType = RequireModApiType("BazaarPlusPlus.ModApi.Models.RunArtifact");
var runArtifactBattleType = RequireModApiType("BazaarPlusPlus.ModApi.Models.RunArtifactBattle");
var battleManifestArtifactType = RequireModApiType(
    "BazaarPlusPlus.ModApi.Models.BattleManifestArtifact"
);
var battleParticipantsArtifactType = RequireModApiType(
    "BazaarPlusPlus.ModApi.Models.BattleParticipantsArtifact"
);
var battleSnapshotsArtifactType = RequireModApiType(
    "BazaarPlusPlus.ModApi.Models.BattleSnapshotsArtifact"
);
var replayPayloadArtifactType = RequireModApiType(
    "BazaarPlusPlus.ModApi.Models.ReplayPayloadArtifact"
);
var cardSetCaptureArtifactType = RequireModApiType(
    "BazaarPlusPlus.ModApi.Models.CardSetCaptureArtifact"
);
var cardSetItemArtifactType = RequireModApiType("BazaarPlusPlus.ModApi.Models.CardSetItemArtifact");
var cardSnapshotType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCardSnapshot");

var shouldAdvanceCheckpoint = syncServiceType.GetMethod(
    "ShouldAdvanceCheckpoint",
    BindingFlags.NonPublic | BindingFlags.Static
);
var tryExtractPayloadFromArtifact = syncServiceType.GetMethod(
    "TryExtractPayloadFromArtifact",
    BindingFlags.NonPublic | BindingFlags.Static
);
var tryParseBattle = apiClientType.GetMethod(
    "TryParseBattle",
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
var isGhostOpponentEliminated = formatterType.GetMethod(
    "IsGhostOpponentEliminated",
    BindingFlags.Public | BindingFlags.Static
);
Assert(
    shouldAdvanceCheckpoint != null,
    "GhostBattleSyncService should expose checkpoint advancement logic."
);
Assert(
    tryExtractPayloadFromArtifact != null,
    "GhostBattleSyncService should expose artifact extraction logic for replay downloads."
);
Assert(serializeArtifact != null, "RunBundleArtifactCodec should expose a serialize helper.");
Assert(tryParseBattle != null, "GhostBattleClient should expose battle parsing logic.");
Assert(
    resolveGhostBattleOutcome != null,
    "HistoryPanelCoordinator should expose ghost-outcome resolution logic."
);
Assert(
    isGhostOpponentEliminated != null,
    "HistoryPanelFormatter should expose ghost opponent elimination logic."
);

{
    var state =
        Activator.CreateInstance(coordinatorStateType)
        ?? throw new InvalidOperationException("HistoryPanelState should be constructible.");
    coordinatorStateType.GetProperty("GhostSyncInProgress")!.SetValue(state, true);
    coordinatorStateType.GetProperty("ReplayActionInProgress")!.SetValue(state, true);
    coordinatorStateType.GetProperty("FinalBuildRefreshInProgress")!.SetValue(state, true);

    var dependencies =
        Activator.CreateInstance(coordinatorDependenciesType, null, null, null, null)
        ?? throw new InvalidOperationException("HistoryPanelDependencies should be constructible.");
    var coordinator =
        Activator.CreateInstance(
            coordinatorType,
            state,
            dependencies,
            (Action)(() => { }),
            (Action)(() => { }),
            (Action<bool>)(_ => { })
        )
        ?? throw new InvalidOperationException("HistoryPanelCoordinator should be constructible.");

    InvokeVoid(coordinatorType, coordinator, "OnPanelHidden", []);

    Assert(
        coordinatorStateType.GetProperty("GhostSyncInProgress")!.GetValue(state) is false,
        "Hiding the history panel should clear ghost sync in-progress state."
    );
    Assert(
        coordinatorStateType.GetProperty("ReplayActionInProgress")!.GetValue(state) is false,
        "Hiding the history panel should clear replay in-progress state."
    );
    Assert(
        coordinatorStateType.GetProperty("FinalBuildRefreshInProgress")!.GetValue(state) is false,
        "Hiding the history panel should clear final-build refresh in-progress state."
    );
}

Assert(
    !(bool)shouldAdvanceCheckpoint!.Invoke(null, [200, 200])!,
    "Ghost sync should not advance the checkpoint when the returned batch hits the limit."
);
Assert(
    (bool)shouldAdvanceCheckpoint.Invoke(null, [12, 200])!,
    "Ghost sync should advance the checkpoint after a non-truncated incremental fetch."
);

var artifact =
    Activator.CreateInstance(runArtifactType)
    ?? throw new InvalidOperationException("RunArtifactV3 should be constructible.");
runArtifactType.GetProperty("RunId")!.SetValue(artifact, "run-001");

var battleArtifact =
    Activator.CreateInstance(runArtifactBattleType)
    ?? throw new InvalidOperationException("RunArtifactBattleV3 should be constructible.");
runArtifactBattleType.GetProperty("BattleId")!.SetValue(battleArtifact, "battle-001");

var manifestArtifact =
    Activator.CreateInstance(battleManifestArtifactType)
    ?? throw new InvalidOperationException("BattleManifestArtifactV3 should be constructible.");
battleManifestArtifactType.GetProperty("BattleId")!.SetValue(manifestArtifact, "battle-001");
battleManifestArtifactType
    .GetProperty("RecordedAtUtc")!
    .SetValue(manifestArtifact, "2026-04-11T00:00:00.000Z");
battleManifestArtifactType.GetProperty("Day")!.SetValue(manifestArtifact, 7);
battleManifestArtifactType.GetProperty("Hour")!.SetValue(manifestArtifact, 2);
battleManifestArtifactType.GetProperty("EncounterId")!.SetValue(manifestArtifact, "encounter-001");
battleManifestArtifactType.GetProperty("CombatKind")!.SetValue(manifestArtifact, "PVPCombat");
battleManifestArtifactType.GetProperty("Result")!.SetValue(manifestArtifact, "Won");
battleManifestArtifactType.GetProperty("WinnerCombatantId")!.SetValue(manifestArtifact, "Player");
battleManifestArtifactType.GetProperty("LoserCombatantId")!.SetValue(manifestArtifact, "Opponent");
runArtifactBattleType.GetProperty("Manifest")!.SetValue(battleArtifact, manifestArtifact);

var participantsArtifact =
    Activator.CreateInstance(battleParticipantsArtifactType)
    ?? throw new InvalidOperationException("BattleParticipantsArtifactV3 should be constructible.");
battleParticipantsArtifactType
    .GetProperty("PlayerName")!
    .SetValue(participantsArtifact, "RemoteGhost");
battleParticipantsArtifactType
    .GetProperty("PlayerAccountId")!
    .SetValue(participantsArtifact, "remote-account-001");
battleParticipantsArtifactType.GetProperty("PlayerHero")!.SetValue(participantsArtifact, "Dooley");
battleParticipantsArtifactType.GetProperty("PlayerRank")!.SetValue(participantsArtifact, "Bronze");
battleParticipantsArtifactType.GetProperty("PlayerRating")!.SetValue(participantsArtifact, 1200);
battleParticipantsArtifactType.GetProperty("PlayerLevel")!.SetValue(participantsArtifact, 9);
battleParticipantsArtifactType.GetProperty("PlayerPrestige")!.SetValue(participantsArtifact, 18);
battleParticipantsArtifactType.GetProperty("PlayerVictories")!.SetValue(participantsArtifact, 3);
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
battleParticipantsArtifactType.GetProperty("OpponentRating")!.SetValue(participantsArtifact, 1500);
battleParticipantsArtifactType.GetProperty("OpponentLevel")!.SetValue(participantsArtifact, 12);
battleParticipantsArtifactType.GetProperty("OpponentPrestige")!.SetValue(participantsArtifact, 12);
battleParticipantsArtifactType.GetProperty("OpponentVictories")!.SetValue(participantsArtifact, 6);
runArtifactBattleType.GetProperty("Participants")!.SetValue(battleArtifact, participantsArtifact);

var snapshotsArtifact =
    Activator.CreateInstance(battleSnapshotsArtifactType)
    ?? throw new InvalidOperationException("BattleSnapshotsArtifactV3 should be constructible.");
var cardSetListType = typeof(List<>).MakeGenericType(cardSetCaptureArtifactType);
var cardSetList = (IList)(
    Activator.CreateInstance(cardSetListType)
    ?? throw new InvalidOperationException("Card set list should be constructible.")
);
var cardSetCapture =
    Activator.CreateInstance(cardSetCaptureArtifactType)
    ?? throw new InvalidOperationException("CardSetCaptureArtifactV3 should be constructible.");
cardSetCaptureArtifactType.GetProperty("Label")!.SetValue(cardSetCapture, "player_hand");
cardSetCaptureArtifactType.GetProperty("Status")!.SetValue(cardSetCapture, "Captured");
cardSetCaptureArtifactType.GetProperty("Source")!.SetValue(cardSetCapture, "LiveRetry");
var cardItemArtifact =
    Activator.CreateInstance(cardSetItemArtifactType)
    ?? throw new InvalidOperationException("CardSetItemArtifact should be constructible.");
cardSetItemArtifactType.GetProperty("InstanceId")!.SetValue(cardItemArtifact, "card-instance-001");
cardSetItemArtifactType.GetProperty("TemplateId")!.SetValue(cardItemArtifact, "card-template-001");
cardSetItemArtifactType.GetProperty("Name")!.SetValue(cardItemArtifact, "Test Card");
var cardItemArtifactListType = typeof(List<>).MakeGenericType(cardSetItemArtifactType);
var cardItemArtifactList = (IList)(
    Activator.CreateInstance(cardItemArtifactListType)
    ?? throw new InvalidOperationException("CardSetItemArtifact list should be constructible.")
);
cardItemArtifactList.Add(cardItemArtifact);
cardSetCaptureArtifactType.GetProperty("Items")!.SetValue(cardSetCapture, cardItemArtifactList);
cardSetList.Add(cardSetCapture);
battleSnapshotsArtifactType.GetProperty("CardSets")!.SetValue(snapshotsArtifact, cardSetList);
runArtifactBattleType.GetProperty("Snapshots")!.SetValue(battleArtifact, snapshotsArtifact);

var replayPayloadArtifact =
    Activator.CreateInstance(replayPayloadArtifactType)
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
    (string?)extractedPayloadType.GetProperty("BattleId")?.GetValue(extractedPayload)
        == "battle-001",
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
Assert(
    (int?)extractedParticipantsType.GetProperty("PlayerPrestige")?.GetValue(extractedParticipants)
        == 18
        && (int?)
            extractedParticipantsType
                .GetProperty("PlayerVictories")
                ?.GetValue(extractedParticipants) == 3
        && (int?)
            extractedParticipantsType
                .GetProperty("OpponentPrestige")
                ?.GetValue(extractedParticipants) == 12
        && (int?)
            extractedParticipantsType
                .GetProperty("OpponentVictories")
                ?.GetValue(extractedParticipants) == 6,
    "Artifact extraction should preserve participant prestige and victories."
);

var ghostPayloadStorePath = Path.Combine(
    Path.GetTempPath(),
    "bpp-ghost-payload-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(ghostPayloadStorePath);
try
{
    var ghostPayloadStore =
        Activator.CreateInstance(ghostPayloadStoreType, ghostPayloadStorePath)
        ?? throw new InvalidOperationException("GhostBattlePayloadStore should be constructible.");
    InvokeVoid(ghostPayloadStoreType, ghostPayloadStore, "Save", [extractedPayload]);
    var reloadedGhostPayload =
        Invoke<object?>(ghostPayloadStoreType, ghostPayloadStore, "Load", ["battle-001"])
        ?? throw new InvalidOperationException(
            "GhostBattlePayloadStore should reload saved payloads."
        );
    var reloadedReplayPayload = reloadedPayloadType(reloadedGhostPayload);
    var reloadedManifest =
        reloadedGhostPayload.GetType().GetProperty("BattleManifest")?.GetValue(reloadedGhostPayload)
        ?? throw new InvalidOperationException(
            "Reloaded ghost payload should include battle manifest."
        );
    Assert(
        (
            (byte[]?)
                replayPayloadType.GetProperty("CombatMessageBytes")?.GetValue(reloadedReplayPayload)
        )?.SequenceEqual(new byte[] { 2 }) == true,
        "Ghost payload store should preserve binary replay payload bytes."
    );
    Assert(
        (string?)extractedManifestType.GetProperty("EncounterId")?.GetValue(reloadedManifest)
            == "encounter-001",
        "Ghost payload store should preserve manifest encounter metadata."
    );
    Assert(
        (DateTimeOffset?)
            extractedManifestType.GetProperty("RecordedAtUtc")?.GetValue(reloadedManifest)
            == DateTimeOffset.Parse("2026-04-11T00:00:00.000Z"),
        "Ghost payload store should preserve manifest timestamps."
    );
    var reloadedSnapshots =
        extractedManifestType.GetProperty("Snapshots")?.GetValue(reloadedManifest)
        ?? throw new InvalidOperationException("Reloaded ghost payload should include snapshots.");
    var playerHandCapture =
        reloadedSnapshots.GetType().GetProperty("PlayerHand")?.GetValue(reloadedSnapshots)
        ?? throw new InvalidOperationException(
            "Reloaded ghost snapshots should include player hand."
        );
    var playerHandItems = (IEnumerable?)
        playerHandCapture.GetType().GetProperty("Items")?.GetValue(playerHandCapture);
    var reloadedCardSnapshot =
        playerHandItems?.Cast<object>().SingleOrDefault()
        ?? throw new InvalidOperationException(
            "Reloaded ghost player hand should preserve card snapshots."
        );
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

var freshRecordedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("o");
var rawBattlePayload = JObject.Parse(
    $$"""
    {
      "battle_id": "ghost-battle-001",
      "recorded_at_utc": "{{freshRecordedAtUtc}}",
      "day": 7,
      "hour": 2,
      "encounter_id": "encounter-001",
      "player_name": "RemoteGhost",
      "player_account_id": "remote-account-001",
      "player_hero": "Dooley",
      "player_rank": "Bronze",
      "player_rating": 1200,
      "player_level": 9,
      "player_prestige": 18,
      "player_victories": 3,
      "opponent_hero": "Vanessa",
      "opponent_rank": "Legendary",
      "opponent_rating": 1500,
      "opponent_level": 12,
      "opponent_prestige": 12,
      "opponent_victories": 6,
      "opponent_account_id": "local-account-001",
      "combat_kind": "PVPCombat",
      "result": "Won",
      "winner_combatant_id": "Player",
      "loser_combatant_id": "Opponent",
      "is_final_battle": true
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
    (string?)importRecordType.GetProperty("WinnerCombatantId")?.GetValue(importRecord) == "Player",
    "Ghost import should preserve winner_combatant_id without flipping."
);
Assert(
    (bool)(importRecordType.GetProperty("IsBundleFinalBattle")?.GetValue(importRecord) ?? false),
    "Ghost import should preserve the bundle-final battle marker."
);
Assert(
    (int?)importRecordType.GetProperty("PlayerPrestige")?.GetValue(importRecord) == 18
        && (int?)importRecordType.GetProperty("PlayerVictories")?.GetValue(importRecord) == 3
        && (int?)importRecordType.GetProperty("OpponentPrestige")?.GetValue(importRecord) == 12
        && (int?)importRecordType.GetProperty("OpponentVictories")?.GetValue(importRecord) == 6,
    "Ghost import should preserve participant prestige and victories."
);

var localWinRecordedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-4).ToString("o");
var localWinBattlePayload = JObject.Parse(
    $$"""
    {
      "battle_id": "ghost-battle-local-win",
      "recorded_at_utc": "{{localWinRecordedAtUtc}}",
      "day": 10,
      "player_name": "RemoteFinal",
      "player_account_id": "remote-account-002",
      "player_hero": "Dooley",
      "opponent_name": "LocalPlayer",
      "opponent_account_id": "local-account-001",
      "opponent_hero": "Vanessa",
      "combat_kind": "PVPCombat",
      "result": "Lost",
      "winner_combatant_id": "Opponent",
      "loser_combatant_id": "Player",
      "is_final_battle": true
    }
    """
);
var localWinImportRecord =
    tryParseBattle!.Invoke(null, [localWinBattlePayload])
    ?? throw new InvalidOperationException("TryParseBattle should return the local-win record.");

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
    var importRecords = Array.CreateInstance(importRecordType, 2);
    importRecords.SetValue(importRecord, 0);
    importRecords.SetValue(localWinImportRecord, 1);
    InvokeVoid(
        repositoryType,
        repository,
        "UpsertGhostBattles",
        ["local-account-001", importRecords]
    );

    var ghostBattles = (System.Collections.IEnumerable)
        Invoke<object>(repositoryType, repository, "ListRecentGhostBattles", [20]);
    var projectedBattles = ghostBattles.Cast<object>().ToList();
    var projectedBattle =
        projectedBattles.SingleOrDefault(battle =>
            (string?)battleRecordType.GetProperty("BattleId")?.GetValue(battle)
            == "ghost-battle-001"
        ) ?? throw new InvalidOperationException("Expected the projected remote-win ghost battle.");
    var projectedLocalWinBattle =
        projectedBattles.SingleOrDefault(battle =>
            (string?)battleRecordType.GetProperty("BattleId")?.GetValue(battle)
            == "ghost-battle-local-win"
        ) ?? throw new InvalidOperationException("Expected the projected local-win ghost battle.");

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
        (string?)battleRecordType.GetProperty("PlayerHero")?.GetValue(projectedBattle) == "Vanessa",
        "Ghost repository reads should project the local hero from the raw opponent_hero field."
    );
    Assert(
        (string?)battleRecordType.GetProperty("Result")?.GetValue(projectedBattle) == "Lost",
        "Ghost repository reads should project the result into local-player perspective."
    );
    Assert(
        (bool)(
            battleRecordType.GetProperty("IsBundleFinalBattle")?.GetValue(projectedBattle) ?? false
        ),
        "Ghost repository reads should preserve the bundle-final battle marker."
    );
    Assert(
        (int?)battleRecordType.GetProperty("PlayerPrestige")?.GetValue(projectedBattle) == 12
            && (int?)battleRecordType.GetProperty("PlayerVictories")?.GetValue(projectedBattle) == 6
            && (int?)battleRecordType.GetProperty("OpponentPrestige")?.GetValue(projectedBattle)
                == 18
            && (int?)battleRecordType.GetProperty("OpponentVictories")?.GetValue(projectedBattle)
                == 3,
        "Ghost repository reads should project participant prestige and victories into local-player perspective."
    );
    Assert(
        (bool)isGhostOpponentEliminated!.Invoke(null, [projectedBattle])! is false,
        "A bundle-final ghost battle should not show elimination text when the local player lost."
    );
    Assert(
        (string?)battleRecordType.GetProperty("Result")?.GetValue(projectedLocalWinBattle) == "Won",
        "Ghost repository reads should project a remote loss into a local-player win."
    );
    Assert(
        (bool)isGhostOpponentEliminated.Invoke(null, [projectedLocalWinBattle])!,
        "A bundle-final ghost battle should show elimination text when the local player won."
    );

    var resolvedOutcome = resolveGhostBattleOutcome!.Invoke(null, [projectedBattle]);
    var lostOutcome = Enum.Parse(coordinatorOutcomeType, "Lost");
    Assert(
        Equals(resolvedOutcome, lostOutcome),
        "Ghost battle filtering should use the projected local-player outcome."
    );

    var serviceScopedImportRecords = Array.CreateInstance(importRecordType, 1);
    var serviceScopedImportRecord =
        tryParseBattle!.Invoke(
            null,
            [
                JObject.Parse(
                    $$"""
                    {
                      "battle_id": "ghost-battle-bearer-scope",
                      "recorded_at_utc": "{{DateTimeOffset.UtcNow.AddMinutes(-3).ToString("o")}}",
                      "day": 11,
                      "player_name": "RemoteBearerScope",
                      "player_account_id": "remote-account-bearer-scope",
                      "player_hero": "Mak",
                      "opponent_name": "LocalBearerScope",
                      "opponent_account_id": "bearer-account-001",
                      "opponent_hero": "Pygmalien",
                      "combat_kind": "PVPCombat",
                      "result": "Lost",
                      "winner_combatant_id": "Opponent",
                      "loser_combatant_id": "Player"
                    }
                    """
                ),
            ]
        )
        ?? throw new InvalidOperationException("TryParseBattle should return bearer-scoped data.");
    serviceScopedImportRecords.SetValue(serviceScopedImportRecord, 0);
    InvokeVoid(
        repositoryType,
        repository,
        "UpsertGhostBattles",
        ["bearer-account-001", serviceScopedImportRecords]
    );

    var dataService =
        Activator.CreateInstance(dataServiceType, repository, null)
        ?? throw new InvalidOperationException("HistoryPanelDataService should be constructible.");
    var loadGhostArgs = new object?[] { 100, null, null, null };
    var loadGhostSucceeded = (bool)
        dataServiceType.GetMethod("TryLoadGhostBattles")!.Invoke(dataService, loadGhostArgs)!;
    var loadedServiceScopedBattles = ((IEnumerable)loadGhostArgs[1]!).Cast<object>().ToList();
    Assert(loadGhostSucceeded, "Ghost battle load should not require a current account.");
    Assert(
        loadedServiceScopedBattles.Any(battle =>
            (string?)battleRecordType.GetProperty("BattleId")?.GetValue(battle)
            == "ghost-battle-bearer-scope"
        )
            && loadedServiceScopedBattles.Any(battle =>
                (string?)battleRecordType.GetProperty("BattleId")?.GetValue(battle)
                == "ghost-battle-001"
            ),
        "Ghost battle loads should include locally cached rows from every account scope."
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
Assert(tryCreateRoutes != null, "ModApiRoutes should expose a static TryCreate factory.");
var routes =
    tryCreateRoutes!.Invoke(null, ["https://mod-api-v4.bazaarplusplus.com"])
    ?? throw new InvalidOperationException("Failed to create ModApiRoutes.");
var queryGhostBattles = (string)(
    routesType.GetProperty("QueryGhostBattles")!.GetValue(routes)
    ?? throw new InvalidOperationException("QueryGhostBattles should be populated.")
);
var replayLink = (string)(
    routesType.GetMethod("CreateReplayLink")!.Invoke(routes, ["battle-001"])
    ?? throw new InvalidOperationException("CreateReplayLink should return a route.")
);
Assert(
    queryGhostBattles == "https://mod-api-v4.bazaarplusplus.com/ghost-battles",
    "ModApiRoutes should publish the /ghost-battles query route."
);
Assert(
    replayLink == "https://mod-api-v4.bazaarplusplus.com/ghost-battles/battle-001/replay-link",
    "ModApiRoutes should publish replay links under /ghost-battles/{battle_id}/replay-link."
);

{
    HttpRequestMessage? replayLinkRequest = null;
    var replayLinkHandler = new RecordingHttpMessageHandler(request =>
    {
        replayLinkRequest = request;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"download_url":"https://r2-presigned.example.com/replays/token-public"}"""
            ),
        };
    });
    using var replayLinkHttpClient = new HttpClient(replayLinkHandler);
    var replayLinkClient =
        Activator.CreateInstance(apiClientType, replayLinkHttpClient, routes)
        ?? throw new InvalidOperationException("GhostBattleClient should be constructible.");
    var requestReplayDownloadLinkAsync = apiClientType.GetMethod(
        "RequestReplayDownloadLinkAsync",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(
        requestReplayDownloadLinkAsync != null,
        "GhostBattleClient should expose replay-link downloads."
    );
    var replayLinkTask = (Task)(
        requestReplayDownloadLinkAsync!.Invoke(
            replayLinkClient,
            ["battle-public", CancellationToken.None]
        ) ?? throw new InvalidOperationException("Replay-link request should return a task.")
    );
    await replayLinkTask;
    var replayLinkResult =
        replayLinkTask.GetType().GetProperty("Result")?.GetValue(replayLinkTask)
        ?? throw new InvalidOperationException("Replay-link request should produce a result.");
    var replayLinkResultType = replayLinkResult.GetType();
    Assert(
        (bool)(replayLinkResultType.GetProperty("Succeeded")?.GetValue(replayLinkResult) ?? false),
        "GhostBattleClient replay-link request should succeed on 200."
    );
    Assert(replayLinkRequest != null, "Replay-link request should reach the HTTP transport.");
    Assert(
        !replayLinkRequest!.Headers.Contains("X-BPP-Installation-Id"),
        "Replay-link requests should not send installation headers."
    );
    Assert(
        replayLinkRequest.Headers.Authorization == null,
        "Replay-link requests should not send an Authorization header after auth removal."
    );

    HttpRequestMessage? replayPayloadRequest = null;
    var replayPayloadHandler = new RecordingHttpMessageHandler(request =>
    {
        replayPayloadRequest = request;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(artifactBytes),
        };
    });
    using var replayPayloadHttpClient = new HttpClient(replayPayloadHandler);
    var replayPayloadClient =
        Activator.CreateInstance(apiClientType, replayPayloadHttpClient, routes)
        ?? throw new InvalidOperationException("GhostBattleClient should be constructible.");
    var downloadReplayBytesAsync = apiClientType.GetMethod(
        "DownloadReplayBytesAsync",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(
        downloadReplayBytesAsync != null,
        "GhostBattleClient should expose replay-bytes downloads."
    );
    var replayPayloadTask = (Task)(
        downloadReplayBytesAsync!.Invoke(
            replayPayloadClient,
            ["https://r2-presigned.example.com/replays/token-public", CancellationToken.None]
        ) ?? throw new InvalidOperationException("Replay-bytes request should return a task.")
    );
    await replayPayloadTask;
    var replayPayloadResult =
        replayPayloadTask.GetType().GetProperty("Result")?.GetValue(replayPayloadTask)
        ?? throw new InvalidOperationException("Replay-bytes request should produce a result.");
    var replayPayloadResultType = replayPayloadResult.GetType();
    Assert(
        (bool)(
            replayPayloadResultType.GetProperty("Succeeded")?.GetValue(replayPayloadResult) ?? false
        ),
        "GhostBattleClient replay-bytes request should succeed on 200."
    );
    Assert(replayPayloadRequest != null, "Replay-bytes request should reach the HTTP transport.");
    Assert(
        !replayPayloadRequest!.Headers.Contains("X-BPP-Installation-Id"),
        "Replay-bytes requests should not send installation headers."
    );
    Assert(
        replayPayloadRequest.Headers.Authorization == null,
        "Replay-bytes requests should not send an Authorization header after auth removal."
    );
}

Console.WriteLine("Ghost battle sync checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static Type RequireModApiType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus.ModApi")
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
    return reloadedGhostPayload
            .GetType()
            .GetProperty("ReplayPayload")
            ?.GetValue(reloadedGhostPayload)
        ?? throw new InvalidOperationException(
            "Reloaded ghost payload should include replay payload."
        );
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

sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(_handler(request));
    }
}
