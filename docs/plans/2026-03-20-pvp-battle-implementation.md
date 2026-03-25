# PVP Battle Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the replay-first combat recording path with a battle-first `PvpBattleManifest + PvpReplayPayload` architecture that uses a single `BattleId`, a manifest-backed read side, and capture-status snapshots for player/opponent hand and skills.

**Architecture:** Keep `CombatReplayRuntime` as the live message observer and replay bootstrap coordinator, but move battle assembly into a new `Game/PvpBattles/` domain. Split the current `CombatReplayCaptureService` into matcher, collector, and factories; save payload before manifest; make `PvpBattleCatalog` the only list/metadata read side; and let replay playback rehydrate from `manifest + payload` instead of a single `CombatReplayRecord`.

**Tech Stack:** C# 12, BepInEx/Unity MonoBehaviours, Harmony patches, Newtonsoft.Json, Microsoft.Data.Sqlite, console-style reflection tests, Python export script.

---

### Task 1: Introduce battle-first contracts and rename run-log identity

**Files:**
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`
- Modify: `tests/RunLoggingCapture.Tests/Program.cs`
- Modify: `Game/RunLogging/RunLogCaptureService.cs`
- Modify: `Game/RunLogging/Models/RunLogEvent.cs`
- Create: `Game/PvpBattles/PvpBattleManifest.cs`
- Create: `Game/PvpBattles/PvpBattleParticipants.cs`
- Create: `Game/PvpBattles/PvpBattleOutcome.cs`
- Create: `Game/PvpBattles/PvpBattleSnapshots.cs`
- Create: `Game/PvpBattles/PvpBattleCardSetCapture.cs`
- Create: `Game/PvpBattles/PvpBattleCaptureStatus.cs`
- Create: `Game/PvpBattles/PvpBattleCaptureSource.cs`
- Create: `Game/PvpBattles/PvpReplayPayload.cs`
- Create: `Game/PvpBattles/PvpBattleCaptureArtifact.cs`
- Create: `Game/PvpBattles/PvpBattleSequenceWindow.cs`

**Step 1: Write the failing tests**

Update the combat replay and run logging tests so they assert the new contract instead of the replay-first one:

- `PvpBattleManifest`, `PvpReplayPayload`, and `PvpBattleCardSetCapture` exist
- `PvpBattleManifest` exposes `BattleId` and does not expose `ReplayId`
- `PvpReplayPayload` exposes `BattleId`
- `RunLogEvent` and the run-log input model use `BattleId` instead of `ReplayId`
- `PvpBattleCardSetCapture` exposes `Items`, `Status`, and `Source`

Use reflection assertions like:

```csharp
var manifestType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifest");
Assert(manifestType.GetProperty("BattleId") != null, "Manifest should expose BattleId.");
Assert(manifestType.GetProperty("ReplayId") == null, "Manifest should not expose ReplayId.");
```

**Step 2: Run tests to verify they fail**

Run:
- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: FAIL because the new battle-first types and `BattleId` plumbing do not exist yet.

**Step 3: Write minimal implementation**

Create the new domain types in `Game/PvpBattles/` and update the run-log models to use `BattleId`.

Keep the first pass simple:

- `PvpBattleManifest` is a pure DTO with `BattleId`, `RunId`, `SavedAtUtc`, `CombatKind`, `Day`, `Hour`, `EncounterId`, `Participants`, `Outcome`, `Snapshots`
- `PvpReplayPayload` is a pure DTO with `BattleId`, `Version`, `SpawnMessageBase64`, `CombatMessageBase64`, `DespawnMessageBase64`
- `PvpBattleCardSetCapture` is the reusable wrapper for player/opponent hand/skills

Use enums like:

```csharp
internal enum PvpBattleCaptureStatus
{
    Missing,
    CapturedEmpty,
    Captured,
}
```

Rename `RunLogCombatReplayInput` to `RunLogPvpBattleInput` and swap `ReplayId` for `BattleId`.

**Step 4: Run tests to verify they pass**

Run:
- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: PASS with the new contracts visible to the test projects.

**Step 5: Commit**

```bash
git add tests/CombatReplayRecording.Tests/Program.cs tests/RunLoggingCapture.Tests/Program.cs Game/PvpBattles Game/RunLogging/RunLogCaptureService.cs Game/RunLogging/Models/RunLogEvent.cs
git commit -m "feat: add pvp battle domain contracts"
```

### Task 2: Split capture into matcher, collector, and factories

**Files:**
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`
- Create: `Game/PvpBattles/PvpBattleSequenceMatcher.cs`
- Create: `Game/PvpBattles/PvpBattleSnapshotCollector.cs`
- Create: `Game/PvpBattles/PvpBattleManifestFactory.cs`
- Create: `Game/PvpBattles/PvpReplayPayloadFactory.cs`
- Modify: `Game/CombatReplay/CombatReplayCaptureService.cs`
- Modify: `Game/CombatReplay/CombatReplayCardSnapshot.cs`

