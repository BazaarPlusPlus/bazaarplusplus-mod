> **Status: IMPLEMENTED (historical).** Durable decision promoted to [ADR-0004](../../adr/0004-preview-visibility-three-state-mode.md); living feature: [tooltip-preview.md](../../features/tooltip-preview.md).

# Pedestal-Aware Enchant / Upgrade Preview Display

**Status:** Draft for implementation planning
**Date:** 2026-05-24
**Owner:** BazaarPlusPlus mod

## 1. Background

### 1.1 Current display behaviour

The mod renders two tooltip previews on item cards:

- **Enchant preview** — appends post-enchant tooltip segments to hand / stash / opponent-board items. Driven by [ItemEnchantPreviewPatch.cs:54-57](../../../Patches/Tooltips/ItemEnchantPreviewPatch.cs:54). Currently shown whenever `EnchantPreviewAlwaysShow` (bool, default `true`) is set **or** the `HoldEnchantPreview` hotkey (default Ctrl) is held. In-combat tooltips are excluded by [ItemEnchantPreviewEligibility.cs](../../../Game/ItemEnchantPreview/ItemEnchantPreviewEligibility.cs).
- **Upgrade preview** — flips the tooltip to the upgraded variant. Driven by [UpgradePreviewTooltipPatch.cs:35](../../../Patches/Tooltips/UpgradePreviewTooltipPatch.cs:35). Currently shown **only** while the `HoldUpgradePreview` hotkey (default Shift) is held. There is no always-show flag.

Both flow through a single switching point: [TooltipModifierRefreshController.GetCurrentMode()](../../../Game/Tooltips/TooltipModifierRefreshController.cs), which returns `Upgrade | Enchant | Normal` once per frame and triggers a tooltip re-render when the mode changes.

The complaint with today's behaviour: enchant text is shown by default on every eligible tooltip, even when no encounter is asking the player to enchant anything. Upgrade preview is invisible unless the player remembers Shift. Neither surface reflects whether the preview is actually relevant *right now*.

### 1.2 What the game tells us before the player commits

The relevant signal already exists. From the decompiled game source:

- [`ChoiceState`](../../../decompiled/TheBazaarRuntime/TheBazaar/ChoiceState.cs:5) is the `RunAppState` while the player is on the map looking at offered encounters. `StateOps.SelectEncounter` is one of the allowed ops.
- [`RunState.SelectionSet`](../../../decompiled/BazaarGameClient/BazaarGameClient.Domain.Models/RunState.cs) is a `List<string>` of encounter template GUIDs currently being offered. Written by `DataExtensions.Update(RunState, RunStateSnapshotDTO)` and overwritten on every server state push.
- [`Data.GetStatic().GetCardById(Guid)`](../../../decompiled/TheBazaarRuntime/TheBazaar/Data.cs:269) returns the `ITCard` template synchronously (the `Task` is already resolved once `IsManagerCreated()` is true).
- A `TCardEncounterPedestal.Behavior` of [`TPedestalBehaviorUpgrade`](../../../decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Encounter.Pedestal.Behaviors/TPedestalBehaviorUpgrade.cs) means the offered pedestal is an upgrade pedestal; [`TPedestalBehaviorEnchant`](../../../decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Encounter.Pedestal.Behaviors/TPedestalBehaviorEnchant.cs) or [`TPedestalBehaviorEnchantRandom`](../../../decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Encounter.Pedestal.Behaviors/TPedestalBehaviorEnchantRandom.cs) means an enchant pedestal. `TPedestalBehaviorTransform` is neither and is irrelevant to this feature.

The mod already has the abstraction for surfacing this data: [EncounterStateProbe](../../../Game/Encounter/EncounterStateProbe.cs) returns an [EncounterStateSnapshot](../../../Core/GameState/EncounterStateSnapshot.cs) every frame, holding things like `CurrentEncounterType` and `InteractionFilterTemplateIds`. Adding one more field to that snapshot is the natural extension point.

### 1.3 Constraint from the game design

A single `SelectionSet` never contains both an upgrade pedestal and an enchant pedestal at the same time. At most one pedestal kind is offered per choice screen. The design encodes this as a plain enum, not flags.

## 2. Goals

