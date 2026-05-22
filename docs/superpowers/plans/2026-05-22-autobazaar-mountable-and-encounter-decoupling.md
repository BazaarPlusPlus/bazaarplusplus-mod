# AutoBazaar Mountable Feature & Encounter Decoupling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make AutoBazaar a single-line composition-time mountable in `BppComposition`, and move the three encounter-state probes out of `Game/AutoBazaar/` into a reusable `Game/Encounter/` module exposed through `IBppServices.EncounterState`.

**Architecture:** Two stacked refactors with no behavioural change. (1) Encounter decoupling: extract `InteractionFilterProbe`, `PedestalEligibilityProbe`, and `EncounterTypeResolver` into `Game/Encounter/`, expose a single pull-based `IEncounterStateProbe` through `IBppServices`, and update `AutoBazaarContextBuilder` to fetch one snapshot. (2) Mountable decoupling: add `IBppMountable` + `BppMountableRegistry` parallel to the existing `IBppFeature` pattern, wrap `AutoBazaarRuntime` in `AutoBazaarMount`, and replace the imperative attach/detach in `Plugin.cs` with `Mountables.MountAll` / `UnmountAll`. AutoBazaar is the only initial mountable; other `MonoBehaviour` features stay hand-wired.

**Tech Stack:** C# (netstandard2.1), Unity 2022 / BepInEx, xUnit, MSBuild via `BazaarPlusPlus.csproj`. No new dependencies.

**Spec:** [docs/superpowers/specs/2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md](../specs/2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md)

**Verification commands:**

- Whole-mod build (Debug + Release):
  ```bash
  dotnet build BazaarPlusPlus.csproj -c Debug
  dotnet build BazaarPlusPlus.csproj -c Release
  ```
- AutoBazaar tests only (smallest relevant test project per project rules):
  ```bash
  dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj -c Debug
  ```

In-game smoke is manual and described in spec §5.3 / §5.4; it is not part of the automated steps below. Run it after Task 10.

---

## Task 1: Add `EncounterStateSnapshot` value type

**Files:**
- Create: `Core/GameState/EncounterStateSnapshot.cs`

- [ ] **Step 1: Create the file**

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Core.GameState;

