using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

var payloadStoreType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayPayloadStore");
var captureServiceType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayCaptureService");
var loaderType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayLoader");
var controllerType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayController");
var artifactType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureArtifact");
var manifestType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifest");
var payloadType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayload");
var candidateType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplaySequenceCandidate");
var cardSetCaptureType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCardSetCapture");
var captureStatusType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureStatus");
var captureSourceType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureSource");
var matcherType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSequenceMatcher");
var collectorType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSnapshotCollector");
var manifestFactoryType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifestFactory");
var payloadFactoryType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayloadFactory");
var catalogType = RequireType("BazaarPlusPlus.Game.PvpBattles.Persistence.PvpBattleCatalog");
var catalogInterfaceType = RequireType(
    "BazaarPlusPlus.Game.PvpBattles.Persistence.IPvpBattleCatalog"
);
var catalogStoreType = RequireType(
    "BazaarPlusPlus.Game.PvpBattles.Persistence.PvpBattleSqliteStore"
);

Assert(
    Type.GetType("BazaarPlusPlus.Game.CombatReplay.CombatReplayRecord, BazaarPlusPlus") == null,
    "CombatReplayRecord should be removed once battle-first replay storage is in place."
);
Assert(
    Type.GetType("BazaarPlusPlus.Game.CombatReplay.CombatReplayStore, BazaarPlusPlus") == null,
    "CombatReplayStore should be removed once payload storage is battle-first."
);
Assert(
    Type.GetType("BazaarPlusPlus.Game.CombatReplay.PvpBattleSqliteStore, BazaarPlusPlus") == null,
    "The legacy CombatReplay.PvpBattleSqliteStore bridge should be removed."
);

Assert(manifestType.GetProperty("BattleId") != null, "Manifest should expose BattleId.");
Assert(manifestType.GetProperty("RecordedAtUtc") != null, "Manifest should expose RecordedAtUtc.");
Assert(
    manifestType.GetProperty("SavedAtUtc") == null,
    "Manifest should not expose SavedAtUtc once replay timestamps follow the server contract directly."
);
Assert(manifestType.GetProperty("ReplayId") == null, "Manifest should not expose ReplayId.");
Assert(payloadType.GetProperty("BattleId") != null, "Replay payload should expose BattleId.");
Assert(
    cardSetCaptureType.GetProperty("Items") != null
        && cardSetCaptureType.GetProperty("Status") != null
        && cardSetCaptureType.GetProperty("Source") != null,
    "PvpBattleCardSetCapture should expose Items, Status, and Source."
);
Assert(
    matcherType != null
        && collectorType != null
        && manifestFactoryType != null
        && payloadFactoryType != null,
    "The PVP battle matcher, collector, and factories should exist."
);
var shouldRefreshPlayerCaptureMethod = collectorType.GetMethod(
    "ShouldRefreshPlayerCapture",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    shouldRefreshPlayerCaptureMethod != null,
    "PvpBattleSnapshotCollector should keep player capture refresh logic testable."
);
Assert(
    catalogType.GetMethod("Save") != null
        && catalogType.GetMethod("TryLoad") != null
        && catalogType.GetMethod("ListRecentBattles") != null
        && catalogInterfaceType != null
        && catalogStoreType != null,
    "The PVP battle catalog read side should expose Save, TryLoad, and ListRecentBattles."
);
Assert(
    catalogType.GetMethod("Delete") != null
        && catalogType.GetMethod("ListBattleIds") != null
        && catalogInterfaceType.GetMethod("Delete") != null
        && catalogInterfaceType.GetMethod("ListBattleIds") != null,
    "The PVP battle catalog should expose Delete and ListBattleIds so replay maintenance can reconcile stored manifests when needed."
);
var persistenceQueueType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.CombatReplayPersistenceQueue"
);
var persistenceResultType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.CombatReplayPersistenceResult"
);
var queueCtor = persistenceQueueType.GetConstructor([
    typeof(Action<>).MakeGenericType(payloadType),
    typeof(Action<>).MakeGenericType(manifestType),
    typeof(Action<string>),
]);
Assert(
    queueCtor != null,
    "Combat replay persistence queue should accept payload-save, manifest-save, and payload-delete callbacks."
);
var queueHarness = new QueuePersistenceHarness();
var queue = queueCtor!.Invoke([
    queueHarness.CreateSavePayloadDelegate(typeof(Action<>).MakeGenericType(payloadType)),
    queueHarness.CreateSaveManifestDelegate(typeof(Action<>).MakeGenericType(manifestType)),
    new Action<string>(queueHarness.DeletePayload),
]);
Assert(queue != null, "Combat replay persistence queue should be constructible.");

var openingEmptySnapshots = (System.Collections.IList)CreateEmptySnapshotList();
Assert(
    (bool)(
        shouldRefreshPlayerCaptureMethod!.Invoke(
            null,
            new object?[] { true, false, openingEmptySnapshots }
        ) ?? false
    ),
    "Player snapshots captured as empty at combat opening should still be eligible for live retry."
);
var populatedSnapshots = (System.Collections.IList)CreateSnapshotList("refresh-1", "tpl-refresh");
Assert(
    !(
        (bool)(
            shouldRefreshPlayerCaptureMethod.Invoke(
                null,
                new object?[] { true, false, populatedSnapshots }
            ) ?? false
        )
    ),
    "Player snapshots captured with opening data should not request a redundant live retry."
);
Assert(
    !(
        (bool)(
            shouldRefreshPlayerCaptureMethod.Invoke(
                null,
                new object?[] { false, true, openingEmptySnapshots }
            ) ?? false
        )
    ),
    "Player snapshots should not request another refresh after live retry already ran."
);

