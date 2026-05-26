# Mod Architecture Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the rest of the mod up to the high-cohesion / low-coupling bar set by the already-extracted `BazaarPlusPlus.ModApi` and `BazaarPlusPlus.Storage` projects. Concretely:

1. Move three misplaced `Game/*/Persistence/` subfolders into the `Storage` project (they already only depend on `BazaarPlusPlus.Storage.*`).
2. Reconcile the `Infrastructure/` folder name vs. namespace, and unify `Patches/` namespaces.
3. Split `Core/` into pure abstractions vs. `GameInterop/` (game-DLL-coupled reflection bridges).
4. Invert the `Settings ↔ feature` bidirectional dependency by introducing a self-registering `ISettingsDockEntry` protocol — eliminates 7 cycles.
5. Resolve the `CombatReplay ↔ PvpBattles` cycle by moving the shared capture orchestration into CombatReplay so PvpBattles holds only data-model types.
6. Complete the `IBppMountable` migration: convert the 12 hand-attached MonoBehaviour features in `Plugin.cs` into `IBppMountable`s registered in `BppComposition`.

Behaviour must be unchanged at every checkpoint. Every commit is preceded by a green build and a relevant test run.

**Architecture:** Four ordered phases, each independently shippable. Phase 1 is pure file moves and namespace fixes (lowest risk). Phase 2 splits `Core/` into pure abstractions and `GameInterop/`. Phase 3 introduces a new registration protocol and resolves the largest cross-module cycle. Phase 4 finishes a migration that is already half-done (`IBppMountable` + `BppMountableRegistry` already exist; only one feature is registered today).

**Tech Stack:** C# 12 / netstandard2.1 / BepInEx 5 / Harmony / Microsoft.Data.Sqlite / Newtonsoft.Json. Tests live under `tests/<Area>.Tests/` and are exe-style test programs (`<TargetFramework>net10.0</TargetFramework>`, `Program.cs` entry point, run via `dotnet test`). Build via `./run.sh build`; full test sweep via `./run.sh test`.

**Source review:** Architecture review session 2026-05-26 — findings summarised inline at the top of each phase.

**Verification commands (used throughout):**

```bash
# Smallest-relevant build (mod + dependent projects)
dotnet build BazaarPlusPlus.csproj -c Debug

# All test projects
./run.sh test

# A single test project (e.g. RunLoggingSqliteStore)
dotnet test tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj -c Debug
```

Per `.rules`, do not run `./run.sh all` (Debug + Release `BuildAll`) until end of each phase unless touching build logic or packaging.

---

## Phase Map

| Phase | Goal | Tasks | Risk | Behaviour change |
|---|---|---|---|---|
| 1 | Move misplaced persistence + cleanup namespaces | 1.1–1.7 | low | no |
| 2 | Split `Core/` into pure abstractions + `GameInterop/` | 2.1–2.4 | low | no |
| 3 | Invert `Settings` registration; break `CombatReplay ↔ PvpBattles` | 3.1–3.6 | medium | no |
| 4 | Migrate 12 features to `IBppMountable` | 4.1–4.7 | medium | no |

Each phase ends with `./run.sh all` (full Debug + Release `BuildAll`) so the installer-source bundle stays valid. Open a PR per phase; titles below.

---

# Phase 1 — Pure moves and namespace hygiene

**PR title:** `Move misplaced persistence into Storage project; tidy namespaces`

**Rationale:** Three `Game/*/Persistence/` subfolders import only `BazaarPlusPlus.Storage.*` (no Unity, no game DLLs). They are SQLite stores misfiled under `Game/`. Moving them into the existing `Storage` project mirrors the `Storage/RunLog/RunLogStore` pattern already in place. The `Infrastructure/` folder has 3 of 4 files declaring `namespace BazaarPlusPlus` (root), and `Patches/` declares 28 files in root vs. 3 in their proper sub-namespace. None of this changes behaviour.

**Visibility note:** All current `Persistence/*` classes are `internal sealed`. Crossing the assembly boundary into `BazaarPlusPlus.Storage.dll` requires them to be `public sealed`. This matches `Storage/RunLog/RunLogStore` which is already `public sealed`.

---

## Task 1.1: Move `PvpBattleSqliteStore` + `IPvpBattleCatalog` + `PvpBattleCatalog` into Storage

**Files:**
- Create: `Storage/PvpBattle/PvpBattleSqliteStore.cs` (relocated from `Game/PvpBattles/Persistence/PvpBattleSqliteStore.cs`)
- Create: `Storage/PvpBattle/IPvpBattleCatalog.cs` (relocated from `Game/PvpBattles/Persistence/IPvpBattleCatalog.cs`)
- Create: `Storage/PvpBattle/PvpBattleCatalog.cs` (relocated from `Game/PvpBattles/Persistence/PvpBattleCatalog.cs`)
- Delete: `Game/PvpBattles/Persistence/` (entire folder, becomes empty)
- Modify: `Game/CombatReplay/ReplayPersistenceOrchestrator.cs` (`using` update)
- Modify: `Game/CombatReplay/CombatReplayController.cs` (`using` update)
- Modify: `Game/RunLogging/RunLoggingController.cs` (`using` update)
- Modify: `Game/RunLogging/Upload/RunBundleUploadStore.cs` (`using` update)
- Modify: `tests/CombatReplayRecording.Tests/Program.cs` (`using` update)

- [ ] **Step 1: Establish baseline green tests**

```bash
dotnet test tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj -c Debug
```

Expected: PASS. If this fails, stop — fix unrelated breakage before refactoring.

- [ ] **Step 2: Move the three files into `Storage/PvpBattle/`**

```bash
mkdir -p Storage/PvpBattle
git mv Game/PvpBattles/Persistence/PvpBattleSqliteStore.cs Storage/PvpBattle/PvpBattleSqliteStore.cs
git mv Game/PvpBattles/Persistence/IPvpBattleCatalog.cs   Storage/PvpBattle/IPvpBattleCatalog.cs
git mv Game/PvpBattles/Persistence/PvpBattleCatalog.cs    Storage/PvpBattle/PvpBattleCatalog.cs
rmdir Game/PvpBattles/Persistence
```

- [ ] **Step 3: Adjust namespace + visibility in each moved file**

In `Storage/PvpBattle/PvpBattleSqliteStore.cs`:
- Change `namespace BazaarPlusPlus.Game.PvpBattles.Persistence;` → `namespace BazaarPlusPlus.Storage.PvpBattle;`
- Change `internal sealed class PvpBattleSqliteStore` → `public sealed class PvpBattleSqliteStore`

In `Storage/PvpBattle/IPvpBattleCatalog.cs`:
- Change `namespace BazaarPlusPlus.Game.PvpBattles.Persistence;` → `namespace BazaarPlusPlus.Storage.PvpBattle;`
- Change `internal interface IPvpBattleCatalog` → `public interface IPvpBattleCatalog`

In `Storage/PvpBattle/PvpBattleCatalog.cs`:
- Change `namespace BazaarPlusPlus.Game.PvpBattles.Persistence;` → `namespace BazaarPlusPlus.Storage.PvpBattle;`
- Change `internal sealed class PvpBattleCatalog` → `public sealed class PvpBattleCatalog`

- [ ] **Step 4: Update the 5 caller files' `using` statements**

In each of:
- `Game/CombatReplay/ReplayPersistenceOrchestrator.cs`
- `Game/CombatReplay/CombatReplayController.cs`
- `Game/RunLogging/RunLoggingController.cs`
- `Game/RunLogging/Upload/RunBundleUploadStore.cs`
- `tests/CombatReplayRecording.Tests/Program.cs`

Replace every occurrence of `using BazaarPlusPlus.Game.PvpBattles.Persistence;` with `using BazaarPlusPlus.Storage.PvpBattle;`.

- [ ] **Step 5: Verify build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: succeeds. If a caller is missed, the error will be `CS0246: The type or namespace 'PvpBattleCatalog' could not be found` — find it via `grep -rn PvpBattleCatalog Game/ Patches/ Plugin.cs BppComposition.cs` and add the new `using`.

- [ ] **Step 6: Run the test that depends on this**

```bash
dotnet test tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj -c Debug
```

Expected: PASS, same test count as Step 1.

- [ ] **Step 7: Commit**

