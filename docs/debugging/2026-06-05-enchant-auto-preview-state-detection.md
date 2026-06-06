# Enchant Auto Preview State Detection Debugging

**Date:** 2026-06-05
**Commit:** `5d1560e Fix auto enchant preview state detection`
**Status:** Implemented and runtime-validated by user

## Summary

The enchant tooltip preview auto mode was not wrong because the enchant catalog
missed the pedestal templates. The failures came from incomplete state coverage:
the game exposes enchant opportunities through several run states, while the mod
initially treated only `ChoiceState` as the source of pedestal choices.

The final fix makes the auto-preview pedestal probe read all observed selection
surfaces and the active pedestal surface:

- `ChoiceState`, `EncounterState`, and `LevelUpState` `SelectionSet` values are
  now treated as selection surfaces.
- `PedestalState` uses the active `CurrentEncounterId` as the pedestal template
  when `SelectionSet` is empty.
- `ShopForecast` logs now expand selection entries and include runtime template
  and pedestal catalog classification, making future state-surface misses visible
  from `LogOutput.log`.

## User-Visible Symptoms

The reported symptom was: in `AutoOnPedestalChoice` mode, some screens showed an
obvious enchant opportunity, but item tooltips did not append enchant-preview
text.

The tooltip path only appends enchant preview text after the mode policy resolves
to `TooltipPreviewMode.Enchant`. The mode policy returns enchant in auto mode
only when the current pedestal snapshot is classified as `Enchant`
([`Game/Tooltips/TooltipPreviewModePolicy.cs:35`](../../src/BazaarPlusPlus/Game/Tooltips/TooltipPreviewModePolicy.cs#L35),
[`Game/Tooltips/TooltipPreviewModePolicy.cs:41`](../../src/BazaarPlusPlus/Game/Tooltips/TooltipPreviewModePolicy.cs#L41)).
The tooltip patch then builds restricted preview segments from that snapshot
([`Patches/Tooltips/ItemEnchantPreviewPatch.cs:76`](../../src/BazaarPlusPlus/Patches/Tooltips/ItemEnchantPreviewPatch.cs#L76),
[`Patches/Tooltips/ItemEnchantPreviewPatch.cs:83`](../../src/BazaarPlusPlus/Patches/Tooltips/ItemEnchantPreviewPatch.cs#L83),
[`Patches/Tooltips/ItemEnchantPreviewPatch.cs:88`](../../src/BazaarPlusPlus/Patches/Tooltips/ItemEnchantPreviewPatch.cs#L88)).

So every miss reduced to the same question: did
`IEncounterStateProbe.GetChoicePedestal()` return an enchant pedestal snapshot
for the actual game state?

## Problem 1: Futura Uses `EncounterState` Selection Entries

### Evidence

The first failing Futura screen showed two enchant pedestal options, but the run
state was `Encounter`, not `Choice`. The diagnostic log captured this shape:

```text
[BPP][ShopForecast] snapshot state=Encounter encounter=aef5e7d8-ea0b-4e7e-8f3d-6e7ba492fa10 selection=2 ... internal='Futura'
[BPP][ShopForecast]   selection[0] state=Encounter id=ped_ZASLvpi ... internal='Guardian's Gorge (Level Up)') pedestalCatalog=Enchant(enchant=Shielded)
[BPP][ShopForecast]   selection[1] state=Encounter id=ped_6uGPqtW ... internal='Languid Dunes (Level Up)') pedestalCatalog=Enchant(enchant=Heavy)
```

Before this fix, `EncounterStateProbe.GetChoicePedestal()` gated on
`IsChoiceState`, so an enchant pedestal offered through `EncounterState` was
discarded before classification.

### Fix

`EncounterIdsSnapshot` now distinguishes "is literally ChoiceState" from "has a
selection surface":
[`Core/GameState/EncounterIdsSnapshot.cs:12`](../../src/BazaarPlusPlus/Core/GameState/EncounterIdsSnapshot.cs#L12),
[`Core/GameState/EncounterIdsSnapshot.cs:13`](../../src/BazaarPlusPlus/Core/GameState/EncounterIdsSnapshot.cs#L13).

`EncounterStateProbe.ReadEncounterIds()` now treats `EncounterState` as a
selection state and resolves each `SelectionSet` entry to stable template ids
([`GameInterop/Encounter/EncounterStateProbe.cs:83`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L83),
[`GameInterop/Encounter/EncounterStateProbe.cs:84`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L84),
[`GameInterop/Encounter/EncounterStateProbe.cs:104`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L104)).

## Problem 2: Active `PedestalState` Has No `SelectionSet`

### Evidence

After choosing an enchant pedestal, the game transitions into `PedestalState`.
The runtime log showed that the active encounter was still an enchant pedestal,
but `selection=0`:

```text
[BPP][ShopForecast] snapshot state=Pedestal encounter=4df2e5f8-1a7f-4046-b374-8f1642b24751 selection=0 ... internal='Guardian's Gorge (Level Up)') pedestalCatalog=Enchant(enchant=Shielded)
```

The decompiled game source explains why this state has a different shape:
`PedestalState.StateName` is `ERunState.Pedestal`
([`decompiled/TheBazaarRuntime/TheBazaar/PedestalState.cs:27`](../../decompiled/TheBazaarRuntime/TheBazaar/PedestalState.cs#L27)),
and `OnEnter()` reads the active pedestal template from
`Data.CurrentEncounterId`, not from `SelectionSet`
([`decompiled/TheBazaarRuntime/TheBazaar/PedestalState.cs:33`](../../decompiled/TheBazaarRuntime/TheBazaar/PedestalState.cs#L33),
[`decompiled/TheBazaarRuntime/TheBazaar/PedestalState.cs:39`](../../decompiled/TheBazaarRuntime/TheBazaar/PedestalState.cs#L39)).

### Fix

When `GetChoicePedestal()` is not on a selection surface, it now checks whether
the app is in `PedestalState` and whether the current encounter template id is
available. If so, it classifies that single template id through the same
pedestal resolver used for selection entries
([`GameInterop/Encounter/EncounterStateProbe.cs:40`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L40),
[`GameInterop/Encounter/EncounterStateProbe.cs:43`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L43),
[`GameInterop/Encounter/EncounterStateProbe.cs:46`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L46)).

Upgrade pedestals stay safe under this path: the resolver returns `Upgrade`, and
the enchant mode policy still only auto-shows when the kind is `Enchant`
([`Game/Tooltips/TooltipPreviewModePolicy.cs:41`](../../src/BazaarPlusPlus/Game/Tooltips/TooltipPreviewModePolicy.cs#L41)).

## Problem 3: Level-Up Pedestal Choices Use `LevelUpState`

### Evidence

After the first two fixes, the next failing case was not Futura. The fresh
runtime log showed the player on `LevelUp` with two selection entries:

```text
[BPP][ShopForecast] snapshot state=LevelUp encounter=(none) selection=2 cost=- remaining=-
[BPP][ShopForecast]   selection[0] state=LevelUp id=ped_LiMiPF8 ... internal='Bladeborn Badlands (Level Up)') pedestalCatalog=Enchant(enchant=Deadly)
[BPP][ShopForecast]   selection[1] state=LevelUp id=ped_MeZSRWJ ... internal='Sirocco Steppe (Level Up)') pedestalCatalog=Enchant(enchant=Turbo)
```

The current `LogOutput.log` has the same evidence at
`/Users/yxinyu/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log:72`.

The decompiled state also confirms that `LevelUpState` is a real app state with
`StateName => ERunState.LevelUp`, and it allows `SelectEncounter`
([`decompiled/TheBazaarRuntime/TheBazaar/LevelUpState.cs:8`](../../decompiled/TheBazaarRuntime/TheBazaar/LevelUpState.cs#L8),
[`decompiled/TheBazaarRuntime/TheBazaar/LevelUpState.cs:10`](../../decompiled/TheBazaarRuntime/TheBazaar/LevelUpState.cs#L10)).
`DataExtensions.Update(RunState, SimUpdateRunState)` copies
`snapshot.SelectionSet` directly into the client run state
([`decompiled/TheBazaarRuntime/TheBazaar/DataExtensions.cs:75`](../../decompiled/TheBazaarRuntime/TheBazaar/DataExtensions.cs#L75),
[`decompiled/TheBazaarRuntime/TheBazaar/DataExtensions.cs:81`](../../decompiled/TheBazaarRuntime/TheBazaar/DataExtensions.cs#L81)).

### Fix

`EncounterStateProbe.ReadEncounterIds()` now includes `LevelUpState` in
`isSelectionState`:
[`GameInterop/Encounter/EncounterStateProbe.cs:83`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L83),
[`GameInterop/Encounter/EncounterStateProbe.cs:84`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L84).

This lets the same selection-entry resolver classify level-up enchant pedestal
offers without adding a separate level-up code path.

## Problem 4: Logs Did Not Show Enough State-Surface Detail

### Evidence

Before this debugging session, `ShopForecastLogPatch` logged the active snapshot
and only expanded choice options. That made it hard to prove whether a missing
preview was caused by:

- no selection entries,
- unresolved live instance ids,
- wrong app state,
- an upgrade pedestal,
- an unknown pedestal template, or
- a tooltip refresh/rendering issue.

### Fix

`ShopForecastLogPatch` now expands selection entries for `Choice`, `Encounter`,
and `LevelUp`
([`Patches/ShopForecast/ShopForecastLogPatch.cs:37`](../../src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs#L37),
[`Patches/ShopForecast/ShopForecastLogPatch.cs:39`](../../src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs#L39)).

Each entry is enriched from the live runtime entity or static template:
[`Patches/ShopForecast/ShopForecastLogPatch.cs:67`](../../src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs#L67),
[`Patches/ShopForecast/ShopForecastLogPatch.cs:109`](../../src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs#L109),
[`Patches/ShopForecast/ShopForecastLogPatch.cs:126`](../../src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs#L126).

Pedestal templates are classified through `PedestalEnchantCatalog`, so logs now
show `pedestalCatalog=Enchant(enchant=...)`, `Upgrade(enchant=none)`, or no
pedestal catalog marker:
[`Patches/ShopForecast/ShopForecastLogPatch.cs:154`](../../src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs#L154),
[`Patches/ShopForecast/ShopForecastLogPatch.cs:157`](../../src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs#L157).

This logging is what exposed the final `LevelUpState` miss.

## Final Behavior

`GetChoicePedestal()` now has two input shapes:

1. Selection surface: classify all resolved template ids from
   `ChoiceState` / `EncounterState` / `LevelUpState` `SelectionSet`
   ([`GameInterop/Encounter/EncounterStateProbe.cs:55`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L55)).
2. Active pedestal surface: if already in `PedestalState`, classify
   `CurrentEncounterTemplateId`
   ([`GameInterop/Encounter/EncounterStateProbe.cs:43`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L43)).

Both shapes produce the same `ChoicePedestalSnapshot` via
`CreateChoicePedestalSnapshot`
([`GameInterop/Encounter/EncounterStateProbe.cs:58`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L58),
[`GameInterop/Encounter/EncounterStateProbe.cs:190`](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L190)).

The user confirmed the runtime behavior after rebuilding and restarting the
game.

## Verification

Commands run before committing `5d1560e`:

```bash
git diff --check
dotnet run --project tests/ChoiceScreenPedestalResolver.Tests/ChoiceScreenPedestalResolver.Tests.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Results:

- `ChoiceScreenPedestalResolver checks passed.`
- `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` completed with `0 Warning(s)` and
  `0 Error(s)`.
- Debug build copied `BazaarPlusPlus.dll` into the BepInEx plugins folder.

## Review Notes

- The fix intentionally keeps the structured pedestal logic in
  `GameInterop/Encounter`, because it reads game runtime state and classifies
  stable template ids. The tooltip policy remains a consumer of the resulting
  `ChoicePedestalSnapshot`.
- `ShopForecast` diagnostic logging is now more verbose. It is useful while
  validating state coverage; it can be demoted or narrowed later if release log
  volume becomes a problem.
- A separate refresh nuance remains outside this fix: if an already-open tooltip
  stays visible while the offered enchant type changes but the mode remains
  `Enchant`, the current refresh controller only tracks pedestal kind, not the
  specific enchant type. That was not the observed failing path after the final
  runtime validation.
