# Unused Code and DTO Deletion Plan

## Goal

Remove only code that is provably disconnected from production behavior, while
preserving reflection, Harmony, serialization, and external contract entry
points that look unused to simple text search.

This plan is intentionally narrow. It does not include broader refactors,
renames, or DTO shape changes unless the current code shows the member is no
longer part of the active contract.

## Current Worktree Context

- `src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs` is already
  deleted in the working tree. Keep that deletion in the cleanup batch.
- `docs/README.md` and `docs/plans/package-card-art-live-game-resolution-goal.md`
  already have unrelated local changes. Do not edit them as part of this cleanup
  unless explicitly requested. The package-card-art implementation no longer
  depends on `ShopForecast`; remaining references in that goal document are
  historical execution notes, not production dependencies.

## Deletion Candidates

### 1. Remove ShopForecast diagnostic patch

Decision: delete.

Evidence:

- The current source tree has no `ShopForecast` or `ShopForecastLogPatch`
  references under `src/` or `tests/` after the file deletion.
- Harmony patches are discovered through `Plugin._harmony.PatchAll()` in
  `src/BazaarPlusPlus/Plugin.cs:146-150`, so a patch file with no direct
  call sites can still be live. This candidate is different: the feature was
  only a diagnostic patch, and no production code consumes a value produced by
  it.
- Current Collection Panel source filtering reads the selected source from its
  own filter state and source catalog, then resolves offer pools in
  `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:795-846`.
- Collection Panel source metadata is loaded from embedded
  `collection-sources.json` through
  `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:60-82`,
  not from `ShopForecast` output.

Implementation:

1. Keep the existing deletion of
   `src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs`.
2. Do not remove archived documentation references in this cleanup. Archived
   docs are historical evidence and may reference deleted files.
3. Do not block this deletion on package-card-art. The implemented package-art
   replacement paths do not read `ShopForecast`; if
   `docs/plans/package-card-art-live-game-resolution-goal.md` is kept, clean up
   its stale diagnostic references separately.

Verification:

1. Run `rg -n "ShopForecast|ShopForecastLogPatch" src tests`.
2. Expected result: no matches.
3. Run `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`.
4. Restart the game and verify the runtime log no longer emits
   `[BPP][ShopForecast]` lines.

### 2. Remove RunIdFactory

Decision: delete.

Evidence:

- `RunIdFactory` is defined in
  `src/BazaarPlusPlus/Game/RunLogging/RunIdFactory.cs:9`.
- A full source/test search for `RunIdFactory`, its `Create` output shape, and
  `NormalizeNonce` only matches its own file.
- Active run logging creates sessions from `RunLogCreateRequest` values read
  from game state, not through `RunIdFactory`; see
  `src/BazaarPlusPlus/Game/RunLogging/RunLoggingGameDataReader.cs:29-39` and
  `src/BazaarPlusPlus/Game/RunLogging/RunLoggingController.cs:107-143`.

Implementation:

1. Delete `src/BazaarPlusPlus/Game/RunLogging/RunIdFactory.cs`.
2. Run a source search for `RunIdFactory` and `run_` id construction.
3. Do not replace it with a new helper unless a current caller appears during
   implementation.

Verification:

