# Architecture Modularization Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the current static-state and instance-to-instance feature wiring with a host-managed modular runtime that uses explicit core services, an internal event bus, and read-only query services.

**Architecture:** Keep the codebase in one assembly for now, introduce a `BppRuntimeHost` composition root, convert Harmony patches into thin adapters, and migrate feature state module-by-module out of `ModState` and runtime singletons. Preserve existing runtime behavior during the migration by adding compatibility seams first and removing them last.

**Tech Stack:** C# 12, BepInEx, Harmony, Unity `MonoBehaviour`, Microsoft.Data.Sqlite, Newtonsoft.Json, console-style test projects.

---

### Task 1: Add the new runtime host and core contracts

**Files:**
- Modify: `Plugin.cs`
- Create: `Core/Runtime/BppRuntimeHost.cs`
- Create: `Core/Events/IBppEventBus.cs`
- Create: `Core/Events/InMemoryBppEventBus.cs`
- Create: `Core/Config/IBppConfig.cs`
- Create: `Core/Paths/IPathService.cs`
- Create: `Core/RunContext/IRunContext.cs`
- Create: `Core/GameState/IGameStateProbe.cs`

**Step 1: Write the failing test**

Add source-level assertions to one existing test project, or create a small new architecture smoke test, that verifies:

- `BppRuntimeHost` exists
- `Plugin` references `BppRuntimeHost`
- `IBppEventBus`, `IBppConfig`, `IPathService`, `IRunContext`, and `IGameStateProbe` exist

Use reflection assertions only. Do not require Unity runtime boot.

**Step 2: Run test to verify it fails**

Run the chosen test project.

Expected: FAIL because the new host and core contracts do not exist yet.

**Step 3: Write minimal implementation**

Create the new core interfaces and a simple synchronous in-memory event bus implementation.

Update `Plugin` so it:

- creates `BppRuntimeHost`
- calls `Install()`
- calls `Start()`
- delegates teardown through the host

Do not remove existing `AddComponent` calls yet. The first pass should only insert the new composition root.

**Step 4: Run test to verify it passes**

Run the same test project.

Expected: PASS with the new host and core contracts visible.

**Step 5: Commit**

```bash
git add Plugin.cs Core
git commit -m "refactor: add runtime host and core contracts"
```

### Task 2: Split `ModState` into compatibility-backed services

**Files:**
- Modify: `Models/ModState.cs`
- Create: `Core/Config/BppConfig.cs`
- Create: `Core/Paths/BppPathService.cs`
- Create: `Core/RunContext/RunContextStore.cs`
- Create: `Core/GameState/GameStateProbe.cs`
- Modify: `Infrastructure/BppLog.cs`

**Step 1: Write the failing test**

Add reflection/source assertions that verify:

- the concrete service types exist
- `BppLog` no longer requires direct writes from arbitrary feature code
- `RunContextStore` exists separately from config and path services

Keep the assertions structural; do not delete `ModState` from the test expectation yet.

**Step 2: Run test to verify it fails**

Run the selected test project.

Expected: FAIL because those services do not exist yet.

**Step 3: Write minimal implementation**

Create the concrete service types and move initialization logic into them.

Keep `ModState` temporarily as a compatibility shell that forwards to the new services where practical. The compatibility layer should be thin and clearly marked.

Do not migrate all feature call sites yet.

**Step 4: Run test to verify it passes**

Run the same test project.

Expected: PASS with the service split visible while old behavior remains intact.

**Step 5: Commit**

```bash
git add Models/ModState.cs Infrastructure/BppLog.cs Core
git commit -m "refactor: split mod state into core services"
```

### Task 3: Convert key patches into event publishers

**Files:**
- Modify: `Patches/RunLogging/RunInitializedPatch.cs`
- Modify: `Patches/Combat/CombatReplayCapturePatch.cs`
- Modify: `Patches/Combat/CombatSimulationPatches.cs`
- Create: `Core/Events/RunInitializedObserved.cs`
- Create: `Core/Events/NetMessageObserved.cs`
- Create: `Core/Events/CombatSimObserved.cs`
- Create: `Core/Events/CombatFrameAdvanced.cs`

**Step 1: Write the failing test**

Add source assertions that verify the key patches:

- reference `IBppEventBus` or an event publisher dependency
- no longer directly call `*.Instance`
- no longer directly mutate feature state except compatibility-safe transitional code

Keep the first wave focused on the three high-value patch files above.

**Step 2: Run test to verify it fails**

Run the selected architecture test project.

Expected: FAIL because the patches still directly call runtimes and static methods.

**Step 3: Write minimal implementation**

Introduce event DTOs for:

- run initialized
- net message observed
- combat sim observed
- combat frame advanced

Update the three patches so they publish events through the host-owned event bus.

If transitional compatibility is needed, keep it in module subscribers, not in the patches.

**Step 4: Run test to verify it passes**

Run the same test project.

Expected: PASS with the patch layer downgraded to adapters.

**Step 5: Commit**

```bash
git add Patches Core/Events
git commit -m "refactor: convert core patches to event publishers"
```