```bash
git add Storage/PvpBattle Game Patches Plugin.cs BppComposition.cs tests/CombatReplayRecording.Tests
git commit -m "Move PvpBattle persistence into BazaarPlusPlus.Storage project"
```

---

## Task 1.2: Move `QueuedRunLogStore` + `ReplicatedRunLogStore` into Storage

**Files:**
- Create: `Storage/RunLog/Replication/QueuedRunLogStore.cs` (relocated from `Game/RunLogging/Persistence/QueuedRunLogStore.cs`)
- Create: `Storage/RunLog/Replication/ReplicatedRunLogStore.cs` (relocated from `Game/RunLogging/Persistence/ReplicatedRunLogStore.cs`)
- Delete: `Game/RunLogging/Persistence/` (entire folder)
- Modify: `Game/RunLogging/RunLoggingController.cs` (`using` update)
- Modify: any test using these (verify with grep)

- [ ] **Step 1: Identify all callers**

```bash
grep -rln "\bQueuedRunLogStore\b\|\bReplicatedRunLogStore\b" Game Patches Plugin.cs BppComposition.cs tests
```

Expected output: at minimum `Game/RunLogging/RunLoggingController.cs` (and the source files themselves). If `tests/` shows hits, include those in Step 5.

- [ ] **Step 2: Establish baseline**

```bash
dotnet test tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj -c Debug
dotnet test tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj -c Debug
```

Expected: PASS both.

- [ ] **Step 3: Move and rename**

```bash
mkdir -p Storage/RunLog/Replication
git mv Game/RunLogging/Persistence/QueuedRunLogStore.cs     Storage/RunLog/Replication/QueuedRunLogStore.cs
git mv Game/RunLogging/Persistence/ReplicatedRunLogStore.cs Storage/RunLog/Replication/ReplicatedRunLogStore.cs
rmdir Game/RunLogging/Persistence
```

- [ ] **Step 4: Adjust namespace + visibility**

In both moved files:
- Change `namespace BazaarPlusPlus.Game.RunLogging.Persistence;` → `namespace BazaarPlusPlus.Storage.RunLog.Replication;`
- Change `internal sealed class` → `public sealed class`

- [ ] **Step 5: Update callers**

In `Game/RunLogging/RunLoggingController.cs`, replace `using BazaarPlusPlus.Game.RunLogging.Persistence;` with `using BazaarPlusPlus.Storage.RunLog.Replication;`.

For any test hits from Step 1, apply the same replacement.

- [ ] **Step 6: Build + test**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet test tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj -c Debug
dotnet test tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj -c Debug
```

Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add Storage/RunLog/Replication Game/RunLogging tests
git commit -m "Move QueuedRunLogStore + ReplicatedRunLogStore into BazaarPlusPlus.Storage project"
```

---

## Task 1.3: Move `RunScreenshotSqliteStore` into Storage

**Files:**
- Create: `Storage/RunScreenshot/RunScreenshotSqliteStore.cs` (relocated from `Game/Screenshots/Persistence/RunScreenshotSqliteStore.cs`)
- Delete: `Game/Screenshots/Persistence/` (entire folder)
- Modify: `Game/Screenshots/EndOfRunScreenshotController.cs` (`using` update)
- Modify: `tests/RunScreenshotSqliteStore.Tests/Program.cs` (`using` update)

- [ ] **Step 1: Baseline**

```bash
dotnet test tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj -c Debug
```

Expected: PASS.

- [ ] **Step 2: Move**

```bash
mkdir -p Storage/RunScreenshot
git mv Game/Screenshots/Persistence/RunScreenshotSqliteStore.cs Storage/RunScreenshot/RunScreenshotSqliteStore.cs
rmdir Game/Screenshots/Persistence
```

- [ ] **Step 3: Adjust namespace + visibility**

In `Storage/RunScreenshot/RunScreenshotSqliteStore.cs`:
- Change `namespace BazaarPlusPlus.Game.Screenshots.Persistence;` → `namespace BazaarPlusPlus.Storage.RunScreenshot;`
- Change `internal sealed class RunScreenshotSqliteStore` → `public sealed class RunScreenshotSqliteStore`

- [ ] **Step 4: Update callers**

In `Game/Screenshots/EndOfRunScreenshotController.cs` and `tests/RunScreenshotSqliteStore.Tests/Program.cs`, replace `using BazaarPlusPlus.Game.Screenshots.Persistence;` with `using BazaarPlusPlus.Storage.RunScreenshot;`.

- [ ] **Step 5: Build + test**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet test tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj -c Debug
```

Expected: green.

- [ ] **Step 6: Commit**

```bash
git add Storage/RunScreenshot Game/Screenshots tests/RunScreenshotSqliteStore.Tests
git commit -m "Move RunScreenshotSqliteStore into BazaarPlusPlus.Storage project"
```

---

## Task 1.4: Unify `Patches/` namespaces

**Files:**
- Modify: every `.cs` file under `Patches/` that declares `namespace BazaarPlusPlus;` (28 files per current count).

**Rationale:** Current state mixes 28 root-namespace declarations with 3 sub-namespace ones. The target is `namespace BazaarPlusPlus.Patches.<AreaFolder>;` for files under a subfolder, and `namespace BazaarPlusPlus.Patches;` for `BppPatchHost.cs`.

- [ ] **Step 1: Enumerate current state**

```bash
grep -rln "^namespace BazaarPlusPlus;" Patches/
```

Expected: ~28 file paths printed. Each one is under some `Patches/<Area>/` folder.

- [ ] **Step 2: Rewrite each file's namespace**

For each file `Patches/<Area>/<File>.cs`, change `namespace BazaarPlusPlus;` to `namespace BazaarPlusPlus.Patches.<Area>;`. Example: `Patches/Combat/CombatSimulationPatches.cs` → `namespace BazaarPlusPlus.Patches.Combat;`.

This is a 1-to-1 replacement; no callers reference patches by type name from outside Patches/ (patches are discovered by Harmony at runtime via `[HarmonyPatch]`), so there is no `using` impact in non-Patches code.

- [ ] **Step 3: Verify build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: succeeds. If a patch file references another patch file's helper class without a `using`, add one. Harmony attribute reflection does not care about namespace.

- [ ] **Step 4: Run patch-relevant test projects**

```bash
dotnet test tests/RandomHeroPoolPatchCompatibility.Tests/RandomHeroPoolPatchCompatibility.Tests.csproj -c Debug
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Patches
git commit -m "Unify Patches/ namespaces under BazaarPlusPlus.Patches.<Area>"
```

---

## Task 1.5: Move `BppHttpClientFactory` into the ModApi project

**Files:**
- Create: `ModApi/Http/BppHttpClientFactory.cs` (relocated from `Infrastructure/BppHttpClientFactory.cs`)
- Delete: `Infrastructure/BppHttpClientFactory.cs`
- Modify: `Plugin.cs` (`using` + call-site change)

**Rationale:** `BppHttpClientFactory` builds an `HttpClient` whose only consumer is `ModOnlineClient` (in ModApi). It belongs in the ModApi project. Moving makes ModApi self-contained: the only thing Plugin.cs needs to know is "construct an HttpClient via ModApi's factory, hand it to ModOnlineClient." `BppPluginVersion` stays in main mod (it reads the BepInEx-emitted `.version` file at runtime, which is a Plugin-host concern); the factory takes a version string parameter instead of statically referencing `BppPluginVersion`.

- [ ] **Step 1: Refactor `BppHttpClientFactory` to take version as a parameter**

Current signature:
```csharp
public static HttpClient Create(string? userAgentSuffix = null, TimeSpan? timeout = null)
```

New signature (still `static`, accepts the version string explicitly so ModApi has no dependency on `BppPluginVersion`):
```csharp
public static HttpClient Create(
    string productVersion,
    string? userAgentSuffix = null,
    TimeSpan? timeout = null
)
```

In `Plugin.cs` `BuildOnlineServices`, change:
```csharp
var httpClient = BppHttpClientFactory.Create(
    userAgentSuffix: "OnlineClient",
    timeout: TimeSpan.FromSeconds(Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds))
);
```
to:
```csharp
var httpClient = BppHttpClientFactory.Create(
    productVersion: BppPluginVersion.Current,
    userAgentSuffix: "OnlineClient",
    timeout: TimeSpan.FromSeconds(Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds))
);
```

In the new `BppHttpClientFactory`, the body that currently reads `BppPluginVersion.Current` reads the `productVersion` parameter instead. The `SanitizeUserAgentToken` / `IsTokenChar` helpers are unchanged.

- [ ] **Step 2: Move the file and adjust namespace + visibility**

```bash
mkdir -p ModApi/Http
git mv Infrastructure/BppHttpClientFactory.cs ModApi/Http/BppHttpClientFactory.cs
```

In `ModApi/Http/BppHttpClientFactory.cs`:
- Change `namespace BazaarPlusPlus;` → `namespace BazaarPlusPlus.ModApi.Http;`
- Change `internal static class BppHttpClientFactory` → `public static class BppHttpClientFactory`
- Remove the `using` for any `BppPluginVersion` reference (no longer used).

- [ ] **Step 3: Update Plugin.cs `using`**

In `Plugin.cs` (top of file): add `using BazaarPlusPlus.ModApi.Http;`. The existing `using BazaarPlusPlus.ModApi;` does not cover the new sub-namespace.

- [ ] **Step 4: Build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add ModApi/Http Plugin.cs Infrastructure
git commit -m "Move BppHttpClientFactory into BazaarPlusPlus.ModApi.Http"
```