**Step 1: Write the failing test**

Extend `tests/CombatReplayRecording.Tests/Program.cs` so it asserts:

- only an opening `PVPCombat` `GameSim` can start a candidate
- stray `CombatSim` messages are ignored
- a second `CombatSim` after a candidate already has one causes reset
- candidates without a `CombatMessage` never produce a battle artifact
- snapshots for `PlayerHand`, `PlayerSkills`, `OpponentHand`, `OpponentSkills` carry `Status + Source`

Assert source-level seams too:

- `PvpBattleSequenceMatcher` exists and is referenced from `CombatReplayCaptureService`
- `PvpBattleSnapshotCollector` exists and no longer leaves capture semantics implicit
- `PvpBattleManifestFactory` is the only place that generates `BattleId`
- `CombatReplayCaptureService` becomes a compatibility wrapper around the new capture pipeline

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: FAIL because the new capture pipeline does not exist yet.

**Step 3: Write minimal implementation**

Port logic out of `CombatReplayCaptureService` into:

- `PvpBattleSequenceMatcher` for message-window assembly
- `PvpBattleSnapshotCollector` for battle facts from `SequenceWindow + live Data`
- `PvpBattleManifestFactory` for `BattleId` assignment and manifest projection
- `PvpReplayPayloadFactory` for payload projection

Implementation rules:

- keep the existing opponent identity priority: opening `GameSim` first, live `Data` fallback second
- keep player live retry behavior, but map success/failure into `PvpBattleCardSetCapture`
- represent “captured empty” explicitly instead of letting an empty list mean “maybe failed”
- keep `CombatReplayRuntime` unchanged in this task; direct runtime wiring moves to Task 4

In this task, `CombatReplayCaptureService` should still exist, but only as a compatibility wrapper that delegates to the new matcher / collector / factories. That keeps the first batch internally executable without forcing runtime changes early.

The collector output should look like:

```csharp
new PvpBattleCardSetCapture(
    items,
    items.Count == 0 ? PvpBattleCaptureStatus.CapturedEmpty : PvpBattleCaptureStatus.Captured,
    PvpBattleCaptureSource.LiveRetry
);
```

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: PASS with the matcher, collector, and factories visible and wired by source assertions.

**Step 5: Commit**

```bash
git add tests/CombatReplayRecording.Tests/Program.cs Game/PvpBattles Game/CombatReplay/CombatReplayCaptureService.cs Game/CombatReplay/CombatReplayCardSnapshot.cs
git commit -m "feat: split pvp battle capture pipeline"
```

### Task 3: Build the manifest catalog and replace the SQLite projection

**Files:**
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`
- Modify: `tests/RunLoggingSqliteSchema.Tests/Program.cs`
- Modify: `tests/RunLoggingExport.Tests/Program.cs`
- Modify: `Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs`
- Create: `Game/PvpBattles/Persistence/IPvpBattleCatalog.cs`
- Create: `Game/PvpBattles/Persistence/PvpBattleCatalog.cs`
- Create: `Game/PvpBattles/Persistence/PvpBattleSqliteStore.cs`
- Modify: `scripts/export_run_log.py`

**Step 1: Write the failing tests**

Add coverage that proves the new read/write side exists:

- `PvpBattleCatalog` exposes `Save`, `TryLoad`, and `ListRecentBattles`
- `pvp_battles` no longer includes `replay_id`
- each snapshot column exports a full `{items,status,source}` object instead of a bare array
- `RunLoggingExport.Tests` reads the new shape from `pvp_battles.ndjson`

Use fixture JSON like:

```json
{
  "status": "Captured",
  "source": "OpeningMessage",
  "items": [{ "instance_id": "hand-1", "name": "Sparkblade" }]
}
```

**Step 2: Run tests to verify they fail**

Run:
- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet run --project tests/RunLoggingSqliteSchema.Tests/RunLoggingSqliteSchema.Tests.csproj`
- `dotnet run --project tests/RunLoggingExport.Tests/RunLoggingExport.Tests.csproj`

