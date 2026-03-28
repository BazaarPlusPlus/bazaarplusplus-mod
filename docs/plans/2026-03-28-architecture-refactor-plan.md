# BazaarPlusPlus Architecture Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reduce architectural drift by shrinking global runtime access, unifying feature lifecycle management, and splitting oversized feature controllers into explicit modules.

**Architecture:** Keep the current `Core / Game / Patches` direction, but move from ambient static access toward explicit composition. Execute the refactor incrementally by feature so each stage preserves behavior, ships with focused tests, and leaves the repo in a cleaner state than before.

**Tech Stack:** C# 12, BepInEx, Harmony, Unity `MonoBehaviour`, focused architecture smoke tests under `tests/`

---

### Task 1: Lock in architectural guardrails before moving code

**Files:**
- Modify: `tests/ArchitectureModularization.Tests/Program.cs`
- Test: `tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`

**Step 1: Add smoke checks for the next migration targets**

Extend `tests/ArchitectureModularization.Tests/Program.cs` to assert:
- `Plugin.cs` does not grow new direct feature wiring beyond composition responsibilities.
- `Game/HistoryPanel/HistoryPanel.cs` does not instantiate repository, replay store, and formatter concerns indefinitely.
- `Game/EncounterTracker.cs` is not allowed to remain the long-term home for module lifecycle and selection projection.

**Step 2: Run the architecture smoke test**

Run: `dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`
Expected: PASS

**Step 3: Tighten the assertions only to behaviors you intend to preserve**

Avoid brittle string checks tied to temporary method names. Prefer checks that enforce boundaries:
- static global access count does not increase
- feature namespaces move in the expected direction
- `Plugin.cs` remains a thin bootstrapper

**Step 4: Re-run the architecture smoke test**

Run: `dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add tests/ArchitectureModularization.Tests/Program.cs
git commit -m "Add architecture guardrails for refactor"
```

### Task 2: Introduce an explicit runtime composition surface

**Files:**
- Modify: `Core/Runtime/BppRuntimeHost.cs`
- Modify: `Plugin.cs`
- Create: `Core/Runtime/BppRuntimeServices.cs`
- Test: `tests/ArchitectureModularization.Tests/Program.cs`

**Step 1: Add a runtime services object**

Create `Core/Runtime/BppRuntimeServices.cs` to hold the runtime-owned dependencies now exposed statically:
- `IBppEventBus`
- `IBppConfig`
- `IPathService`
- `IRunContext`
- `IGameStateProbe`
- `IMonsterCatalog`

Use a single immutable container so feature constructors can accept one argument where that improves ergonomics.

**Step 2: Make `BppRuntimeHost` own and expose services as an instance payload**

Update `Core/Runtime/BppRuntimeHost.cs` so:
- the host still constructs the concrete services
- the host exposes them through an instance `Services` property
- static members remain temporarily only as compatibility shims during migration

**Step 3: Change `Plugin.cs` to compose features from `runtimeHost.Services`**

Update `Plugin.cs` so new feature constructors receive dependencies from the host instance instead of calling static `BppRuntimeHost.*` where practical.

**Step 4: Keep compatibility shims narrow**

If some static access must remain for the first pass, mark those access points as transitional and limit new usages in touched files.

**Step 5: Verify**

Run: `dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`
Expected: PASS

**Step 6: Commit**

```bash
git add Core/Runtime/BppRuntimeHost.cs Core/Runtime/BppRuntimeServices.cs Plugin.cs tests/ArchitectureModularization.Tests/Program.cs
git commit -m "Introduce explicit runtime services composition"
```

### Task 3: Unify feature lifecycle registration

**Files:**
- Modify: `Plugin.cs`
- Modify: `Core/Runtime/BppRuntimeHost.cs`
- Create: `Core/Runtime/IBppFeature.cs`
- Create: `Core/Runtime/BppFeatureRegistry.cs`
- Modify: `Game/RunLifecycle/RunLifecycleModule.cs`
- Modify: `Game/CombatReplay/CombatReplayModule.cs`
- Modify: `Game/CombatStatusBar/CombatStatusBarModule.cs`
- Test: `tests/ArchitectureModularization.Tests/Program.cs`

**Step 1: Define a tiny feature lifecycle contract**

Create `Core/Runtime/IBppFeature.cs` with:
- `void Install()`
- `void Start()`
- `void Stop()`

Keep it minimal. Do not add feature discovery, reflection, or generalized DI.

**Step 2: Add a registry owned by the runtime host**

Create `Core/Runtime/BppFeatureRegistry.cs` to:
- register features in deterministic order
- start them in registration order
- stop them in reverse order

**Step 3: Adapt existing host-managed modules**

Wrap or adapt:
- `RunLifecycleModule`
- `CombatReplayModule`
- `CombatStatusBarModule`