---

## Task 1.6: Fix the `Infrastructure/` namespace mismatch on remaining files

**Files:**
- Modify: `Infrastructure/BppLog.cs` (namespace)
- Modify: `Infrastructure/BppPluginVersion.cs` (namespace)
- Modify: every file in the mod that uses `BppLog` or `BppPluginVersion` without a `using` (`grep`-driven)

**Rationale:** After Task 1.5, only `BppLog`, `BppPluginVersion`, and `LogRepeatSuppressor` remain in `Infrastructure/`. `LogRepeatSuppressor` already declares `namespace BazaarPlusPlus.Infrastructure;`. The other two declare root `namespace BazaarPlusPlus;`. Bring them in line.

- [ ] **Step 1: Update namespaces**

In `Infrastructure/BppLog.cs`: change `namespace BazaarPlusPlus;` → `namespace BazaarPlusPlus.Infrastructure;`. Keep the class `internal static class BppLog` (it is used from all over the mod assembly; visibility remains internal).

In `Infrastructure/BppPluginVersion.cs`: change `namespace BazaarPlusPlus;` → `namespace BazaarPlusPlus.Infrastructure;`. Visibility stays `internal static`.

- [ ] **Step 2: Add `using BazaarPlusPlus.Infrastructure;` to every file that currently uses `BppLog` or `BppPluginVersion` without a `using`**

Find them:
```bash
grep -rln "\bBppLog\.\|\bBppPluginVersion\." Plugin.cs BppComposition.cs Core Game Patches Infrastructure
```

For each file whose `using` block doesn't already have `BazaarPlusPlus.Infrastructure`, add it. Files inside `Infrastructure/` itself don't need the `using`.

- [ ] **Step 3: Build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: succeeds. If any file errors with `CS0103: The name 'BppLog' does not exist in the current context`, add the missing `using`.

- [ ] **Step 4: Commit**

```bash
git add Infrastructure Plugin.cs BppComposition.cs Core Game Patches
git commit -m "Align Infrastructure namespaces with folder"
```

---

## Task 1.7: Phase 1 end-of-phase verification

- [ ] **Step 1: Full Debug + Release build (`BuildAll`)**

```bash
./run.sh all
```

Expected: both Debug and Release succeed; Debug copies DLLs to BepInEx/plugins and Release copies to the installer source tree (per `BazaarPlusPlus.csproj` targets).

- [ ] **Step 2: Full test sweep**

```bash
./run.sh test
```

Expected: every test project exits 0.

- [ ] **Step 3: Quick smoke (manual, optional)**

Launch the game; confirm `[BPP][Plugin] Plugin initialization completed` appears in BepInEx log; one combat round to verify replay capture still works (touches PvpBattles + CombatReplay + Storage paths just moved).

- [ ] **Step 4: Open PR**

PR title: `Move misplaced persistence into Storage project; tidy namespaces`. Body should list the 6 sub-tasks 1.1–1.6 as bullets and include the `Release Notes: - N/A` block per `.rules`.

---

# Phase 2 — Split `Core/` into pure abstractions vs. `GameInterop/`

**PR title:** `Split Core into pure abstractions and GameInterop`

**Rationale:** `Core/` mixes two blast radii — pure abstractions with no game-DLL references (Config, Paths, Events/IBppEventBus, Runtime/IBpp* interfaces, RunContext/RunExitKind enum, GameState interfaces) and game-DLL-coupled reflection bridges (`BppClientCacheBridge` uses TempoNet + Harmony AccessTools, `BppStaticDataAccess` uses TheBazaar, `GameStateProbe` uses TheBazaar, `RunContextStore` uses BazaarGameShared.Domain, two `Events/*Observed` messages carry `INetMessage` payloads). The mix makes it impossible to glance at Core/ and know what breaks when the game updates.

After this phase, `Core/` is the pure-abstraction layer; `GameInterop/` holds every file that imports a game DLL or HarmonyLib. No public type names change; no behaviour changes.

---

## Task 2.1: Create `GameInterop/` and move the four game-DLL-coupled non-event files

**Files:**
- Create: `GameInterop/BppClientCacheBridge.cs` (from `Core/Runtime/BppClientCacheBridge.cs`)
- Create: `GameInterop/BppStaticDataAccess.cs` (from `Core/Runtime/BppStaticDataAccess.cs`)
- Create: `GameInterop/GameStateProbe.cs` (from `Core/GameState/GameStateProbe.cs`)
- Create: `GameInterop/RunContextStore.cs` (from `Core/RunContext/RunContextStore.cs`)
- Delete: the four originals listed above
- Modify: every file that imports these types (`grep`-driven)

- [ ] **Step 1: Map current consumers**

```bash
grep -rln "\bBppClientCacheBridge\b\|\bBppStaticDataAccess\b\|\bGameStateProbe\b\|\bRunContextStore\b" Plugin.cs BppComposition.cs Core Game Patches Infrastructure tests
```

Record the file list — these are the files whose `using` statements you will touch in Step 4. The interface types (`IGameStateProbe`, `IRunContext`) stay in `Core/`, so callers that only reference the interfaces don't need updates.

- [ ] **Step 2: Move the four files**

```bash
mkdir -p GameInterop
git mv Core/Runtime/BppClientCacheBridge.cs   GameInterop/BppClientCacheBridge.cs
git mv Core/Runtime/BppStaticDataAccess.cs    GameInterop/BppStaticDataAccess.cs
git mv Core/GameState/GameStateProbe.cs       GameInterop/GameStateProbe.cs
git mv Core/RunContext/RunContextStore.cs     GameInterop/RunContextStore.cs
```

- [ ] **Step 3: Adjust namespace in each moved file**

In all four files, change the existing `namespace BazaarPlusPlus.Core.<Sub>;` to `namespace BazaarPlusPlus.GameInterop;`. Visibility (`internal`) is unchanged.

- [ ] **Step 4: Update `using` statements in callers**

For each file from Step 1, add `using BazaarPlusPlus.GameInterop;` if not already present. Remove the old `using BazaarPlusPlus.Core.Runtime;` / `using BazaarPlusPlus.Core.GameState;` / `using BazaarPlusPlus.Core.RunContext;` only if no other Core type from that namespace is referenced. The grep from Step 1 plus `dotnet build` errors will guide you.

In particular: `BppComposition.cs` instantiates `RunContextStore` and `GameStateProbe`; it needs `using BazaarPlusPlus.GameInterop;`.

- [ ] **Step 5: Build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: succeeds.

- [ ] **Step 6: Run a representative test**

```bash
dotnet test tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj -c Debug
```

Expected: PASS. This test exercises code that depends on `BppClientCacheBridge` indirectly.

- [ ] **Step 7: Commit**

```bash
git add GameInterop Core Plugin.cs BppComposition.cs Game Patches tests
git commit -m "Move game-DLL-coupled bridges from Core/ to GameInterop/"
```

---

## Task 2.2: Move the two game-typed events into `GameInterop/Events/`