var slowPayload = Activator.CreateInstance(payloadType);
Assert(slowPayload != null, "Slow replay payload should be constructible.");
SetProperty(payloadType, slowPayload!, "BattleId", "battle-dispose-slow");
var slowManifest = Activator.CreateInstance(manifestType);
Assert(slowManifest != null, "Slow replay manifest should be constructible.");
SetProperty(manifestType, slowManifest!, "BattleId", "battle-dispose-slow");

var abandonedPayload = Activator.CreateInstance(payloadType);
Assert(abandonedPayload != null, "Abandoned replay payload should be constructible.");
SetProperty(payloadType, abandonedPayload!, "BattleId", "battle-dispose-abandoned");
var abandonedManifest = Activator.CreateInstance(manifestType);
Assert(abandonedManifest != null, "Abandoned replay manifest should be constructible.");
SetProperty(manifestType, abandonedManifest!, "BattleId", "battle-dispose-abandoned");

Invoke(persistenceQueueType, queue!, "Enqueue", new object?[] { slowPayload!, slowManifest! });
Invoke(
    persistenceQueueType,
    queue!,
    "Enqueue",
    new object?[] { abandonedPayload!, abandonedManifest! }
);
Assert(
    queueHarness.FirstPayloadStarted.Wait(TimeSpan.FromSeconds(2)),
    "Queue should begin persisting the first replay request before disposal starts."
);

var disposeTask = Task.Run(() =>
    Invoke(persistenceQueueType, queue!, "Dispose", Array.Empty<object?>())
);
var shutdownStopwatch = Stopwatch.StartNew();
Assert(
    disposeTask.Wait(TimeSpan.FromSeconds(2)),
    "Disposing the persistence queue should stop waiting after the shutdown timeout instead of blocking on an in-flight replay write."
);
shutdownStopwatch.Stop();
Assert(
    shutdownStopwatch.Elapsed < TimeSpan.FromSeconds(2),
    "Disposing the persistence queue should return on the bounded shutdown path."
);

var abandonedResults = new List<object>();
while (TryDequeuePersistenceResult(persistenceQueueType, queue!, out var abandonedResult))
{
    abandonedResults.Add(abandonedResult!);
}

Assert(
    abandonedResults.Count == 1,
    "Timed-out shutdown should immediately surface one abandoned result for queued replay work."
);
Assert(
    abandonedResults.Any(result =>
        !(bool)GetProperty(persistenceResultType, result, "Succeeded")
        && string.Equals(
            (string?)GetProperty(
                manifestType,
                GetProperty(persistenceResultType, result, "Manifest"),
                "BattleId"
            ),
            "battle-dispose-abandoned",
            StringComparison.Ordinal
        )
    ),
    "Timed-out shutdown should convert queued replay work into a completed failure result."
);

queueHarness.AllowFirstPayloadToComplete.Set();
Assert(
    SpinWait.SpinUntil(
        () => queueHarness.SavedManifestBattleIds.Contains("battle-dispose-slow"),
        millisecondsTimeout: 5000
    ),
    "The in-flight replay write should still be allowed to finish asynchronously after shutdown returns."
);

var queueResults = new List<object>();
object? firstCompletedResult = null;
Assert(
    SpinWait.SpinUntil(
        () => TryDequeuePersistenceResult(persistenceQueueType, queue!, out firstCompletedResult),
        millisecondsTimeout: 5000
    ),
    "Queue disposal should eventually surface the successful in-flight replay result."
);
if (firstCompletedResult != null)
    queueResults.Add(firstCompletedResult);
while (TryDequeuePersistenceResult(persistenceQueueType, queue!, out var result))
{
    queueResults.Add(result!);
}

Assert(
    queueResults.Any(result =>
        (bool)GetProperty(persistenceResultType, result, "Succeeded")
        && string.Equals(
            (string?)GetProperty(
                manifestType,
                GetProperty(persistenceResultType, result, "Manifest"),
                "BattleId"
            ),
            "battle-dispose-slow",
            StringComparison.Ordinal
        )
    ),
    "Queue disposal should preserve the successful in-flight replay result even when shutdown returns before it finishes."
);
Assert(
    queueHarness.SavedManifestBattleIds.SequenceEqual(["battle-dispose-slow"]),
    "Forced shutdown should not begin persisting the second replay manifest after cancellation starts."
);
Assert(
    !queueHarness.DeletedPayloadBattleIds.Any(),
    "Forced shutdown abandonment should not roll back payloads when the manifest save was never attempted."
);
Assert(
    !(bool)GetProperty(persistenceQueueType, queue!, "HasPendingPersistence"),
    "After draining the completed queue results from disposal, replay persistence should no longer report outstanding work."
);
var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-combat-replay-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "run-logs.db");

