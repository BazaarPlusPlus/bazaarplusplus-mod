> **Status: IMPLEMENTED (historical).** Shipped; as-built generalized to ComponentMount<T> for ~9 features. Durable decision promoted to [ADR-0002](../../adr/0002-mountable-feature-registry.md).

# AutoBazaar Mountable Feature & Encounter Tracking Decoupling

**Status:** Draft for implementation planning
**Date:** 2026-05-22
**Owner:** BazaarPlusPlus mod

## 1. Background

### 1.1 How AutoBazaar is wired today

AutoBazaar is a `MonoBehaviour` (`AutoBazaarRuntime`) attached imperatively from the plugin entry point. The integration is spread across three places in [Plugin.cs](../../../Plugin.cs):

- Import: `using BazaarPlusPlus.Game.AutoBazaar;` at the top of the file.
- Attach: inside `AttachRuntimeComponents` —
  ```csharp
  var autoBazaar = gameObject.AddComponent<AutoBazaarRuntime>();
  autoBazaar.Initialize(services);
  ```
- Detach: inside `DetachRuntimeComponents` — `DestroyComponentIfPresent<AutoBazaarRuntime>();`.

To turn AutoBazaar off at the DLL level you have to touch all three spots. The existing `AutoBazaarEnabled` cfg flag only gates the HTTP listener inside `AutoBazaarRuntime.ReconcileListener`; the `MonoBehaviour` itself, its `Update` tick, the snapshot publisher, and all of its reflection probes still load and run on every frame regardless of the flag.

The mod already has a clean modular pattern for non-`MonoBehaviour` code:

- [IBppFeature](../../../Core/Runtime/IBppFeature.cs) — `Start()` / `Stop()`.
- [BppFeatureRegistry](../../../Core/Runtime/BppFeatureRegistry.cs) — `Register`, `Start`, `Stop`.
- [BppComposition](../../../BppComposition.cs) — owns the registry and registers each module: `RunLifecycleModule`, `CombatReplayModule`, `CombatStatusBarModule`.

`MonoBehaviour` features have no equivalent. They are all hand-wired in `Plugin.AttachRuntimeComponents` / `Plugin.DetachRuntimeComponents`. AutoBazaar is one of about ten such features.

### 1.2 Encounter tracking inside AutoBazaar

Three pieces of code inside `Game/AutoBazaar/` are not actually AutoBazaar-specific — they are general reads of the current encounter situation that AutoBazaar happens to be the only consumer of today:

- [AutoBazaarInteractionFilterProbe](../../../Game/AutoBazaar/AutoBazaarInteractionFilterProbe.cs) reads `AppState._iteractionFilter` via reflection. When non-empty, the game is in a target-selection state (upgrade / enchant / etc.) and only owned cards whose `templateId` is in the filter are accepted by `BuyItemCommand`. The set of allowed template IDs is exactly the "what items can this encounter currently target" set.
- [AutoBazaarPedestalEligibilityProbe](../../../Game/AutoBazaar/AutoBazaarPedestalEligibilityProbe.cs) invokes `PedestalState.ValidateCards()` via reflection and reads `_validCards`, yielding the set of owned-card `InstanceId`s the active pedestal would accept.
- `ResolveCurrentEncounterType` inside [AutoBazaarContextBuilder.cs:756](../../../Game/AutoBazaar/AutoBazaarContextBuilder.cs:756) resolves the encounter-card type name from `RunState.CurrentEncounterId`.

All three are pure reads with no AutoBazaar state. Their location inside `Game/AutoBazaar/` is incidental.

### 1.3 The future enchant / upgrade preview gating

The enchant / upgrade tooltip preview is currently unconditional. [ItemEnchantPreviewService](../../../Game/ItemEnchantPreview/ItemEnchantPreviewService.cs) and [ItemEnchantPreviewEligibility](../../../Game/ItemEnchantPreview/ItemEnchantPreviewEligibility.cs) emit post-enchant tooltip segments for every hand and stash item; the only gates are the `EnchantPreviewAlwaysShow` cfg toggle and the held hotkey. [UpgradePreviewTooltipPatch](../../../Patches/Tooltips/UpgradePreviewTooltipPatch.cs) similarly attaches the upgrade preview whenever the upgrade-preview hotkey is held and `card.CanCardUpgrade()` is true.

