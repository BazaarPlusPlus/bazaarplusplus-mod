# Collection Panel Hero Portrait Chips Implementation Plan

> **Status: IMPLEMENTED — 历史归档(spent plan)。** HeroPortraitSpriteProvider(GameInterop/HeroPortraits/)与 CollectionPanel hero chips(CollectionPanelView.Filters.cs)已全部落地;§2 的 token 草案值与 as-built 不同(实际 HeroChipIconSize=48f、HeroChipButtonSize=56f,无 HeroChipMinWidth)。复选框为历史状态,勿据此重做。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace CollectionPanel hero filter text chips with readable hero chips that show the game's native hero portrait sprite when available, while keeping text fallback and preparing the same sprite provider for a later HistoryPanel badge pass.

**Architecture:** Add a small shared hero portrait sprite provider under `GameInterop/HeroPortraits/` that uses the game's own `CollectionManager.GetDefaultHeroSkin(EHero)` -> `SkinAssetDataSO.LoadPortraitSpriteAsync()` path, with per-hero cache and in-flight task coalescing. CollectionPanel consumes that provider from its UITK hero chip factory by rendering a child icon inside each existing `Button`, leaving filter state, catalog, overlay, card virtualizer, and selection behavior unchanged.

**Tech Stack:** C# 12, Unity UI Toolkit, Unity `Sprite`, The Bazaar `CollectionManager` / `SkinAssetDataSO`, BepInEx logging through `BppLog`, existing `Infrastructure/UiTokens` tokens.

---

## Current Source Facts

- The older spec at `docs/design/2026-05-31-history-panel-hero-portrait-badge-design.md` is HistoryPanel-only and still marked `Status: Draft`.
- The old spec's core asset chain is still valid: `CollectionManager.GetDefaultHeroSkin(EHero)` synchronously returns `SkinAssetDataSO`, and `SkinAssetDataSO.LoadPortraitSpriteAsync()` asynchronously returns `Sprite?`.
- `EHero` currently contains `Common`, seven real heroes, and `Hero8`. `Common` and `Hero8` are not normal portrait targets.
- CollectionPanel hero filters are text `Button` chips created in `Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs`.
- CollectionPanel's filter state and engine already operate on `EHero`; no data-layer change is needed.
- CollectionPanel has a known history of overlay/input regressions. This plan does not add UGUI overlays, `GraphicRaycaster`, transparent blockers, or card-area input routing.
- There is no useful automated test seam for actual game sprite loading or UITK visual inspection. Verification is build plus in-game UI/log inspection.

## File Structure

- Create `GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs`
  - Shared game-coupled asset provider for default hero portrait sprites.
  - Owns per-hero completed cache and in-flight task coalescing.
  - Skips `EHero.Common` and `EHero.Hero8`.
  - Does not release Addressables handles; the game `AssetLoader` owns the asset lifecycle.
- Modify `Infrastructure/UiTokens/Sizes.cs`
  - Add a specific icon-size token for CollectionPanel hero chips.
- Modify `Game/CollectionPanel/Ui/CollectionPanelView.cs`
  - Add dictionaries for hero chip icon and label children.
- Modify `Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs`
  - Render hero chips as icon + label buttons for real heroes.
  - Keep `Common` as text-only fallback.
  - Keep generic chip creation and refresh behavior for tier, size, merchant, package, and clear controls.
- Modify `docs/design/README.md`
  - Add this active proposal to the design index.

## Scope Boundaries

- This plan implements CollectionPanel first.
- This plan does not change HistoryPanel code yet.
- This plan does not add a shared hero badge renderer for `Label` pills yet; that belongs to the HistoryPanel follow-up once the provider and UITK sprite API are verified.
- This plan does not replace CollectionPanel card previews, card art, or tooltip icons.
- This plan does not use the private serialized `TooltipHeroIconController.heroIcons` array. That array is prefab-local and not a stable global registry.

---

### Task 1: Add Shared Hero Portrait Sprite Provider

**Files:**
- Create: `GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs`

- [ ] **Step 1: Create the provider file**

