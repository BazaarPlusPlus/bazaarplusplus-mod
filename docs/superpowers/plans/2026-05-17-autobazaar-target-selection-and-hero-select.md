# AutoBazaar — Target-Selection Mode + Hero-Select Detection

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Close two known gaps in the AutoBazaar context surface so external tools can drive (1) upgrade/enchant target-selection dialogs and (2) starting a run from the hero-select lobby.

**Architecture:**
- **Target-selection mode**: when `AppState._iteractionFilter` (private static `List<string>` of templateIds) is non-empty, the game expects the player to click one of their *owned* cards whose templateId is in the filter. Currently the mod's `availableActions` doesn't expose this state. We add a new context field `InteractableTemplateIds` and emit owned-card `SelectItem` decision options for each eligible owned card, suppressing the standard offer-based `SelectItem` set during filter-mode so the script doesn't waste POSTs on cards the game silently ignores. Dispatcher path is unchanged: `SelectItem` ActionKind already routes through `AppState.CurrentState.BuyItemCommand(card, sockets, section)`, which handles owned-card upgrades via the `CanFuse()` early return.
- **Hero-select detection**: `CanStartOrContinueRun` is currently hardcoded `false`. The investigation found `SceneLoader.HeroSelectSceneName = "HeroSelectScene"` and `SceneID.HeroSelectScene`. We compute the value as a conservative AND chain: scene == HeroSelectScene, AppState.CurrentState == null, ClientCache.Profile.Value != null, ClientCache.RunConfig.Value.HasActiveRun decides between "Play new" and "Resume" but both fire `GameInstance.Instance.StartNewRun()` so we don't branch on it.