This is noisy and a spoiler when no encounter is actually offering the enchant or upgrade. The intended behaviour — out of scope for this design but it is the reason for the decoupling — is to show the post-enchant text only for items the active encounter explicitly targets, i.e. items whose `templateId` is in the interaction filter (for enchants) or whose `InstanceId` is in the pedestal's eligible set (for upgrades).

That gating becomes a trivial change once the three probes live behind a single pull-based service that anyone — AutoBazaar, the enchant preview, the upgrade preview — can call. The decoupling has to happen first.

## 2. Goals

1. **Single-line mount toggle for AutoBazaar.** Adding or removing one line in `BppComposition` turns the entire feature on or off at composition time: no `MonoBehaviour` attached, no `Update` tick, no probes loaded, no HTTP listener regardless of the cfg flag, no `using` of `Game.AutoBazaar` from `Plugin.cs`.
2. **A small reusable mountable-feature abstraction.** `IBppMountable` parallel to the existing `IBppFeature`, with a `BppMountableRegistry` that mounts on a host `GameObject`. AutoBazaar is the first user. Other `MonoBehaviour` features in `Plugin.AttachRuntimeComponents` are unchanged; they can opt in incrementally later if a reason appears.
3. **Encounter tracking lives in its own module.** A new `Game/Encounter/` namespace owns the three probes. A pull-based `IEncounterStateProbe` returns an `EncounterStateSnapshot` value type. The probe is exposed through `IBppServices` alongside `IGameStateProbe`.
4. **Make the future preview gating a small, obvious next step.** The same `IEncounterStateProbe` surface that AutoBazaar consumes is the surface `ItemEnchantPreviewService` and `UpgradePreviewTooltipPatch` will consume in a follow-up. This design does not change the preview behaviour; it only puts the data source in place.
5. **Zero observable change today.** AutoBazaar runs exactly as it does now when the mount line is present. The HTTP endpoint, decision log, tick cadence, and validation rules are unchanged. The three probes return the same values via a different namespace.

## 3. Non-Goals

- Migrating other `MonoBehaviour` features (CombatReplay, HistoryPanel, StatusBar, RunLogging, MonsterPreview, screenshot, tooltip, video recorder) to the new registry. They stay hand-wired in `Plugin.cs`.
- Changing `AutoBazaarEnabled` cfg semantics. It continues to gate the HTTP listener while the `MonoBehaviour` stays mounted. The mount switch is a separate, composition-time concern.
- Implementing the actual enchant / upgrade preview gating. That ships in a follow-up.
- Turning the encounter snapshot into an event-bus signal. Consumers pull on demand.
- Generalising `IGameStateProbe`. The new `IEncounterStateProbe` is narrower and lives alongside it.

## 4. Plan

### 4.1 Final shape

```
BppComposition
  ├─ _featureRegistry   (pure C# modules — unchanged)
  ├─ _mountables        (NEW: MonoBehaviour mountables)
  │   └─ AutoBazaarMount
  └─ services
      └─ EncounterState  (NEW: IEncounterStateProbe)

Plugin.Awake
  ├─ composition.Start()
  ├─ AttachRuntimeComponents(...)         (one fewer AddComponent call)
  └─ composition.Mountables.MountAll(gameObject, services)

Plugin.OnDestroy
  ├─ composition.Mountables.UnmountAll(gameObject)
  ├─ DetachRuntimeComponents()            (one fewer DestroyImmediate call)
  └─ composition.Dispose()
```

The dotted boxes below are new; the rest exists today.

```text
Game/AutoBazaar/                   Game/Encounter/  (NEW)
  AutoBazaarRuntime.cs               EncounterStateProbe.cs        ──┐
  AutoBazaarMount.cs  (NEW)          InteractionFilterProbe.cs       │ pure reads, no
  AutoBazaarContextBuilder.cs ──┐    PedestalEligibilityProbe.cs     │ AutoBazaar state
  …                             │    EncounterTypeResolver.cs      ──┘
                                │           ▲
                                └───────────┘ consumes via IBppServices.EncounterState

Core/GameState/
  IGameStateProbe.cs                 (existing)
  IEncounterStateProbe.cs  (NEW)
  EncounterStateSnapshot.cs (NEW)

Core/Runtime/
  IBppFeature.cs                     (existing)
  BppFeatureRegistry.cs              (existing)
  IBppMountable.cs   (NEW)
  BppMountableRegistry.cs  (NEW)
```