Create `GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

internal static class HeroPortraitSpriteProvider
{
    private const string LogComponent = "HeroPortrait";

    private static readonly Dictionary<EHero, Sprite?> CachedSprites = new();
    private static readonly Dictionary<EHero, Task<Sprite?>> InFlightLoads = new();

    internal static bool IsRenderableHero(EHero hero) =>
        hero != EHero.Common && hero != EHero.Hero8;

    internal static bool TryGetCached(EHero hero, out Sprite? sprite)
    {
        sprite = null;
        return IsRenderableHero(hero) && CachedSprites.TryGetValue(hero, out sprite);
    }

    internal static Task<Sprite?> LoadDefaultPortraitAsync(EHero hero)
    {
        if (!IsRenderableHero(hero))
            return Task.FromResult<Sprite?>(null);

        if (CachedSprites.TryGetValue(hero, out var cached))
            return Task.FromResult(cached);

        if (InFlightLoads.TryGetValue(hero, out var inFlight))
            return inFlight;

        var task = LoadAndMaybeCacheAsync(hero);
        InFlightLoads[hero] = task;
        return task;
    }

    private static async Task<Sprite?> LoadAndMaybeCacheAsync(EHero hero)
    {
        Sprite? result = null;
        var shouldCacheResult = false;

        try
        {
            if (!Services.TryGet<CollectionManager>(out var collectionManager) || collectionManager == null)
            {
                BppLog.Warn(LogComponent, $"CollectionManager unavailable for hero={hero}; using text fallback.");
                return null;
            }

            SkinAssetDataSO? skin = collectionManager.GetDefaultHeroSkin(hero);
            shouldCacheResult = true;
            if (skin == null)
            {
                BppLog.Warn(LogComponent, $"No default hero skin for hero={hero}; using text fallback.");
                return null;
            }

            result = await skin.LoadPortraitSpriteAsync();
            if (result == null)
                BppLog.Debug(LogComponent, $"No static portrait sprite for hero={hero}; using text fallback.");
            return result;
        }
        catch (Exception ex)
        {
            BppLog.Warn(LogComponent, $"Failed to load hero portrait for hero={hero}: {ex.Message}");
            return null;
        }
        finally
        {
            InFlightLoads.Remove(hero);
            if (shouldCacheResult)
                CachedSprites[hero] = result;
        }
    }
}
```

- [ ] **Step 2: Build to verify game API names**

Run:

```bash
dotnet build BazaarPlusPlus.csproj --no-restore
```

Expected: build succeeds. If the local game assembly path is not auto-detected, rerun with the local `ManagedPath` instead of editing project references.

- [ ] **Step 3: Fix only compile-proven API mismatches**

If Step 2 fails because `Services.TryGet<CollectionManager>` is unavailable from the mod assembly, replace that block with the narrower `Services.Get<CollectionManager>()` path:

```csharp
var collectionManager = Services.Get<CollectionManager>();
if (collectionManager == null)
{
    BppLog.Warn(LogComponent, $"CollectionManager unavailable for hero={hero}; using text fallback.");
    return null;
}
```

Then rerun:

```bash
dotnet build BazaarPlusPlus.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs
git commit -m "Add shared hero portrait sprite provider"
```

---

### Task 2: Add CollectionPanel Hero Chip UI Tokens and State

**Files:**
- Modify: `Infrastructure/UiTokens/Sizes.cs`
- Modify: `Game/CollectionPanel/Ui/CollectionPanelView.cs`

- [ ] **Step 1: Add icon size tokens**

In `Infrastructure/UiTokens/Sizes.cs`, after `public const float ChipHeight = 32f;`, add:

```csharp
public const float HeroChipIconSize = 24f;
public const float HeroChipMinWidth = 118f;
```

Rationale: `ChipHeight` remains shared by all chips. The icon gets its own token so avatar sizing does not accidentally alter other pill/chip layouts.

- [ ] **Step 2: Add hero chip child dictionaries**

In `Game/CollectionPanel/Ui/CollectionPanelView.cs`, after the existing `_heroChips` dictionary, add:

```csharp
private readonly Dictionary<EHero, VisualElement> _heroChipIcons = new();
private readonly Dictionary<EHero, Label> _heroChipLabels = new();
```

- [ ] **Step 3: Build to verify field additions compile**

Run:

```bash
dotnet build BazaarPlusPlus.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add Infrastructure/UiTokens/Sizes.cs Game/CollectionPanel/Ui/CollectionPanelView.cs
git commit -m "Add collection hero chip UI state"
```

---

### Task 3: Render CollectionPanel Hero Chips With Portrait Icons

**Files:**
- Modify: `Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs`

- [ ] **Step 1: Add provider namespace**

In `Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs`, add this `using` with the other project namespaces:

```csharp
using BazaarPlusPlus.GameInterop.HeroPortraits;
using UnityEngine.UIElements;
```

If `UnityEngine.UIElements` is already present in the file after edits, keep only one import.

- [ ] **Step 2: Replace hero chip creation**

Replace `EnsureHeroChips` with:

```csharp
private void EnsureHeroChips(IReadOnlyList<EHero> heroes)
{
    if (_heroChipRow == null)
        return;
    if (HeroChipsMatch(heroes))
        return;

    ClearHeroChipRow();
    foreach (var hero in heroes)
    {
        var chip = HeroPortraitSpriteProvider.IsRenderableHero(hero)
            ? CreateHeroChipButton(hero, () => _toggleHero(hero))
            : CreateChipButton(CollectionPanelText.Hero(hero), () => _toggleHero(hero));
        _heroChips[hero] = chip;
        _heroChipRow.Add(chip);
    }
}
```