**Files:**
- Create: `GameInterop/Events/NetMessageObserved.cs` (from `Core/Events/NetMessageObserved.cs`)
- Create: `GameInterop/Events/CombatSimObserved.cs` (from `Core/Events/CombatSimObserved.cs`)
- Delete: the two originals
- Modify: any subscriber/publisher (`Patches/Combat/CombatSimulationPatches.cs`, `Patches/Combat/CombatReplayCapturePatch.cs`, and consumers under `Game/`)

**Rationale:** `NetMessageObserved.Message` is `INetMessage` (BazaarGameShared.Infra.Messages); `CombatSimObserved.Message` is `NetMessageCombatSim`. Both leak game DLL types into `Core/Events`, which otherwise is pure. The remaining 5 event types in `Core/Events` (`CombatFrameAdvanced`, `CombatReplayPersistenceDrained`, `RunInitializedObserved`, `RunLifecycleChanged`, etc.) use primitive types only.

- [ ] **Step 1: Map subscribers**

```bash
grep -rln "\bNetMessageObserved\b\|\bCombatSimObserved\b" Plugin.cs BppComposition.cs Core Game Patches GameInterop
```

- [ ] **Step 2: Move and rename namespace**

```bash
mkdir -p GameInterop/Events
git mv Core/Events/NetMessageObserved.cs GameInterop/Events/NetMessageObserved.cs
git mv Core/Events/CombatSimObserved.cs  GameInterop/Events/CombatSimObserved.cs
```

In both moved files, change `namespace BazaarPlusPlus.Core.Events;` → `namespace BazaarPlusPlus.GameInterop.Events;`.

- [ ] **Step 3: Update subscribers**

For each file in Step 1, add `using BazaarPlusPlus.GameInterop.Events;` and remove the now-unused old `using BazaarPlusPlus.Core.Events;` only if no other Core event type is still referenced.

- [ ] **Step 4: Build + smoke test**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet test tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj -c Debug
```

Expected: both pass.

- [ ] **Step 5: Commit**

```bash
git add GameInterop/Events Core/Events Plugin.cs BppComposition.cs Game Patches
git commit -m "Move game-typed events from Core/ to GameInterop/Events"
```

---

## Task 2.3: Optional pure-abstractions extraction (`BazaarPlusPlus.Abstractions.csproj`)

**Decision gate:** Skip this task unless at least one of the following is true:
- Phase 3 introduces shared types between `Settings` registration protocol and ModApi/Storage.
- A future spec calls for a new pure-utility netstandard project that needs `IBppEventBus`.

If neither holds, defer — the `Core/` folder inside the main assembly is fine. Document the skip in the PR description with the line "Task 2.3 skipped — no consumer outside the main assembly yet."

- [ ] **Step 1: Decide skip vs. proceed**

State the decision explicitly. If skipping, mark this task complete and move to Task 2.4. If proceeding, this becomes its own follow-up PR — write a separate plan for it; do not inline here.

---

## Task 2.4: Phase 2 end-of-phase verification

- [ ] **Step 1: Full build**

```bash
./run.sh all
```

Expected: green Debug + Release.

- [ ] **Step 2: Full tests**

```bash
./run.sh test
```

Expected: green.

- [ ] **Step 3: Audit Core/ for residual game-DLL imports**

```bash
grep -rE "^using (TheBazaar|BazaarGameShared|BazaarGameClient|BazaarBattleService|HarmonyLib)" Core/
```

Expected: empty output. If any file still imports a game DLL, decide whether it moves to `GameInterop/` (most likely) or stays in `Core/` with justification in the file's doc comment.

- [ ] **Step 4: Open PR**

Title: `Split Core into pure abstractions and GameInterop`. Body lists 2.1–2.3 with the Step-3 audit output proving the split is clean.

---

# Phase 3 — Decouple Settings registration; resolve `CombatReplay ↔ PvpBattles`

**PR title:** `Invert Settings registration; one-way CombatReplay→PvpBattles`

**Rationale (Settings):** `Game/Settings/BppSettingsDockCatalog.cs` hardcodes `using` statements for 6 feature modules (CombatStatusBar, ItemEnchantPreview, LegendaryPosition, NameOverride, Screenshots.Upload, UpgradePreview). Those modules also depend back on Settings (for the toggle UI binding). Seven bidirectional cycles. The target pattern is the **inverse**: each feature defines and registers its own `ISettingsDockEntry`; the catalog becomes a registry that knows nothing about specific features.

**Rationale (CombatReplay ↔ PvpBattles):** 14 imports of PvpBattles from CombatReplay; 4 imports of CombatReplay from PvpBattles. The 4 reverse imports come from `PvpBattleCardSetCapture.cs`, `PvpBattleSequenceMatcher.cs`, and `PvpBattleSnapshotCollector.cs` — they reach into CombatReplay's payload/persistence types. The fix: move those three files into `Game/CombatReplay/Capture/` so PvpBattles holds only the wire/manifest DTOs and CombatReplay holds the active capture-and-orchestration code.

---

## Task 3.1: Add `ISettingsDockEntry` protocol + `SettingsDockEntryRegistry`

**Files:**
- Create: `Game/Settings/ISettingsDockEntry.cs`
- Create: `Game/Settings/SettingsDockEntryRegistry.cs`
- Create: `tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj`
- Create: `tests/SettingsDockRegistry.Tests/Program.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj` mirroring an existing test csproj (use `tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj` as the literal template — same `<TargetFramework>net10.0</TargetFramework>`, same `<ProjectReference Include="../../BazaarPlusPlus.csproj" />`).

Create `tests/SettingsDockRegistry.Tests/Program.cs`:

```csharp
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class SettingsDockRegistryTests
{
    private sealed class FakeEntry : ISettingsDockEntry
    {
        public string Id { get; }
        public int CallCount { get; private set; }
        public FakeEntry(string id) { Id = id; }
        public BppSettingsDockDefinition Build(IBppConfig config)
        {
            CallCount++;
            return new BppSettingsDockDefinition(Id, $"Label-{Id}", isEnabled: () => true);
        }
    }

    [Fact]
    public void Materialize_returns_one_definition_per_registered_entry()
    {
        var registry = new SettingsDockEntryRegistry();
        var a = new FakeEntry("A");
        var b = new FakeEntry("B");
        registry.Register(a);
        registry.Register(b);

        var defs = registry.MaterializeAll(config: null!);

        Assert.Equal(2, defs.Count);
        Assert.Contains(defs, d => d.Id == "A");
        Assert.Contains(defs, d => d.Id == "B");
        Assert.Equal(1, a.CallCount);
        Assert.Equal(1, b.CallCount);
    }

    [Fact]
    public void Registering_same_id_twice_throws()
    {
        var registry = new SettingsDockEntryRegistry();
        registry.Register(new FakeEntry("A"));
        Assert.Throws<System.InvalidOperationException>(
            () => registry.Register(new FakeEntry("A"))
        );
    }

    public static int Main() => 0;
}
```

The exact `BppSettingsDockDefinition` constructor signature must match what already exists. Before writing the test, read `Game/Settings/BppSettingsDockDefinition.cs` to confirm the constructor arguments. Adjust the test's `new BppSettingsDockDefinition(...)` line to match. If the existing definition's constructor differs from `(id, label, isEnabled)`, use the actual signature.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj -c Debug
```

Expected: FAIL with `CS0246: The type or namespace name 'ISettingsDockEntry' could not be found`.

- [ ] **Step 3: Create the interface**

`Game/Settings/ISettingsDockEntry.cs`:

```csharp
#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Settings;

/// <summary>
/// A feature module's contribution to the BPP settings dock. Each feature owns its
/// own implementation; the registry collects them so SettingsDockCatalog does not need
/// to import individual feature namespaces.
/// </summary>
internal interface ISettingsDockEntry
{
    /// <summary>Stable identifier used for ordering and duplicate detection.</summary>
    string Id { get; }

    BppSettingsDockDefinition Build(IBppConfig config);
}
```

- [ ] **Step 4: Create the registry**