Expected: FAIL because SQLite still writes replay-first rows and export still expects `replay_id` plus bare arrays.

**Step 3: Write minimal implementation**

Create `PvpBattleCatalog` as the manifest read-side interface and make the SQLite store back it.

Database changes:

- remove `replay_id` from `pvp_battles`
- keep `battle_id`, identities, outcome columns, and four JSON snapshot columns
- write each JSON column as serialized `PvpBattleCardSetCapture`

Read-side methods to implement first:

```csharp
PvpBattleManifest? TryLoad(string battleId);
IReadOnlyList<PvpBattleManifest> ListRecentBattles(int limit);
void Save(PvpBattleManifest manifest);
```

Update `export_run_log.py` to emit:

- `battle_id`
- `player_hand`, `player_skills`, `opponent_hand`, `opponent_skills`

where each of those values is the full capture object, not only `items`.

**Step 4: Run tests to verify they pass**

Run:
- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet run --project tests/RunLoggingSqliteSchema.Tests/RunLoggingSqliteSchema.Tests.csproj`
- `dotnet run --project tests/RunLoggingExport.Tests/RunLoggingExport.Tests.csproj`

Expected: PASS with catalog reads working and export reflecting the new manifest projection.

**Step 5: Commit**

```bash
git add tests/CombatReplayRecording.Tests/Program.cs tests/RunLoggingSqliteSchema.Tests/Program.cs tests/RunLoggingExport.Tests/Program.cs Game/PvpBattles/Persistence Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs scripts/export_run_log.py
git commit -m "feat: persist pvp battle manifests in sqlite"
```

### Task 4: Cut replay storage and playback over to `BattleId + payload`

**Files:**
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Modify: `Game/CombatReplay/CombatReplayController.cs`
- Modify: `Game/CombatReplay/CombatReplayLoader.cs`
- Create: `Game/CombatReplay/CombatReplayPayloadStore.cs`

**Step 1: Write the failing test**

Update the replay-recording test suite to assert the new runtime flow:

- `CombatReplayLoader` loads `PvpReplayPayload`, not `CombatReplayRecord`
- `CombatReplayController` first reads manifest from `PvpBattleCatalog`, then payload from `CombatReplayPayloadStore`
- `CombatReplayRuntime.ObserveMessage(...)` saves payload first, manifest second, and only then emits a run-log event
- replay-side rehydration still uses manifest snapshots for player/opponent hand and skills

Add source assertions such as:

```csharp
Assert(
    runtimeSource.IndexOf("payloadStore.Save", StringComparison.Ordinal)
        < runtimeSource.IndexOf("battleCatalog.Save", StringComparison.Ordinal),
    "Runtime should persist payloads before making the battle visible via the catalog."
);
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: FAIL because runtime/controller/loader still depend on `CombatReplayRecord` and `CombatReplayStore`.

**Step 3: Write minimal implementation**

Implement `CombatReplayPayloadStore` with only `Save`, `Load`, and `Exists`.

Change controller/runtime flow to:

1. matcher + collector + factories produce `manifest` and `payload`
2. runtime calls `payloadStore.Save(payload)`
3. runtime calls `battleCatalog.Save(manifest)`
4. runtime calls `RunLoggingController.CapturePvpBattle(manifest)`

Change replay reads to:

1. `catalog.TryLoad(battleId)`
2. `payloadStore.Load(battleId)`
3. `loader.Load(payload)`
4. `TryInjectSavedReplayAsync(..., manifest, sequence, battleId)`

Do not touch the replay bootstrap scene-loading code in this task.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: PASS with the runtime and controller using `BattleId + payload`.