1. **Auto-show previews exactly when the player is deciding whether to take a relevant pedestal.** Hovering an inventory item while a pedestal is on the choice screen reveals the matching preview without the player holding Ctrl or Shift.
2. **Give the player a single, configurable visibility mode per preview kind.** Three values: `Off`, `AutoOnPedestalChoice`, `Always`. Default `AutoOnPedestalChoice`.
3. **Hold-key remains the manual override in every mode.** Ctrl and Shift always win.
4. **No new event plumbing.** The change rides the existing per-frame mode comparison in `TooltipModifierRefreshController.Update()`.
5. **No UI patches.** Detection is a probe + state-machine read, not a hook into Unity scene objects.
6. **Backward-compatible config migration.** Users who deliberately disabled `EnchantPreviewAlwaysShow` get the new auto behaviour; users who left it on (the default) get the explicit `Always` mode and see no change.

## 3. Non-Goals

- Auto-show while the player is **inside** a pedestal (`PedestalState`). The user-confirmed scope is pre-entry only; hold-key still works inside the pedestal.
- Auto-show for `TPedestalBehaviorTransform` pedestals. Neither enchant nor upgrade preview applies to transforms.
- Per-item filtering inside the preview path (e.g., "only show the preview for items the pedestal would accept"). That is a separate, larger change against `ItemEnchantPreviewService` / `UpgradePreviewTooltipPatch`. Hover-on-item behaviour is unchanged here.
- Hooking encounter-card hover on the map UI (`EncounterPickerMapController`, `EncounterController`). Trying to scope auto-show to "hovering the pedestal node specifically" adds Unity scene fragility for very little extra precision; `ChoiceState` already lines up with the picker being visible.
- Reading `Data.CurrentState.CurrentEncounterId`. That field is only populated once the encounter is entered, which is too late.

## 4. Design

### 4.1 Component map

```
Core/Config/
  IBppConfig.cs                       ← add: EnchantPreviewModeConfig, UpgradePreviewModeConfig;
                                        remove: EnchantPreviewAlwaysShowConfig
  BppConfig.cs                        ← bind enum keys; migration from old bool
  PreviewVisibilityMode.cs            ← NEW enum: Off | AutoOnPedestalChoice | Always

Core/GameState/
  EncounterStateSnapshot.cs           ← add: ChoiceScreenPedestalKind field
  IEncounterStateProbe.cs             ← (no contract change; snapshot grows)

Game/Encounter/
  ChoiceScreenPedestalKind.cs         ← NEW enum: None | Upgrade | Enchant
  ChoiceScreenPedestalResolver.cs     ← NEW pure helper
  EncounterStateProbe.cs              ← populate ChoiceScreenPedestalKind in GetCurrent()

Game/Tooltips/
  TooltipModifierRefreshController.cs ← rewrite GetCurrentMode() priority

Game/Settings/
  BppSettingsDockCatalog.cs           ← swap enchant always-show toggle for mode dropdown;
                                        add upgrade mode dropdown
  EnchantPreview.SettingsMenuLabel.cs ← labels for the three enchant modes
  UpgradePreview.SettingsMenuLabel.cs ← NEW: labels for the three upgrade modes
```

No new folders. No new module classes. No new `IBppFeature` registrations.

### 4.2 Config surface and migration

New enum in `Core/Config/PreviewVisibilityMode.cs`:

```csharp
public enum PreviewVisibilityMode {
    Off,                  // hold-key only
    AutoOnPedestalChoice, // auto-show while a matching pedestal is on ChoiceState; hold-key otherwise
    Always,               // append to every eligible tooltip (legacy enchant default)
}
```

Two new entries on `IBppConfig`, both as `ConfigEntry<PreviewVisibilityMode>?` (mirroring the existing `ChineseLocaleModeConfig` enum binding):

| Field | BepInEx section / key | Default | Notes |
|---|---|---|---|
| `EnchantPreviewModeConfig` | `EnchantPreview` / `Mode` | `AutoOnPedestalChoice` | Replaces `EnchantPreviewAlwaysShowConfig`. |
| `UpgradePreviewModeConfig` | `UpgradePreview` / `Mode` | `AutoOnPedestalChoice` | No prior config; brand new. |

The old `EnchantPreviewAlwaysShowConfig` is **removed from `IBppConfig`**. Hotkey path entries (`EnchantPreviewHotkeyPathConfig`, `UpgradePreviewHotkeyPathConfig`) are untouched.

**Migration** runs once on `BppConfig.Initialize(ConfigFile)`, after binding the new entries:

1. Probe `config.OrphanedEntries` for `[EnchantPreview] AlwaysShow`. Orphaned-entry presence is the unambiguous "this user upgraded from a pre-spec build" signal — no sentinel field needed.
2. If present, parse the orphaned value as `bool`:
   - `true` → assign `EnchantPreviewModeConfig.Value = PreviewVisibilityMode.Always`.
   - `false` → leave the new entry at its default (`AutoOnPedestalChoice`).
3. Remove the entry from `OrphanedEntries` and call `config.Save()`. From this launch onward the old key is gone from the file, so step 1 returns negative on every subsequent run — the migration is **idempotent**.
4. Log a single info line summarising whether a migration ran and what value was written.

Fresh installs hit none of the migration paths and just see the default `AutoOnPedestalChoice` for both previews.

### 4.3 Encounter detection

#### Enum

`Game/Encounter/ChoiceScreenPedestalKind.cs`:

```csharp
public enum ChoiceScreenPedestalKind { None, Upgrade, Enchant }
```

Plain enum, not flags — see §1.3.

#### Pure resolver

`Game/Encounter/ChoiceScreenPedestalResolver.cs`:

```csharp
public static class ChoiceScreenPedestalResolver {
    public static ChoiceScreenPedestalKind Resolve(
        IReadOnlyList<string>? selectionSet,
        Func<Guid, ITCard?> templateLookup)
    {
        if (selectionSet == null || selectionSet.Count == 0) return ChoiceScreenPedestalKind.None;
        foreach (var id in selectionSet) {
            if (!Guid.TryParse(id, out var guid)) continue;
            if (templateLookup(guid) is not TCardEncounterPedestal pedestal) continue;
            var kind = ClassifyBehavior(pedestal.Behavior);
            if (kind != ChoiceScreenPedestalKind.None) return kind;
        }
        return ChoiceScreenPedestalKind.None;
    }

    private static ChoiceScreenPedestalKind ClassifyBehavior(ITPedestalBehavior behavior) => behavior switch {
        TPedestalBehaviorUpgrade       => ChoiceScreenPedestalKind.Upgrade,
        TPedestalBehaviorEnchant       => ChoiceScreenPedestalKind.Enchant,
        TPedestalBehaviorEnchantRandom => ChoiceScreenPedestalKind.Enchant,
        _                              => ChoiceScreenPedestalKind.None,
    };
}
```

Pure function. No static `Data.GetStatic()` reference. Takes the lookup as a delegate so it's unit-testable in isolation.

#### Probe integration

`EncounterStateProbe.GetCurrent()` gets one additional step: compute `ChoiceScreenPedestalKind` and stamp it into the snapshot. With a reference-identity cache:

```csharp
private IReadOnlyList<string>? _cachedSelectionSet;
private ChoiceScreenPedestalKind _cachedKind;

private ChoiceScreenPedestalKind GetChoiceScreenPedestalKind() {
    if (AppState.CurrentState is not ChoiceState) {
        _cachedSelectionSet = null;
        return ChoiceScreenPedestalKind.None;
    }
    var set = Data.CurrentState?.SelectionSet;
    if (ReferenceEquals(set, _cachedSelectionSet)) return _cachedKind;
    if (!Data.IsManagerCreated()) return ChoiceScreenPedestalKind.None;

    _cachedSelectionSet = set;
    var manager = Data.GetStatic().GetAwaiter().GetResult(); // already-resolved per Data.cs:269
    _cachedKind = ChoiceScreenPedestalResolver.Resolve(set, manager.GetCardById);
    return _cachedKind;
}
```

`SelectionSet` is rebound (not mutated) by `DataExtensions.Update` on every server state push, so reference identity is a sufficient invalidation key — no per-entry hashing.

`EncounterStateSnapshot` gains one record field:

```csharp
public ChoiceScreenPedestalKind ChoiceScreenPedestalKind { get; init; }
```

Existing snapshot consumers are unaffected.

### 4.4 `GetCurrentMode()` rewrite

[TooltipModifierRefreshController.GetCurrentMode()](../../../Game/Tooltips/TooltipModifierRefreshController.cs:47) returns the controller-private `TooltipModifierMode` enum (`Normal | Enchant | Upgrade`). The enum itself is unchanged. The body is replaced with an explicit, four-tier priority:

```csharp
private TooltipModifierMode GetCurrentMode()
{
    // Tier 1: hold-key (manual override always wins).
    if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview)) return TooltipModifierMode.Upgrade;
    if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview)) return TooltipModifierMode.Enchant;

    var upgradeMode = _config?.UpgradePreviewModeConfig?.Value ?? PreviewVisibilityMode.AutoOnPedestalChoice;
    var enchantMode = _config?.EnchantPreviewModeConfig?.Value ?? PreviewVisibilityMode.AutoOnPedestalChoice;

    // Tier 2: Always. If both configured Always, upgrade wins the exclusive-mode tie-break.
    if (upgradeMode == PreviewVisibilityMode.Always) return TooltipModifierMode.Upgrade;
    if (enchantMode == PreviewVisibilityMode.Always) return TooltipModifierMode.Enchant;

    // Tier 3: AutoOnPedestalChoice. Game guarantees at most one pedestal kind in SelectionSet.
    if (upgradeMode == PreviewVisibilityMode.AutoOnPedestalChoice
        || enchantMode == PreviewVisibilityMode.AutoOnPedestalChoice)
    {
        var kind = _encounterState?.GetCurrent().ChoiceScreenPedestalKind ?? ChoiceScreenPedestalKind.None;
        if (kind == ChoiceScreenPedestalKind.Upgrade && upgradeMode == PreviewVisibilityMode.AutoOnPedestalChoice)
            return TooltipModifierMode.Upgrade;
        if (kind == ChoiceScreenPedestalKind.Enchant && enchantMode == PreviewVisibilityMode.AutoOnPedestalChoice)
            return TooltipModifierMode.Enchant;
    }

    // Tier 4: nothing applies.
    return TooltipModifierMode.Normal;
}
```

`Initialize()` gains a second argument:

```csharp
internal void Initialize(IBppConfig config, IEncounterStateProbe encounterState)
{
    _config = config ?? throw new ArgumentNullException(nameof(config));
    _encounterState = encounterState ?? throw new ArgumentNullException(nameof(encounterState));
}
```

The caller — currently `Plugin.AttachRuntimeComponents` — passes `services.EncounterState` alongside the existing `services.Config`. Both are already exposed on `IBppServices`.

The surrounding `Update()` loop, mode-change detection, and `TryRefreshCurrentItemTooltip()` machinery are untouched. The change in classification flows through the existing refresh path the same way Ctrl / Shift do today.

The existing eligibility filter inside [ItemEnchantPreviewService](../../../Game/ItemEnchantPreview/ItemEnchantPreviewService.cs) (in-combat exclusion, hand / stash / opponent-board scope) and the `card.CanCardUpgrade()` precondition in [UpgradePreviewTooltipPatch](../../../Patches/Tooltips/UpgradePreviewTooltipPatch.cs) both still apply. Auto-mode only flips the *mode* bit — it does not bypass per-item gating.

### 4.5 Settings dock

`BppSettingsDockCatalog`:

- The existing **Enchant Preview: always show** toggle is replaced with an **Enchant Preview** 3-state dropdown bound to `EnchantPreviewModeConfig`.
- A new **Upgrade Preview** 3-state dropdown is added, bound to `UpgradePreviewModeConfig`.
- Both rows are localised through label files following the existing dock pattern (`EnchantPreview.SettingsMenuLabel.cs` already exists; add a parallel `UpgradePreview.SettingsMenuLabel.cs`).

The dropdown values are localised with three label keys per dropdown (`Off`, `AutoOnPedestalChoice`, `Always`). English / Chinese variants follow the existing dock convention.

## 5. Behaviour matrix

`U` = `UpgradePreviewMode`. `E` = `EnchantPreviewMode`. `Kind` = `ChoiceScreenPedestalKind`. `Held` = hotkey currently pressed.

| Held | U | E | Kind | Result |
|---|---|---|---|---|
| Shift | * | * | * | Upgrade |
| Ctrl | * | * | * | Enchant |
| — | Always | * | * | Upgrade |
| — | not Always | Always | * | Enchant |
| — | Auto | Auto | Upgrade | Upgrade |
| — | Auto | Auto | Enchant | Enchant |
| — | Auto | Auto | None | Normal |
| — | Auto | Off | Upgrade | Upgrade |
| — | Auto | Off | Enchant | Normal |
| — | Off | Auto | Enchant | Enchant |
| — | Off | Auto | Upgrade | Normal |
| — | Off | Off | * | Normal |