`Game/Settings/SettingsDockEntryRegistry.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class SettingsDockEntryRegistry
{
    private readonly List<ISettingsDockEntry> _entries = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public void Register(ISettingsDockEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));
        if (!_ids.Add(entry.Id))
            throw new InvalidOperationException(
                $"SettingsDockEntry with Id '{entry.Id}' is already registered."
            );
        _entries.Add(entry);
    }

    public IReadOnlyList<BppSettingsDockDefinition> MaterializeAll(IBppConfig config)
    {
        var result = new List<BppSettingsDockDefinition>(_entries.Count);
        foreach (var entry in _entries)
            result.Add(entry.Build(config));
        return result;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj -c Debug
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Game/Settings/ISettingsDockEntry.cs Game/Settings/SettingsDockEntryRegistry.cs tests/SettingsDockRegistry.Tests
git commit -m "Add ISettingsDockEntry protocol and SettingsDockEntryRegistry"
```

---

## Task 3.2: Migrate one feature off the catalog hard-import (`CombatStatusBar`)

**Goal:** Use one feature as a template. Pick `CombatStatusBar` because it is small (7 files, 1129 lines) and has a clear settings surface.

**Files:**
- Create: `Game/CombatStatusBar/CombatStatusBarSettingsDockEntry.cs`
- Modify: `Game/Settings/BppSettingsDockCatalog.cs` — remove the hardcoded `CombatStatusBar` reference; instead read from the registry
- Modify: `BppComposition.cs` — instantiate `SettingsDockEntryRegistry`, register `CombatStatusBarSettingsDockEntry`, expose registry through a `SettingsDockRegistry` accessor (mirrors the existing `RunLifecycle` / `Mountables` accessors)
- Modify: `Plugin.cs` — pass the registry to `BppSettingsDockCatalog.Install` via the new signature

- [ ] **Step 1: Read the current `CombatStatusBar` block in the catalog**

```bash
grep -n "CombatStatusBar" Game/Settings/BppSettingsDockCatalog.cs
```

Note the exact `BppSettingsDockDefinition` construction code so it can be copied verbatim into the new entry.

- [ ] **Step 2: Extract the entry into a new file**

Create `Game/CombatStatusBar/CombatStatusBarSettingsDockEntry.cs`:

```csharp
#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed class CombatStatusBarSettingsDockEntry : ISettingsDockEntry
{
    public string Id => "CombatStatusBar"; // ← preserve the exact Id the catalog uses today

    public BppSettingsDockDefinition Build(IBppConfig config)
    {
        // Paste the exact construction body from BppSettingsDockCatalog here.
        // Replace `config` references appropriately; do not reach into other modules.
        return new BppSettingsDockDefinition(
            id: Id,
            // ... rest of args identical to what BppSettingsDockCatalog currently passes ...
        );
    }
}
```

The `id` MUST match exactly what `BppSettingsDockCatalog` uses today — config-file keys are derived from it. Verify by reading the catalog line found in Step 1.

- [ ] **Step 3: Wire registration into composition**

In `BppComposition.cs`:

Add a private field next to the other registries:
```csharp
private readonly SettingsDockEntryRegistry _settingsDockRegistry = new();
```

Add a public accessor next to `Mountables`:
```csharp
public SettingsDockEntryRegistry SettingsDockRegistry => _settingsDockRegistry;
```

In the constructor (after `_config.Initialize(configFile)`), register the entry:
```csharp
_settingsDockRegistry.Register(new CombatStatusBarSettingsDockEntry());
```

Add `using BazaarPlusPlus.Game.CombatStatusBar;` and `using BazaarPlusPlus.Game.Settings;` if not already present.

- [ ] **Step 4: Change `BppSettingsDockCatalog.Install` signature**

Current call site in `Plugin.cs`:
```csharp
BppSettingsDockCatalog.Install(services.Config);
```

New:
```csharp
BppSettingsDockCatalog.Install(services.Config, _composition!.SettingsDockRegistry);
```

In `Game/Settings/BppSettingsDockCatalog.cs`, change `Install`:
```csharp
public static void Install(IBppConfig config, SettingsDockEntryRegistry registry)
```

In the body, after building any still-hardcoded definitions, append the registry-materialised ones:
```csharp
foreach (var def in registry.MaterializeAll(config))
    // ... add to whatever collection BppSettingsDockCatalog currently builds ...
```

Remove the `using BazaarPlusPlus.Game.CombatStatusBar;` line and the in-place `CombatStatusBar` block from the catalog.

- [ ] **Step 5: Build + run CombatStatusBar test**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj -c Debug
```

Expected: green.

- [ ] **Step 6: Commit**

```bash
git add Game/CombatStatusBar/CombatStatusBarSettingsDockEntry.cs Game/Settings BppComposition.cs Plugin.cs
git commit -m "Migrate CombatStatusBar settings to ISettingsDockEntry"
```

---

## Task 3.3: Migrate the remaining 5 catalog-imported features

**Goal:** Apply the Task 3.2 template to the 5 remaining hardcoded features.

| Feature | New entry file | Test project |
|---|---|---|
| `ItemEnchantPreview` | `Game/ItemEnchantPreview/ItemEnchantPreviewSettingsDockEntry.cs` | `tests/ItemEnchantPreview.Tests/` |
| `LegendaryPosition` | `Game/LegendaryPosition/LegendaryPositionSettingsDockEntry.cs` | (no dedicated test; smoke via full build) |
| `NameOverride` | `Game/NameOverride/NameOverrideSettingsDockEntry.cs` | (no dedicated test; smoke via full build) |
| `Screenshots.Upload` | `Game/Screenshots/Upload/BazaarDbScreenshotUploadSettingsDockEntry.cs` | `tests/BazaarDbScreenshotUploadStore.Tests/` |
| `UpgradePreview` | `Game/UpgradePreview/UpgradePreviewSettingsDockEntry.cs` | (no dedicated test; smoke via full build) |

For each row, do one commit's worth of work using the same six steps as Task 3.2:

- [ ] **Step 1: Read the feature's existing block** — `grep -n "<FeatureName>" Game/Settings/BppSettingsDockCatalog.cs`
- [ ] **Step 2: Create `<Feature>SettingsDockEntry.cs`** — copy the block verbatim into `Build(IBppConfig)`; preserve the `Id` exactly.
- [ ] **Step 3: Register it in `BppComposition.cs`** — `_settingsDockRegistry.Register(new <Feature>SettingsDockEntry());`
- [ ] **Step 4: Delete the feature's `using` + inline block from `BppSettingsDockCatalog.cs`**
- [ ] **Step 5: Build + run the feature's test** (or full build if no dedicated test)
- [ ] **Step 6: Commit** with message `Migrate <FeatureName> settings to ISettingsDockEntry`

**Acceptance criterion (after all 5):** `BppSettingsDockCatalog.cs` no longer contains any `using BazaarPlusPlus.Game.<feature>` line. Verify with:

```bash
grep "using BazaarPlusPlus.Game" Game/Settings/BppSettingsDockCatalog.cs
```

Expected: empty.

---

## Task 3.4: Remove `BppSettingsDockController`'s `Input` + `Screenshots` imports

**Files:**
- Modify: `Game/Settings/BppSettingsDockController.cs` (remove `using BazaarPlusPlus.Game.Input;` and `using BazaarPlusPlus.Game.Screenshots;`)
- Modify: `Game/Settings/BppSettingsDockController.Presentation.cs` (remove `using BazaarPlusPlus.Game.Input;`)
- Modify: `Game/Settings/BppSettingsDockDefinition.cs` — add optional delegate fields `Action? OnRowActivated { get; init; }` and `Func<string>? GetHotkeyHint { get; init; }`
- Modify: Each feature's `*SettingsDockEntry` that needs the action — wire the delegate in `Build`.

**Rationale:** These two remaining imports exist in the controller (not the catalog) because the controller invokes `BppHotkeyService` (Input) and screenshot-capture commands (Screenshots) directly. Invert by routing the call through a delegate on the definition; the feature's own entry constructs the delegate over its own service.

- [ ] **Step 1: Identify the exact call sites**

```bash
grep -n "BppHotkeyService\|ScreenshotService\|Screenshots\." Game/Settings/BppSettingsDockController*.cs
```

- [ ] **Step 2: Extend `BppSettingsDockDefinition`**

In `Game/Settings/BppSettingsDockDefinition.cs`, add the two optional fields. Default null. Existing call sites still compile (they use object-initializer syntax already).

- [ ] **Step 3: Replace each direct call in the controller with the delegate**

Example before:
```csharp
BppHotkeyService.GetHint(actionId)
```
Example after:
```csharp
definition.GetHotkeyHint?.Invoke() ?? string.Empty
```

- [ ] **Step 4: Populate the delegates in the affected `*SettingsDockEntry` files**

For the keybind-row feature(s), the entry's `Build` method now closes over `BppHotkeyService`:
```csharp
return new BppSettingsDockDefinition(...)
{
    GetHotkeyHint = () => BppHotkeyService.GetHint(BppHotkeyActionId.HoldEnchantPreview),
    // ...
};
```

For the screenshot row, `OnRowActivated` calls `ScreenshotService` from inside the entry.

- [ ] **Step 5: Remove the controller's `using` lines**

- [ ] **Step 6: Build + run relevant tests**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj -c Debug
```