### Task 4: Create `EncounterTrackingModule` as the single owner of selection state

**Files:**
- Modify: `Game/EncounterTracker.cs`
- Create: `Game/EncounterTracking/EncounterTrackingModule.cs`
- Create: `Game/EncounterTracking/EncounterTrackingStateStore.cs`
- Create: `Game/EncounterTracking/IEncounterSelectionQuery.cs`
- Create: `Game/EncounterTracking/EncounterSelectionSnapshot.cs`
- Create: `Core/Events/SelectionObserved.cs`

**Step 1: Write the failing test**

Add tests or source assertions that verify:

- a new `EncounterTrackingModule` exists
- `EncounterTracker` no longer owns canonical shared state
- a read-only `IEncounterSelectionQuery` exists
- selection observation is emitted as an event

**Step 2: Run test to verify it fails**

Run the chosen test project.

Expected: FAIL because the new module and state store do not exist yet.

**Step 3: Write minimal implementation**

Create the module and state store.

Refactor `EncounterTracker` so it becomes either:

- a compatibility wrapper around the new module, or
- a thin game event adapter that feeds the module

Move ownership of:

- available encounters
- current encounter choices
- monster preview cache

out of `ModState` and into the new module state store.

Publish `SelectionObserved` after state updates.

**Step 4: Run test to verify it passes**

Run the same test project and any selection-related tests already in the repo.

Expected: PASS with encounter state owned by the new module.

**Step 5: Commit**

```bash
git add Game/EncounterTracker.cs Game/EncounterTracking Core/Events
git commit -m "refactor: isolate encounter tracking module"
```

### Task 5: Move run logging to event-driven inputs and feature-local mappers

**Files:**
- Modify: `Game/RunLogging/RunLoggingController.cs`
- Modify: `Game/GameDataReader.cs`
- Create: `Game/RunLogging/RunLoggingModule.cs`
- Create: `Game/RunLogging/Mappers/RunLogRequestMapper.cs`
- Create: `Game/RunLogging/Mappers/RunLogSelectionMapper.cs`
- Create: `Game/RunLogging/Queries/IRunLoggingQuery.cs`

**Step 1: Write the failing test**

Extend existing run logging tests so they verify:

- `RunLoggingModule` exists
- selection capture can be triggered from a `SelectionObserved` input
- run-log DTO construction is no longer owned by `GameDataReader`

Prefer source assertions for ownership and existing run logging tests for behavior.

**Step 2: Run test to verify it fails**

Run:
- `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`
- `dotnet run --project tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj`

Expected: FAIL or remain incomplete because the new module and mappers do not exist yet.

**Step 3: Write minimal implementation**

Create `RunLoggingModule` that subscribes to:

- run lifecycle events
- selection observed events
- pvp battle captured events

Move feature-specific DTO construction out of `GameDataReader` into run logging mappers.

Keep `RunLoggingController` as a thin facade during migration, but route new behavior through the module.

**Step 4: Run test to verify it passes**

Run the two run logging test projects again.

Expected: PASS with run logging fed by event-driven inputs and feature-local mappers.

**Step 5: Commit**

```bash
git add Game/RunLogging Game/GameDataReader.cs
git commit -m "refactor: drive run logging from modular event inputs"
```

### Task 6: Separate `CombatStatusBar` UI from state and consume combat events

**Files:**
- Modify: `Game/CombatStatusBar/CombatStatusBar.cs`
- Modify: `Game/CombatStatusBar/CombatStatusBar.State.cs`
- Create: `Game/CombatStatusBar/CombatStatusBarModule.cs`
- Create: `Game/CombatStatusBar/CombatStatusBarStateStore.cs`
- Create: `Game/CombatStatusBar/ICombatStatusBarQuery.cs`

**Step 1: Write the failing test**

Add tests or source assertions that verify:

- `CombatStatusBarStateStore` exists
- combat status state is no longer stored purely as static members on the view class
- the feature consumes combat events instead of direct patch-to-static calls

**Step 2: Run test to verify it fails**