**Step 5: Commit**

```bash
git add tests/CombatReplayRecording.Tests/Program.cs Game/CombatReplay/CombatReplayRuntime.cs Game/CombatReplay/CombatReplayController.cs Game/CombatReplay/CombatReplayLoader.cs Game/CombatReplay/CombatReplayPayloadStore.cs
git commit -m "feat: replay saved battles from manifest and payload"
```

### Task 5: Move debug replay lists and labels to the manifest catalog

**Files:**
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`
- Modify: `Game/DebugPanel/DebugPanel.cs`
- Modify: `Game/CombatReplay/CombatReplayController.cs`
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`

**Step 1: Write the failing test**

Add assertions that:

- the debug panel still shows `Replay Latest`
- replay labels are built from manifest metadata
- `ListSavedReplays()` or its replacement reads from the manifest catalog instead of enumerating payload files
- the active replay identity is a `BattleId`

Check source strings like:

```csharp
Assert(
    debugPanelSource.Contains("ListRecentBattles", StringComparison.Ordinal),
    "Debug panel should build replay entries from the battle catalog."
);
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: FAIL because the debug UI still depends on the old replay-file list.

**Step 3: Write minimal implementation**

Change the read-side surface so `CombatReplayRuntime` and `CombatReplayController` expose manifest-backed list APIs. Keep the debug button text stable for now, but make the underlying lookup battle-first.

Recommended minimal API:

```csharp
IReadOnlyList<PvpBattleManifest> ListRecentBattles();
PvpBattleManifest? GetLatestBattle();
bool ReplaySaved(string battleId);
```

If you keep compatibility wrapper names on the runtime temporarily, make them thin shims over the catalog-backed API and remove them in Task 8.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: PASS with the debug panel reading recent battles from the catalog.

**Step 5: Commit**

```bash
git add tests/CombatReplayRecording.Tests/Program.cs Game/DebugPanel/DebugPanel.cs Game/CombatReplay/CombatReplayController.cs Game/CombatReplay/CombatReplayRuntime.cs
git commit -m "feat: read replay lists from pvp battle catalog"
```

### Task 6: Replace replay-first run logging with manifest-first battle events

**Files:**
- Modify: `tests/RunLoggingCapture.Tests/Program.cs`
- Modify: `Game/RunLogging/RunLoggingController.cs`
- Modify: `Game/RunLogging/RunLogCaptureService.cs`
- Modify: `Game/RunLogging/Models/RunLogEvent.cs`

**Step 1: Write the failing test**

Change `tests/RunLoggingCapture.Tests/Program.cs` so it asserts:

- `RunLoggingController` exposes `CapturePvpBattle(PvpBattleManifest manifest)`
- the emitted event contains `BattleId` rather than `ReplayId`
- the event kind remains `pvp_combat_recorded`
- non-`PVPCombat` manifests are ignored at the controller boundary

Use a focused seam assertion:

```csharp
Assert(
    battleEvent.BattleId == "battle-123",
    "PVP battle events should preserve the canonical battle id."
);
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: FAIL because run logging still accepts replay-first input.

**Step 3: Write minimal implementation**

Refactor the controller and capture service so they consume manifest-derived inputs.

Recommended shape:

```csharp
public RunLogEvent? CapturePvpBattle(PvpBattleManifest manifest)
```

with a small internal projection:

```csharp
new RunLogPvpBattleInput
{
    BattleId = manifest.BattleId,
    Day = manifest.Day,
    Hour = manifest.Hour,
    EncounterId = manifest.EncounterId,
    CombatKind = manifest.CombatKind,
    OpponentName = manifest.Participants.OpponentName,
}
```

Do not let run logging become a second source of truth for battle visibility; it should stay best-effort.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: PASS with battle-first run-log events.

**Step 5: Commit**

```bash
git add tests/RunLoggingCapture.Tests/Program.cs Game/RunLogging/RunLoggingController.cs Game/RunLogging/RunLogCaptureService.cs Game/RunLogging/Models/RunLogEvent.cs
git commit -m "feat: capture manifest-backed pvp battle events"
```

### Task 7: Inject manifest-based metadata into `CombatLogRuntime`