so they can be managed consistently through the same feature contract.

**Step 4: Move plugin-managed startup toward registry-based startup**

Update `Plugin.cs` so composition happens in one place:
- construct the registry
- register runtime features
- register feature `MonoBehaviour` hosts only where Unity callbacks are required

The end state for this task is not "remove every `AddComponent`", but "make the bootstrap path unambiguous."

**Step 5: Verify**

Run: `dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`
Expected: PASS

**Step 6: Commit**

```bash
git add Core/Runtime/IBppFeature.cs Core/Runtime/BppFeatureRegistry.cs Core/Runtime/BppRuntimeHost.cs Plugin.cs Game/RunLifecycle/RunLifecycleModule.cs Game/CombatReplay/CombatReplayModule.cs Game/CombatStatusBar/CombatStatusBarModule.cs tests/ArchitectureModularization.Tests/Program.cs
git commit -m "Unify feature lifecycle registration"
```

### Task 4: Replace `EncounterTracker` static orchestration with a real feature module

**Files:**
- Modify: `Game/EncounterTracker.cs`
- Modify: `Game/EncounterTracking/EncounterTrackingModule.cs`
- Create: `Game/EncounterTracking/EncounterTrackingController.cs`
- Create: `Game/EncounterTracking/EncounterTrackingFeature.cs`
- Modify: `Plugin.cs`
- Test: `tests/EncounterTrackerState.Tests/EncounterTrackerState.Tests.csproj`
- Test: `tests/ArchitectureModularization.Tests/Program.cs`

**Step 1: Separate Unity/event hookup from selection-state logic**

Create `Game/EncounterTracking/EncounterTrackingController.cs` for game callback hookup:
- subscribe to `Events.CardDealtSimEvent`
- normalize incoming data
- delegate to the module/service layer

Keep `EncounterTrackingModule` focused on state transitions and query exposure.

**Step 2: Add a feature wrapper**

Create `Game/EncounterTracking/EncounterTrackingFeature.cs` that owns:
- the controller lifecycle
- the module lifecycle
- access to event bus and runtime services

**Step 3: Shrink `Game/EncounterTracker.cs` into a compatibility shell or remove it**

Preferred end state:
- remove it entirely if no external callers require it

Acceptable intermediate state:
- leave a thin compatibility shim that delegates to the new feature and contains no logic

**Step 4: Update plugin composition**

Register the new encounter tracking feature through the unified startup path.

**Step 5: Verify with focused tests**

Run: `dotnet test tests/EncounterTrackerState.Tests/EncounterTrackerState.Tests.csproj`
Expected: PASS

Run: `dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`
Expected: PASS

**Step 6: Commit**

```bash
git add Game/EncounterTracker.cs Game/EncounterTracking/EncounterTrackingModule.cs Game/EncounterTracking/EncounterTrackingController.cs Game/EncounterTracking/EncounterTrackingFeature.cs Plugin.cs tests/ArchitectureModularization.Tests/Program.cs
git commit -m "Refactor encounter tracking into feature module"
```

### Task 5: Split `HistoryPanel` into controller, services, and view helpers

**Files:**
- Modify: `Game/HistoryPanel/HistoryPanel.cs`
- Modify: `Game/HistoryPanel/HistoryPanel.Canvas.cs`
- Modify: `Game/HistoryPanel/HistoryPanelRepository.cs`
- Modify: `Game/HistoryPanel/HistoryPanelPreviewRenderer.cs`
- Create: `Game/HistoryPanel/HistoryPanelController.cs`
- Create: `Game/HistoryPanel/HistoryPanelDataService.cs`
- Create: `Game/HistoryPanel/HistoryPanelReplayService.cs`
- Create: `Game/HistoryPanel/HistoryPanelFormatter.cs`
- Test: `tests/HistoryPanelRepository.Tests/HistoryPanelRepository.Tests.csproj`
- Test: `tests/ArchitectureModularization.Tests/Program.cs`

**Step 1: Extract pure formatting first**

Move static formatting helpers from `HistoryPanel.cs` into `Game/HistoryPanel/HistoryPanelFormatter.cs`.

This is the safest first extraction and reduces noise before touching behavior.

**Step 2: Extract data access and deletion workflow**

Create `Game/HistoryPanel/HistoryPanelDataService.cs` to own:
- load recent runs
- load battles for a run
- delete run records

Keep repository-specific SQL in `HistoryPanelRepository.cs`; keep orchestration in the new data service.

**Step 3: Extract replay and payload cleanup workflow**

Create `Game/HistoryPanel/HistoryPanelReplayService.cs` to own:
- replay eligibility checks
- replay dispatch
- replay payload cleanup after delete

This removes combat replay knowledge from the view/controller.

**Step 4: Turn `HistoryPanel.cs` into a thin MonoBehaviour controller**