### 4.2 New abstractions

#### `IBppMountable`

```csharp
// Core/Runtime/IBppMountable.cs
internal interface IBppMountable
{
    void Mount(GameObject host, IBppServices services);
    void Unmount(GameObject host);
}
```

Mirror of `IBppFeature` but accepts the host `GameObject` so implementors can `AddComponent`. `IBppFeature` is deliberately Unity-free; `IBppMountable` is the Unity-aware counterpart.

#### `BppMountableRegistry`

```csharp
// Core/Runtime/BppMountableRegistry.cs
internal sealed class BppMountableRegistry
{
    private readonly List<IBppMountable> _mountables = new();

    public void Register(IBppMountable mountable) => _mountables.Add(mountable);

    public void MountAll(GameObject host, IBppServices services)
    {
        foreach (var m in _mountables) m.Mount(host, services);
    }

    public void UnmountAll(GameObject host)
    {
        for (var i = _mountables.Count - 1; i >= 0; i--) _mountables[i].Unmount(host);
    }
}
```

Order of registration is the mount order; unmount is reverse, matching `BppFeatureRegistry`.

#### `AutoBazaarMount`

```csharp
// Game/AutoBazaar/AutoBazaarMount.cs
internal sealed class AutoBazaarMount : IBppMountable
{
    private AutoBazaarRuntime? _runtime;

    public void Mount(GameObject host, IBppServices services)
    {
        _runtime = host.AddComponent<AutoBazaarRuntime>();
        _runtime.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        if (_runtime != null) UnityEngine.Object.DestroyImmediate(_runtime);
        _runtime = null;
    }
}
```

Holds the component reference so unmount targets the exact instance it mounted, not whatever `GetComponent<T>` happens to find.

### 4.3 New Encounter module

#### `EncounterStateSnapshot` (value type, primitives only)

```csharp
// Core/GameState/EncounterStateSnapshot.cs
internal readonly struct EncounterStateSnapshot
{
    public string? CurrentEncounterId { get; init; }
    public string? CurrentEncounterType { get; init; }

    /// Template IDs the game's interaction filter currently restricts
    /// SelectItem to. Empty when no upgrade/enchant target-selection is active.
    public IReadOnlyList<string> InteractionFilterTemplateIds { get; init; }

    /// Owned-card InstanceIds the active PedestalState would accept.
    /// Empty when not on a pedestal or no card satisfies the criteria.
    /// `HashSet<string>` (concrete) instead of `IReadOnlySet<string>` because
    /// netstandard2.1 does not have the latter; callers treat it as read-only.
    public HashSet<string> PedestalEligibleInstanceIds { get; init; }

    public static EncounterStateSnapshot Empty { get; } = new()
    {
        CurrentEncounterId = null,
        CurrentEncounterType = null,
        InteractionFilterTemplateIds = Array.Empty<string>(),
        PedestalEligibleInstanceIds = new HashSet<string>(),
    };
}
```

Fields are primitives so the type can live in `Core/GameState/` without taking a dependency on `BazaarGameClient` types.

#### `IEncounterStateProbe`

```csharp
// Core/GameState/IEncounterStateProbe.cs
internal interface IEncounterStateProbe
{
    /// Main thread only. Returns a fresh snapshot of the current encounter
    /// situation. Never throws — failures degrade individual fields to their
    /// empty values.
    EncounterStateSnapshot GetCurrent();
}
```

Pull-based, no caching at the interface level. AutoBazaar already self-rate-limits to ~1.5 s/tick; the future preview consumers run only on tooltip hover state changes. Neither is a hot path. If a future profile shows the pedestal reflection invocation is too expensive under tooltip churn, the implementation can add per-frame memoisation behind the same interface without rippling out to callers.

#### Probe moves