- [ ] **Step 7: Verify the goal**

```bash
grep -rn "^using BazaarPlusPlus\.Game\." Game/Settings/
```

Expected: empty. The Settings module now depends on `BazaarPlusPlus.Core.*` only.

- [ ] **Step 8: Commit**

```bash
git add Game/Settings Game BppComposition.cs
git commit -m "Route Input + Screenshots access through SettingsDockDefinition delegates"
```

---

## Task 3.5: Break the `CombatReplay ↔ PvpBattles` cycle

**Files:**
- Create: `Game/CombatReplay/Capture/PvpBattleCardSetCapture.cs` (relocated)
- Create: `Game/CombatReplay/Capture/PvpBattleSequenceMatcher.cs` (relocated)
- Create: `Game/CombatReplay/Capture/PvpBattleSnapshotCollector.cs` (relocated)
- Delete: the three originals under `Game/PvpBattles/`
- Modify: consumer `using` statements (`grep`-driven)

**Rationale:** PvpBattles should be the "data model" module (DTO/manifest types); CombatReplay should depend on it one-way. Today three files in PvpBattles execute capture logic that reaches into CombatReplay's payload store — they are misclassified.

- [ ] **Step 1: List the four imports**

```bash
grep -n "using BazaarPlusPlus\.Game\.CombatReplay" Game/PvpBattles/
```

Expected output (per the prior review):
```
Game/PvpBattles/PvpBattleCardSetCapture.cs:3:using BazaarPlusPlus.Game.CombatReplay;
Game/PvpBattles/PvpBattleSequenceMatcher.cs:4:using BazaarPlusPlus.Game.CombatReplay;
Game/PvpBattles/PvpBattleSnapshotCollector.cs:11:using BazaarPlusPlus.Game.CombatReplay;
Game/PvpBattles/PvpBattleSnapshotCollector.cs:12:using BazaarPlusPlus.Game.RunLogging;
```

- [ ] **Step 2: Read each file's top 30 lines to confirm they are orchestration files**

```bash
head -30 Game/PvpBattles/PvpBattleCardSetCapture.cs
head -30 Game/PvpBattles/PvpBattleSequenceMatcher.cs
head -30 Game/PvpBattles/PvpBattleSnapshotCollector.cs
```

Confirmation criterion: the file's primary responsibility is to *capture* live game state into a PvpBattle DTO. If any file is genuinely a passive DTO that just happens to reference a CombatReplay type for a property, do NOT move it — instead, abstract the dependency in CombatReplay first. The review's hypothesis is that all three are orchestration files; only deviate if the file content clearly contradicts.

- [ ] **Step 3: Move the three files**

```bash
mkdir -p Game/CombatReplay/Capture
git mv Game/PvpBattles/PvpBattleCardSetCapture.cs       Game/CombatReplay/Capture/PvpBattleCardSetCapture.cs
git mv Game/PvpBattles/PvpBattleSequenceMatcher.cs      Game/CombatReplay/Capture/PvpBattleSequenceMatcher.cs
git mv Game/PvpBattles/PvpBattleSnapshotCollector.cs    Game/CombatReplay/Capture/PvpBattleSnapshotCollector.cs
```

- [ ] **Step 4: Update namespace in each moved file**

In each, change `namespace BazaarPlusPlus.Game.PvpBattles;` → `namespace BazaarPlusPlus.Game.CombatReplay.Capture;`. Do **not** rename the classes — callers reference them by type name.

- [ ] **Step 5: Update consumer `using` statements**

```bash
grep -rln "PvpBattleCardSetCapture\|PvpBattleSequenceMatcher\|PvpBattleSnapshotCollector" Game Patches BppComposition.cs Plugin.cs tests
```

For each file in the list, ensure it has `using BazaarPlusPlus.Game.CombatReplay.Capture;` and remove the now-unused `using BazaarPlusPlus.Game.PvpBattles;` only if no other PvpBattles type is still referenced.

- [ ] **Step 6: Verify the cycle is gone**

```bash
grep -rn "^using BazaarPlusPlus\.Game\.CombatReplay" Game/PvpBattles/
```

Expected: empty. `Game/PvpBattles/` now depends only on `Core.*` / `Storage.*` / BCL.

- [ ] **Step 7: Build + tests**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet test tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj -c Debug
dotnet test tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj -c Debug
```

Expected: green.

- [ ] **Step 8: Commit**

```bash
git add Game/CombatReplay/Capture Game/PvpBattles Game Patches BppComposition.cs Plugin.cs tests
git commit -m "Move PvpBattle capture orchestration into CombatReplay/Capture"
```

---

## Task 3.6: Phase 3 end-of-phase verification

- [ ] **Step 1: Full build**

```bash
./run.sh all
```

- [ ] **Step 2: Full tests**

```bash
./run.sh test
```

- [ ] **Step 3: Verify no Settings ↔ feature back-edge**

```bash
grep -rn "^using BazaarPlusPlus\.Game\." Game/Settings/
```

Expected: empty.

- [ ] **Step 4: Verify no PvpBattles → CombatReplay edge**

```bash
grep -rn "^using BazaarPlusPlus\.Game\.CombatReplay" Game/PvpBattles/
```

Expected: empty.

- [ ] **Step 5: In-game smoke**

Launch the game; open the BPP settings dock; toggle each affected feature's row once; confirm behaviour matches pre-refactor. (The Id strings preserved in 3.2/3.3 ensure config files persist across the refactor; verify by checking `BazaarPlusPlus.cfg` after toggling.)

- [ ] **Step 6: Open PR**

Title: `Invert Settings registration; one-way CombatReplay→PvpBattles`. Body includes the two grep results above as proof.

---

# Phase 4 — Complete the `IBppMountable` migration

**PR title:** `Migrate Plugin.cs MonoBehaviour features to IBppMountable`

**Rationale:** `BppMountableRegistry`, `IBppMountable`, and `BppComposition.Mountables` already exist (see `Core/Runtime/IBppMountable.cs`, `Core/Runtime/BppMountableRegistry.cs`, `BppComposition.cs:31,69`). The only registered mountable today is `AutoBazaarMount`, and that registration line is commented out. Meanwhile `Plugin.cs:133-172` (`AttachRuntimeComponents`) hand-attaches 12 MonoBehaviour features, and `Plugin.cs:236-252` (`DetachRuntimeComponents`) hand-detaches them. Adding a new feature requires editing three places. Adopting Mountables consistently makes feature addition a one-line registration in `BppComposition`.

**Inventory:** 12 features to migrate (from `Plugin.cs` `AttachRuntimeComponents` in current `master`):

| # | MonoBehaviour | Initialize signature | Mount file |
|---|---|---|---|
| 1 | `RunLoggingController` | `Initialize(IBppServices services)` | `Game/RunLogging/RunLoggingMount.cs` |
| 2 | `RunUploadController` | `Initialize(IBppServices services)` | `Game/RunLogging/Upload/RunUploadMount.cs` |
| 3 | `HistoryPanel` + `HistoryPanelFactory.Create(...)` | configured via `historyPanel.Configure(...)` | `Game/HistoryPanel/HistoryPanelMount.cs` |
| 4 | `CombatStatusBar` | `Initialize(IBppServices services)` | `Game/CombatStatusBar/CombatStatusBarMount.cs` |
| 5 | `MonsterPreviewWarmupController` | no Initialize | `Game/MonsterPreview/MonsterPreviewWarmupMount.cs` |
| 6 | `CardSetPreviewRuntime` | no Initialize | `Game/MonsterPreview/CardSetPreviewMount.cs` |
| 7 | `MonsterPreviewItemBoardRuntime` | `Initialize(IBppServices services)` | `Game/MonsterPreview/MonsterPreviewItemBoardMount.cs` |
| 8 | `EndOfRunScreenshotController` | `Initialize(IBppServices services)` | `Game/Screenshots/EndOfRunScreenshotMount.cs` |
| 9 | `BazaarDbScreenshotUploadController` | `Initialize(IBppServices services)` | `Game/Screenshots/Upload/BazaarDbScreenshotUploadMount.cs` |
| 10 | `TooltipModifierRefreshController` | `Initialize(IBppConfig config, IEncounterStateProbe encounterState)` | `Game/Tooltips/TooltipModifierRefreshMount.cs` |
| 11 | `CombatReplayVideoRecorder` | `Initialize(IBppServices services)` | `Game/CombatReplay/Video/CombatReplayVideoRecorderMount.cs` |
| 12 | `CombatReplayRuntime` | `Initialize(IBppServices services, RunLifecycleModule)` | **Keep imperative** — bootstrap-special; see Task 4.5 |

`HistoryPanel` (row 3) is the one feature whose `Initialize` is asymmetric — it needs `_onlineClient` constructed in `BuildOnlineServices`. Its mount accepts a `Func<ModOnlineClient?>` accessor. Detail in Task 4.3.

---

## Task 4.1: Template task — `RunLoggingMount`

**Files:**
- Create: `Game/RunLogging/RunLoggingMount.cs`
- Modify: `BppComposition.cs` (register `RunLoggingMount`)
- Modify: `Plugin.cs` (remove the two lines that add+initialize `RunLoggingController`; remove the corresponding `DestroyComponentIfPresent<RunLoggingController>()` line)

- [ ] **Step 1: Write the mount class**

`Game/RunLogging/RunLoggingMount.cs`:

```csharp
#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLoggingMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var controller = host.AddComponent<RunLoggingController>();
        controller.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        var controller = host.GetComponent<RunLoggingController>();
        if (controller != null)
            UnityEngine.Object.DestroyImmediate(controller);
    }
}
```

- [ ] **Step 2: Register in composition**

In `BppComposition.cs` constructor, add (alongside the existing commented-out `// _mountables.Register(new AutoBazaarMount());`):
```csharp
_mountables.Register(new RunLoggingMount());
```
Add `using BazaarPlusPlus.Game.RunLogging;` to the `using` block if not already present.