**Files:**
- Modify: `tests/CombatLogRuntime.Tests/Program.cs`
- Modify: `Game/CombatLog/CombatLogRuntime.cs`
- Modify: `Game/CombatLog/CombatLogController.cs`
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Create: `Game/CombatLog/ICombatCardMetadataSource.cs`
- Create: `Game/CombatLog/PvpBattleManifestCardMetadataSource.cs`

**Step 1: Write the failing test**

Extend `tests/CombatLogRuntime.Tests/Program.cs` to assert:

- `CombatLogRuntime` can resolve names from an injected metadata source
- injected metadata wins over live `Data`
- fallback still uses the short identifier when neither source resolves a card

Add a test setup like:

```csharp
var runtime = new CombatLogRuntime(new TestMetadataSource(
    new CombatLogCardDisplayInfo("card-a", "tpl-a", "Manifest Dagger")
));
```

Then assert rendered rows use `"Manifest Dagger"` instead of the live-data name.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: FAIL because combat log metadata is still only a resolver delegate plus live `Data`.

**Step 3: Write minimal implementation**

Introduce a tiny abstraction:

```csharp
internal interface ICombatCardMetadataSource
{
    CombatLogCardDisplayInfo? Resolve(string instanceId);
}
```

Use it in `CombatLogRuntime` with fixed priority:

1. injected metadata source
2. live `Data`
3. fallback short identifier

Create `PvpBattleManifestCardMetadataSource` that projects `manifest.Snapshots` into `CombatLogCardDisplayInfo`.

Have `CombatReplayRuntime` or `CombatLogController` inject that source only while replay playback is active.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS with stable manifest-backed replay names.

**Step 5: Commit**

```bash
git add tests/CombatLogRuntime.Tests/Program.cs Game/CombatLog/CombatLogRuntime.cs Game/CombatLog/CombatLogController.cs Game/CombatReplay/CombatReplayRuntime.cs Game/CombatLog/ICombatCardMetadataSource.cs Game/CombatLog/PvpBattleManifestCardMetadataSource.cs
git commit -m "feat: resolve replay combat log names from manifests"
```

### Task 8: Remove replay-first legacy types and run the full verification sweep

**Files:**
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`
- Delete: `Game/CombatReplay/CombatReplayRecord.cs`
- Delete: `Game/CombatReplay/CombatReplayStore.cs`
- Delete: `Game/CombatReplay/CombatReplayCaptureService.cs`
- Delete: `Game/CombatReplay/CombatReplaySequenceCandidate.cs`

**Step 1: Remove dead code**

Delete the replay-first types once all call sites are moved:

- `CombatReplayRecord`
- `CombatReplayStore`
- `CombatReplayCaptureService`
- `CombatReplaySequenceCandidate`

Also remove any remaining `ReplayId`-named fields or compatibility shims on the runtime/controller surface.

**Step 2: Update tests to assert the old path is gone**

Change `tests/CombatReplayRecording.Tests/Program.cs` so it asserts the new types exist and the deleted ones no longer drive the runtime.

Source assertions should check for:

- `PvpBattleSequenceMatcher`
- `PvpBattleCatalog`
- `CombatReplayPayloadStore`

and should stop checking for:

- `_store.Save(record);`
- `_pvpBattleStore.Save(record);`
- `CombatReplayCaptureService`

**Step 3: Run focused regression tests**

Run:
- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`
- `dotnet run --project tests/RunLoggingSqliteSchema.Tests/RunLoggingSqliteSchema.Tests.csproj`
- `dotnet run --project tests/RunLoggingExport.Tests/RunLoggingExport.Tests.csproj`
- `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS

If `CombatLogRuntime.Tests` cannot auto-detect the Bazaar managed DLL path on the current machine, rerun with:

```bash
dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj -p:ManagedPath="/absolute/path/to/TheBazaar_Data/Managed"
```

**Step 4: Build the plugin**

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: `BUILD SUCCEEDED`

**Step 5: Commit**

```bash
git add tests/CombatReplayRecording.Tests/Program.cs Game/PvpBattles Game/CombatReplay Game/CombatLog Game/RunLogging scripts/export_run_log.py
git commit -m "refactor: make pvp battles the primary replay record"
```