| From | To | Notes |
|---|---|---|
| `Game/AutoBazaar/AutoBazaarInteractionFilterProbe.cs` | `Game/Encounter/InteractionFilterProbe.cs` | Rename type to `InteractionFilterProbe`. Stays `internal static`. Behaviour unchanged. |
| `Game/AutoBazaar/AutoBazaarPedestalEligibilityProbe.cs` | `Game/Encounter/PedestalEligibilityProbe.cs` | Same rename pattern. |
| `AutoBazaarContextBuilder.ResolveCurrentEncounterType` (private) | `Game/Encounter/EncounterTypeResolver.cs` (`internal static class EncounterTypeResolver` with `Resolve(string?)`) | Moves the `Data.Entities` scan out of the context builder. |

Log tag of the moved probes changes from `"AutoBazaar"` to `"Encounter"` so misbehaviour points at the right owner; this is the only observable difference from the move.

#### `EncounterStateProbe`

```csharp
// Game/Encounter/EncounterStateProbe.cs
internal sealed class EncounterStateProbe : IEncounterStateProbe
{
    public EncounterStateSnapshot GetCurrent()
    {
        var runState = Data.CurrentState;
        var appState = AppState.CurrentState;

        var currentEncounterId = runState?.CurrentEncounterId;
        var currentEncounterType = EncounterTypeResolver.Resolve(currentEncounterId);

        var filter = InteractionFilterProbe.ReadCurrentFilter();

        var pedestalEligible = appState is PedestalState ped
            ? PedestalEligibilityProbe.ReadEligibleInstanceIds(ped)
            : new HashSet<string>();

        return new EncounterStateSnapshot
        {
            CurrentEncounterId = currentEncounterId,
            CurrentEncounterType = currentEncounterType,
            InteractionFilterTemplateIds = filter,
            PedestalEligibleInstanceIds = pedestalEligible,
        };
    }
}
```

One pedestal `ValidateCards()` invocation per `GetCurrent()` call, only when the player is on a pedestal. Matches the current per-tick cost in `AutoBazaarContextBuilder`.

### 4.4 Wiring through `IBppServices`

`IEncounterStateProbe` is added alongside the existing `IGameStateProbe`:

```csharp
// Core/Runtime/IBppServices.cs
internal interface IBppServices
{
    IBppEventBus EventBus { get; }
    IBppConfig Config { get; }
    IPathService Paths { get; }
    IRunContext RunContext { get; }
    IGameStateProbe GameStateProbe { get; }
    IEncounterStateProbe EncounterState { get; }   // NEW
    ManualLogSource Logger { get; }
}
```

`BppRuntimeServices` gets a matching constructor parameter and property. `BppComposition` constructs the probe and passes it in:

```csharp
// BppComposition.cs
private readonly EncounterStateProbe _encounterStateProbe = new();
private readonly BppMountableRegistry _mountables = new();

public BppMountableRegistry Mountables => _mountables;

public BppComposition(ManualLogSource logger, ConfigFile configFile)
{
    // … existing init …
    _services = new BppRuntimeServices(
        _eventBus, _config, _paths, _runContext,
        _gameStateProbe, _encounterStateProbe, logger);

    // existing feature modules (unchanged)
    _featureRegistry.Register(_runLifecycle);
    _featureRegistry.Register(_combatReplayModule);
    _featureRegistry.Register(_combatStatusBarModule);

    // mountables — single line per feature
    _mountables.Register(new AutoBazaarMount());
}
```

Disabling AutoBazaar at compile time is now exactly one comment:

```csharp
// _mountables.Register(new AutoBazaarMount());
```

No other file changes.

### 4.5 `Plugin.cs` changes

Three small edits:

1. Drop `using BazaarPlusPlus.Game.AutoBazaar;` (no longer referenced from `Plugin.cs`).
2. In `AttachRuntimeComponents`, remove the two-line AutoBazaar block and, **after** the existing hand-wired adds finish, call `_composition!.Mountables.MountAll(gameObject, services);`. Mount order: existing hand-wired features first, then mountables. AutoBazaar today is added near the end, so this preserves its relative position.
3. In `DetachRuntimeComponents`, call `_composition?.Mountables.UnmountAll(gameObject);` **before** the existing destroy chain (reverse of mount order), and remove `DestroyComponentIfPresent<AutoBazaarRuntime>();`.