Run:
- `dotnet run --project tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: FAIL because the state store and module do not exist yet.

**Step 3: Write minimal implementation**

Move mutable state into `CombatStatusBarStateStore`.

Keep the Unity-facing view class, but have it read through a query/service boundary.

Route combat frame and combat sim updates from module subscribers rather than static patch calls.

**Step 4: Run test to verify it passes**

Run the status bar test project again.

Expected: PASS with state moved behind a module-owned store.

**Step 5: Commit**

```bash
git add Game/CombatStatusBar
git commit -m "refactor: separate combat status bar state from view"
```

### Task 7: Replace global monster preview lookups with a resolver

**Files:**
- Modify: `Game/MonsterPreview/MonsterLockShowcaseRuntime.cs`
- Create: `Game/MonsterPreview/Sources/IMonsterPreviewSourceResolver.cs`
- Create: `Game/MonsterPreview/Sources/EncounterCacheSource.cs`
- Create: `Game/MonsterPreview/Sources/MonsterDatabaseSource.cs`
- Create: `Game/MonsterPreview/MonsterPreviewModule.cs`

**Step 1: Write the failing test**

Extend monster preview tests so they verify:

- a resolver abstraction exists
- `MonsterLockShowcaseRuntime` does not require direct reads from `ModState`
- encounter-cache and database-backed sources can both satisfy preview lookup

**Step 2: Run test to verify it fails**

Run:
- `dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`
- `dotnet run --project tests/EncounterPreviewConversion.Tests/EncounterPreviewConversion.Tests.csproj`

Expected: FAIL because the resolver abstraction does not exist yet.

**Step 3: Write minimal implementation**

Create `IMonsterPreviewSourceResolver` and two concrete sources:

- encounter cache source
- monster database source

Update `MonsterLockShowcaseRuntime` to query the resolver instead of directly reading global state.

Keep the existing board rendering architecture unchanged in this task.

**Step 4: Run test to verify it passes**

Run the two monster preview test projects again.

Expected: PASS with preview lookup detached from global state.

**Step 5: Commit**

```bash
git add Game/MonsterPreview
git commit -m "refactor: modularize monster preview sources"
```

### Task 8: Split `CombatReplayRuntime` into capture, playback, and bootstrap responsibilities

**Files:**
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Create: `Game/CombatReplay/ReplayCaptureGateway.cs`
- Create: `Game/CombatReplay/ReplayPlaybackService.cs`
- Create: `Game/CombatReplay/ReplayBootstrapper.cs`
- Create: `Game/CombatReplay/ReplayRuntimeFacade.cs`
- Modify: `Game/PvpBattles/Persistence/PvpBattleCatalog.cs`

**Step 1: Write the failing test**

Add source-level assertions that verify:

- `CombatReplayRuntime` no longer owns all major responsibilities directly
- capture, playback, and bootstrap seams exist as separate concrete types
- the runtime acts as a thin facade rather than the canonical owner of all replay logic

**Step 2: Run test to verify it fails**

Run:
- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: FAIL because replay responsibilities are still concentrated in one runtime class.

**Step 3: Write minimal implementation**

Extract:

- message observation and capture handoff into `ReplayCaptureGateway`
- replay startup/playback orchestration into `ReplayPlaybackService`
- scene/bootstrap/reflection preparation into `ReplayBootstrapper`

Reduce `CombatReplayRuntime` to a facade that delegates to those services.

Do not change saved replay behavior in this task beyond the structural split.

**Step 4: Run test to verify it passes**

Run the replay recording test project and any replay-related smoke checks.

Expected: PASS with the replay runtime structurally decomposed.

**Step 5: Commit**

```bash
git add Game/CombatReplay Game/PvpBattles/Persistence
git commit -m "refactor: split combat replay runtime responsibilities"
```

### Task 9: Remove compatibility shims and delete migrated `ModState` usage

**Files:**
- Modify: `Models/ModState.cs`
- Modify: all migrated feature files from earlier tasks
- Modify: `Game/GameDataReader.cs`

**Step 1: Write the failing test**

Add source assertions that verify:

- migrated features no longer read or write deprecated `ModState` fields
- `GameDataReader` no longer owns feature-specific mappers
- direct `*.Instance` cross-feature calls are removed from migrated paths

**Step 2: Run test to verify it fails**

Run the architecture/source-assertion test project and a representative set of existing feature tests.

Expected: FAIL because compatibility references still remain.

**Step 3: Write minimal implementation**

Delete deprecated fields and compatibility access paths that earlier tasks have already migrated away from.

Keep only truly unmigrated compatibility logic, or remove `ModState` entirely if nothing remains.

Clean up `GameDataReader` so it is either:

- a pure game snapshot reader, or
- replaced by smaller snapshot readers

**Step 4: Run test to verify it passes**

Run the architecture test plus the representative feature tests again.

Expected: PASS with compatibility shims removed.

**Step 5: Commit**

```bash
git add Models/ModState.cs Game/GameDataReader.cs
git commit -m "refactor: remove legacy modularization shims"
```

### Task 10: Run end-to-end verification across representative feature areas

**Files:**
- No code changes required unless verification finds regressions

**Step 1: Run modularization verification suite**

Run:
- `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`
- `dotnet run --project tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj`
- `dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj`
- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet run --project tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
- `dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`
- `dotnet run --project tests/EncounterPreviewConversion.Tests/EncounterPreviewConversion.Tests.csproj`

Expected: PASS across the representative modularized features.

**Step 2: Fix any failing area**

If a test fails, make the minimal targeted fix in the affected module. Re-run only the failing test first, then re-run the full verification suite.

**Step 3: Commit**

```bash
git add .
git commit -m "test: verify architecture modularization"
```

Plan complete and saved to `docs/plans/2026-03-20-architecture-modularization.md`. Two execution options:

**1. Subagent-Driven (this session)** - I dispatch fresh subagent per task, review between tasks, fast iteration

**2. Parallel Session (separate)** - Open new session with executing-plans, batch execution with checkpoints

Which approach?