/// <summary>
/// Immutable snapshot of the current encounter situation. Primitive fields only
/// so the type can live in Core without depending on BazaarGameClient types.
/// </summary>
internal readonly struct EncounterStateSnapshot
{
    public string? CurrentEncounterId { get; init; }
    public string? CurrentEncounterType { get; init; }

    /// <summary>Template IDs the game's interaction filter currently restricts
    /// SelectItem to. Empty when no upgrade/enchant target-selection is active.</summary>
    public IReadOnlyList<string> InteractionFilterTemplateIds { get; init; }

    /// <summary>Owned-card InstanceIds the active PedestalState would accept.
    /// Empty when not on a pedestal or no card satisfies the criteria.
    /// HashSet&lt;string&gt; (concrete) instead of IReadOnlySet&lt;string&gt; because
    /// netstandard2.1 does not expose the latter; callers treat it as read-only.</summary>
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

- [ ] **Step 2: Build to verify the type compiles**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: build succeeds. `EncounterStateSnapshot` has no callers yet — this only verifies the type compiles standalone.

- [ ] **Step 3: Commit**

```bash
git add Core/GameState/EncounterStateSnapshot.cs
git commit -m "Add EncounterStateSnapshot value type"
```

---

## Task 2: Add `IEncounterStateProbe` interface

**Files:**
- Create: `Core/GameState/IEncounterStateProbe.cs`

- [ ] **Step 1: Create the file**

```csharp
#nullable enable

namespace BazaarPlusPlus.Core.GameState;

internal interface IEncounterStateProbe
{
    /// <summary>Main thread only. Returns a fresh snapshot of the current encounter
    /// situation. Never throws — failures degrade individual fields to their
    /// empty values.</summary>
    EncounterStateSnapshot GetCurrent();
}
```

- [ ] **Step 2: Build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add Core/GameState/IEncounterStateProbe.cs
git commit -m "Add IEncounterStateProbe interface"
```

---

## Task 3: Move `AutoBazaarInteractionFilterProbe` to `Game/Encounter/InteractionFilterProbe`

**Files:**
- Create: `Game/Encounter/InteractionFilterProbe.cs`
- Delete: `Game/AutoBazaar/AutoBazaarInteractionFilterProbe.cs`
- Modify: `Game/AutoBazaar/AutoBazaarContextBuilder.cs` (single call site)

- [ ] **Step 1: Create `Game/Encounter/` directory by writing the new file**

Write `Game/Encounter/InteractionFilterProbe.cs`:

```csharp
#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Game.Encounter;

/// <summary>Reads <c>AppState._iteractionFilter</c> via reflection. When the
/// filter is non-empty, the game is in a target-selection state (upgrade,
/// enchant, etc.) and only owned cards whose templateId is in the filter are
/// accepted by <c>BuyItemCommand</c>; other clicks silently no-op.</summary>
internal static class InteractionFilterProbe
{
    private static FieldInfo? _filterField;
    private static bool _resolveAttempted;

    private static readonly string[] EmptyArray = System.Array.Empty<string>();

    /// <summary>Main thread only. Returns the current filter as an immutable
    /// snapshot. Empty result means either no filter is active or reflection
    /// couldn't reach the field; callers treat both the same.</summary>
    public static System.Collections.Generic.IReadOnlyList<string> ReadCurrentFilter()
    {
        try
        {
            if (!_resolveAttempted)
            {
                _resolveAttempted = true;
                _filterField = AccessTools.Field(typeof(AppState), "_iteractionFilter");
                if (_filterField is null)
                {
                    BppLog.Info("Encounter", "AppState._iteractionFilter field not found via reflection");
                }
            }
            if (_filterField is null) return EmptyArray;
            if (_filterField.GetValue(null) is not System.Collections.IList list) return EmptyArray;
            if (list.Count == 0) return EmptyArray;
            var copy = new string[list.Count];
            for (var i = 0; i < list.Count; i++) copy[i] = list[i]?.ToString() ?? "";
            return copy;
        }
        catch (Exception ex)
        {
            BppLog.Error("Encounter", "ReadCurrentFilter reflection failed", ex);
            return EmptyArray;
        }
    }
}
```

Note: log tag flipped from `"AutoBazaar"` to `"Encounter"` per spec §4.3.

- [ ] **Step 2: Delete the old file**

```bash
git rm Game/AutoBazaar/AutoBazaarInteractionFilterProbe.cs
```

- [ ] **Step 3: Update the call site in `AutoBazaarContextBuilder.cs:129`**

Add this using directive near the top of `Game/AutoBazaar/AutoBazaarContextBuilder.cs` (alongside the other `using BazaarGameClient...` lines and `using BazaarPlusPlus.Core.Runtime;`):

```csharp
using BazaarPlusPlus.Game.Encounter;
```

Replace the call on line 129 from:

```csharp
        var interactionFilterList = AutoBazaarInteractionFilterProbe.ReadCurrentFilter();
```

to:

```csharp
        var interactionFilterList = InteractionFilterProbe.ReadCurrentFilter();
```

- [ ] **Step 4: Build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: build succeeds. No references to `AutoBazaarInteractionFilterProbe` should remain — verify with:

```bash
grep -rn "AutoBazaarInteractionFilterProbe" --include='*.cs' --exclude-dir=decompiled --exclude-dir=obj --exclude-dir=bin .
```

Expected output: empty.

- [ ] **Step 5: Run AutoBazaar tests**

```bash
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj -c Debug
```

Expected: all tests pass. (The test project does not link the probe file — see `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj` — so this just guards the unrelated coverage.)

- [ ] **Step 6: Commit**

```bash
git add Game/Encounter/InteractionFilterProbe.cs Game/AutoBazaar/AutoBazaarContextBuilder.cs
git commit -m "Move InteractionFilterProbe to Game/Encounter namespace"
```

(`git rm` from Step 2 is already staged.)

---

## Task 4: Move `AutoBazaarPedestalEligibilityProbe` to `Game/Encounter/PedestalEligibilityProbe`

**Files:**
- Create: `Game/Encounter/PedestalEligibilityProbe.cs`
- Delete: `Game/AutoBazaar/AutoBazaarPedestalEligibilityProbe.cs`
- Modify: `Game/AutoBazaar/AutoBazaarContextBuilder.cs` (single call site)

- [ ] **Step 1: Create the new file**

```csharp
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Game.Encounter;

/// <summary>Reads the active <c>PedestalState</c>'s eligible-card set once per tick.
/// Calls <c>PedestalState.ValidateCards()</c> via reflection (it's private but
/// idempotent — just recomputes <c>_validCards</c> from Hand+Stash through the
/// pedestal template's SelectionCriteria), then reads <c>_validCards</c> and
/// returns the InstanceIds. Replaces N invocations of the public
/// <c>CanBeUpgraded(Card)</c> — each of which would re-run ValidateCards internally
/// for an O(N²) cost per tick.</summary>
internal static class PedestalEligibilityProbe
{
    private static MethodInfo? _validateCardsMethod;
    private static FieldInfo? _validCardsField;
    private static bool _reflectionAttempted;

    private static readonly HashSet<string> EmptySet = new();

    /// <summary>Main thread only. Returns the set of InstanceIds the pedestal would
    /// accept right now. Empty when not on a pedestal, reflection fails, or no
    /// owned card satisfies the template's SelectionCriteria.</summary>
    public static HashSet<string> ReadEligibleInstanceIds(PedestalState pedestalState)
    {
        try
        {
            if (!_reflectionAttempted)
            {
                _reflectionAttempted = true;
                _validateCardsMethod = AccessTools.Method(typeof(PedestalState), "ValidateCards");
                _validCardsField = AccessTools.Field(typeof(PedestalState), "_validCards");
                if (_validateCardsMethod is null) BppLog.Info("Encounter", "PedestalState.ValidateCards not found via reflection");
                if (_validCardsField is null) BppLog.Info("Encounter", "PedestalState._validCards not found via reflection");
            }
            if (_validateCardsMethod is null || _validCardsField is null) return EmptySet;

            _validateCardsMethod.Invoke(pedestalState, null);
            if (_validCardsField.GetValue(pedestalState) is not IList list || list.Count == 0)
                return EmptySet;

            var ids = new HashSet<string>(list.Count);
            foreach (var entry in list)
            {
                if (entry is Card card)
                {
                    var iid = card.InstanceId.Value;
                    if (!string.IsNullOrEmpty(iid)) ids.Add(iid);
                }
            }
            return ids;
        }
        catch (Exception ex)
        {
            BppLog.Info("Encounter", $"PedestalEligibilityProbe transient failure: {ex.GetType().Name}: {ex.Message}");
            return EmptySet;
        }
    }
}
```

- [ ] **Step 2: Delete the old file**

```bash
git rm Game/AutoBazaar/AutoBazaarPedestalEligibilityProbe.cs
```

- [ ] **Step 3: Update the call site in `AutoBazaarContextBuilder.cs:619`**

(The `using BazaarPlusPlus.Game.Encounter;` directive added in Task 3 already covers both files.)

Replace the line:

```csharp
                : AutoBazaarPedestalEligibilityProbe.ReadEligibleInstanceIds(pedestalState);
```

with:

```csharp
                : PedestalEligibilityProbe.ReadEligibleInstanceIds(pedestalState);
```

- [ ] **Step 4: Build and verify no stale references**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
grep -rn "AutoBazaarPedestalEligibilityProbe" --include='*.cs' --exclude-dir=decompiled --exclude-dir=obj --exclude-dir=bin .
```

Expected: build succeeds, grep prints nothing.

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj -c Debug
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add Game/Encounter/PedestalEligibilityProbe.cs Game/AutoBazaar/AutoBazaarContextBuilder.cs
git commit -m "Move PedestalEligibilityProbe to Game/Encounter namespace"
```

---

## Task 5: Extract `EncounterTypeResolver` from `AutoBazaarContextBuilder`

**Files:**
- Create: `Game/Encounter/EncounterTypeResolver.cs`
- Modify: `Game/AutoBazaar/AutoBazaarContextBuilder.cs` (extract private method, update single call site)

- [ ] **Step 1: Create the new file**

```csharp
#nullable enable
using System;
using BazaarGameClient.Domain.Models.Cards;
using TheBazaar;

namespace BazaarPlusPlus.Game.Encounter;

/// <summary>Resolves the encounter-card type name from a `RunState.CurrentEncounterId`
/// GUID by scanning `Data.Entities` for the matching card template. Pure read,
/// main thread only.</summary>
internal static class EncounterTypeResolver
{
    public static string? Resolve(string? currentEncounterId)
    {
        if (string.IsNullOrWhiteSpace(currentEncounterId)) return null;
        if (!Guid.TryParse(currentEncounterId, out var templateId)) return null;

        foreach (var entity in Data.Entities.Values)
        {
            if (entity is not Card card) continue;
            if (card.TemplateId != templateId) continue;
            return card.Template?.GetType().Name ?? card.Type.ToString();
        }
        return null;
    }
}
```

- [ ] **Step 2: Update the call site in `AutoBazaarContextBuilder.cs:110`**

Replace:

```csharp
        string? currentEncounterType = ResolveCurrentEncounterType(currentEncounterId);
```

with:

```csharp
        string? currentEncounterType = EncounterTypeResolver.Resolve(currentEncounterId);
```

- [ ] **Step 3: Remove the now-unused private method in `AutoBazaarContextBuilder.cs`**

Delete the entire `ResolveCurrentEncounterType` method (lines 756–768 in the pre-edit file, immediately before `BuildAttributes`):

```csharp
    private static string? ResolveCurrentEncounterType(string? currentEncounterId)
    {
        if (string.IsNullOrWhiteSpace(currentEncounterId)) return null;
        if (!Guid.TryParse(currentEncounterId, out var templateId)) return null;

        foreach (var entity in Data.Entities.Values)
        {
            if (entity is not Card card) continue;
            if (card.TemplateId != templateId) continue;
            return card.Template?.GetType().Name ?? card.Type.ToString();
        }
        return null;
    }
```

Use Edit with the exact block above as `old_string` and an empty string as `new_string`. Verify nothing else in the file references `ResolveCurrentEncounterType`:

```bash
grep -n "ResolveCurrentEncounterType" Game/AutoBazaar/AutoBazaarContextBuilder.cs
```

Expected: empty.

- [ ] **Step 4: Build and run tests**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj -c Debug
```

Expected: build succeeds, tests pass.

- [ ] **Step 5: Commit**

```bash
git add Game/Encounter/EncounterTypeResolver.cs Game/AutoBazaar/AutoBazaarContextBuilder.cs
git commit -m "Extract EncounterTypeResolver from AutoBazaarContextBuilder"
```

---

## Task 6: Add `EncounterStateProbe` aggregator

**Files:**
- Create: `Game/Encounter/EncounterStateProbe.cs`

- [ ] **Step 1: Create the file**

```csharp
#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Core.GameState;
using TheBazaar;

namespace BazaarPlusPlus.Game.Encounter;

/// <summary>Aggregates the three encounter-state probes into a single snapshot
/// consumers can pull on demand. One <c>PedestalState.ValidateCards()</c>
/// invocation per call, only when the player is currently on a pedestal.</summary>
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

- [ ] **Step 2: Build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: build succeeds. The probe has no callers yet — wiring happens in Task 7.

- [ ] **Step 3: Commit**

```bash
git add Game/Encounter/EncounterStateProbe.cs
git commit -m "Add EncounterStateProbe aggregator"
```

---

## Task 7: Wire `IEncounterStateProbe` through `IBppServices` and consume from `AutoBazaarContextBuilder`

**Files:**
- Modify: `Core/Runtime/IBppServices.cs`
- Modify: `Core/Runtime/BppRuntimeServices.cs`
- Modify: `BppComposition.cs`
- Modify: `Game/AutoBazaar/AutoBazaarContextBuilder.cs`

- [ ] **Step 1: Extend `IBppServices`**

In `Core/Runtime/IBppServices.cs`, add a `using` for `BazaarPlusPlus.Core.GameState;` is already present. Insert the new property after `GameStateProbe`:

```csharp
internal interface IBppServices
{
    IBppEventBus EventBus { get; }
    IBppConfig Config { get; }
    IPathService Paths { get; }
    IRunContext RunContext { get; }
    IGameStateProbe GameStateProbe { get; }
    IEncounterStateProbe EncounterState { get; }
    ManualLogSource Logger { get; }
}
```

- [ ] **Step 2: Extend `BppRuntimeServices`**

In `Core/Runtime/BppRuntimeServices.cs`, add the constructor parameter and matching property. The full file becomes:

```csharp
#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.RunContext;
using BepInEx.Logging;

namespace BazaarPlusPlus.Core.Runtime;

internal sealed class BppRuntimeServices : IBppServices
{
    public BppRuntimeServices(
        IBppEventBus eventBus,
        IBppConfig config,
        IPathService paths,
        IRunContext runContext,
        IGameStateProbe gameStateProbe,
        IEncounterStateProbe encounterState,
        ManualLogSource logger
    )
    {
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        RunContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        GameStateProbe = gameStateProbe ?? throw new ArgumentNullException(nameof(gameStateProbe));
        EncounterState = encounterState ?? throw new ArgumentNullException(nameof(encounterState));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IBppEventBus EventBus { get; }
    public IBppConfig Config { get; }
    public IPathService Paths { get; }
    public IRunContext RunContext { get; }
    public IGameStateProbe GameStateProbe { get; }
    public IEncounterStateProbe EncounterState { get; }
    public ManualLogSource Logger { get; }
}
```

- [ ] **Step 3: Wire from `BppComposition.cs`**

In `BppComposition.cs`, add the field declaration alongside the other probe field:

```csharp
    private readonly GameStateProbe _gameStateProbe = new();
    private readonly EncounterStateProbe _encounterStateProbe = new();
```

To make `EncounterStateProbe` resolve, also add this using near the top:

```csharp
using BazaarPlusPlus.Game.Encounter;
```

Then update the `BppRuntimeServices` constructor call to pass the new probe. The full new-services block becomes:

```csharp
        _services = new BppRuntimeServices(
            _eventBus,
            _config,
            _paths,
            _runContext,
            _gameStateProbe,
            _encounterStateProbe,
            logger
        );
```

- [ ] **Step 4: Consume the snapshot from `AutoBazaarContextBuilder.BuildCore`**

In `Game/AutoBazaar/AutoBazaarContextBuilder.cs`, replace the three individual probe lines with one snapshot fetch. The relevant region (lines 109–132 in the post-Task-5 file) currently reads:

```csharp
        string? currentEncounterId = runState?.CurrentEncounterId;
        string? currentEncounterType = EncounterTypeResolver.Resolve(currentEncounterId);

        // --- Card inventories ---
        bool canSell = canHandleOp(StateOps.SellItem);
        bool canMove = canHandleOp(StateOps.MoveItem);

        var boardItems = BuildBoardCards(run, playerGold, canSell, AutoBazaarCardLocation.Board);
        var chestItems = BuildBoardCards(run, playerGold, canSell, AutoBazaarCardLocation.Chest);
        var playerSkills = BuildSkillCards(run, canSell);

        var sellableItems = BuildSellableItems(boardItems, chestItems, canSell);

        // Selection set
        List<AutoBazaarCardSnapshot> selectionOptions = BuildSelectionOptions(
            runState, playerGold, selectionIsFree, run, canHandleOp(StateOps.SelectItem));

        // Target-selection mode (upgrade/enchant): when AppState._iteractionFilter
        // is non-empty, the game restricts SelectItem to owned cards whose
        // templateId is in the filter. Offer-based clicks silently no-op.
        var interactionFilterList = InteractionFilterProbe.ReadCurrentFilter();
        ISet<string>? interactionFilter = interactionFilterList.Count > 0
            ? new HashSet<string>(interactionFilterList)
            : null;
```

Change it to (one snapshot fetch near the top; everything below it reads from `encounter`):

```csharp
        var encounter = services.EncounterState.GetCurrent();

        string? currentEncounterId = encounter.CurrentEncounterId;
        string? currentEncounterType = encounter.CurrentEncounterType;

        // --- Card inventories ---
        bool canSell = canHandleOp(StateOps.SellItem);
        bool canMove = canHandleOp(StateOps.MoveItem);

        var boardItems = BuildBoardCards(run, playerGold, canSell, AutoBazaarCardLocation.Board);
        var chestItems = BuildBoardCards(run, playerGold, canSell, AutoBazaarCardLocation.Chest);
        var playerSkills = BuildSkillCards(run, canSell);

        var sellableItems = BuildSellableItems(boardItems, chestItems, canSell);

        // Selection set
        List<AutoBazaarCardSnapshot> selectionOptions = BuildSelectionOptions(
            runState, playerGold, selectionIsFree, run, canHandleOp(StateOps.SelectItem));

        // Target-selection mode (upgrade/enchant): when AppState._iteractionFilter
        // is non-empty, the game restricts SelectItem to owned cards whose
        // templateId is in the filter. Offer-based clicks silently no-op.
        var interactionFilterList = encounter.InteractionFilterTemplateIds;
        ISet<string>? interactionFilter = interactionFilterList.Count > 0
            ? new HashSet<string>(interactionFilterList)
            : null;
```

`runState?.CurrentEncounterId` previously came from the local `runState` variable at the top of `BuildCore`. The snapshot returns the same value (the probe also reads `Data.CurrentState`), so there is no behavioural drift. Keep the existing `var appState = AppState.CurrentState;` and `var runState = Data.CurrentState;` lines untouched — they're still needed by other code in the method (e.g. `ResolveStateName`).

- [ ] **Step 5: Update the pedestal branch in `BuildActions`**

The pedestal branch (lines 614–644 in the post-Task-4 file) currently reads:

```csharp
        if (stateName == AutoBazaarRunStateName.Pedestal && canHandleOp(StateOps.CommitToPedestal))
        {
            var pedestalState = AppState.CurrentState as PedestalState;
            var eligibleIds = pedestalState is null
                ? new HashSet<string>()
                : PedestalEligibilityProbe.ReadEligibleInstanceIds(pedestalState);
            foreach (var card in boardItems)
            {
```

This still has a direct probe call. Threading the snapshot all the way into `BuildActions` would change the method signature; instead, since the snapshot is already in scope for the `BuildCore` body, switch `BuildActions` to take the pedestal eligible-ID set as a parameter.

Update the `BuildActions` signature and its single call site in `BuildCore` to thread `pedestalEligibleIds` through.

First, in `BuildCore`, change the call (around line 135):

```csharp
        var actions = BuildActions(
            stateName, isInRun, canHandleOp,
            canReroll, canStartOrContinueRun,
            runState, selectionOptions, boardItems, chestItems, playerSkills, canMove, canSell,
            run);
```

to:

```csharp
        var actions = BuildActions(
            stateName, isInRun, canHandleOp,
            canReroll, canStartOrContinueRun,
            runState, selectionOptions, boardItems, chestItems, playerSkills, canMove, canSell,
            run, encounter.PedestalEligibleInstanceIds);
```

Then change `BuildActions`'s signature from:

```csharp
    private static IReadOnlyList<AutoBazaarDecisionOption> BuildActions(
        AutoBazaarRunStateName stateName,
        bool isInRun,
        Func<StateOps, bool> canHandleOp,
        bool canReroll,
        bool canStartOrContinueRun,
        RunState? runState,
        List<AutoBazaarCardSnapshot> selectionOptions,
        IReadOnlyList<AutoBazaarCardSnapshot> boardItems,
        IReadOnlyList<AutoBazaarCardSnapshot> chestItems,
        IReadOnlyList<AutoBazaarCardSnapshot> playerSkills,
        bool canMove,
        bool canSell,
        Run? run)
```

to (one parameter added at the end):

```csharp
    private static IReadOnlyList<AutoBazaarDecisionOption> BuildActions(
        AutoBazaarRunStateName stateName,
        bool isInRun,
        Func<StateOps, bool> canHandleOp,
        bool canReroll,
        bool canStartOrContinueRun,
        RunState? runState,
        List<AutoBazaarCardSnapshot> selectionOptions,
        IReadOnlyList<AutoBazaarCardSnapshot> boardItems,
        IReadOnlyList<AutoBazaarCardSnapshot> chestItems,
        IReadOnlyList<AutoBazaarCardSnapshot> playerSkills,
        bool canMove,
        bool canSell,
        Run? run,
        HashSet<string> pedestalEligibleIds)
```

And replace the pedestal branch body so it uses the parameter instead of calling the probe:

```csharp
        if (stateName == AutoBazaarRunStateName.Pedestal && canHandleOp(StateOps.CommitToPedestal))
        {
            var eligibleIds = pedestalEligibleIds;
            foreach (var card in boardItems)
            {
```

The `var pedestalState = AppState.CurrentState as PedestalState;` cast and the `PedestalEligibilityProbe.ReadEligibleInstanceIds(...)` invocation both disappear from `BuildActions`. The rest of the pedestal block (the two `foreach` loops over `boardItems` / `chestItems` reading `eligibleIds`) is unchanged.

- [ ] **Step 6: Remove the now-unused `using BazaarPlusPlus.Game.Encounter;` from `AutoBazaarContextBuilder.cs`**

After Step 5, the context builder no longer references types in `BazaarPlusPlus.Game.Encounter` directly. Confirm with:

```bash
grep -n "Encounter" Game/AutoBazaar/AutoBazaarContextBuilder.cs
```

If the only matches are field names from the snapshot (`encounter.*`, `CurrentEncounterType`, etc.) and not type names like `InteractionFilterProbe`, `PedestalEligibilityProbe`, or `EncounterTypeResolver`, remove the `using BazaarPlusPlus.Game.Encounter;` import added in Task 3.

- [ ] **Step 7: Build and test**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj -c Debug
```

Expected: build succeeds, tests pass.

- [ ] **Step 8: Commit**

```bash
git add Core/Runtime/IBppServices.cs Core/Runtime/BppRuntimeServices.cs BppComposition.cs Game/AutoBazaar/AutoBazaarContextBuilder.cs
git commit -m "Expose IEncounterStateProbe via IBppServices and consume from context builder"
```

---

## Task 8: Add `IBppMountable` and `BppMountableRegistry`

**Files:**
- Create: `Core/Runtime/IBppMountable.cs`
- Create: `Core/Runtime/BppMountableRegistry.cs`

- [ ] **Step 1: Create `IBppMountable`**

```csharp
#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Core.Runtime;

/// <summary>Unity-aware counterpart of <see cref="IBppFeature"/>. Mount-time
/// receives the host <c>GameObject</c> so implementors can <c>AddComponent</c>.</summary>
internal interface IBppMountable
{
    void Mount(GameObject host, IBppServices services);
    void Unmount(GameObject host);
}
```

- [ ] **Step 2: Create `BppMountableRegistry`**

```csharp
#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace BazaarPlusPlus.Core.Runtime;

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

Order: registration order is mount order; unmount is reverse, matching `BppFeatureRegistry`.

- [ ] **Step 3: Build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: build succeeds. No callers yet.

- [ ] **Step 4: Commit**

```bash
git add Core/Runtime/IBppMountable.cs Core/Runtime/BppMountableRegistry.cs
git commit -m "Add IBppMountable abstraction and BppMountableRegistry"
```

---

## Task 9: Add `AutoBazaarMount` and register it in `BppComposition`

**Files:**
- Create: `Game/AutoBazaar/AutoBazaarMount.cs`
- Modify: `BppComposition.cs`

- [ ] **Step 1: Create `AutoBazaarMount`**

```csharp
#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.AutoBazaar;

/// <summary>Wraps <see cref="AutoBazaarRuntime"/> as an <see cref="IBppMountable"/>
/// so it can be registered from the composition root instead of hand-wired in
/// <c>Plugin.AttachRuntimeComponents</c>. Holds the component reference so
/// unmount targets the exact instance it mounted.</summary>
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

- [ ] **Step 2: Expose `Mountables` on `BppComposition` and register `AutoBazaarMount`**

In `BppComposition.cs`:

- Add a using directive (in the existing using block, alphabetised):
  ```csharp
  using BazaarPlusPlus.Game.AutoBazaar;
  ```
- Add the registry field next to `_featureRegistry`:
  ```csharp
  private readonly BppFeatureRegistry _featureRegistry = new();
  private readonly BppMountableRegistry _mountables = new();
  ```
- Expose it as a public property next to `Services` and `RunLifecycle`:
  ```csharp
  public IBppServices Services => _services;
  public RunLifecycleModule RunLifecycle => _runLifecycle;
  public BppMountableRegistry Mountables => _mountables;
  ```
- At the bottom of the constructor (after `_featureRegistry.Register(_combatStatusBarModule);`), register the mount:
  ```csharp
      _featureRegistry.Register(_combatStatusBarModule);

      _mountables.Register(new AutoBazaarMount());
  }
  ```

- [ ] **Step 3: Build**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: build succeeds. `Plugin.cs` still attaches `AutoBazaarRuntime` directly — that's removed in Task 10.

- [ ] **Step 4: Commit**

```bash
git add Game/AutoBazaar/AutoBazaarMount.cs BppComposition.cs
git commit -m "Register AutoBazaar as a BppMountable in BppComposition"
```

---

## Task 10: Swap `Plugin.cs` attach/detach to `MountAll` / `UnmountAll`

**Files:**
- Modify: `Plugin.cs`

- [ ] **Step 1: Drop the `using BazaarPlusPlus.Game.AutoBazaar;` import**

In `Plugin.cs:8`, delete the line:

```csharp
using BazaarPlusPlus.Game.AutoBazaar;
```

After this edit, `Plugin.cs` no longer references any type in `BazaarPlusPlus.Game.AutoBazaar`.

- [ ] **Step 2: Remove the AutoBazaarRuntime attach block in `AttachRuntimeComponents`**

In `Plugin.cs` around lines 178–179 (inside `AttachRuntimeComponents`), delete the two-line block:

```csharp
        var autoBazaar = gameObject.AddComponent<AutoBazaarRuntime>();
        autoBazaar.Initialize(services);
```

The surrounding context is:

```csharp
        AddConfiguredTooltipModifierRefreshController(services.Config);

        var autoBazaar = gameObject.AddComponent<AutoBazaarRuntime>();
        autoBazaar.Initialize(services);

        var combatReplayVideoRecorder = gameObject.AddComponent<CombatReplayVideoRecorder>();
```

becomes:

```csharp
        AddConfiguredTooltipModifierRefreshController(services.Config);

        var combatReplayVideoRecorder = gameObject.AddComponent<CombatReplayVideoRecorder>();
```

- [ ] **Step 3: Call `MountAll` at the end of `AttachRuntimeComponents`**

In `Plugin.cs` immediately before the final `BppLog.Info("Plugin", "Runtime components attached");` line of `AttachRuntimeComponents`, add:

```csharp
        _composition?.Mountables.MountAll(gameObject, services);
```

The end of the method becomes:

```csharp
        var combatReplayVideoRecorder = gameObject.AddComponent<CombatReplayVideoRecorder>();
        combatReplayVideoRecorder.Initialize(services);

        _composition?.Mountables.MountAll(gameObject, services);

        BppLog.Info("Plugin", "Runtime components attached");
    }
```

Mount order: existing hand-wired adds first, then mountables. This preserves AutoBazaar's previous relative position (it used to be the second-to-last add).

- [ ] **Step 4: Call `UnmountAll` at the start of `DetachRuntimeComponents` and remove the AutoBazaarRuntime line**

In `Plugin.cs:265–280` (`DetachRuntimeComponents`), the current method is:

```csharp
    private void DetachRuntimeComponents()
    {
        DestroyComponentIfPresent<CombatReplayVideoRecorder>();
        DestroyComponentIfPresent<AutoBazaarRuntime>();
        DestroyComponentIfPresent<TooltipModifierRefreshController>();
        DestroyComponentIfPresent<EndOfRunScreenshotController>();
        DestroyComponentIfPresent<MonsterPreviewItemBoardRuntime>();
        DestroyComponentIfPresent<CardSetPreviewRuntime>();
        DestroyComponentIfPresent<MonsterPreviewWarmupController>();
        DestroyComponentIfPresent<CombatStatusBar>();
        DestroyComponentIfPresent<HistoryPanel>();
        DestroyComponentIfPresent<PlayerObservationController>();
        DestroyComponentIfPresent<RunUploadController>();
        DestroyComponentIfPresent<RunLoggingController>();
        DestroyComponentIfPresent<CombatReplayRuntime>();
    }
```

Change it to (call `UnmountAll` first to reverse mount order, then drop the `AutoBazaarRuntime` destroy line):

```csharp
    private void DetachRuntimeComponents()
    {
        _composition?.Mountables.UnmountAll(gameObject);

        DestroyComponentIfPresent<CombatReplayVideoRecorder>();
        DestroyComponentIfPresent<TooltipModifierRefreshController>();
        DestroyComponentIfPresent<EndOfRunScreenshotController>();
        DestroyComponentIfPresent<MonsterPreviewItemBoardRuntime>();
        DestroyComponentIfPresent<CardSetPreviewRuntime>();
        DestroyComponentIfPresent<MonsterPreviewWarmupController>();
        DestroyComponentIfPresent<CombatStatusBar>();
        DestroyComponentIfPresent<HistoryPanel>();
        DestroyComponentIfPresent<PlayerObservationController>();
        DestroyComponentIfPresent<RunUploadController>();
        DestroyComponentIfPresent<RunLoggingController>();
        DestroyComponentIfPresent<CombatReplayRuntime>();
    }
```

This is safe in the `CleanupFailedInitialization` path: `_composition` is null when initialisation fails before `_composition` is assigned, and `?.` handles that.

- [ ] **Step 5: Verify no `Plugin.cs` references remain to `AutoBazaarRuntime`**

```bash
grep -n "AutoBazaar" Plugin.cs
```

Expected: empty.

- [ ] **Step 6: Build Debug + Release**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
dotnet build BazaarPlusPlus.csproj -c Release
```

Expected: both builds succeed. The Debug build's `CopyToBepInExPlugins` target should still copy `BazaarPlusPlus.dll` into `$(GamePath)/BepInEx/plugins` (no change to that target). Release should produce the installer copy as before.

- [ ] **Step 7: Run AutoBazaar tests**

```bash
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj -c Debug
```

Expected: all tests pass.

- [ ] **Step 8: Toggle-off verification (compile only, no game launch required)**

Comment out the mount registration in `BppComposition.cs`:

```csharp
    // _mountables.Register(new AutoBazaarMount());
```

Rebuild:

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: build succeeds. (Spec §5.4 requires that with this line commented, no `AutoBazaarRuntime` is attached at runtime — the in-game smoke confirms that, not the compile.)

**Revert** the comment immediately so the committed state has AutoBazaar mounted:

```csharp
    _mountables.Register(new AutoBazaarMount());
```

Rebuild again to confirm clean state:

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

- [ ] **Step 9: Final commit**

```bash
git add Plugin.cs
git commit -m "Mount AutoBazaar through BppComposition.Mountables instead of Plugin.cs"
```

---

## Post-implementation checks (spec §5.5 regression checklist)

These are not separate tasks but the executor should verify them after Task 10 commits:

- **Build artefacts:** confirm Debug `BazaarPlusPlus.dll` lands in `$(GamePath)/BepInEx/plugins` (check `BazaarPlusPlus.csproj` `CopyToBepInExPlugins` target executed). No changes to that target are expected.
- **Test coverage:** confirm the full `tests/AutoBazaar.Tests/` suite passes — the spec lists each test class by name in §5.2; all of them must remain green.
- **Search for dead references:** run
  ```bash
  grep -rn "AutoBazaarInteractionFilterProbe\|AutoBazaarPedestalEligibilityProbe\|ResolveCurrentEncounterType" --include='*.cs' --exclude-dir=decompiled --exclude-dir=obj --exclude-dir=bin .
  ```
  Expected: empty.
- **In-game smoke (manual, spec §5.3):** launch the Debug build against the game, verify `endpoint.json`, `/v1/context` response, enchant encounter `interactableTemplateIds`, pedestal `CommitToPedestal` options. This is not part of the automated workflow but should be performed before declaring the work shippable. If the executing agent cannot run the game, it must say so explicitly rather than claim "verified".

---

## Order notes for the executor

- Tasks 1–7 (encounter decoupling) are independently shippable. If Task 8–10 hit unexpected friction, the encounter half can land first.
- Tasks 3, 4, 5 each move one piece independently. They can be merged together if convenient — the plan presents them as separate commits for cleaner bisect.
- After every task, the build must pass and the AutoBazaar test suite must remain green. If either fails, stop and investigate before continuing — the spec promises "zero observable change today" and any test regression breaks that promise.