`CleanupFailedInitialization` and `OnDestroy` already call `DetachRuntimeComponents`, so unmount is covered by the existing failure path.

### 4.6 `AutoBazaarContextBuilder` migration

Replace direct probe calls and the private `ResolveCurrentEncounterType` with one `IEncounterStateProbe` call near the top of `BuildCore`:

```csharp
var encounter = services.EncounterState.GetCurrent();

string? currentEncounterId = encounter.CurrentEncounterId;
string? currentEncounterType = encounter.CurrentEncounterType;

ISet<string>? interactionFilter = encounter.InteractionFilterTemplateIds.Count > 0
    ? new HashSet<string>(encounter.InteractionFilterTemplateIds)
    : null;

// in BuildActions, pedestal branch:
var eligibleIds = encounter.PedestalEligibleInstanceIds;
```

The `appState is PedestalState ped` cast disappears from `BuildActions` because the probe already does it. `interactionFilterList` becomes `encounter.InteractionFilterTemplateIds`. `ResolveCurrentEncounterType` is removed (moved to `EncounterTypeResolver`).

Behaviour is identical: same fields populated, same `Data.Entities` scan in `EncounterTypeResolver`, same `ValidateCards()` invocation when on a pedestal, same empty defaults when reflection fails.

### 4.7 Future preview gating (sketch, not implemented here)

Once `IBppServices.EncounterState` is in place, the future change is small and lives in the existing preview classes:

```csharp
// ItemEnchantPreviewService.BuildPreviewSegments — future
var encounter = BppPatchHost.Services.EncounterState.GetCurrent();
if (encounter.InteractionFilterTemplateIds.Count > 0
    && !encounter.InteractionFilterTemplateIds.Contains(itemCard.TemplateId.ToString("D")))
{
    return empty;  // not a target of the active enchant encounter
}
```

```csharp
// UpgradePreviewTooltipPatch.TryScheduleUpgradeTooltip — future
var encounter = BppPatchHost.Services.EncounterState.GetCurrent();
if (encounter.PedestalEligibleInstanceIds.Count > 0
    && !encounter.PedestalEligibleInstanceIds.Contains(card.InstanceId.Value ?? ""))
{
    return false;  // pedestal would not accept this card
}
```

Off-encounter behaviour (`InteractionFilterTemplateIds.Count == 0` and `PedestalEligibleInstanceIds.Count == 0`) is the existing always-show path — i.e. gating only narrows, never broadens, the cases where the preview disappears. The two cfg flags (`EnchantPreviewAlwaysShow`, the hotkey holds) remain in front of this check.

This design ships the data plumbing; the actual gating change is a separate spec.

### 4.8 Migration order

Mechanical, no behavioural change at any step:

1. Add `Core/GameState/EncounterStateSnapshot.cs` + `IEncounterStateProbe.cs`.
2. Move `AutoBazaarInteractionFilterProbe` → `Game/Encounter/InteractionFilterProbe.cs`; flip log tag to `"Encounter"`. Update the one call site in `AutoBazaarContextBuilder`.
3. Move `AutoBazaarPedestalEligibilityProbe` → `Game/Encounter/PedestalEligibilityProbe.cs`; flip log tag. Update the one call site.
4. Extract `EncounterTypeResolver` from `AutoBazaarContextBuilder.ResolveCurrentEncounterType`. Update the one call site.
5. Add `Game/Encounter/EncounterStateProbe.cs`.
6. Add `IBppServices.EncounterState`, plumb through `BppRuntimeServices` and `BppComposition`.
7. Refactor `AutoBazaarContextBuilder.BuildCore` and the pedestal branch in `BuildActions` to fetch one snapshot via `services.EncounterState.GetCurrent()` and read all three fields from it, replacing the three direct probe calls left over from steps 2–4.
8. Add `Core/Runtime/IBppMountable.cs` + `BppMountableRegistry.cs`.
9. Add `Game/AutoBazaar/AutoBazaarMount.cs`. Expose `Mountables` on `BppComposition`. Register `AutoBazaarMount`.
10. Edit `Plugin.cs`: drop the using, swap the attach/detach blocks for `MountAll` / `UnmountAll`.