try
{
    var payloadStore = Activator.CreateInstance(payloadStoreType, tempRoot);
    Assert(payloadStore != null, "CombatReplayPayloadStore should be constructible.");
    var battleCatalog = Activator.CreateInstance(catalogType, dbPath);
    Assert(battleCatalog != null, "PvpBattleCatalog should be constructible.");

    var payload = Activator.CreateInstance(payloadType);
    Assert(payload != null, "PvpReplayPayload should be constructible.");
    SetProperty(payloadType, payload!, "BattleId", "battle-001");
    SetProperty(payloadType, payload!, "Version", 1);
    SetProperty(payloadType, payload!, "SpawnMessageBytes", new byte[] { 1, 2, 3 });
    SetProperty(payloadType, payload!, "CombatMessageBytes", new byte[] { 4, 5, 6 });
    SetProperty(payloadType, payload!, "DespawnMessageBytes", new byte[] { 7, 8, 9 });
    Invoke(payloadStoreType, payloadStore!, "Save", new object?[] { payload! });
    Assert(
        Equals(
            Invoke(payloadStoreType, payloadStore!, "Exists", new object?[] { "battle-001" }),
            true
        ),
        "Payload store should report saved payloads."
    );
    var loadedPayload = Invoke(
        payloadStoreType,
        payloadStore!,
        "Load",
        new object?[] { "battle-001" }
    );
    Assert(loadedPayload != null, "Payload store should load a saved payload by battle id.");
    Assert(
        ((byte[]?)GetProperty(payloadType, loadedPayload!, "CombatMessageBytes"))?.SequenceEqual(
            new byte[] { 4, 5, 6 }
        ) == true,
        "Payload store should preserve the serialized combat payload."
    );
    var payloadFilesAfterFirstSave = Directory
        .EnumerateFiles(tempRoot, "battle-001.payload.mpack.gz*")
        .Select(Path.GetFileName)
        .ToArray();
    Assert(
        payloadFilesAfterFirstSave.SequenceEqual(["battle-001.payload.mpack.gz"]),
        "Payload store should not leave temporary files behind after the initial atomic save."
    );

    SetProperty(payloadType, payload!, "CombatMessageBytes", new byte[] { 9, 8, 7 });
    Invoke(payloadStoreType, payloadStore!, "Save", new object?[] { payload! });
    var reloadedPayload = Invoke(
        payloadStoreType,
        payloadStore!,
        "Load",
        new object?[] { "battle-001" }
    );
    Assert(reloadedPayload != null, "Payload store should reload an overwritten payload.");
    Assert(
        ((byte[]?)GetProperty(payloadType, reloadedPayload!, "CombatMessageBytes"))?.SequenceEqual(
            new byte[] { 9, 8, 7 }
        ) == true,
        "Atomic overwrite should publish the latest replay payload bytes."
    );
    var payloadFilesAfterOverwrite = Directory
        .EnumerateFiles(tempRoot, "battle-001.payload.mpack.gz*")
        .Select(Path.GetFileName)
        .ToArray();
    Assert(
        payloadFilesAfterOverwrite.SequenceEqual(["battle-001.payload.mpack.gz"]),
        "Atomic overwrite should replace the target file without leaving temp artifacts behind."
    );

    var manifest = CreateManifestFixture(
        manifestType,
        cardSetCaptureType,
        captureStatusType,
        captureSourceType,
        battleId: "battle-001",
        runId: "run-001",
        savedAtUtc: new DateTimeOffset(2026, 3, 18, 1, 2, 3, TimeSpan.Zero),
        combatKind: "PVPCombat",
        day: 3,
        hour: 5,
        encounterId: "encounter-abc",
        playerName: "Local Player",
        playerAccountId: "player-account-001",
        opponentName: "Test Opponent",
        opponentHero: "Vanessa",
        opponentRank: "Legend",
        opponentRating: 2048,
        opponentLevel: 12,
        opponentAccountId: "opponent-account-001",
        result: "win",
        winnerCombatantId: "Player",
        loserCombatantId: "Opponent",
        playerHandCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "LiveRetry",
            CreateSnapshotList("p-hand-1", "tpl-p-hand")
        ),
        playerSkillsCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Missing",
            "Unknown",
            CreateEmptySnapshotList()
        ),
        opponentHandCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "OpeningMessage",
            CreateSnapshotList("o-hand-1", "tpl-o-hand")
        ),
        opponentSkillsCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "OpeningMessage",
            CreateSnapshotList("o-skill-1", "tpl-o-skill")
        )
    );
    Invoke(catalogType, battleCatalog!, "Save", new object?[] { manifest! });
    var loadedManifest = Invoke(
        catalogType,
        battleCatalog!,
        "TryLoad",
        new object?[] { "battle-001" }
    );
    Assert(loadedManifest != null, "Catalog should load a saved manifest by battle id.");
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, loadedManifest!, "EncounterId"),
            "encounter-abc",
            StringComparison.Ordinal
        ),
        "Catalog should preserve manifest metadata."
    );

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            CountRows(connection, "battles") == 1,
            "Saving a PVP battle manifest should insert one row into battles."
        );
        Assert(
            CountRows(connection, "battle_snapshots") == 1,
            "Saving a PVP battle manifest should persist one snapshot row."
        );
        Assert(
            GetString(
                connection,
                "SELECT player_name FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Local Player",
            "battles should persist the player name."
        );
        Assert(
            GetString(
                connection,
                "SELECT player_account_id FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "player-account-001",
            "battles should persist the player account id."
        );
        Assert(
            GetString(
                connection,
                "SELECT opponent_account_id FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "opponent-account-001",
            "battles should persist the opponent account id."
        );
        Assert(
            GetString(
                connection,
                "SELECT opponent_hero FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Vanessa",
            "battles should persist the opponent hero."
        );
        Assert(
            GetString(
                connection,
                "SELECT opponent_rank FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Legend",
            "battles should persist the opponent rank."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT opponent_rating FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 2048,
            "battles should persist the opponent rating."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT opponent_level FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 12,
            "battles should persist the opponent level."
        );
        Assert(
            GetString(
                connection,
                "SELECT result FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "win",
            "battles should persist the player's combat result."
        );
        Assert(
            GetString(
                connection,
                "SELECT winner_combatant_id FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Player",
            "battles should persist the winner combatant id."
        );
        var playerHandJson = GetString(
            connection,
            "SELECT player_hand_json FROM battle_snapshots WHERE battle_id = $battleId;",
            "battle-001"
        );
        Assert(
            playerHandJson.Contains("status", StringComparison.Ordinal)
                && playerHandJson.Contains("source", StringComparison.Ordinal)
                && playerHandJson.Contains("items", StringComparison.Ordinal)
                && playerHandJson.Contains("Captured", StringComparison.Ordinal)
                && playerHandJson.Contains("LiveRetry", StringComparison.Ordinal)
                && playerHandJson.Contains("p-hand-1", StringComparison.Ordinal)
                && playerHandJson.Contains("Sparkblade", StringComparison.Ordinal)
                && playerHandJson.Contains("Radiant", StringComparison.Ordinal)
                && playerHandJson.Contains("Damage", StringComparison.Ordinal),
            "battle_snapshots should persist detailed capture objects for hand-card metadata."
        );
        var playerSkillsJson = GetString(
            connection,
            "SELECT player_skills_json FROM battle_snapshots WHERE battle_id = $battleId;",
            "battle-001"
        );
        Assert(
            playerSkillsJson.Contains("Missing", StringComparison.Ordinal)
                && playerSkillsJson.Contains("Unknown", StringComparison.Ordinal)
                && !playerSkillsJson.Contains("CapturedEmpty", StringComparison.Ordinal),
            "battle_snapshots should preserve explicit missing capture semantics instead of guessing from empty lists."
        );
    }

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        using var createRun = connection.CreateCommand();
        createRun.CommandText = """
            INSERT INTO runs (
                run_id,
                started_at_utc,
                last_seen_at_utc,
                status,
                completed,
                hero,
                game_mode,
                last_seq
            ) VALUES (
                $runId,
                $startedAtUtc,
                $lastSeenAtUtc,
                'active',
                0,
                'Vanessa',
                'Ranked',
                0
            );
            """;
        createRun.Parameters.AddWithValue("$runId", "run-001");
        createRun.Parameters.AddWithValue("$startedAtUtc", "2026-03-18T01:00:00.0000000+00:00");
        createRun.Parameters.AddWithValue("$lastSeenAtUtc", "2026-03-18T01:00:00.0000000+00:00");
        createRun.ExecuteNonQuery();
    }

    Invoke(catalogType, battleCatalog!, "AttachToRun", new object?[] { "battle-001", "run-001" });
    var reboundManifest = Invoke(
        catalogType,
        battleCatalog!,
        "TryLoad",
        new object?[] { "battle-001" }
    );
    Assert(reboundManifest != null, "Catalog should still load a rebound battle manifest.");
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, reboundManifest!, "RunId"),
            "run-001",
            StringComparison.Ordinal
        ),
        "AttachToRun should backfill the saved battle row once the owning run exists."
    );
    var reboundBattles = (
        (System.Collections.IEnumerable)Invoke(
            catalogType,
            battleCatalog!,
            "ListByRunId",
            new object?[] { "run-001" }
        )
    )
        .Cast<object>()
        .ToList();
    Assert(
        reboundBattles.Any(entry =>
            string.Equals(
                (string?)GetProperty(manifestType, entry, "BattleId"),
                "battle-001",
                StringComparison.Ordinal
            )
        ),
        "ListByRunId should surface battles whose run id was backfilled after persistence."
    );

    var captureService = Activator.CreateInstance(captureServiceType);
    Assert(captureService != null, "CombatReplayCaptureService should be constructible.");

    var nonCombatStart = CreateGameSimMessage(
        "Encounter",
        day: 1,
        hour: 1,
        encounterId: "encounter-ignore",
        opponentName: "Ignored Opponent"
    );
    var combatMessage = CreateCombatSimMessage();
    var pveCombatStart = CreateGameSimMessage(
        "Combat",
        day: 2,
        hour: 4,
        encounterId: "encounter-pve",
        opponentName: "PvE Opponent"
    );
    var combatStart = CreateGameSimMessage(
        "PVPCombat",
        day: 3,
        hour: 4,
        encounterId: "encounter-live",
        opponentName: "Rival"
    );
    var combatEnd = CreateGameSimMessage(
        "Encounter",
        day: 3,
        hour: 5,
        encounterId: null,
        opponentName: null
    );

    var ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { nonCombatStart, "run-ignore" }
    );
    Assert(
        ignoredResult == null,
        "Non-combat opening GameSim should not immediately create a replay."
    );
    ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { combatMessage, "run-ignore" }
    );
    Assert(
        ignoredResult == null,
        "A CombatSim without a combat-opening GameSim should not create a replay."
    );
    ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { combatEnd, "run-ignore" }
    );
    Assert(
        ignoredResult == null,
        "An invalid GameSim/CombatSim/GameSim triplet should be ignored."
    );
    ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { pveCombatStart, "run-ignore" }
    );
    Assert(ignoredResult == null, "A non-PVP combat opening should not start replay capture.");
    ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { combatMessage, "run-ignore" }
    );
    Assert(ignoredResult == null, "A CombatSim after a non-PVP opening should still be ignored.");

    Assert(
        Invoke(
            captureServiceType,
            captureService!,
            "Accept",
            new object?[] { combatStart, "run-42" }
        ) == null,
        "Combat opening GameSim should buffer until the sequence completes."
    );
    Assert(
        Invoke(
            captureServiceType,
            captureService!,
            "Accept",
            new object?[] { combatMessage, "run-42" }
        ) == null,
        "CombatSim should buffer until the closing GameSim arrives."
    );
    var completedArtifact = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { combatEnd, "run-42" }
    );
    Assert(completedArtifact != null, "A combat triplet should produce a battle artifact.");
    var completedManifest = GetProperty(artifactType, completedArtifact!, "Manifest");
    var completedPayload = GetProperty(artifactType, completedArtifact!, "Payload");
    Assert(completedManifest != null, "Battle artifacts should include a manifest.");
    Assert(completedPayload != null, "Battle artifacts should include a replay payload.");
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, completedManifest!, "RunId"),
            "run-42",
            StringComparison.Ordinal
        ),
        "Completed battle manifests should preserve the run id."
    );
    Assert(
        Equals(GetProperty(manifestType, completedManifest!, "Day"), 3),
        "Completed battle manifests should capture the combat day."
    );
    Assert(
        Equals(GetProperty(manifestType, completedManifest!, "Hour"), 4),
        "Completed battle manifests should capture the combat hour from the opening snapshot."
    );
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, completedManifest!, "CombatKind"),
            "PVPCombat",
            StringComparison.Ordinal
        ),
        "Completed battle manifests should preserve the combat kind from the opening snapshot."
    );
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, completedManifest!, "EncounterId"),
            "encounter-live",
            StringComparison.Ordinal
        ),
        "Completed battle manifests should preserve the opening encounter id."
    );
    Assert(
        ((byte[]?)GetProperty(payloadType, completedPayload!, "SpawnMessageBytes"))?.Length > 0,
        "Completed replay payloads should serialize the opening GameSim."
    );
    Assert(
        ((byte[]?)GetProperty(payloadType, completedPayload!, "CombatMessageBytes"))?.Length > 0,
        "Completed replay payloads should serialize the CombatSim."
    );
    Assert(
        ((byte[]?)GetProperty(payloadType, completedPayload!, "DespawnMessageBytes"))?.Length > 0,
        "Completed replay payloads should serialize the closing GameSim."
    );
    Assert(
        GetProperty(manifestType, completedManifest!, "Snapshots") != null,
        "Completed battle manifests should capture snapshot wrappers for both sides."
    );
    Assert(
        !string.IsNullOrWhiteSpace(
            (string?)GetProperty(manifestType, completedManifest!, "BattleId")
        ),
        "Completed battle manifests should allocate a battle id."
    );

    var collector = Activator.CreateInstance(collectorType);
    Assert(collector != null, "PvpBattleSnapshotCollector should be constructible.");
    var participantCandidate = Activator.CreateInstance(candidateType);
    Assert(
        participantCandidate != null,
        "CombatReplaySequenceCandidate should be constructible for participant tests."
    );
    SetProperty(candidateType, participantCandidate!, "PlayerRank", "Legendary 5");
    SetProperty(candidateType, participantCandidate!, "PlayerRating", 502);
    SetProperty(candidateType, participantCandidate!, "OpponentName", "Snapshot Opponent");
    SetProperty(candidateType, participantCandidate!, "OpponentHero", "Vanessa");
    SetProperty(candidateType, participantCandidate!, "OpponentRank", "Legendary");
    SetProperty(candidateType, participantCandidate!, "OpponentRating", 728);
    SetProperty(candidateType, participantCandidate!, "OpponentLevel", 9);
    SetProperty(candidateType, participantCandidate!, "OpponentAccountId", "opponent-snapshot-id");
    var participants = Invoke(
        collectorType,
        collector!,
        "BuildParticipants",
        new object?[] { participantCandidate! }
    );
    Assert(participants != null, "BuildParticipants should return a participants snapshot.");
    var participantsType = participants!.GetType();
    Assert(
        string.Equals(
            (string?)GetProperty(participantsType, participants, "PlayerRank"),
            "Legendary 5",
            StringComparison.Ordinal
        ) && Equals(GetProperty(participantsType, participants, "PlayerRating"), 502),
        "BuildParticipants should preserve the player rank and rating captured on the opening candidate."
    );
    Assert(
        string.Equals(
            (string?)GetProperty(participantsType, participants, "OpponentName"),
            "Snapshot Opponent",
            StringComparison.Ordinal
        )
            && string.Equals(
                (string?)GetProperty(participantsType, participants, "OpponentAccountId"),
                "opponent-snapshot-id",
                StringComparison.Ordinal
            ),
        "BuildParticipants should preserve opponent identity captured on the opening candidate."
    );

    var loader = Activator.CreateInstance(loaderType);
    Assert(loader != null, "CombatReplayLoader should be constructible.");
    var loadedSequence = Invoke(loaderType, loader!, "Load", new object?[] { completedPayload! });
    Assert(loadedSequence != null, "CombatReplayLoader should deserialize a saved replay payload.");
    Assert(
        GetFieldValue(loadedSequence!.GetType(), loadedSequence, "SpawnMessage") != null,
        "Loaded replay sequences should include the opening GameSim."
    );
    Assert(
        GetFieldValue(loadedSequence!.GetType(), loadedSequence, "CombatMessage") != null,
        "Loaded replay sequences should include the CombatSim."
    );
    Assert(
        GetFieldValue(loadedSequence!.GetType(), loadedSequence, "DespawnMessage") != null,
        "Loaded replay sequences should include the closing GameSim."
    );

    Invoke(payloadStoreType, payloadStore!, "Save", new object?[] { completedPayload! });
    Invoke(catalogType, battleCatalog!, "Save", new object?[] { completedManifest! });

    var controller = Activator.CreateInstance(controllerType, battleCatalog, payloadStore, loader);
    Assert(controller != null, "CombatReplayController should be constructible.");
    var missingPayloadManifest = CreateManifestFixture(
        manifestType,
        cardSetCaptureType,
        captureStatusType,
        captureSourceType,
        battleId: "battle-missing-payload",
        runId: "run-missing-payload",
        savedAtUtc: new DateTimeOffset(2026, 3, 19, 1, 2, 3, TimeSpan.Zero),
        combatKind: "PVPCombat",
        day: 4,
        hour: 6,
        encounterId: "encounter-missing-payload",
        playerName: "Local Player",
        playerAccountId: "player-account-001",
        opponentName: "Missing Payload Opponent",
        opponentHero: "Pygmalien",
        opponentRank: "Master",
        opponentRating: 2199,
        opponentLevel: 14,
        opponentAccountId: "opponent-account-missing-payload",
        result: "loss",
        winnerCombatantId: "Opponent",
        loserCombatantId: "Player",
        playerHandCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "LiveRetry",
            CreateSnapshotList("missing-p-hand-1", "tpl-missing-p-hand")
        ),
        playerSkillsCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "CapturedEmpty",
            "LiveRetry",
            CreateEmptySnapshotList()
        ),
        opponentHandCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "OpeningMessage",
            CreateSnapshotList("missing-o-hand-1", "tpl-missing-o-hand")
        ),
        opponentSkillsCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "OpeningMessage",
            CreateSnapshotList("missing-o-skill-1", "tpl-missing-o-skill")
        )
    );
    Invoke(catalogType, battleCatalog!, "Save", new object?[] { missingPayloadManifest! });
    var savedReplays = (
        (System.Collections.IEnumerable)Invoke(
            controllerType,
            controller!,
            "ListRecentBattles",
            Array.Empty<object?>()
        )
    )
        .Cast<object>()
        .ToList();
    Assert(savedReplays.Count >= 1, "Controller should expose recent battle manifests.");
    Assert(
        savedReplays.All(manifestEntry =>
            !string.Equals(
                (string?)GetProperty(manifestType, manifestEntry, "BattleId"),
                "battle-missing-payload",
                StringComparison.Ordinal
            )
        ),
        "Controller should hide battle manifests whose replay payload file is missing."
    );
    var latestBattle = Invoke(
        controllerType,
        controller!,
        "GetLatestBattle",
        Array.Empty<object?>()
    );
    Assert(
        latestBattle != null
            && !string.Equals(
                (string?)GetProperty(manifestType, latestBattle, "BattleId"),
                "battle-missing-payload",
                StringComparison.Ordinal
            ),
        "Controller should not surface a latest battle whose replay payload file is missing."
    );

    var battleId = (string?)GetProperty(manifestType, completedManifest!, "BattleId");
    var loadedManifestFromController = Invoke(
        controllerType,
        controller!,
        "LoadBattle",
        new object?[] { battleId! }
    );
    Assert(
        loadedManifestFromController != null,
        "Controller should load a saved battle manifest by id."
    );
    Assert(
        Invoke(
            controllerType,
            controller!,
            "LoadBattle",
            new object?[] { "battle-missing-payload" }
        ) == null,
        "Controller should reject loading a battle whose replay payload file is missing."
    );
    var loadedPayloadFromController = Invoke(
        controllerType,
        controller!,
        "LoadPayload",
        new[] { loadedManifestFromController }
    );
    Assert(
        loadedPayloadFromController != null,
        "Controller should load a saved replay payload by battle id."
    );
    var loadedFromController = Invoke(
        controllerType,
        controller!,
        "LoadReplay",
        new[] { loadedPayloadFromController }
    );
    Assert(loadedFromController != null, "Controller should deserialize a loaded replay payload.");
    Assert(
        string.Equals(
            (string?)GetProperty(controllerType, controller!, "ActiveBattleId"),
            battleId,
            StringComparison.Ordinal
        ),
        "Controller should track the active battle id."
    );

    Console.WriteLine("CombatReplayRecording store checks passed.");
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
                System.Threading.Thread.Sleep(200);
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup on Windows when SQLite releases the file handle late.
            }
        }
    }
}

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static object? Invoke(Type type, object instance, string methodName, object?[] args)
{
    var method = type.GetMethod(
        methodName,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(method != null, $"Method not found: {type.FullName}.{methodName}");
    return method!.Invoke(instance, args);
}

static bool TryDequeuePersistenceResult(Type queueType, object queue, out object? result)
{
    var method = queueType.GetMethod(
        "TryDequeueResult",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(method != null, $"Method not found: {queueType.FullName}.TryDequeueResult");
    var args = new object?[] { null };
    var dequeued = (bool)(method!.Invoke(queue, args) ?? false);
    result = args[0];
    return dequeued;
}

static void SetProperty(Type type, object instance, string name, object? value)
{
    var property = type.GetProperty(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(property != null, $"Property not found: {type.FullName}.{name}");
    property!.SetValue(instance, value);
}

static object? GetProperty(Type type, object instance, string name)
{
    var property = type.GetProperty(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(property != null, $"Property not found: {type.FullName}.{name}");
    return property!.GetValue(instance);
}

static object? GetFieldValue(Type type, object instance, string name)
{
    var field = type.GetField(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(field != null, $"Field not found: {type.FullName}.{name}");
    return field!.GetValue(instance);
}

static object CreateSnapshotList(string instanceId, string templateId)
{
    var snapshotType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayCardSnapshot");
    var listType = typeof(List<>).MakeGenericType(snapshotType);
    var list =
        Activator.CreateInstance(listType)
        ?? throw new InvalidOperationException("Snapshot list should be constructible.");
    var snapshot =
        Activator.CreateInstance(snapshotType)
        ?? throw new InvalidOperationException("Snapshot should be constructible.");
    SetProperty(snapshotType, snapshot, "InstanceId", instanceId);
    SetProperty(snapshotType, snapshot, "TemplateId", templateId);
    SetProperty(
        snapshotType,
        snapshot,
        "Type",
        ParseEnum("BazaarGameShared.Domain.Core.Types.ECardType", "BazaarGameShared", "Skill")
    );
    SetProperty(
        snapshotType,
        snapshot,
        "Name",
        instanceId.StartsWith("p-skill", StringComparison.Ordinal) ? "Arcane Mastery" : "Sparkblade"
    );
    SetProperty(snapshotType, snapshot, "Tier", "Gold");
    SetProperty(snapshotType, snapshot, "Enchant", "Radiant");
    SetProperty(snapshotType, snapshot, "Tags", new List<string> { "Weapon", "Burst" });
    SetProperty(
        snapshotType,
        snapshot,
        "Attributes",
        new Dictionary<string, int> { ["Damage"] = 42, ["Cooldown"] = 3 }
    );
    Invoke(listType, list, "Add", new[] { snapshot });
    return list;
}

static object CreateEmptySnapshotList()
{
    var snapshotType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayCardSnapshot");
    var listType = typeof(List<>).MakeGenericType(snapshotType);
    return Activator.CreateInstance(listType)
        ?? throw new InvalidOperationException("Empty snapshot list should be constructible.");
}

static object CreateCardSetCapture(
    Type cardSetCaptureType,
    Type captureStatusType,
    Type captureSourceType,
    string status,
    string source,
    object items
)
{
    var capture =
        Activator.CreateInstance(cardSetCaptureType)
        ?? throw new InvalidOperationException("PvpBattleCardSetCapture should be constructible.");
    SetProperty(cardSetCaptureType, capture, "Items", items);
    SetProperty(cardSetCaptureType, capture, "Status", Enum.Parse(captureStatusType, status));
    SetProperty(cardSetCaptureType, capture, "Source", Enum.Parse(captureSourceType, source));
    return capture;
}

static object CreateManifestFixture(
    Type manifestType,
    Type cardSetCaptureType,
    Type captureStatusType,
    Type captureSourceType,
    string battleId,
    string runId,
    DateTimeOffset savedAtUtc,
    string combatKind,
    int day,
    int hour,
    string encounterId,
    string playerName,
    string playerAccountId,
    string opponentName,
    string opponentHero,
    string opponentRank,
    int opponentRating,
    int opponentLevel,
    string opponentAccountId,
    string result,
    string winnerCombatantId,
    string loserCombatantId,
    object playerHandCapture,
    object playerSkillsCapture,
    object opponentHandCapture,
    object opponentSkillsCapture
)
{
    var participantsType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleParticipants");
    var outcomeType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleOutcome");
    var snapshotsType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSnapshots");

    var participants = Activator.CreateInstance(participantsType)!;
    SetProperty(participantsType, participants, "PlayerName", playerName);
    SetProperty(participantsType, participants, "PlayerAccountId", playerAccountId);
    SetProperty(participantsType, participants, "OpponentName", opponentName);
    SetProperty(participantsType, participants, "OpponentHero", opponentHero);
    SetProperty(participantsType, participants, "OpponentRank", opponentRank);
    SetProperty(participantsType, participants, "OpponentRating", opponentRating);
    SetProperty(participantsType, participants, "OpponentLevel", opponentLevel);
    SetProperty(participantsType, participants, "OpponentAccountId", opponentAccountId);

    var outcome = Activator.CreateInstance(outcomeType)!;
    SetProperty(outcomeType, outcome, "Result", result);
    SetProperty(outcomeType, outcome, "WinnerCombatantId", winnerCombatantId);
    SetProperty(outcomeType, outcome, "LoserCombatantId", loserCombatantId);

    var snapshots = Activator.CreateInstance(snapshotsType)!;
    SetProperty(snapshotsType, snapshots, "PlayerHand", playerHandCapture);
    SetProperty(snapshotsType, snapshots, "PlayerSkills", playerSkillsCapture);
    SetProperty(snapshotsType, snapshots, "OpponentHand", opponentHandCapture);
    SetProperty(snapshotsType, snapshots, "OpponentSkills", opponentSkillsCapture);

    var manifest = Activator.CreateInstance(manifestType)!;
    SetProperty(manifestType, manifest, "BattleId", battleId);
    SetProperty(manifestType, manifest, "RunId", runId);
    SetProperty(manifestType, manifest, "RecordedAtUtc", savedAtUtc);
    SetProperty(manifestType, manifest, "CombatKind", combatKind);
    SetProperty(manifestType, manifest, "Day", day);
    SetProperty(manifestType, manifest, "Hour", hour);
    SetProperty(manifestType, manifest, "EncounterId", encounterId);
    SetProperty(manifestType, manifest, "Participants", participants);
    SetProperty(manifestType, manifest, "Outcome", outcome);
    SetProperty(manifestType, manifest, "Snapshots", snapshots);
    return manifest;
}

static int ReadSnapshotCount(Type recordType, object instance, string propertyName)
{
    return ((System.Collections.IEnumerable?)GetProperty(recordType, instance, propertyName))
            ?.Cast<object>()
            .Count()
        ?? 0;
}

static long CountRows(SqliteConnection connection, string tableName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
    return (long)(command.ExecuteScalar() ?? 0L);
}

static string GetString(SqliteConnection connection, string sql, string battleId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$battleId", battleId);
    return (string)(
        command.ExecuteScalar()
        ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
}

static int GetInt32(SqliteConnection connection, string sql, string battleId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$battleId", battleId);
    return Convert.ToInt32(
        command.ExecuteScalar()
            ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
}

static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info({tableName});";
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        if (
            string.Equals(
                reader.GetString(reader.GetOrdinal("name")),
                columnName,
                StringComparison.Ordinal
            )
        )
            return true;
    }

    return false;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static object CreateGameSimMessage(
    string stateName,
    uint day,
    uint hour,
    string? encounterId,
    string? opponentName
)
{
    var gameSimType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.GameSim",
        "BazaarGameShared"
    );
    var simUpdateRunType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.SimUpdateRun",
        "BazaarGameShared"
    );
    var simUpdateRunStateType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.SimUpdateRunState",
        "BazaarGameShared"
    );
    var runStateType = RequireExternalType(
        "BazaarGameShared.Domain.Runs.ERunState",
        "BazaarGameShared"
    );
    var simPvpOpponentType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.SimPvpOpponent",
        "BazaarGameShared"
    );
    var loadoutType = RequireExternalType(
        "BazaarGameShared.TempoNet.Models.BazaarCollectionLoadout",
        "BazaarGameShared"
    );
    var netMessageGameSimType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.NetMessageGameSim",
        "BazaarGameShared"
    );

    var gameSim = Activator.CreateInstance(gameSimType)!;
    var run = Activator.CreateInstance(simUpdateRunType)!;
    SetField(simUpdateRunType, run, "Day", day);
    SetField(simUpdateRunType, run, "Hour", hour);
    SetField(gameSimType, gameSim, "Run", run);

    var runState = Activator.CreateInstance(simUpdateRunStateType)!;
    SetField(
        simUpdateRunStateType,
        runState,
        "StateName",
        Enum.Parse(runStateType, stateName, ignoreCase: false)
    );
    SetField(simUpdateRunStateType, runState, "CurrentEncounterId", encounterId);
    SetField(
        simUpdateRunStateType,
        runState,
        "PvpOpponent",
        opponentName == null
            ? null
            : CreatePvpOpponent(simPvpOpponentType, loadoutType, opponentName)
    );
    SetField(gameSimType, gameSim, "CurrentState", runState);

    return Activator.CreateInstance(netMessageGameSimType, gameSim)!;
}

static object CreateCombatSimMessage()
{
    var combatSimType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.CombatSimEvents.CombatSim",
        "BazaarGameShared"
    );
    var netMessageCombatSimType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.NetMessageCombatSim",
        "BazaarGameShared"
    );
    var combatSim = Activator.CreateInstance(combatSimType)!;
    return Activator.CreateInstance(netMessageCombatSimType, combatSim)!;
}