**Tech Stack:**
- Mod (`netstandard2.1` + Newtonsoft 13.0.3 + BepInEx + HarmonyLib for `AccessTools.TypeByName`)
- Tests (`net10.0` + xunit 2.9.3, restricted to pure-logic helpers that don't import game types)

---

## File structure

| File | Responsibility | Touched |
|---|---|---|
| `Game/AutoBazaar/AutoBazaarDecision.cs` | DTOs | Modify (add `InteractableTemplateIds` to `AutoBazaarContext`) |
| `Game/AutoBazaar/AutoBazaarContextSnapshot.cs` | Snapshot publisher with content-fingerprint equality | Modify (compare new field) |
| `Game/AutoBazaar/AutoBazaarTargetSelectionActions.cs` | **New, pure helper.** Given a filter set + iterable of owned cards (instanceId, templateId, section, leftSocketId, size), return a list of `AutoBazaarDecisionOption` for `SelectItem` targeting each eligible owned card. Pure C#, no game-type imports, testable in xunit. |  Create |
| `Game/AutoBazaar/AutoBazaarSceneProbe.cs` | **New, reflection-only helper.** Encapsulates the reads of `SceneLoader.ActiveScene` (compared by name), `ClientCache.Profile.Value`, `ClientCache.RunConfig.Value.HasActiveRun`. Provides one method: `bool IsAtHeroSelectAndReadyForNewRun()`. | Create |
| `Game/AutoBazaar/AutoBazaarInteractionFilterProbe.cs` | **New, reflection-only helper.** Reads `AppState._iteractionFilter` (private static `List<string>`). Provides one method: `IReadOnlyList<string> ReadCurrentFilter()`. Returns empty list when filter is empty/missing/inaccessible. | Create |
| `Game/AutoBazaar/AutoBazaarContextBuilder.cs` | Live-state → context builder | Modify (wire in 3 new helpers, populate `CanStartOrContinueRun` + `InteractableTemplateIds`, suppress offer SelectItem when filter non-empty, emit owned-card SelectItem when filter non-empty) |
| `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj` | Test project | Modify (add Compile includes for the two new pure-logic files: `AutoBazaarTargetSelectionActions.cs`) — `AutoBazaarSceneProbe.cs` and `AutoBazaarInteractionFilterProbe.cs` reference game types so they stay out of the test project |
| `tests/AutoBazaar.Tests/AutoBazaarTargetSelectionActionsTests.cs` | xunit tests for the pure helper | Create |
| `docs/reference/auto-bazaar-http-api-v1.md` | Public API spec | Modify (new section on Target-Selection Mode + remove the v1 punt note on `CanStartOrContinueRun`) |
| `docs/reference/auto-bazaar-decision-surface.md` | Internal field mapping | Modify (document the two new derivations) |

---

## Cross-cutting conventions

- `#nullable enable`, file-scoped namespace, `internal sealed`.
- All game-state access via `HarmonyLib.AccessTools` reflection (`TypeByName`, `Field`, `Method`) — matches the existing pattern in `AutoBazaarActionDispatcher.cs` (the `ClientCache.RunConfig.SetSelected*` helpers).
- Reflection lookups cached in static nullable fields on first successful access (per-tick reflection cost is real on Unity main thread).
- Errors swallowed with `BppLog.Error("AutoBazaar", "<context>", ex)` — the mod must never crash from a reflection miss when the game patches and renames something.
- Don't add tests for ContextBuilder (it imports game types — same `.rules`-permitted skip as Phase 2).
- The two probe helpers (`AutoBazaarSceneProbe`, `AutoBazaarInteractionFilterProbe`) get no tests either — they're thin reflection wrappers and a unit test would only verify "reflection works on a synthetic type", which is the mock-call-sequence anti-pattern from `.rules`.
- The pure helper `AutoBazaarTargetSelectionActions` is the only new file with a test seam; tests it gets.

---

## Phase A — Target-Selection Mode

### Task A.1: Pure helper `AutoBazaarTargetSelectionActions` + tests (TDD)

**Files:**
- Create: `Game/AutoBazaar/AutoBazaarTargetSelectionActions.cs`
- Create: `tests/AutoBazaar.Tests/AutoBazaarTargetSelectionActionsTests.cs`
- Modify: `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj` (add Compile include)

- [ ] **Step 1: Write the failing tests.**

```csharp
// tests/AutoBazaar.Tests/AutoBazaarTargetSelectionActionsTests.cs
using System.Collections.Generic;
using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarTargetSelectionActionsTests
{
    private static AutoBazaarTargetSelectionActions.OwnedCardRef Card(
        string id, string templateId, AutoBazaarTargetSection section,
        string leftSocket, int size)
        => new(id, templateId, section, leftSocket, size);

    [Fact]
    public void Emit_EmptyFilter_ReturnsEmpty()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string>(),
            new[] { Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1) });
        Assert.Empty(emit);
    }

    [Fact]
    public void Emit_FilterMatchesOneCard_SingleOption()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "t1" },
            new[]
            {
                Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1),
                Card("itm_b", "t2", AutoBazaarTargetSection.Hand, "Socket_3", 1),
            });
        Assert.Single(emit);
        var o = emit[0];
        Assert.Equal(AutoBazaarActionKind.SelectItem, o.ActionKind);
        Assert.Equal(AutoBazaarActionGroup.Offer, o.Group);
        Assert.Equal("itm_a", o.CardInstanceId);
        Assert.Equal(AutoBazaarTargetSection.Hand, o.TargetSection);
        Assert.NotNull(o.TargetSockets);
        Assert.Single(o.TargetSockets!);
        Assert.Equal("Socket_2", o.TargetSockets![0]);
        Assert.StartsWith("SelectItem:itm_a", o.DisplayKey);
    }

    [Fact]
    public void Emit_FilterMatchesMultipleCards_OnePerCard()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "t1", "t2" },
            new[]
            {
                Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1),
                Card("itm_b", "t2", AutoBazaarTargetSection.Stash, "Socket_5", 1),
                Card("itm_c", "t3", AutoBazaarTargetSection.Hand, "Socket_7", 1),
            });
        Assert.Equal(2, emit.Count);
        var ids = new HashSet<string>();
        foreach (var o in emit) ids.Add(o.CardInstanceId!);
        Assert.Contains("itm_a", ids);
        Assert.Contains("itm_b", ids);
        Assert.DoesNotContain("itm_c", ids);
    }

    [Fact]
    public void Emit_MultiCellItem_SocketsContiguousFromLeft()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "t1" },
            new[] { Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_3", 3) });
        Assert.Single(emit);
        Assert.Equal(new[] { "Socket_3", "Socket_4", "Socket_5" }, emit[0].TargetSockets);
    }

    [Fact]
    public void Emit_SkillCard_NoSocketsField()
    {
        // Skills don't sit on Hand/Stash sockets; they get Section=Skill and no socket list.
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "ts1" },
            new[] { Card("skl_a", "ts1", AutoBazaarTargetSection.Skill, leftSocket: "", size: 1) });
        Assert.Single(emit);
        Assert.Equal(AutoBazaarTargetSection.Skill, emit[0].TargetSection);
        Assert.True(emit[0].TargetSockets is null || emit[0].TargetSockets!.Count == 0);
    }

    [Fact]
    public void Emit_DuplicateTemplateInFilter_NoDuplicateEmits()
    {
        // Filter is HashSet so duplicates aren't possible, but verify the helper
        // doesn't emit the same card twice from a single matching templateId.
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "t1" },
            new[]
            {
                Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1),
                Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1),
            });
        Assert.Single(emit);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail.**

```powershell
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --nologo --filter FullyQualifiedName~AutoBazaarTargetSelectionActions
```

Expected: compile error — `AutoBazaarTargetSelectionActions` not defined.

- [ ] **Step 3: Implement the helper.**

```csharp
// Game/AutoBazaar/AutoBazaarTargetSelectionActions.cs
#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal static class AutoBazaarTargetSelectionActions
{
    /// <summary>Lightweight value snapshot of an owned card, with everything the
    /// emitter needs to build a SelectItem decision option targeting it. Decoupled
    /// from game types so the helper stays unit-testable.</summary>
    internal readonly record struct OwnedCardRef(
        string InstanceId,
        string TemplateId,
        AutoBazaarTargetSection Section,
        string LeftSocketId,
        int Size);

    /// <summary>Emit one SelectItem AutoBazaarDecisionOption per owned card whose
    /// TemplateId is in <paramref name="filter"/>. When <paramref name="filter"/>
    /// is empty, returns empty. Skill-section cards get no TargetSockets list.</summary>
    public static IReadOnlyList<AutoBazaarDecisionOption> Emit(
        IReadOnlySet<string> filter,
        IEnumerable<OwnedCardRef> ownedCards)
    {
        if (filter.Count == 0) return System.Array.Empty<AutoBazaarDecisionOption>();
        var result = new List<AutoBazaarDecisionOption>();
        var seen = new HashSet<string>();
        foreach (var c in ownedCards)
        {
            if (string.IsNullOrEmpty(c.TemplateId)) continue;
            if (!filter.Contains(c.TemplateId)) continue;
            if (!seen.Add(c.InstanceId)) continue;

            IReadOnlyList<string>? sockets = null;
            if (c.Section is AutoBazaarTargetSection.Hand or AutoBazaarTargetSection.Stash
                && !string.IsNullOrEmpty(c.LeftSocketId)
                && c.Size > 0)
            {
                var list = new string[c.Size];
                if (TryParseSocketIndex(c.LeftSocketId, out var start))
                {
                    for (var i = 0; i < c.Size; i++) list[i] = $"Socket_{start + i}";
                    sockets = list;
                }
                else
                {
                    // unexpected format — leave sockets null rather than emit garbage
                    sockets = null;
                }
            }

            var sectionSeg = c.Section.ToString();
            var socketSeg = sockets is null ? "" : $":{string.Join(",", sockets)}";
            result.Add(new AutoBazaarDecisionOption
            {
                ActionKind = AutoBazaarActionKind.SelectItem,
                Group = AutoBazaarActionGroup.Offer,
                DisplayKey = $"SelectItem:{c.InstanceId}:{sectionSeg}{socketSeg}",
                CardInstanceId = c.InstanceId,
                TargetSection = c.Section,
                TargetSockets = sockets,
            });
        }
        return result;
    }

    private static bool TryParseSocketIndex(string socketId, out int index)
    {
        index = 0;
        if (string.IsNullOrEmpty(socketId)) return false;
        const string prefix = "Socket_";
        if (!socketId.StartsWith(prefix, System.StringComparison.Ordinal)) return false;
        return int.TryParse(socketId.AsSpan(prefix.Length), out index);
    }
}
```

Note: `IReadOnlySet<string>` requires `System.Collections.Generic.IReadOnlySet<>`. On netstandard2.1 this type doesn't exist — change the parameter to `ISet<string>` (works with `HashSet<string>` and `SortedSet<string>`). If the C# compiler complains, switch.

- [ ] **Step 4: Add `<Compile>` to test csproj.**

In `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj`, inside the existing `<ItemGroup>` of `<Compile Include="..\..\Game\AutoBazaar\...">`, add:

```xml
<Compile
  Include="..\..\Game\AutoBazaar\AutoBazaarTargetSelectionActions.cs"
  Link="AutoBazaarTargetSelectionActions.cs"
/>
```

- [ ] **Step 5: Run tests to verify they pass.**

```powershell
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --nologo --filter FullyQualifiedName~AutoBazaarTargetSelectionActions
```

Expected: all 6 facts pass.

- [ ] **Step 6: Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarTargetSelectionActions.cs tests/AutoBazaar.Tests/AutoBazaarTargetSelectionActionsTests.cs tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj
git commit -m "Add AutoBazaarTargetSelectionActions emitter for upgrade target picker"
```

---

### Task A.2: Probe helper `AutoBazaarInteractionFilterProbe`

**Files:**
- Create: `Game/AutoBazaar/AutoBazaarInteractionFilterProbe.cs`

No tests — pure reflection wrapper, no clean seam.

- [ ] **Step 1: Write the probe.**

```csharp
// Game/AutoBazaar/AutoBazaarInteractionFilterProbe.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Game.AutoBazaar;

/// <summary>Reads <c>AppState._iteractionFilter</c> via reflection. When the
/// filter is non-empty, the game is in a target-selection state (upgrade,
/// enchant, etc.) and only owned cards whose templateId is in the filter are
/// accepted by <c>BuyItemCommand</c>; other clicks silently no-op.</summary>
internal static class AutoBazaarInteractionFilterProbe
{
    private static FieldInfo? _filterField;
    private static bool _resolveAttempted;

    private static readonly string[] EmptyArray = System.Array.Empty<string>();

    /// <summary>Main thread only. Returns the current filter as an immutable
    /// snapshot. Empty result means either no filter is active or reflection
    /// couldn't reach the field; callers treat both the same.</summary>
    public static IReadOnlyList<string> ReadCurrentFilter()
    {
        try
        {
            if (!_resolveAttempted)
            {
                _resolveAttempted = true;
                _filterField = AccessTools.Field(typeof(AppState), "_iteractionFilter");
                if (_filterField is null)
                {
                    BppLog.Info("AutoBazaar", "AppState._iteractionFilter field not found via reflection");
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
            BppLog.Error("AutoBazaar", "ReadCurrentFilter reflection failed", ex);
            return EmptyArray;
        }
    }
}
```

- [ ] **Step 2: Build.**

```powershell
dotnet build BazaarPlusPlus.csproj -nologo -clp:NoSummary -v:quiet
```

Expected: success. (`MSB3061` post-build copy is environmental — see note in `docs/superpowers/plans/2026-05-17-autobazaar-http-endpoint.md`.)

- [ ] **Step 3: Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarInteractionFilterProbe.cs
git commit -m "Add AutoBazaarInteractionFilterProbe reflection helper"
```

---

### Task A.3: `AutoBazaarContext` gains `InteractableTemplateIds` field

**Files:**
- Modify: `Game/AutoBazaar/AutoBazaarDecision.cs`
- Modify: `Game/AutoBazaar/AutoBazaarContextSnapshot.cs`

- [ ] **Step 1: Add the field on `AutoBazaarContext`.**

Open `Game/AutoBazaar/AutoBazaarDecision.cs`. Inside `internal sealed class AutoBazaarContext`, after the existing `ActionCooldownRemainingSeconds` property and before the `BoardItems` line, add:

```csharp
/// <summary>Template IDs the game currently restricts player clicks to (target-selection mode: upgrade, enchant, etc.). Empty/null means no filter is active. When non-empty, only owned cards whose templateId is in this set will accept a SelectItem POST; offer-based SelectItem actions are suppressed from <see cref="AvailableActions"/>.</summary>
public IReadOnlyList<string>? InteractableTemplateIds { get; init; }
```

- [ ] **Step 2: Add field to snapshot equality.**

Open `Game/AutoBazaar/AutoBazaarContextSnapshot.cs`. Inside `EqualsIgnoreTimeAndTick(AutoBazaarContext a, AutoBazaarContext b)`, add a line near the other scalar comparisons (before the card-list comparisons):

```csharp
if (!StringListEqual(a.InteractableTemplateIds, b.InteractableTemplateIds)) return false;
```

Inside `CloneWithTickId`, add to the new-context initializer:

```csharp
InteractableTemplateIds = src.InteractableTemplateIds,
```

If `StringListEqual` doesn't already exist as a helper (the existing equality uses `SocketsEqual`), add it as a private static at the bottom of the publisher:

```csharp
private static bool StringListEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
{
    if (ReferenceEquals(a, b)) return true;
    if (a is null || b is null) return (a?.Count ?? 0) == (b?.Count ?? 0);
    if (a.Count != b.Count) return false;
    for (var i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
    return true;
}
```

- [ ] **Step 3: Build.**

```powershell
dotnet build BazaarPlusPlus.csproj -nologo -clp:NoSummary -v:quiet
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --nologo
```

Expected: success. Existing snapshot tests still pass.

- [ ] **Step 4: Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarDecision.cs Game/AutoBazaar/AutoBazaarContextSnapshot.cs
git commit -m "Add AutoBazaarContext.InteractableTemplateIds for target-selection mode"
```

---

### Task A.4: Wire filter + emit owned-card SelectItem into ContextBuilder

**Files:**
- Modify: `Game/AutoBazaar/AutoBazaarContextBuilder.cs`

- [ ] **Step 1: Read the filter and stash it in the context.**

Inside `AutoBazaarContextBuilder.Build` (or `BuildCore` — whichever holds the main body), add — near where the scalar fields are populated, before assembling `AvailableActions`:

```csharp
var filterList = AutoBazaarInteractionFilterProbe.ReadCurrentFilter();
IReadOnlySet<string>? filterSet = null;
if (filterList.Count > 0)
{
    filterSet = new HashSet<string>(filterList);
}
```

(Use `ISet<string>` if `IReadOnlySet<>` isn't available on netstandard2.1.)

Then in the `AutoBazaarContext { ... }` initializer add:

```csharp
InteractableTemplateIds = filterList.Count > 0 ? filterList : null,
```

- [ ] **Step 2: Build the `ownedCardRefs` collection.**

In the same `Build` method, where you already enumerate hand / stash / skills, also produce a parallel `List<AutoBazaarTargetSelectionActions.OwnedCardRef>` (one record per owned card across all three locations). For each card:

```csharp
ownedCardRefs.Add(new AutoBazaarTargetSelectionActions.OwnedCardRef(
    InstanceId: card.InstanceId.Value,
    TemplateId: card.TemplateId.ToString(),
    Section: section,   // Hand / Stash / Skill — matches the section you assigned to the snapshot
    LeftSocketId: card.LeftSocketId.ToString() ?? "",
    Size: (int)card.Size));
```

(Use the canonical names from the existing builder code — `card.InstanceId`, `card.TemplateId`, `card.LeftSocketId`, `card.Size`. The skill section uses `LeftSocketId = ""` since skills aren't socketed in Hand/Stash.)

- [ ] **Step 3: Suppress offer SelectItem when filter is active; emit owned-card SelectItem.**

In the same builder, find where you build the `availableActions` list. Currently you append `SelectItem` entries for each item in `selectionOptions` (offers). Wrap that block:

```csharp
if (filterSet is null)
{
    // existing offer-based SelectItem emission unchanged
    foreach (var offer in selectionOptions /* whatever the local name is */)
    {
        // existing per-offer placement enumeration → SelectItem options
    }
}
else
{
    // Target-selection mode: offers are clicks-to-nowhere. Emit owned-card SelectItem
    // for each card whose template is in the filter.
    var targetOpts = AutoBazaarTargetSelectionActions.Emit(filterSet, ownedCardRefs);
    foreach (var opt in targetOpts) availableActions.Add(opt);
}
```

The other action emissions (Wait, AbandonRun, Reroll, ExitState, AdvanceEndRun, SellItem, MoveItem, SelectSkill, SelectEncounter, CommitToPedestal) stay unchanged — they're still valid actions during target-selection mode.

- [ ] **Step 4: Build + run tests.**

```powershell
dotnet build BazaarPlusPlus.csproj -nologo -clp:NoSummary -v:quiet
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --nologo
```

Expected: success; all prior tests still pass.

- [ ] **Step 5: Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarContextBuilder.cs
git commit -m "Surface target-selection mode in AutoBazaarContextBuilder"
```

---

## Phase B — Hero-Select Detection

### Task B.1: Scene + ready-for-new-run probe

**Files:**
- Create: `Game/AutoBazaar/AutoBazaarSceneProbe.cs`

No tests — pure reflection wrapper.

- [ ] **Step 1: Write the probe.**

```csharp
// Game/AutoBazaar/AutoBazaarSceneProbe.cs
#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using TheBazaar;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.Game.AutoBazaar;

/// <summary>Determines whether the game is sitting on the hero-select lobby
/// with no active session, i.e. it is safe to invoke
/// <c>GameInstance.Instance.StartNewRun()</c> via the dispatcher. All checks
/// are conservative — a false negative just means the action isn't offered
/// this tick, while a false positive would let StartNewRun fire when
/// <c>NetworkManager.IsInitialized</c> is true and the call would be rejected
/// server-side.</summary>
internal static class AutoBazaarSceneProbe
{
    private const string HeroSelectSceneName = "HeroSelectScene";

    // Cached reflection handles
    private static FieldInfo? _clientCacheProfileField;
    private static FieldInfo? _clientCacheRunConfigField;
    private static PropertyInfo? _profileValueProp;
    private static PropertyInfo? _runConfigValueProp;
    private static PropertyInfo? _hasActiveRunProp;
    private static bool _reflectionAttempted;

    public static bool IsAtHeroSelectAndReadyForNewRun()
    {
        try
        {
            // 1. Unity active scene name
            var sceneName = SceneManager.GetActiveScene().name;
            if (!string.Equals(sceneName, HeroSelectSceneName, StringComparison.Ordinal)) return false;

            // 2. No active AppState (no run in progress on client)
            if (AppState.CurrentState != null) return false;

            // 3. Profile loaded — required by StartNewRun
            if (!TryReadProfileLoaded()) return false;

            // (4) We deliberately DON'T require !HasActiveRun: a player resuming a
            //     run from the lobby still clicks the same Play button, which calls
            //     StartNewRun(); the server figures out new-vs-resume from the
            //     PlayerProfileResponse.ActiveRun field. So `CanStartOrContinueRun`
            //     should be true in both "fresh run" and "resume run" lobby states.
            return true;
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", "IsAtHeroSelectAndReadyForNewRun failed", ex);
            return false;
        }
    }

    private static bool TryReadProfileLoaded()
    {
        if (!_reflectionAttempted)
        {
            _reflectionAttempted = true;
            var clientCacheType = AccessTools.TypeByName("TheBazaar.ClientCache");
            if (clientCacheType is null) return false;
            _clientCacheProfileField = clientCacheType.GetField("Profile", BindingFlags.Static | BindingFlags.Public);
            _clientCacheRunConfigField = clientCacheType.GetField("RunConfig", BindingFlags.Static | BindingFlags.Public);
            if (_clientCacheProfileField is not null)
            {
                _profileValueProp = _clientCacheProfileField.FieldType.GetProperty("Value");
            }
            if (_clientCacheRunConfigField is not null)
            {
                _runConfigValueProp = _clientCacheRunConfigField.FieldType.GetProperty("Value");
                if (_runConfigValueProp is not null)
                {
                    _hasActiveRunProp = _runConfigValueProp.PropertyType.GetProperty("HasActiveRun");
                }
            }
        }
        if (_clientCacheProfileField is null || _profileValueProp is null) return false;
        var profileCache = _clientCacheProfileField.GetValue(null);
        if (profileCache is null) return false;
        var profile = _profileValueProp.GetValue(profileCache);
        return profile is not null;
    }
}
```

- [ ] **Step 2: Build.**

```powershell
dotnet build BazaarPlusPlus.csproj -nologo -clp:NoSummary -v:quiet
```

Expected: success. (`UnityEngine.SceneManagement` is already referenced by the project per the existing `using UnityEngine;` usage.)

- [ ] **Step 3: Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarSceneProbe.cs
git commit -m "Add AutoBazaarSceneProbe for hero-select detection"
```

---

### Task B.2: Wire `CanStartOrContinueRun` in ContextBuilder

**Files:**
- Modify: `Game/AutoBazaar/AutoBazaarContextBuilder.cs`

- [ ] **Step 1: Replace the hardcoded `false`.**

Find the line in `AutoBazaarContextBuilder.Build` that sets `CanStartOrContinueRun`. It currently reads (paraphrased):

```csharp
// v1 punt: scene detection deferred
CanStartOrContinueRun = false,
```

Replace it with:

```csharp
CanStartOrContinueRun = AutoBazaarSceneProbe.IsAtHeroSelectAndReadyForNewRun(),
```

- [ ] **Step 2: Build + run tests.**

```powershell
dotnet build BazaarPlusPlus.csproj -nologo -clp:NoSummary -v:quiet
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --nologo
```

Expected: success; existing tests pass (snapshot equality doesn't care about CanStartOrContinueRun since the field is already in the equality comparison — confirm by reading `AutoBazaarContextSnapshot.cs`).

- [ ] **Step 3: Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarContextBuilder.cs
git commit -m "Compute CanStartOrContinueRun from scene + ready-for-new-run probe"
```

---

## Phase C — Verification + Docs

### Task C.1: Update API spec

**Files:**
- Modify: `docs/reference/auto-bazaar-http-api-v1.md`

- [ ] **Step 1: Add a "Target-selection mode" section.**

Open the doc. Find the section that describes the `AutoBazaarContext` body fields, and add after the `actionCooldownRemainingSeconds` row:

```markdown
| `interactableTemplateIds` | `string[]?` (omitted when empty) | When present and non-empty, the game is in a target-selection state (upgrade / enchant). Clients should pick from `availableActions` entries of kind `SelectItem` whose `cardInstanceId` belongs to one of the player's owned cards (Hand / Stash / Skills) whose templateId is in this set. The mod suppresses offer-based `SelectItem` options in this mode because they silently no-op server-side. Other actions (`Wait`, `ExitState`, `MoveItem`, `SellItem`, etc.) remain valid where allowed by the state. |
```

Add a new top-level section above "Cooldown gate":

```markdown
### Target-selection mode

The game shows an upgrade / enchant dialog by populating an internal interaction-filter set with the templateIds of cards eligible to be the target of the upgrade. While that filter is non-empty:

- The mod sets `context.interactableTemplateIds` to the filter contents.
- The mod removes offer-based `SelectItem` decision options from `availableActions` (they hit a silent-no-op on the game's `CanInteractWithCard` check).
- For each of the player's owned cards (Hand / Stash / Skills) whose templateId is in the filter, the mod emits a `SelectItem` `AutoBazaarDecisionOption` targeting that card. The `targetSection` and `targetSockets` correspond to the card's current placement so the dispatcher's `AppState.BuyItemCommand` walks the `CanFuse()` path and the server applies the upgrade.

Client recipe: when `interactableTemplateIds` is present, pick the first matching `SelectItem` action from `availableActions` and POST it verbatim. If no SelectItem options are present despite a non-empty filter, none of your owned cards match — bail with `ExitState` (where allowed) or `AbandonRun`.
```

- [ ] **Step 2: Remove the v1-punt note on `CanStartOrContinueRun`.**

Search the doc for "punted" or "v1 limitation" or "scene detection" near `CanStartOrContinueRun`. Remove those caveats. Replace with:

```markdown
`canStartOrContinueRun` is true when the game is on the hero-select scene, no run-state is active, and the player profile has loaded. It covers both fresh-run and resume-run cases — the server distinguishes via the player profile.
```

- [ ] **Step 3: Commit.**

```powershell
git add docs/reference/auto-bazaar-http-api-v1.md
git commit -m "Document AutoBazaar target-selection mode and hero-select availability"
```

---

### Task C.2: Update decision-surface doc

**Files:**
- Modify: `docs/reference/auto-bazaar-decision-surface.md`

- [ ] **Step 1: Add the two new derivation entries.**

Inside the top-level scalar mapping table, add:

```markdown
| `interactableTemplateIds` | `AutoBazaarInteractionFilterProbe.ReadCurrentFilter()` — reflection on `AppState._iteractionFilter`. Null/omitted when the list is empty or reflection fails. |
| `canStartOrContinueRun` | `AutoBazaarSceneProbe.IsAtHeroSelectAndReadyForNewRun()` — true iff active scene == `HeroSelectScene` AND `AppState.CurrentState == null` AND `ClientCache.Profile.Value != null`. |
```

In the AvailableActions derivation section, add:

```markdown
- **Target-selection mode**: when `interactableTemplateIds` is non-empty, offer-based `SelectItem` options are suppressed and the builder emits one `SelectItem` `AutoBazaarDecisionOption` per owned card whose templateId is in the filter, using the card's current `Section` + `LeftSocketId` + `Size` to fill `targetSection` and `targetSockets`.
```

- [ ] **Step 2: Commit.**

```powershell
git add docs/reference/auto-bazaar-decision-surface.md
git commit -m "Document target-selection and hero-select derivations"
```

---

### Task C.3: Build + manual smoke

**Files:** none (manual).

- [ ] **Step 1: Final build.**

```powershell
dotnet build BazaarPlusPlus.csproj -nologo -clp:NoSummary -v:quiet
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --nologo
```

Expected: success on both. If post-build copy fails because the game is running, manually copy `bin/Debug/netstandard2.1/BazaarPlusPlus.dll` → `<game>/BepInEx/plugins/BazaarPlusPlus.dll` (the source DLL isn't locked because BepInEx shadow-copies it on load).

- [ ] **Step 2: Restart The Bazaar.**

Quit any running instance, relaunch.

- [ ] **Step 3: Hero-select smoke.**

At the main menu / hero-select screen:

```powershell
curl http://127.0.0.1:47900/v1/context | python -c "import json,sys; ctx=json.load(sys.stdin); print('canStartOrContinueRun=', ctx['canStartOrContinueRun'], 'state=', ctx['stateName'])"
```

Expected: `canStartOrContinueRun=True state=Unknown`.

POST a start:

```powershell
curl -X POST -H "Content-Type: application/json" -d '{"actionKind":"StartOrContinueRun","hero":"Karnok","playMode":"Unranked"}' http://127.0.0.1:47900/v1/actions
```

Expected: HTTP 200 + `executed:true`. The game should advance into a run (state becomes `StartRun` or `Choice`).

- [ ] **Step 4: Target-selection smoke.**

Play / fast-forward until the game shows an upgrade or enchant dialog (typically a story encounter that asks you to choose one of your owned items). Then:

```powershell
curl http://127.0.0.1:47900/v1/context | python -c "
import json, sys
ctx = json.load(sys.stdin)
print('interactableTemplateIds=', ctx.get('interactableTemplateIds'))
selitem_avail = [a for a in ctx['availableActions'] if a['actionKind']=='SelectItem']
print('SelectItem options:', len(selitem_avail))
for a in selitem_avail[:5]: print(' ', a['cardInstanceId'], a.get('targetSection'), a.get('targetSockets'))"
```

Expected: `interactableTemplateIds` is a non-empty array; `SelectItem options` is non-zero; each option's `cardInstanceId` corresponds to an owned card (matches an entry in `boardItems` / `chestItems` / `playerSkills`).

POST the first option:

```powershell
TICK=$(curl -s http://127.0.0.1:47900/v1/context | python -c "import json,sys; print(json.load(sys.stdin)['tickId'])")
# Manually substitute fields from the SelectItem output above
curl -X POST -H "Content-Type: application/json" -d "{\"actionKind\":\"SelectItem\",\"cardInstanceId\":\"<id>\",\"targetSection\":\"<section>\",\"targetSockets\":[\"<sockets>\"],\"forTickId\":$TICK}" http://127.0.0.1:47900/v1/actions
```

Expected: HTTP 200 + `executed:true`. Within ~1s, `interactableTemplateIds` disappears from context and game advances. UI animations should play normally (we're routing through `AppState.BuyItemCommand`).

- [ ] **Step 5: If both smoke tests pass, commit a note + ship.**

```powershell
git log --oneline -10
git push origin master  # if previously authorized
```

If either smoke test fails, file a follow-up: capture the gap-dump JSON from `C:/Users/cauyx/AppData/Local/Temp/autobazaar_gap_dumps/` and post-mortem before shipping.

---

## Self-review

Walked the spec end-to-end. Items verified:

1. **Spec coverage:**
   - Target-selection mode surface (`InteractableTemplateIds`, owned-card SelectItem actions, offer suppression) → Tasks A.1–A.4. ✓
   - Hero-select detection (`CanStartOrContinueRun`) → Tasks B.1–B.2. ✓
   - Public + internal docs updated → Tasks C.1–C.2. ✓
   - Smoke verification → Task C.3. ✓

2. **Placeholder scan:** every code block contains compilable code; no TBD, no "add appropriate". The "v1 punt" prose only appears as text to *remove* in C.1. ✓

3. **Type consistency:**
   - `AutoBazaarTargetSelectionActions.OwnedCardRef` matches the ContextBuilder's field selection in A.4 (InstanceId/TemplateId/Section/LeftSocketId/Size). ✓
   - `AutoBazaarInteractionFilterProbe.ReadCurrentFilter()` returns `IReadOnlyList<string>`, consumed by A.4 which casts to `HashSet<string>`. ✓
   - `AutoBazaarSceneProbe.IsAtHeroSelectAndReadyForNewRun()` returns `bool`, consumed by B.2 directly. ✓
   - `InteractableTemplateIds` field declared in A.3 as `IReadOnlyList<string>?`, populated in A.4 as `filterList.Count > 0 ? filterList : null`. ✓
   - Snapshot equality (A.3) uses `StringListEqual` helper added in same task. ✓

4. **`.rules` compliance:**
   - No `BuildAll` chain for code-only changes (tasks use targeted builds). ✓
   - No source-text tests, no mock-call-sequence tests. The one new test file exercises the pure helper's behavior via constructed inputs and asserts on output structure, not source-text. ✓
   - `decompiled/` not touched. ✓
   - `BazaarPlusPlus.csproj` copy flow not touched. ✓
   - Test project pattern (`<Compile Include="..\..\Game\AutoBazaar\..." Link="..." />`) followed. ✓
   - Docs minimal — additive only, no rewrites. ✓

---

## Risks / things to watch

- **`AppState._iteractionFilter` name stability**: the field name has a typo (`iteractionFilter` not `interactionFilter`). If the developers fix the typo in a future patch, the probe's reflection will miss and `interactableTemplateIds` will be silently empty. The probe logs an Info line on first miss — keep an eye on BepInEx logs after game updates.
- **Skill section sockets**: skills don't have meaningful `LeftSocketId` in Hand/Stash terms. The emitter handles this by leaving `TargetSockets` null; verify in the smoke test that selecting a skill target works.
- **`IReadOnlySet<string>`**: not available on netstandard2.1. Use `ISet<string>` in the helper signature; both `HashSet<string>` and `SortedSet<string>` implement it.
- **Resume-vs-new-run**: the recommendation is to leave `CanStartOrContinueRun = true` for both cases. If smoke testing reveals that calling `StartNewRun` while a server-side active run exists has a bad failure mode (rather than just the server returning the in-progress state), narrow the probe to also require `HasActiveRun == false` — but the investigation suggests the same code path handles both.

*End of plan.*