## 6. Edge cases

1. **`ChoiceState` with empty / null `SelectionSet`** → `kind = None`. Treated identically to "not in `ChoiceState`."
2. **Inside the pedestal (`PedestalState`)** → not `ChoiceState`, `kind = None`. Per-user scope; hold-key still works.
3. **Game data manager not initialised** (`Data.IsManagerCreated() == false`) → return `None` without calling `GetStatic()`. Avoids sync-over-async deadlock risk at startup.
4. **Template ID not parseable as Guid** → skip that entry; other entries still classified.
5. **`GetCardById` returns null** → skip that entry. Game data may briefly lag a server update; under-trigger rather than crash.
6. **Transform pedestal in `SelectionSet`** → ignored (resolver returns `None` for that entry; other entries may still classify).
7. **Mixed `SelectionSet` (e.g., upgrade pedestal + combat encounter)** → result is `Upgrade`. Non-pedestal entries are inert.
8. **Combat tooltips with `EnchantPreviewMode = Always`** → existing eligibility filter still excludes in-combat tooltips. No regression.
9. **Both modes configured `Always`** → `Upgrade` wins (tier-2 tie-break). Hold Ctrl to flip to Enchant.

## 7. Testing

The repo convention is `tests/<feature>.Tests/Program.cs` — a console runner with no test framework dependency.

**New** `tests/ChoiceScreenPedestalResolver.Tests/`:

- Empty / null SelectionSet → `None`.
- Single `TPedestalBehaviorUpgrade` template → `Upgrade`.
- Single `TPedestalBehaviorEnchant` template → `Enchant`.
- Single `TPedestalBehaviorEnchantRandom` template → `Enchant`.
- Single `TPedestalBehaviorTransform` template → `None`.
- Non-Guid string entries → skipped.
- Lookup returns null → skipped.
- Mixed: pedestal + combat → pedestal's kind.
- Two pedestals of the same kind (theoretical) → first match's kind.

The resolver takes `Func<Guid, ITCard?>` so the test passes a fake lookup. No game runtime, no static `Data.GetStatic()` dependency.

**No test for `GetCurrentMode()`.** Per project rules, "tests whose primary purpose is asserting mock call sequences" are out of scope. The controller is tightly coupled to static hotkey state and the encounter snapshot. §5's matrix is the spec; the resolver tests cover the part with a clean seam.

**No test for the config migration.** One-shot string parsing against a BepInEx file. Manual smoke-test on a copy of an existing user config is sufficient.

## 8. Verification plan

1. `dotnet build` the mod project — verify the new enum, snapshot field, resolver, and `GetCurrentMode` compile.
2. Run the new `ChoiceScreenPedestalResolver.Tests` console runner; expect all cases green.
3. In-game smoke test on a single run:
   - Enter `ChoiceState` with a known upgrade pedestal offered → hover a hand item → expect the upgrade preview without holding Shift. Move off the choice screen → expect the preview to disappear.
   - Repeat for an enchant pedestal → expect the enchant preview without holding Ctrl.
   - Pick a non-pedestal node → expect previews suppressed.
   - Hold Ctrl while inside `PedestalState` → expect enchant preview to still appear (hold-key escape hatch intact).
4. Flip `EnchantPreviewMode` to `Always` → expect legacy behaviour (preview on every eligible tooltip outside combat).
5. Flip both modes to `Off` → expect hold-key as the only trigger.
6. Sanity-check the migration on a copy of an existing user `BepInEx/config/com.bazaarplusplus.cfg`:
   - File with `[EnchantPreview] AlwaysShow = true` → after launch, file has `[EnchantPreview] Mode = Always` and the `AlwaysShow` line is gone. Second launch: no migration log line.
   - File with `[EnchantPreview] AlwaysShow = false` → after launch, file has `[EnchantPreview] Mode = AutoOnPedestalChoice` and the `AlwaysShow` line is gone.
   - Fresh file (no `[EnchantPreview]` section) → after launch, both new keys appear at default; no migration log line.

## 9. Out-of-scope follow-ups

- Per-item filtering inside the preview path (only show preview for items in `InteractionFilterTemplateIds` / `PedestalEligibleInstanceIds`). The data is already in the snapshot; this design intentionally does not consume it.
- A separate "next encounter on the map node" tracker. The game does not expose a future-encounter API; if one becomes available, the resolver signature is already general enough to consume it.