Leave `HistoryPanel.cs` responsible for:
- Unity lifecycle
- keybind handling
- selection state
- delegating to data/replay/preview services

The target is a controller that reads like UI state coordination, not feature business logic.

**Step 5: Verify with focused tests**

Run: `dotnet test tests/HistoryPanelRepository.Tests/HistoryPanelRepository.Tests.csproj`
Expected: PASS

Run: `dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`
Expected: PASS

**Step 6: Commit**

```bash
git add Game/HistoryPanel/HistoryPanel.cs Game/HistoryPanel/HistoryPanel.Canvas.cs Game/HistoryPanel/HistoryPanelRepository.cs Game/HistoryPanel/HistoryPanelPreviewRenderer.cs Game/HistoryPanel/HistoryPanelController.cs Game/HistoryPanel/HistoryPanelDataService.cs Game/HistoryPanel/HistoryPanelReplayService.cs Game/HistoryPanel/HistoryPanelFormatter.cs tests/ArchitectureModularization.Tests/Program.cs
git commit -m "Split history panel responsibilities"
```

### Task 6: Migrate root-namespace feature files into feature namespaces

**Files:**
- Modify: `Game/HistoryPanel/HistoryPanel.cs`
- Modify: `Game/HistoryPanel/HistoryPanel.Canvas.cs`
- Modify: `Game/HistoryPanel/HistoryPanelRepository.cs`
- Modify: `Game/HistoryPanel/HistoryPanelPreviewRenderer.cs`
- Modify: `Game/EncounterTracker.cs` or its replacement
- Modify: `Game/RunStateSyncController.cs`
- Modify: `Game/Tooltips/TooltipModifierRefreshController.cs`
- Modify: `Game/Input/BppKeyBindRowController.cs`
- Modify callers affected by namespace moves
- Test: `tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`

**Step 1: Move one feature at a time**

Start with `Game/HistoryPanel/*` and migrate to `BazaarPlusPlus.Game.HistoryPanel`.

Avoid repo-wide namespace churn in one commit.

**Step 2: Migrate cross-feature root files**

Move files like:
- `Game/RunStateSyncController.cs`
- encounter-tracking entry points

into the most specific namespace that matches their feature responsibility.

**Step 3: Update architecture checks**

Extend the smoke tests so new feature files are not allowed to remain in the root namespace without a good reason.

**Step 4: Verify**

Run: `dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/HistoryPanel Game/RunStateSyncController.cs Game/EncounterTracking tests/ArchitectureModularization.Tests/Program.cs
git commit -m "Align feature namespaces with directory boundaries"
```

### Task 7: Remove transitional static access from touched features

**Files:**
- Modify: `Core/Runtime/BppRuntimeHost.cs`
- Modify: touched files under `Game/`, `Patches/`, and `Plugin.cs`
- Test: `tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`
- Test: focused feature test projects for each migrated area

**Step 1: Pick one static accessor at a time**

Examples:
- replace direct `BppRuntimeHost.Paths` usage in `HistoryPanel`
- replace direct `BppRuntimeHost.RunContext` usage in encounter tracking
- replace direct `BppRuntimeHost.EventBus` usage in newly composed features

**Step 2: Convert only callers already inside migrated features**

Do not attempt a repo-wide removal in one pass. Shrink the static surface opportunistically as features become injectable.

**Step 3: Fail the architecture test on new static usage**

Add assertions that no newly refactored feature reintroduces static runtime access after migration.

**Step 4: Verify**

Run the smallest relevant project for each touched feature, then run:

`dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add Core/Runtime/BppRuntimeHost.cs Plugin.cs Game tests/ArchitectureModularization.Tests/Program.cs
git commit -m "Retire transitional runtime statics from migrated features"
```

### Task 8: Final validation pass for the refactor tranche

**Files:**
- No intentional code changes unless fixes are required

**Step 1: Run focused feature tests**

Run the relevant test projects touched by the tranche, for example:
- `dotnet test tests/EncounterTrackerState.Tests/EncounterTrackerState.Tests.csproj`
- `dotnet test tests/HistoryPanelRepository.Tests/HistoryPanelRepository.Tests.csproj`

**Step 2: Run architecture smoke tests**

Run: `dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj`
Expected: PASS

**Step 3: Run broader validation only if the tranche touched shared runtime/build behavior**

If the refactor changed common startup/build behavior significantly, run the next-broader test surface. Do not default to `BuildAll` unless build logic or packaging changed.

**Step 4: Prepare PR notes**

Include:
- what global access was removed
- what lifecycle path was unified
- what compatibility shims remain
- `Suggested .rules additions` only if a repeated non-obvious trap showed up during execution

**Step 5: Commit any final fixups**

```bash
git add -A
git commit -m "Finalize architecture refactor tranche"
```
