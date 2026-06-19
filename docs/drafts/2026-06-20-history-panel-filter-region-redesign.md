# History Panel filter-region redesign + richer Runs/Ghost filters — implementation plan

Date: 2026-06-20
Status: plan, ready to implement (user implements, then hands back for review)
Scope: `src/BazaarPlusPlus/Game/HistoryPanel/` only. Left core (Runs/Battles lists + preview) NOT touched.

All file paths below are under `src/BazaarPlusPlus/Game/HistoryPanel/`.

---

## 1. Goal & confirmed decisions

Three things, all inside the right operation rail:

1. **Filter region → fixed top slot.** Move the mode tabs (Runs/Ghost) + the Ghost result filter out
   of the scrolling `railScroll` into a new non-scrolling `_filterSlot` pinned directly under the
   subtitle (Collection's `primaryControlsRow` prior-art, `Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:75-135`).
   Resolves the two rail instabilities: filter floating with `selectedDetailCard` (①) and scroll-content
   resize on mode toggle (②).
2. **New filters.**
   - **Runs mode:** filter by hero — 7 three-letter codes (`VAN PYG DOO MAK JUL KAR STE`), **single-select**
     (click the active code to clear; none selected = all), hero-colored when active.
   - **Ghost mode:** an extra independent toggle **Day ≥ 10** (includes day 10), AND-combined with the
     existing All/Won/Lost result filter.
3. **Detail card → more compact:** drop the redundant "当前战斗/Selected" title, stop the card from
   stretching (`flexGrow:0`), remove the bottom spacer, move the day pill to the right of the first row,
   tighten spacing.

Not touched this round: left core columns + preview, status banner, action buttons, overview chips
(stay in `railScroll`), and the Runs↔Ghost left-core reflow (③, accepted as-is).

### Layout shape (target rail, top → bottom)

```
titleRow (Title + Close)                       fixed   [unchanged]
subtitle (supporters)                          fixed   [unchanged]
_filterSlot  (NEW, fixed, non-scrolling card)  fixed
  tabsRow:  [Runs] [Ghost]                              (both modes, constant)
  Row B (one row, ButtonCompactHeight, NoWrap) — exactly one shown per mode, same height:
    _runsFilterRow : [VAN][PYG][DOO][MAK][JUL][KAR][STE]   (Runs mode)
    _ghostFilterRow: [全部][赢][输]   [≥10天]               (Ghost mode)
railBody (flexGrow:1):
  selectedDetailCard  (flexGrow:0 now — hugs content, title removed)
  railScroll (flexGrow:1): overviewGroup (count/battle/db chips + health button)  <- only scrolling content
statusLabel                                    fixed   [unchanged]
actions (replay/record/delete)                 fixed   [unchanged]
```

Both Row B variants are a single `ButtonCompactHeight` row → `_filterSlot` height is identical in both
modes → no jump. The slot is a fixed sibling above `railScroll`, so toggling its contents never resizes
scroll content and never floats with the detail card.

---

## 2. Core mechanic: Runs gets the same "filtered list + index" machinery Ghost already has

Today only Ghost battles are filtered. `SelectedGhostBattleIndex` indexes into the **filtered** list
(`FilteredGhostBattles`), recomputed lazily via a dirty flag (`HistoryPanelCoordinator.GetFilteredGhostBattles`,
`HistoryPanelCoordinator.cs:591`; invalidated by `InvalidateFilteredGhostBattles`, `:672`).

Mirror this exactly for Runs. **The hard rule: after this change, `SelectedRunIndex` indexes into
`FilteredRuns`, NOT `Runs`.** Every consumer of the run index must go through `FilteredRuns`. The four
consumers to convert (any miss = wrong run selected):

- `HistoryPanelCoordinator.RefreshData` clamp (`:123-127`)
- `HistoryPanelCoordinator.SelectRun` guard (`:209`)
- `HistoryPanelCoordinator.GetSelectedRun` (private, `:631-636`)
- `HistoryPanelState.GetSelectedRun()` (consumed by `HistoryPanel.SelectedRun`, `HistoryPanel.cs:52`)
- view `itemsSource` + count chip (BuildUiModel)

---

## 3. Per-file changes

### 3.1 `HistoryPanelState.cs` — new state + filtered-run accessor

Add fields (next to the Ghost equivalents):

```csharp
public string? SelectedRunHero { get; set; }            // null = all heroes
public bool GhostDayMin10 { get; set; }                 // Ghost "Day >= 10" toggle
public List<HistoryRunRecord> FilteredRuns { get; } = new();
public bool FilteredRunsDirty { get; set; } = true;
```

Replace the existing no-arg `GetSelectedRun()` with the filtered overload (mirror
`GetSelectedGhostBattle`):

```csharp
// was: public HistoryRunRecord? GetSelectedRun() => SafeIndex(Runs, SelectedRunIndex);
public HistoryRunRecord? GetSelectedRun(IReadOnlyList<HistoryRunRecord> filteredRuns) =>
    SafeIndex(filteredRuns, SelectedRunIndex);
```

The only caller of the no-arg version is `HistoryPanel.cs:52` (updated in 3.6). Confirm no other callers
(`grep -rn "\.GetSelectedRun()" Game/HistoryPanel`).

### 3.2 `HistoryPanelRunHeroFilter.cs` — NEW (sibling of `HistoryPanelGhostBattleFilter.cs`)

```csharp
#nullable enable
using System;
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelRunHeroFilter
{
    // null/empty selectedHero = show all. Compares against the raw HistoryRunRecord.Hero name.
    public static bool Matches(string? selectedHero, HistoryRunRecord run)
    {
        if (string.IsNullOrEmpty(selectedHero))
            return true;
        return string.Equals(run.Hero, selectedHero, StringComparison.OrdinalIgnoreCase);
    }
}
```

### 3.3 `HistoryPanelGhostBattleFilter.cs` — add a day-aware overload

Keep the existing `Matches(GhostBattleFilter, HistoryBattleRecord)` (still used + reflected by tests).
Add:

```csharp
public static bool Matches(GhostBattleFilter filter, bool dayMin10, HistoryBattleRecord battle)
{
    if (!Matches(filter, battle))
        return false;
    if (dayMin10 && (battle.Day ?? int.MinValue) < 10)   // null Day fails the >=10 test (hidden when toggle on)
        return false;
    return true;
}
```

### 3.4 `HistoryPanelCoordinator.cs`

**`GetFilteredRuns()` (new, mirror `GetFilteredGhostBattles` `:591`):**

```csharp
public IReadOnlyList<HistoryRunRecord> GetFilteredRuns()
{
    if (!_state.FilteredRunsDirty)
        return _state.FilteredRuns;

    _state.FilteredRuns.Clear();
    foreach (var run in _state.Runs)
        if (HistoryPanelRunHeroFilter.Matches(_state.SelectedRunHero, run))
            _state.FilteredRuns.Add(run);

    _state.FilteredRunsDirty = false;
    return _state.FilteredRuns;
}

private void InvalidateFilteredRuns() => _state.FilteredRunsDirty = true;
```

**`GetSelectedRun()` (private `:631`) → use filtered list:**

```csharp
private HistoryRunRecord? GetSelectedRun()
{
    var filtered = GetFilteredRuns();
    return filtered.Count == 0
        ? null
        : filtered[Mathf.Clamp(_state.SelectedRunIndex, 0, filtered.Count - 1)];
}
```

**`RefreshData()` (`:122-128`) — invalidate + clamp against filtered:**

```csharp
_state.Runs.AddRange(runs);
InvalidateFilteredRuns();
_state.SelectedRunIndex = Mathf.Clamp(
    _state.SelectedRunIndex, 0, Mathf.Max(0, GetFilteredRuns().Count - 1));
LoadBattlesForSelectedRun();
```

(The hero filter `SelectedRunHero` persists across `RefreshData`, so re-entering Runs mode keeps the
filter applied — desired.)

**`SelectRun(int index)` (`:207`) — guard against filtered count:**

```csharp
public void SelectRun(int index)
{
    var filtered = GetFilteredRuns();
    if (index < 0 || index >= filtered.Count)
        return;
    if (_state.SelectedRunIndex != index)
        ClearDeleteRunConfirmation();
    _state.SelectedRunIndex = index;
    LoadBattlesForSelectedRun();
    _state.PreviewSelectionMode = PreviewSelectionMode.Run;
    _requestUiRefresh();
    _requestPreviewRefresh();
}
```

**`SetRunHeroFilter(string hero)` (new, toggle; mirror `SetGhostBattleFilter` `:190`):**

```csharp
public void SetRunHeroFilter(string hero)
{
    // single-select toggle: clicking the active hero clears the filter
    var next = string.Equals(_state.SelectedRunHero, hero, StringComparison.OrdinalIgnoreCase)
        ? null
        : hero;
    _state.SelectedRunHero = next;
    InvalidateFilteredRuns();
    _state.SelectedRunIndex = 0;          // select the first run of the newly-filtered set
    ClearDeleteRunConfirmation();
    LoadBattlesForSelectedRun();          // selected run changed -> reload its battles
    _state.PreviewSelectionMode = PreviewSelectionMode.Run;
    _requestUiRefresh();
    _requestPreviewRefresh();
}
```

**`SetGhostDayMin10(bool)` + `ToggleGhostDayMin10()` (new; mirror `SetGhostBattleFilter`):**

```csharp
public void ToggleGhostDayMin10() => SetGhostDayMin10(!_state.GhostDayMin10);

public void SetGhostDayMin10(bool value)
{
    if (_state.GhostDayMin10 == value)
        return;
    _state.GhostDayMin10 = value;
    InvalidateFilteredGhostBattles();
    _state.SelectedGhostBattleIndex = Mathf.Clamp(
        _state.SelectedGhostBattleIndex, 0, Mathf.Max(0, GetFilteredGhostBattles().Count - 1));
    _state.PreviewSelectionMode = PreviewSelectionMode.Battle;
    _requestUiRefresh();
    _requestPreviewRefresh();
}
```

**`GetFilteredGhostBattles()` (`:597-601`) — pass the day flag:**

```csharp
if (HistoryPanelGhostBattleFilter.Matches(_state.GhostBattleFilter, _state.GhostDayMin10, battle))
    _state.FilteredGhostBattles.Add(battle);
```

### 3.5 `HistoryPanelController.cs` — thin wrappers (next to `SetGhostBattleFilter` `:27`)

```csharp
private void SetRunHero(string hero) => _coordinator?.SetRunHeroFilter(hero);
private void ToggleGhostDayMin10() => _coordinator?.ToggleGhostDayMin10();
```

### 3.6 `HistoryPanel.cs`

Add a `FilteredRuns` property (next to `FilteredGhostBattles` `:62`) and route `SelectedRun` through it:

```csharp
private IReadOnlyList<HistoryRunRecord> FilteredRuns =>
    _coordinator?.GetFilteredRuns() ?? _state.FilteredRuns;

// was: private HistoryRunRecord? SelectedRun => _state.GetSelectedRun();
private HistoryRunRecord? SelectedRun => _state.GetSelectedRun(FilteredRuns);
```

### 3.7 `HistoryPanel.UiToolkit.cs`

**`EnsureUi` — pass two new callbacks** (constructor arg order must match 3.8):

```csharp
_uiView = new HistoryPanelUiToolkitView(
    transform,
    () => SetHistoryVisible(false),
    () => TryReplaySelectedBattle(false),
    () => TryReplaySelectedBattle(true),
    TryDeleteSelectedRun,
    TryCheckServerHealth,
    SelectRun,
    SelectBattle,
    SetSectionMode,
    SetGhostBattleFilter,
    SetRunHero,           // NEW  Action<string>
    ToggleGhostDayMin10   // NEW  Action
);
```

**`BuildUiModel`:**
- runs list source → filtered:
  ```csharp
  var visibleRuns = FilteredRuns.ToList();
  ```
- `Runs = visibleRuns`
- count chip for Runs mode → filtered count:
  ```csharp
  CountChipText = _sectionMode == HistorySectionMode.Ghost
      ? HistoryPanelText.CountGhost(FilteredGhostBattles.Count)
      : HistoryPanelText.CountRuns(FilteredRuns.Count),   // was _runs.Count
  ```
  (BattleChipText for Runs stays `_battles.Count`.)
- new model fields:
  ```csharp
  SelectedRunHero = _state.SelectedRunHero,
  GhostDayMin10 = _state.GhostDayMin10,
  ```
- `SelectedRunIndex` stays `_selectedRunIndex` (now interpreted in filtered space; the view clamps via
  `model.Runs.Count`).

**`HistoryPanelUiToolkitModel` class** — add:
```csharp
public string? SelectedRunHero { get; set; }
public bool GhostDayMin10 { get; set; }
```

### 3.8 `HistoryPanelUiToolkitView.cs`

- Constructor: add `Action<string> setRunHero, Action toggleGhostDayMin10` (after `setGhostFilter`),
  store in fields `_setRunHero` / `_toggleGhostDayMin10` with null-checks (match existing style).
- New fields:
  ```csharp
  private VisualElement? _filterSlot;
  private VisualElement? _runsFilterRow;
  private Button[] _heroChips = Array.Empty<Button>();    // parallel to HeroRoster
  private Button? _ghostDayButton;
  private ScrollView? _railScrollView;
  private HistorySectionMode? _lastSectionMode;           // for scroll reset on mode change
  ```
- Static hero roster (order = display order; codes from `GetHeroBadgeStyle`):
  ```csharp
  private static readonly string[] HeroRoster =
      { "Vanessa", "Pygmalien", "Dooley", "Mak", "Jules", "Karnok", "Stelle" };
  ```
- **`Refresh`:**
  - Delete the `_detailTitle` line (`:209`).
  - Hero chips: `for (var i = 0; i < _heroChips.Length; i++) RefreshHeroChip(_heroChips[i], HeroRoster[i], string.Equals(model.SelectedRunHero, HeroRoster[i], StringComparison.OrdinalIgnoreCase));`
  - Day toggle: `_ghostDayButton!.text = HistoryPanelText.FilterDayMin10(); RefreshGhostFilterButton(_ghostDayButton, model.GhostDayMin10);`
  - Row B per-mode visibility (REPLACES the old `_ghostFilterRow` display logic at `:249-250`):
    ```csharp
    var isGhost = model.SectionMode == HistorySectionMode.Ghost;
    _runsFilterRow!.style.display = isGhost ? DisplayStyle.None : DisplayStyle.Flex;
    _ghostFilterRow!.style.display = isGhost ? DisplayStyle.Flex : DisplayStyle.None;
    ```
  - Scroll reset on mode change:
    ```csharp
    if (_lastSectionMode != model.SectionMode)
    {
        _railScrollView!.scrollOffset = Vector2.zero;
        _lastSectionMode = model.SectionMode;
    }
    ```
  - Keep the existing left-core toggles (`_runsSection.display`, `_battlesSection.marginLeft`,
    `_battlesTitle.display` at `:200-205`) UNCHANGED — left core is out of scope.

### 3.9 `HistoryPanelUiToolkitView.Tree.cs`

**`BuildOperationRail`:** call `BuildFilterSlot(rail)` between `rail.Add(_subtitle)` (`:186`) and the
`railBody` creation (`:191`).

**New `BuildFilterSlot(VisualElement rail)`** (move tab + ghost-filter construction here; the old
`navGroup` block at `:347-410` is deleted):

```csharp
private void BuildFilterSlot(VisualElement rail)
{
    _filterSlot = new VisualElement();
    _filterSlot.style.flexDirection = FlexDirection.Column;
    _filterSlot.style.flexShrink = 0f;
    _filterSlot.style.marginTop = UiSpacing.Xl;
    _filterSlot.style.backgroundColor = Colors.HistorySectionBackground;  // reads as one fixed region
    UiStyle.Radius(_filterSlot.style, Radii.Md);
    UiStyle.Padding(_filterSlot.style, UiSpacing.Md);
    rail.Add(_filterSlot);

    // Row A: mode tabs (moved from old navGroup; keep fixedWidth:false for CJK 对局/幽灵)
    var tabsRow = new VisualElement();
    tabsRow.style.flexDirection = FlexDirection.Row;
    tabsRow.style.flexWrap = Wrap.NoWrap;
    tabsRow.style.alignItems = Align.Center;
    _filterSlot.Add(tabsRow);
    _runsTabButton = CreateButton(HistoryPanelText.RunsTab(),
        () => _setSectionMode(HistorySectionMode.Runs), 0f, Sizes.ButtonStandardHeight, fixedWidth: false);
    _ghostTabButton = CreateButton(HistoryPanelText.GhostTab(),
        () => _setSectionMode(HistorySectionMode.Ghost), 0f, Sizes.ButtonStandardHeight, fixedWidth: false);
    tabsRow.Add(_runsTabButton);
    _ghostTabButton.style.marginLeft = UiSpacing.Sm;
    tabsRow.Add(_ghostTabButton);

    // Row B — Runs: hero chips (single row, NoWrap)
    _runsFilterRow = new VisualElement();
    _runsFilterRow.style.flexDirection = FlexDirection.Row;
    _runsFilterRow.style.flexWrap = Wrap.NoWrap;
    _runsFilterRow.style.alignItems = Align.Center;
    _runsFilterRow.style.marginTop = UiSpacing.Sm;
    _runsFilterRow.style.display = DisplayStyle.None;
    _filterSlot.Add(_runsFilterRow);
    _heroChips = new Button[HeroRoster.Length];
    for (var i = 0; i < HeroRoster.Length; i++)
    {
        var hero = HeroRoster[i];
        var chip = CreateButton(GetHeroBadgeStyle(hero).ShortCode,
            () => _setRunHero(hero), 0f, Sizes.ButtonCompactHeight, fixedWidth: false);
        if (i > 0) chip.style.marginLeft = UiSpacing.Xs;
        _runsFilterRow.Add(chip);
        _heroChips[i] = chip;
    }

    // Row B — Ghost: result segment + day toggle (single row, NoWrap)
    _ghostFilterRow = new VisualElement();
    _ghostFilterRow.style.flexDirection = FlexDirection.Row;
    _ghostFilterRow.style.flexWrap = Wrap.NoWrap;
    _ghostFilterRow.style.alignItems = Align.Center;
    _ghostFilterRow.style.marginTop = UiSpacing.Sm;
    _ghostFilterRow.style.display = DisplayStyle.None;
    _filterSlot.Add(_ghostFilterRow);
    _ghostAllButton = CreateButton(HistoryPanelText.FilterAll(),
        () => _setGhostFilter(GhostBattleFilter.All), 0f, Sizes.ButtonCompactHeight, fixedWidth: false);
    _ghostWonButton = CreateButton(HistoryPanelText.FilterIWon(),
        () => _setGhostFilter(GhostBattleFilter.IWon), 0f, Sizes.ButtonCompactHeight, fixedWidth: false);
    _ghostLostButton = CreateButton(HistoryPanelText.FilterILost(),
        () => _setGhostFilter(GhostBattleFilter.ILost), 0f, Sizes.ButtonCompactHeight, fixedWidth: false);
    _ghostDayButton = CreateButton(HistoryPanelText.FilterDayMin10(),
        () => _toggleGhostDayMin10(), 0f, Sizes.ButtonCompactHeight, fixedWidth: false);
    _ghostFilterRow.Add(_ghostAllButton);
    _ghostWonButton.style.marginLeft = UiSpacing.Xs;  _ghostFilterRow.Add(_ghostWonButton);
    _ghostLostButton.style.marginLeft = UiSpacing.Xs; _ghostFilterRow.Add(_ghostLostButton);
    _ghostDayButton.style.marginLeft = UiSpacing.Md;  // visual gap separating the independent day axis
    _ghostFilterRow.Add(_ghostDayButton);
}
```

**`railScroll` capture:** change `var railScroll = new ScrollView(...)` (`:298`) to assign the new field
`_railScrollView = new ScrollView(...)` and use `_railScrollView` thereafter (so `Refresh` can reset its
offset).

**Detail card compaction** (in the existing detail-card block `:200-295`):
- Delete `_detailTitle` creation (`:212-214`) and remove the `_detailTitle` field.
- `selectedDetailCard.style.flexGrow = 0f;` and `selectedDetailCard.style.flexShrink = 0f;` (were 1f).
- Delete the `detailFlex` spacer (`:273-275`).
- Reorder `resultRow` children to `[resultPill] [opponentName] [dayPill]` and give the name flex so the
  day pill sits at the right: create `_opponentName` before `_dayPill`, set
  `_opponentName.style.flexGrow = 1f;` (keep its `minWidth=0` / `overflow=Hidden`).
- Tighten: `UiStyle.Padding(selectedDetailCard.style, UiSpacing.Lg)` (was Xl); `_detailMeta.marginTop =
  UiSpacing.Xs` (was Sm).

### 3.10 Hero-chip styling helper (`HistoryPanelUiToolkitView.DynamicStyles.cs`)

```csharp
// selected = filled with the hero's badge colors; unselected = neutral filter background.
private static void RefreshHeroChip(Button button, string heroName, bool selected)
{
    if (selected)
    {
        var style = GetHeroBadgeStyle(heroName);
        StyleButton(button, style.Background, style.Text);
    }
    else
    {
        StyleButton(button, Colors.GhostFilterBackground, Colors.White);
    }
}
```

(Optional polish, not required: tint the unselected chip's border with the hero color so heroes are
identifiable at a glance even when inactive.)

### 3.11 `HistoryPanelText.Runs.cs` — day toggle label

```csharp
private static readonly LocalizedTextSet FilterDayMin10Text = new("≥10d", "≥10天", "≥10天");
internal static string FilterDayMin10() => Resolve(FilterDayMin10Text);
```

Because it is a static `LocalizedTextSet` field, `FontAtlasSample()` (`Text/HistoryPanelText.FontSample.cs`)
picks it up by reflection and warms the glyphs automatically — no manual atlas edit. Hero codes
(`VAN`…`STE`) are ASCII uppercase, already covered by the atlas ASCII string.

**Glyph caveat:** if `≥` renders as a tofu box in the game font, switch the label to `"10天+"` /
`"Day 10+"` (ASCII `+`, glyph-safe) — semantics are identical (day 10 and up).

---

## 4. Edge cases & gotchas

- **Index space flip.** `SelectedRunIndex` now means "index into `FilteredRuns`." Every spot in §2 must
  use `FilteredRuns`. The most likely bug is leaving one path on `_state.Runs` → selecting the wrong run.
- **Null `Day` ghost battles** are excluded when `≥10天` is on (`(Day ?? int.MinValue) < 10`). Intended;
  call it out if any battles legitimately lack a Day.
- **Empty hero result.** Selecting a hero with zero runs → `FilteredRuns` empty → list empty, no selected
  run, battles + preview empty. Acceptable; optionally surface a "no runs for hero" status (not required).
- **Filter-slot constant height.** Both Row B variants must be one `ButtonCompactHeight` row with
  `Wrap.NoWrap`. Verify the 7 hero chips do not wrap at the min rail width (`OperationRailMinWidth=360`);
  `fixedWidth:false` lets them shrink via flex, so they should fit. If they look cramped, drop chip font
  or gap, do NOT enable wrap (wrapping reintroduces variable height).
- **Detail card `flexGrow:0`.** The card now hugs; `railScroll` (flexGrow:1) absorbs the rail slack. This
  does NOT affect the preview-bounds geometry callback — `OnPreviewContainerGeometryChanged` watches
  `_previewContainer`, which lives in the left **core**, not the rail.
- **Hero filter persistence.** `SelectedRunHero` survives mode switches and `RefreshData`, so it re-applies
  on return to Runs — desired. (If you want it to reset on panel close, clear it in `OnPanelHidden`.)
- **Architecture boundary.** `HistoryPanelRunHeroFilter` is product/filtering rule → stays in `Game/`
  (not `GameInterop/`), like `HistoryPanelGhostBattleFilter`.

---

## 5. Verification

- `./run.sh build` (Debug; auto-copies to `BepInEx/plugins`).
- `./run.sh test` — adding the day overload is additive; existing `Matches(filter, battle)` and the
  `ResolveGhostBattleOutcome` compat shim are untouched. Confirm no HistoryPanel test pins a constructor
  shape that the new state fields break.
- In-game (launch The Bazaar via Steam, App ID 1617400; `open "steam://run/1617400"`), press F8:
  1. Filter slot is pinned at the rail top, **identical height** in Runs vs Ghost, no jump when toggling
     the Runs/Ghost tab; nothing scrolls the filter away.
  2. **Runs:** 7 hero chips; clicking one filters the run list to that hero (active chip is hero-colored);
     clicking the active chip clears; the selected run resets to the first filtered run and the battles
     column + preview follow; the count chip shows the filtered count.
  3. **Ghost:** the `≥10天` toggle AND-combines with All/Won/Lost; counts update; toggling off restores.
  4. **Detail card:** no "当前战斗" title; compact; day pill on the right of the first row; eliminated
     notice still appears for eliminations.
  5. Switch Runs↔Ghost several times: filter slot stable; `railScroll` resets to top on switch.

---

## 6. Review checklist (what I'll verify when you hand the diff back)

- [ ] `SelectedRunIndex` consistently in filtered space; no `_state.Runs[index]` / `_runs.Count` leak in
      any run-selection path; no dangling no-arg `GetSelectedRun()` caller.
- [ ] `FilteredRunsDirty` invalidated on every mutation that changes the run set or hero filter
      (`RefreshData`, `SetRunHeroFilter`); not left stale after mode switch.
- [ ] Ghost day predicate AND-semantics correct; null-Day handling intentional.
- [ ] `_filterSlot` truly constant height across modes (both Row B = one `ButtonCompactHeight`, NoWrap);
      tabs `fixedWidth:false`.
- [ ] Detail-card `flexGrow:0` + spacer removal don't regress the rail layout or the preview geometry
      callback; day-pill reorder reads correctly when fields are missing (truncation/empty states).
- [ ] New filter helper stays in `Game/`; no `GameInterop` boundary violation; architecture tests pass.
- [ ] `≥10天` label renders (no tofu); `csharpier format .` clean; keep unrelated reformats out of the diff.

---

## Appendix: hero roster ↔ short code (from `Ui/HistoryPanelUiToolkitView.Badges.cs:106-121`)

The filter row is **exactly these 7 chips** — a fixed set, so the row width is constant ("刚好充满").

| Hero name | Code |
| --- | --- |
| Vanessa | VAN |
| Pygmalien | PYG |
| Dooley | DOO |
| Mak | MAK |
| Jules | JUL |
| Karnok | KAR |
| Stelle | STE |

Runs whose hero is not one of these 7 need **no special handling and no UNK chip**: with a hero
selected they simply don't match (hidden); with nothing selected they show like everything else. That
falls straight out of `HistoryPanelRunHeroFilter.Matches` returning true when `SelectedRunHero` is null.