Steps 1–7 are the encounter decoupling and are independently shippable. Steps 8–10 are the mountable decoupling and depend on 1–7 only for the cleaner `AutoBazaarContextBuilder` diff.

## 5. Verification

### 5.1 Build

- `dotnet build BazaarPlusPlus.csproj -c Debug` and `-c Release` must both succeed.
- The Debug build's existing `CopyToBepInExPlugins` target must still copy `BazaarPlusPlus.dll` into `$(GamePath)/BepInEx/plugins`.
- `BuildAll` is not required per repo `.rules` — this is targeted refactoring, not packaging or build-logic work.

### 5.2 Unit tests

- The existing `tests/AutoBazaar.Tests/` suite must continue to pass without modification:
  - `AutoBazaarContextSnapshotTests` (the only test directly exercising context-builder output)
  - `AutoBazaarActionValidatorTests`, `AutoBazaarTargetSelectionActionsTests`, `AutoBazaarMoveTargetPlannerTests`, `AutoBazaarActionQueueTests`, `AutoBazaarDecisionLogTests`, `AutoBazaarHttpServerTests`, `AutoBazaarUlidTests`, `AutoBazaarSchemaTests`, `AutoBazaarResponseJsonTests`
- The three probes are reflection-against-the-game and cannot be exercised in isolation under the test runner; no new probe-level tests. This matches their current state and the repo rule against tests that only assert mock call sequences.
- If `AutoBazaarContextSnapshotTests` injects encounter fields directly today, it continues to do so. If it routes through real probes (it does not), it will be adapted to inject an `IEncounterStateProbe` test double; this is allowed only because the existing tests need to keep their current coverage.

### 5.3 In-game smoke (Debug build, manual)

Run each scenario on a real game launch with the freshly-copied DLL.

**Mount-on (line present):**
- Start a run. Confirm `BazaarPlusPlus/AutoBazaar/endpoint.json` appears with `port=47900` (or the configured port).
- `curl http://127.0.0.1:47900/v1/context` returns a snapshot whose `currentEncounterId`, `currentEncounterType`, `interactableTemplateIds`, and pedestal-related action options match the pre-refactor build's output on the same save state.
- `BazaarPlusPlus/AutoBazaar/decisions.ndjson` accumulates entries as actions execute.
- Trigger an enchant encounter, confirm `interactableTemplateIds` is populated.
- Enter a pedestal, confirm `CommitToPedestal` actions appear for eligible owned items only (same items as today).

**Mount-off (registration line commented):**
- Re-launch. No `endpoint.json` file. `curl http://127.0.0.1:47900/v1/context` fails with connection refused.
- No `decisions.ndjson` is created. `BazaarPlusPlus.log` shows no `AutoBazaar` lines.
- `AutoBazaarEnabled` cfg flag has no effect either way — confirms the compile-time mount wins.

**Preview behaviour unchanged:**
- Hover over hand and stash items. Enchant preview tooltip text appears under "Bazaar++" exactly as before, regardless of encounter state. The future gating is not part of this work.
- Hold the upgrade-preview hotkey. Upgrade preview behaves exactly as before.

### 5.4 Toggle proof

Adding `// ` to the front of `_mountables.Register(new AutoBazaarMount());` in `BppComposition.cs` and rebuilding must produce a DLL where:

- No `AutoBazaarRuntime` is attached to the BepInEx host `GameObject`.
- No HTTP listener binds to the configured port.
- No `AutoBazaar/` directory or file is created under `BazaarPlusPlus/`.

This is the acceptance criterion for "single line of configuration."

### 5.5 Regression checklist

The following pre-existing behaviours must be unaffected by the refactor:

- CombatReplay, HistoryPanel, StatusBar, RunLogging, MonsterPreview, screenshot, tooltip-modifier-refresh, and combat-replay video recorder still attach and function identically.
- `AutoBazaarEnabled=false` (with mount on) still stops the listener while leaving the `MonoBehaviour` mounted, exactly as today.
- Existing enchant preview always-show toggle, the enchant-preview held-hotkey, and the upgrade-preview held-hotkey produce identical tooltip output.
- `IGameStateProbe.ComputeIsInGameRun()` and the `RunLifecycleModule` event chain are untouched.

If any of the above changes observably, the refactor has overreached.