- [ ] **Step 3: Remove the hand-wired code from Plugin.cs**

In `Plugin.cs` `AttachRuntimeComponents`, delete:
```csharp
var runLogging = gameObject.AddComponent<RunLoggingController>();
runLogging.Initialize(services);
```

In `DetachRuntimeComponents`, delete:
```csharp
DestroyComponentIfPresent<RunLoggingController>();
```

- [ ] **Step 4: Build + test**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet test tests/RunLoggingModule.Tests/RunLoggingModule.Tests.csproj -c Debug
```

Expected: green.

- [ ] **Step 5: Commit**

```bash
git add Game/RunLogging/RunLoggingMount.cs BppComposition.cs Plugin.cs
git commit -m "Migrate RunLoggingController to IBppMountable"
```

---

## Task 4.2: Apply the template to features #2, #4–#9, #11

**Goal:** Repeat Task 4.1 for each row in the inventory whose `Initialize` is `Initialize(IBppServices services)` (rows 2, 4, 7, 8, 9, 11) or has no `Initialize` (rows 5, 6).

For each feature, perform the same five steps as Task 4.1, with two minor variations.

**Class shape — features with `Initialize(IBppServices services)`:**
```csharp
#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.<Area>;

internal sealed class <Feature>Mount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var component = host.AddComponent<<MonoBehaviour>>();
        component.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        var component = host.GetComponent<<MonoBehaviour>>();
        if (component != null)
            UnityEngine.Object.DestroyImmediate(component);
    }
}
```

**Class shape — features with no Initialize (rows 5, 6):**
```csharp
public void Mount(GameObject host, IBppServices services)
{
    host.AddComponent<<MonoBehaviour>>();
}
```

**Per-feature checklist (commit one feature at a time so reviews are clean):**

- [ ] Row 2: `RunUploadController` → `Game/RunLogging/Upload/RunUploadMount.cs`. Test: `tests/StartupUploadRunner.Tests/...`.
- [ ] Row 4: `CombatStatusBar` → `Game/CombatStatusBar/CombatStatusBarMount.cs`. Test: `tests/CombatStatusBarState.Tests/...`.
- [ ] Row 5: `MonsterPreviewWarmupController` → `Game/MonsterPreview/MonsterPreviewWarmupMount.cs`. Test: `tests/MonsterPreviewResilience.Tests/...`.
- [ ] Row 6: `CardSetPreviewRuntime` → `Game/MonsterPreview/CardSetPreviewMount.cs`. Test: `tests/CardSetBuildRecommendationTier.Tests/...`.
- [ ] Row 7: `MonsterPreviewItemBoardRuntime` → `Game/MonsterPreview/MonsterPreviewItemBoardMount.cs`. Test: `tests/MonsterPreviewResilience.Tests/...`.
- [ ] Row 8: `EndOfRunScreenshotController` → `Game/Screenshots/EndOfRunScreenshotMount.cs`. Test: `tests/EndOfRunScreenshotGate.Tests/...`.
- [ ] Row 9: `BazaarDbScreenshotUploadController` → `Game/Screenshots/Upload/BazaarDbScreenshotUploadMount.cs`. Test: `tests/BazaarDbScreenshotUploadService.Tests/...`.
- [ ] Row 11: `CombatReplayVideoRecorder` → `Game/CombatReplay/Video/CombatReplayVideoRecorderMount.cs`. Test: `tests/CombatReplayRecording.Tests/...`.

Each per-row block runs Task 4.1 Steps 1–5 with the row's values. Commit message: `Migrate <Feature> to IBppMountable`.

---

## Task 4.3: Migrate `HistoryPanel` (the asymmetric case)

**Files:**
- Create: `Game/HistoryPanel/HistoryPanelMount.cs`
- Modify: `BppComposition.cs` — accept the online-client dependency at registration time via a `Func<ModOnlineClient?>` indirection
- Modify: `CombatReplayModule.cs` — expose `Runtime` property if not already exposed
- Modify: `Plugin.cs` — remove `AddConfiguredHistoryPanel` entirely; add `composition.AttachOnlineClient(_onlineClient)` after `BuildOnlineServices()`

**Challenge:** `AddConfiguredHistoryPanel` in Plugin.cs needs `_onlineClient` (built in `BuildOnlineServices`) AND `combatReplayRuntime` (created earlier and attached via `composition.AttachCombatReplayRuntime`). The mount must therefore have these dependencies available at `Mount(...)` time, not at registration time (the online client is constructed after composition.Start()).

**Approach:** Pass deferred accessors (`Func<T?>`) into the mount's constructor.

- [ ] **Step 1: Add `AttachOnlineClient` + `OnlineClient` to `BppComposition`**

```csharp
private ModOnlineClient? _onlineClientRef;

public void AttachOnlineClient(ModOnlineClient? client) => _onlineClientRef = client;