static object CreatePvpOpponent(Type simPvpOpponentType, Type loadoutType, string opponentName)
{
    var loadout = Activator.CreateInstance(loadoutType)!;
    SetField(loadoutType, loadout, "accountId", "opponent-account-live");
    return Activator.CreateInstance(
        simPvpOpponentType,
        opponentName,
        null,
        null,
        ParseEnum("BazaarGameShared.TempoNet.Enums.ERank", "BazaarGameShared", "Gold"),
        1337,
        null,
        null,
        null,
        9,
        ParseEnum("BazaarGameShared.Domain.Core.Types.EHero", "BazaarGameShared", "Vanessa"),
        loadout,
        null
    )!;
}

static Type RequireExternalType(string fullName, string assemblyName)
{
    return Type.GetType($"{fullName}, {assemblyName}")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static object ParseEnum(string fullName, string assemblyName, string name)
{
    var enumType = RequireExternalType(fullName, assemblyName);
    return Enum.Parse(enumType, name);
}

static void SetField(Type type, object instance, string name, object? value)
{
    var field = type.GetField(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(field != null, $"Field not found: {type.FullName}.{name}");
    field!.SetValue(instance, value);
}

file sealed class QueuePersistenceHarness
{
    private int _payloadSaveCallCount;

    public ManualResetEventSlim FirstPayloadStarted { get; } = new(initialState: false);

    public ManualResetEventSlim AllowFirstPayloadToComplete { get; } = new(initialState: false);

    public ConcurrentQueue<string> SavedManifestBattleIds { get; } = new();

    public ConcurrentQueue<string> DeletedPayloadBattleIds { get; } = new();

    public Delegate CreateSavePayloadDelegate(Type delegateType)
    {
        return CreateObjectForwardingDelegate(delegateType, nameof(SavePayload));
    }

    public Delegate CreateSaveManifestDelegate(Type delegateType)
    {
        return CreateObjectForwardingDelegate(delegateType, nameof(SaveManifest));
    }

    public void SavePayload(object payload)
    {
        var battleId = ReadBattleId(payload);
        if (Interlocked.Increment(ref _payloadSaveCallCount) == 1)
        {
            FirstPayloadStarted.Set();
            AllowFirstPayloadToComplete.Wait(TimeSpan.FromSeconds(5));
        }

        if (string.IsNullOrWhiteSpace(battleId))
            throw new InvalidOperationException("Replay payload should expose a battle id.");
    }

    public void SaveManifest(object manifest)
    {
        SavedManifestBattleIds.Enqueue(ReadBattleId(manifest));
    }

    public void DeletePayload(string battleId)
    {
        DeletedPayloadBattleIds.Enqueue(battleId);
    }

    private static string ReadBattleId(object instance)
    {
        return (string)(
            instance
                .GetType()
                .GetProperty("BattleId", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(instance)
            ?? throw new InvalidOperationException("BattleId property not found.")
        );
    }

    private Delegate CreateObjectForwardingDelegate(Type delegateType, string methodName)
    {
        var argumentType = delegateType.GenericTypeArguments.Single();
        var parameter = Expression.Parameter(argumentType, "value");
        var body = Expression.Call(
            Expression.Constant(this),
            GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Method not found: {methodName}"),
            Expression.Convert(parameter, typeof(object))
        );
        return Expression.Lambda(delegateType, body, parameter).Compile();
    }
}