1. Run `rg -n "RunIdFactory|NormalizeNonce|run_\\{timeFragment" src tests`.
2. Expected result: no matches.
3. Run `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`.
4. Run the run-logging related tests:
   - `dotnet run --project tests/RunLoggingModule.Tests/RunLoggingModule.Tests.csproj`
   - `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
   - `dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj`
   - `dotnet run --project tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj`

### 3. Remove EndOfRunContinueStateEvaluator

Decision: delete, with matching test cleanup.

Evidence:

- `EndOfRunContinueStateEvaluator` is defined in
  `src/BazaarPlusPlus/Game/Screenshots/EndOfRunContinueStateEvaluator.cs:7`.
- The production Harmony prefix no longer calls it. The prefix delegates
  directly to `EndOfRunScreenshotController` in
  `src/BazaarPlusPlus/Patches/EndOfRun/EndOfRunScreenshotPatch.cs:15-28`.
- The current mouse blocker and first-capture gate are owned by
  `EndOfRunScreenshotController`, which creates `_mouseBlocker` at
  `src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:28`,
  runs `SyncEndOfRunMouseBlocker()` from `Update()` at
  `src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:112-115`,
  and attaches/detaches the blocker at
  `src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:470-505`.
- The remaining references are test-only reflection checks in
  `tests/EndOfRunScreenshotGate.Tests/Program.cs:87-90` and
  `tests/EndOfRunScreenshotGate.Tests/Program.cs:215-246`.

Implementation:

1. Delete `src/BazaarPlusPlus/Game/Screenshots/EndOfRunContinueStateEvaluator.cs`.
2. Remove the `continueStateEvaluatorType` lookup from
   `tests/EndOfRunScreenshotGate.Tests/Program.cs`.
3. Remove the `InvokeShouldAllowContinue` helper from
   `tests/EndOfRunScreenshotGate.Tests/Program.cs`.
4. Remove the three assertions that only validate
   `EndOfRunContinueStateEvaluator.ShouldAllowContinue`.
5. Keep tests for `EndOfRunScreenshotGate`, `EndOfRunSummaryRevealDetector`,
   screenshot path construction, and capture-source enum behavior.

Verification:

1. Run `rg -n "EndOfRunContinueStateEvaluator|ShouldAllowContinue|TryShouldAllowContinue|TryIsInteractionBlocked|TryGetTransitionCount" src tests`.
2. Expected result: no matches, except unrelated game-test fixture fields named
   `_transitionCount` if they remain useful for reveal detector tests.
3. Run `dotnet run --project tests/EndOfRunScreenshotGate.Tests/EndOfRunScreenshotGate.Tests.csproj`.
4. Run `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`.

### 4. Remove RunProjection.Battles

Decision: delete.

Evidence:

- `RunProjection.Battles` is marked `[JsonIgnore]` in
  `src/BazaarPlusPlus.ModApi/Models/RunBundleUploadRequest.cs:75-76`.
- The active upload path fills top-level `BattleProjections`, not nested
  `RunProjection.Battles`, in
  `src/BazaarPlusPlus/Game/RunLogging/Upload/RunBundleUploadStore.cs:205-230`.
- The current test at
  `tests/ModApi.Tests/BazaarDbSnapshotClientTests.cs:144-168` only proves that
  nested `RunProjection.Battles` is excluded from the current wire contract and
  that top-level `battle_projections` remains the source of truth.
- Server confirmation: `../bazaarplusplus-server/src/features/runBundles/upload.ts`
  parses metadata into `RawRunBundleRequest` at lines 18-24, reads run fields
  from `rawBody.run_projection` at lines 333-355, and reads battles only from
  top-level `rawBody.battle_projections` at lines 357-359; it does not read or
  validate `run_projection.battles`.
- Server wire docs confirm the same contract:
  `../bazaarplusplus-server/docs/api-reference.md:53-78` lists run fields under
  `run_projection` and battle fields under top-level `battle_projections[]`.
- Server verification passed on 2026-06-11:
  `npm run check` and
  `npm test -- test/runBundles.parse.test.ts test/runBundles.battles.test.ts`.

Implementation:

1. Delete the `[JsonIgnore] public List<BattleProjection> Battles` property from
   `RunProjection`.
2. Update `tests/ModApi.Tests/BazaarDbSnapshotClientTests.cs` by removing the
   nested `Battles = [...]` setup.
3. Keep the assertion that `battle_projections` is top-level.
4. Keep an assertion that `json["run_projection"]?["battles"] == null`; it
   should still pass because the property no longer exists.

Risk:

- This is a public model in `BazaarPlusPlus.ModApi`. Even though the member is
  ignored for JSON and unused by current production code, deleting it is a
  source-level API break for any external code compiling against the assembly.
- The server wire contract is unaffected because this member is not serialized
  today and the server consumes only top-level `battle_projections`.

Verification:

1. Run `rg -n "RunProjection\\.Battles|Battles =|\\[JsonIgnore\\]" src tests`.
2. Review any remaining `Battles =` hits manually; many unrelated models have a
   legitimate `Battles` property.
3. Run:
   - `dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj`
   - `dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj`
   - `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`

## Non-Candidates

Do not delete these based on low text-reference counts:

- Harmony patch classes under `src/BazaarPlusPlus/Patches/`. They are discovered
  by `Plugin._harmony.PatchAll()` at `src/BazaarPlusPlus/Plugin.cs:146-150`.
- `CollectionSource*Dto` classes. They are required by JSON deserialization and
  catalog build code at
  `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:96-105`,
  `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:239-286`,
  and the nested DTO graph in
  `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceDtos.cs:7-109`.
- Run bundle artifact DTOs. They are serialized through
  `src/BazaarPlusPlus.ModApi/RunBundleArtifactCodec.cs:10-17` and built from
  persisted battle data in
  `src/BazaarPlusPlus/Game/RunLogging/Upload/RunBundleUploadStore.cs:343-430`.
- Snapshot upload DTOs. They are built in
  `src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadStore.cs:187-224`
  and posted through
  `src/BazaarPlusPlus.ModApi/Clients/BazaarDbSnapshotClient.cs:23`.
- BazaarAgent contract DTOs. They are public HTTP contract models, and many are
  serialized or deserialized at runtime rather than directly called.

## Suggested Execution Order

1. Keep `ShopForecastLogPatch` deletion.
2. Delete `RunIdFactory`.
3. Delete `EndOfRunContinueStateEvaluator` and update its test.
4. Delete `RunProjection.Battles` and update the ModApi serialization test.
5. Build and run the targeted tests above.
6. Run `./run.sh test` if the targeted tests and build are green.
7. Review the final diff before commit. Expected production deletions should be
   limited to the three internal files plus the DTO member.

## Rollback Plan

- If build or tests fail after deleting `RunIdFactory`, restore only
  `src/BazaarPlusPlus/Game/RunLogging/RunIdFactory.cs` and inspect the new
  caller that the search missed.
- If end-of-run tests fail after deleting `EndOfRunContinueStateEvaluator`,
  first update the test expectations to the current controller-owned gate path;
  restore the evaluator only if production code still needs transition-count
  behavior.
- If ModApi tests fail after deleting `RunProjection.Battles`, first remove the
  stale nested `Battles = [...]` setup from the serialization test. Restore the
  property only if a current production caller or server contract dependency is
  found.