public ModOnlineClient? OnlineClient => _onlineClientRef;
```

Add `using BazaarPlusPlus.ModApi.Clients;` to `BppComposition.cs` if not present.

- [ ] **Step 2: Expose `CombatReplayRuntime` from `CombatReplayModule`**

In `Game/CombatReplay/CombatReplayModule.cs` (or wherever `AttachRuntime` lives), ensure there is a public accessor:
```csharp
public CombatReplayRuntime? Runtime { get; private set; }
public void AttachRuntime(CombatReplayRuntime runtime) => Runtime = runtime;
```

If `AttachRuntime` already exists with different mechanics, leave it; only add the `Runtime` getter.

- [ ] **Step 3: Write `HistoryPanelMount`**

`Game/HistoryPanel/HistoryPanelMount.cs`:

```csharp
#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Clients;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelMount : IBppMountable
{
    private readonly Func<CombatReplayRuntime?> _combatReplayRuntime;
    private readonly Func<ModOnlineClient?> _onlineClient;

    public HistoryPanelMount(
        Func<CombatReplayRuntime?> combatReplayRuntime,
        Func<ModOnlineClient?> onlineClient
    )
    {
        _combatReplayRuntime = combatReplayRuntime;
        _onlineClient = onlineClient;
    }

    public void Mount(GameObject host, IBppServices services)
    {
        var combatReplayRuntime = _combatReplayRuntime();
        if (combatReplayRuntime == null)
        {
            BppLog.Warn("HistoryPanelMount", "CombatReplayRuntime unavailable; skipping.");
            return;
        }

        var panel = host.AddComponent<HistoryPanel>();
        var runtime = new HistoryPanelRuntime(
            services.RunContext,
            services.Paths.RunLogDatabasePath,
            services.Paths.CombatReplayDirectoryPath,
            () => combatReplayRuntime
        );

        var onlineClient = _onlineClient();
        if (onlineClient == null)
        {
            BppLog.Warn(
                "HistoryPanelMount",
                "Online client unavailable; HistoryPanel left unconfigured."
            );
            return;
        }

        panel.Configure(HistoryPanelFactory.Create(runtime, onlineClient));
    }

    public void Unmount(GameObject host)
    {
        var panel = host.GetComponent<HistoryPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
    }
}
```

- [ ] **Step 4: Register the mount in `BppComposition`**

In the constructor, after `_combatReplayModule = new CombatReplayModule(_eventBus);`:
```csharp
_mountables.Register(new HistoryPanelMount(
    combatReplayRuntime: () => _combatReplayModule.Runtime,
    onlineClient: () => _onlineClientRef
));
```

- [ ] **Step 5: Update `Plugin.cs`**

Delete the entire `AddConfiguredHistoryPanel` method.

In `Awake`, after `BuildOnlineServices()`, add:
```csharp
_composition.AttachOnlineClient(_onlineClient);
```

In `AttachRuntimeComponents`, delete the `AddConfiguredHistoryPanel(services, combatReplayRuntime);` call.

In `DetachRuntimeComponents`, delete `DestroyComponentIfPresent<HistoryPanel>();`.

- [ ] **Step 6: Build + run HistoryPanel test**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet test tests/HistoryPanelRepository.Tests/HistoryPanelRepository.Tests.csproj -c Debug
```

Expected: green.

- [ ] **Step 7: Commit**

```bash
git add Game/HistoryPanel/HistoryPanelMount.cs BppComposition.cs Plugin.cs Game/CombatReplay
git commit -m "Migrate HistoryPanel to IBppMountable with deferred online-client dependency"
```

---

## Task 4.4: Migrate `TooltipModifierRefreshController` (custom Initialize signature)

**Files:**
- Create: `Game/Tooltips/TooltipModifierRefreshMount.cs`
- Modify: `BppComposition.cs` (register)
- Modify: `Plugin.cs` (delete `AddConfiguredTooltipModifierRefreshController` and its call site, plus the corresponding `DestroyComponentIfPresent<TooltipModifierRefreshController>()`)

This feature's `Initialize` is `Initialize(IBppConfig config, IEncounterStateProbe encounterState)`. Adapt:

```csharp
#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.Tooltips;

internal sealed class TooltipModifierRefreshMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var controller = host.AddComponent<TooltipModifierRefreshController>();
        controller.Initialize(services.Config, services.EncounterState);
    }

    public void Unmount(GameObject host)
    {
        var controller = host.GetComponent<TooltipModifierRefreshController>();
        if (controller != null)
            UnityEngine.Object.DestroyImmediate(controller);
    }
}
```

- [ ] **Step 1: Create the mount file with the content above**

- [ ] **Step 2: Register in `BppComposition`**

```csharp
_mountables.Register(new TooltipModifierRefreshMount());
```

- [ ] **Step 3: Delete `AddConfiguredTooltipModifierRefreshController` + its call + the Destroy line in `Plugin.cs`**

- [ ] **Step 4: Build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

- [ ] **Step 5: Commit**

```bash
git add Game/Tooltips/TooltipModifierRefreshMount.cs BppComposition.cs Plugin.cs
git commit -m "Migrate TooltipModifierRefreshController to IBppMountable"
```

---

## Task 4.5: Document `CombatReplayRuntime` exception

`CombatReplayRuntime` is created in `Plugin.Awake` *before* `composition.Start()` because the rest of the composition needs it (`composition.AttachCombatReplayRuntime(combatReplayRuntime)`). It is not a typical mountable.

**Decision:** Leave it imperative. Document the exception in `Plugin.cs`.

- [ ] **Step 1: Add a one-line comment above the `gameObject.AddComponent<CombatReplayRuntime>()` line in `Awake`**

```csharp
// CombatReplayRuntime is constructed before composition.Start() because RunLifecycle
// and several features take a reference through CombatReplayModule. Not a mountable.
var combatReplayRuntime = gameObject.AddComponent<CombatReplayRuntime>();
```

- [ ] **Step 2: Commit**

```bash
git add Plugin.cs
git commit -m "Document why CombatReplayRuntime stays imperative in Plugin.cs"
```

---

## Task 4.6: Collapse `AttachRuntimeComponents` and `DetachRuntimeComponents`

**Files:**
- Modify: `Plugin.cs`

By the time Task 4.5 is complete, `AttachRuntimeComponents` should contain only the `_composition?.Mountables.MountAll(gameObject, services)` call (since every per-feature block has been removed). Similarly, `DetachRuntimeComponents` should contain only `_composition?.Mountables.UnmountAll(gameObject)` and the residual `DestroyComponentIfPresent<CombatReplayRuntime>()`.

Inline these into `Awake` / `OnDestroy` directly. Delete the two helper methods.

- [ ] **Step 1: Inline `AttachRuntimeComponents`'s remaining body into `Awake`**

Replace the call site:
```csharp
AttachRuntimeComponents(services, combatReplayRuntime);
```
with:
```csharp
_composition?.Mountables.MountAll(gameObject, services);
```

- [ ] **Step 2: Inline `DetachRuntimeComponents` into `OnDestroy` and `CleanupFailedInitialization`**

Replace both call sites:
```csharp
DetachRuntimeComponents();
```
with:
```csharp
_composition?.Mountables.UnmountAll(gameObject);
DestroyComponentIfPresent<CombatReplayRuntime>();
```

- [ ] **Step 3: Delete the two helper methods (`AttachRuntimeComponents` and `DetachRuntimeComponents`)**

- [ ] **Step 4: Build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

- [ ] **Step 5: Commit**

```bash
git add Plugin.cs
git commit -m "Collapse Plugin.cs attach/detach into Mountables.MountAll/UnmountAll"
```

---

## Task 4.7: Phase 4 end-of-phase verification

- [ ] **Step 1: Full Debug + Release**

```bash
./run.sh all
```

- [ ] **Step 2: Full tests**

```bash
./run.sh test
```

- [ ] **Step 3: Manual in-game smoke**

Per `.rules`, frontend-style features get a manual smoke. Launch game → start a run → fight one battle → check that HistoryPanel populates, CombatStatusBar updates, monster preview shows. Look for any new BepInEx warnings in console.

- [ ] **Step 4: Verify Plugin.cs is small**

```bash
wc -l Plugin.cs
```

Target: under 130 lines (was 261). If above 130, look for residual dead helpers and remove them.

- [ ] **Step 5: Open PR**

Title: `Migrate Plugin.cs MonoBehaviour features to IBppMountable`. Body lists all 12 features, calls out the documented `CombatReplayRuntime` exception, and includes a before/after `wc -l Plugin.cs`.

---

# Final acceptance — run after all four phase PRs merge

- [ ] `grep -rln "^using BazaarPlusPlus\.Game\." Game/Settings/` → empty
- [ ] `grep -rn "^using BazaarPlusPlus\.Game\.CombatReplay" Game/PvpBattles/` → empty
- [ ] `grep -rE "^using (TheBazaar|BazaarGameShared|BazaarGameClient|BazaarBattleService|HarmonyLib)" Core/` → empty
- [ ] `find Game -path '*/Persistence/*' -name '*.cs'` → empty
- [ ] `grep -rln "^namespace BazaarPlusPlus;" Patches/` → empty (or only `BppPatchHost.cs`, by design)
- [ ] `./run.sh all` green
- [ ] `./run.sh test` green
- [ ] `wc -l Plugin.cs` ≤ 130

Behaviour unchanged. The mod ships the same DLL contents; only the source structure improved.