- [ ] **Step 3: Add hero chip cleanup**

Add this method near `ClearChipRow`:

```csharp
private void ClearHeroChipRow()
{
    foreach (var button in _heroChips.Values)
    {
        if (button.parent != null)
            button.parent.Remove(button);
    }

    _heroChips.Clear();
    _heroChipIcons.Clear();
    _heroChipLabels.Clear();
}
```

- [ ] **Step 4: Add hero chip factory**

Add this method after `CreateChipButton`:

```csharp
private Button CreateHeroChipButton(EHero hero, Action onClick)
{
    var labelText = CollectionPanelText.Hero(hero);
    var chip = CreateButton(string.Empty, onClick, Sizes.HeroChipMinWidth, Sizes.ChipHeight);
    chip.tooltip = labelText;
    chip.style.flexDirection = FlexDirection.Row;
    chip.style.justifyContent = Justify.Center;
    chip.style.alignItems = Align.Center;
    chip.style.marginRight = UiSpacing.Sm;
    chip.style.marginBottom = UiSpacing.Xs;
    StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);

    var icon = new VisualElement { pickingMode = PickingMode.Ignore };
    icon.style.width = Sizes.HeroChipIconSize;
    icon.style.height = Sizes.HeroChipIconSize;
    icon.style.minWidth = Sizes.HeroChipIconSize;
    icon.style.minHeight = Sizes.HeroChipIconSize;
    icon.style.marginRight = UiSpacing.Xs;
    icon.style.display = DisplayStyle.None;
    icon.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
    UiStyle.Radius(icon.style, Sizes.HeroChipIconSize / 2f);
    chip.Add(icon);

    var label = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistoryChipText);
    label.text = labelText;
    label.pickingMode = PickingMode.Ignore;
    label.style.unityTextAlign = TextAnchor.MiddleCenter;
    label.style.flexShrink = 1f;
    chip.Add(label);

    _heroChipIcons[hero] = icon;
    _heroChipLabels[hero] = label;
    LoadHeroChipIcon(hero, icon);
    return chip;
}
```

- [ ] **Step 5: Add async icon loading helpers**

Add these methods after `CreateHeroChipButton`:

```csharp
private static void LoadHeroChipIcon(EHero hero, VisualElement icon)
{
    icon.userData = hero;

    if (HeroPortraitSpriteProvider.TryGetCached(hero, out var cached))
    {
        ApplyHeroChipIcon(icon, cached);
        return;
    }

    ApplyHeroChipIcon(icon, null);
    _ = ApplyHeroChipIconWhenLoadedAsync(hero, icon);
}

private static async System.Threading.Tasks.Task ApplyHeroChipIconWhenLoadedAsync(
    EHero hero,
    VisualElement icon
)
{
    var sprite = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero);
    if (!Equals(icon.userData, hero))
        return;
    ApplyHeroChipIcon(icon, sprite);
}

private static void ApplyHeroChipIcon(VisualElement icon, Sprite? sprite)
{
    if (sprite == null)
    {
        icon.style.display = DisplayStyle.None;
        icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
        return;
    }

    icon.style.display = DisplayStyle.Flex;
    icon.style.backgroundImage = new StyleBackground(sprite);
    icon.MarkDirtyRepaint();
}
```

- [ ] **Step 6: Replace hero selected-state refresh**

In `CollectionPanelView.Refresh`, replace:

```csharp
foreach (var pair in _heroChips)
    RefreshChip(pair.Value, model.SelectedHeroes.Contains(pair.Key));
```

with:

```csharp
foreach (var pair in _heroChips)
    RefreshHeroChip(pair.Key, pair.Value, model.SelectedHeroes.Contains(pair.Key));
```

Then add this method near `RefreshChip`:

```csharp
private void RefreshHeroChip(EHero hero, Button chip, bool selected)
{
    RefreshChip(chip, selected);
    if (_heroChipLabels.TryGetValue(hero, out var label))
        label.style.color = selected ? Colors.ButtonSelectedText : Colors.HistoryChipText;
}
```

- [ ] **Step 7: Build to verify UI Toolkit sprite API**

Run:

```bash
dotnet build BazaarPlusPlus.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 8: Fix only compile-proven UI Toolkit API mismatches**

If `new StyleBackground(sprite)` does not compile in the Unity UI Toolkit reference assemblies used by this project, replace the sprite assignment with:

```csharp
icon.style.backgroundImage = Background.FromSprite(sprite);
```

If `new StyleBackground(StyleKeyword.Null)` does not compile, replace it with:

```csharp
icon.style.backgroundImage = StyleKeyword.Null;
```

Then rerun:

```bash
dotnet build BazaarPlusPlus.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 9: Commit**

```bash
git add Game/CollectionPanel/Ui/CollectionPanelView.cs Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs Infrastructure/UiTokens/Sizes.cs
git commit -m "Show hero portraits in collection filters"
```

---

### Task 4: Validate CollectionPanel Behavior In Game

**Files:**
- No source files changed in this task.

- [ ] **Step 1: Run focused build**

Run:

```bash
dotnet build BazaarPlusPlus.csproj --no-restore
```

Expected: build succeeds and Debug build copies the mod to `BepInEx/plugins/` if the game install is detected.

- [ ] **Step 2: Open the game and inspect CollectionPanel**

Start The Bazaar, open `卡牌图鉴`, and inspect the hero filter area.

Expected:
- `Common` / `通用` remains readable as text.
- Real hero chips keep readable hero names.
- Real hero chips show portrait icons when sprites load.
- Missing sprites fall back to text-only chips.
- The hero filter row does not push tier, merchant, size, status, or close controls out of the operation rail.

- [ ] **Step 3: Verify filter behavior did not change**

Inside `卡牌图鉴`, click these chips:

```text
Vanessa
Pygmalien
Common / 通用
```

Expected:
- Each click toggles the selected visual state.
- Card counts update normally.
- Item tab and Skill tab still scroll.
- Hovering a card still shows the native tooltip.
- Clicking empty grid space does not freeze selection or scrolling.

- [ ] **Step 4: Inspect runtime logs**

Run:

```bash
rg -n "\\[BPP\\]\\[HeroPortrait\\]|\\[BPP\\]\\[CollectionPanel\\]" "/Users/yxinyu/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log"
```

Expected:
- No repeated warning spam for the same hero.
- If a hero has no static sprite, one debug-only fallback line is acceptable in Debug builds.
- No `CollectionPanel` errors appear while opening, filtering, scrolling, or hovering.

- [ ] **Step 5: Run diff hygiene**

Run:

```bash
git diff --check
git status --short
```

Expected:
- No whitespace errors.
- Dirty files are limited to the provider, CollectionPanel UI files, UI tokens, and this plan/README if they were not committed earlier.

---

### Task 5: Prepare the HistoryPanel Follow-Up Boundary

**Files:**
- Modify: `docs/design/2026-05-31-history-panel-hero-portrait-badge-design.md`
- Modify: `docs/design/README.md`

- [ ] **Step 1: Update the older HistoryPanel draft after CollectionPanel validation**

After Task 4 succeeds, add this note near the top of `docs/design/2026-05-31-history-panel-hero-portrait-badge-design.md`, below `Status: Draft`:

```markdown
> Supersession note: CollectionPanel is taking the first implementation pass through `GameInterop/HeroPortraits/HeroPortraitSpriteProvider`. When HistoryPanel work starts, reuse that provider and only add HistoryPanel-specific `Label` badge rendering and ListView stale-bind protection here.
```

- [ ] **Step 2: Update `docs/design/README.md` if implementation lands**

If the CollectionPanel implementation is committed, update this plan's README entry from draft wording to implemented-with-follow-up wording:

```markdown
- [`2026-06-01-collection-panel-hero-portrait-chips-plan.md`](2026-06-01-collection-panel-hero-portrait-chips-plan.md) — CollectionPanel hero filter chips use the game's default hero portrait sprites through a shared provider; HistoryPanel badge adoption remains a follow-up.
```

- [ ] **Step 3: Commit docs follow-up**

```bash
git add docs/design/2026-05-31-history-panel-hero-portrait-badge-design.md docs/design/README.md
git commit -m "Document hero portrait follow-up boundary"
```

---

## Follow-Up Gates

- Do not implement HistoryPanel in the same pass unless CollectionPanel build and live UI validation have already passed.
- Do not replace the provider with tooltip prefab reflection unless the default-skin portrait path fails in-game for multiple real heroes.
- Do not add UGUI overlays for CollectionPanel filter chips. The filter rail is UI Toolkit; keep icons in UITK.
- Do not add automated tests that only assert source text or mock call sequences. This change needs build plus in-game verification.
- If sprite loading causes visible hitches, add `HeroPortraitLoad` timing logs before changing cache strategy.

## Self-Review Checklist

- Spec coverage: CollectionPanel first, shared provider, text fallback, `Common/Hero8` exclusion, no overlay/input changes, HistoryPanel follow-up boundary.
- Placeholder scan: no open-ended filler phrases, no vague validation instructions, no code steps without concrete snippets.
- Type consistency: provider uses `EHero`, `Sprite?`, `Task<Sprite?>`; CollectionPanel UI keeps `Button` chips and child `VisualElement` icons.
- Verification scope: focused build, live Chinese UI checks, runtime log inspection, and diff hygiene.
