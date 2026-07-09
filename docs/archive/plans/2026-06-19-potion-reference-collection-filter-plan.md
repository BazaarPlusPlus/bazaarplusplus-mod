---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at master (commits 67be1c4a, 831ed76b, d7a1e909); all 3 tasks shipped verbatim — ReferenceTagBaseResolver.cs added, PotionReference exposed as keyword/reference chip, tests link the resolver.

# Potion Reference Collection Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose `EHiddenTag.PotionReference` as a CollectionPanel keyword/reference filter option when current static card data contains that hidden tag, while keeping the existing `ECardTag.Potion` item-tag filter separate.

**Architecture:** Keep classification ownership at the existing CollectionPanel seams. `CollectionKeywordWhitelist` owns which hidden tags can become player-facing keyword/reference chips, `CollectionFacetAvailability` decides whether those chips are available for the current catalog, and `CollectionFilterEngine` already filters selected keywords against `CollectionCardVm.HiddenTags`. Add a small pure `GameInterop.TagTypography` module for reference-tag base display resolution so `NativeTagTypography` can render a hidden-tag reference whose base is an `ECardTag`.

**Tech Stack:** C# 12, `netstandard2.1` mod assembly, executable `CollectionFilterEngine.Tests`, The Bazaar publicized `BazaarGameShared` enums, UI Toolkit chips through existing `NativeTagTypography`.

---

## Source Evidence

- `Potion` is a card tag in game code: `decompiled/BazaarGameShared/BazaarGameShared.Domain.Core.Types/ECardTag.cs:3-9`.
- CollectionPanel already exposes `ECardTag.Potion` in the item tag whitelist: `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTagWhitelist.cs:12-24`.
- `PotionReference` exists as a game hidden tag: `decompiled/BazaarGameShared/BazaarGameShared.Domain.Core.Types/EHiddenTag.cs:59-80`.
- CollectionPanel keyword availability only returns hidden tags that are present in the catalog and present in `CollectionKeywordWhitelist.Ordered`: `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFacetAvailability.cs:96-131`.
- The current keyword whitelist includes several `*Reference` hidden tags, but not `PotionReference`: `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs:13-73`.
- The current tests explicitly classify `EHiddenTag.PotionReference` as a non-curated reference tag: `tests/CollectionFilterEngine.Tests/Program.cs:832-846`.
- UI creates the "reference" subsection from `CollectionKeywordWhitelist.IsReferenceKeyword`, then renders each chip through `NativeTagTypography.Resolve(EHiddenTag)`: `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs:150-172`.
- `NativeTagTypography` can render reference hidden tags only when it can map the reference back to a base hidden tag; `PotionReference` cannot use the existing `EHiddenTag -> EHiddenTag` mapping because the base is `ECardTag.Potion`: `src/BazaarPlusPlus/GameInterop/TagTypography/NativeTagTypography.cs:176-223`.
- Source offer filtering already handles Eli's potion pool through `tagsAny: ["Potion"]`, so this plan does not change source rules: `src/BazaarPlusPlus/Data/CollectionSources/collection-sources.json:307-330`.

## Scope

Implement:

- `PotionReference` appears in the keyword/reference facet only when at least one non-package visible card has `EHiddenTag.PotionReference`.
- Selecting `PotionReference` filters by `CollectionCardVm.HiddenTags`, not by `CollectionCardVm.Tags`.
- Selecting the existing `Potion` tag continues to filter by `ECardTag.Potion`, not by `EHiddenTag.PotionReference`.
- The `PotionReference` chip renders as the native `Potion` tag label/icon/color plus the existing reference suffix.

Do not implement:

- Do not edit `decompiled/`.
- Do not derive `PotionReference` from card text, names, or `ArtKey`.
- Do not merge `Potion` and `PotionReference` into one user selection.
- Do not change source offer rules or `CollectionHiddenTagGroups`; merchant/source pools are a separate feature path and Eli already uses `tagsAny: Potion`.
- Do not add a fallback data file for current game card content. The current runtime card database remains the source of truth.

## File Map

- Create: `src/BazaarPlusPlus/GameInterop/TagTypography/ReferenceTagBaseResolver.cs`
  - Responsibility: pure mapping from reference hidden tags to their display base, supporting both hidden-tag bases and card-tag bases.
- Modify: `src/BazaarPlusPlus/GameInterop/TagTypography/NativeTagTypography.cs`
  - Responsibility: consume `ReferenceTagBaseResolver` when rendering reference chips; keep tooltip/string-table lookup in this existing adapter.
- Modify: `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs`
  - Responsibility: expose `EHiddenTag.PotionReference` as a player-facing keyword/reference option.
- Modify: `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`
  - Responsibility: link the new pure resolver into the executable test project.
- Modify: `tests/CollectionFilterEngine.Tests/Program.cs`
  - Responsibility: cover reference base mapping, whitelist membership, facet availability, and the separation between `Potion` tag and `PotionReference` keyword.

## Task 1: Add A Pure Reference Base Resolver

**Files:**
- Create: `src/BazaarPlusPlus/GameInterop/TagTypography/ReferenceTagBaseResolver.cs`
- Modify: `src/BazaarPlusPlus/GameInterop/TagTypography/NativeTagTypography.cs`
- Modify: `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`
- Modify: `tests/CollectionFilterEngine.Tests/Program.cs`

- [ ] **Step 1: Write the failing resolver tests**

Modify `tests/CollectionFilterEngine.Tests/Program.cs` imports:

```csharp
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.GameInterop.TagTypography;
```

Add this block after the existing keyword whitelist include/exclude assertions around `CollectionKeywordWhitelist`:

```csharp
AssertTrue(
    ReferenceTagBaseResolver.TryResolve(EHiddenTag.PotionReference, out var potionReferenceBase),
    "PotionReference should resolve to a base display tag."
);
AssertEqual(
    (ECardTag?)ECardTag.Potion,
    potionReferenceBase.CardTag,
    "PotionReference should render through the Potion card-tag display."
);
AssertEqual(
    (EHiddenTag?)null,
    potionReferenceBase.HiddenTag,
    "PotionReference should not claim a hidden-tag base because no EHiddenTag.Potion exists."
);

AssertTrue(
    ReferenceTagBaseResolver.TryResolve(EHiddenTag.PoisonReference, out var poisonReferenceBase),
    "Existing hidden-tag references should still resolve."
);
AssertEqual(
    (EHiddenTag?)EHiddenTag.Poison,
    poisonReferenceBase.HiddenTag,
    "PoisonReference should keep its Poison hidden-tag display base."
);
AssertEqual(
    (ECardTag?)null,
    poisonReferenceBase.CardTag,
    "PoisonReference should not claim a card-tag display base."
);

AssertFalse(
    ReferenceTagBaseResolver.TryResolve(EHiddenTag.Poison, out _),
    "Non-reference hidden tags should not resolve through the reference base resolver."
);
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

Expected: FAIL at compile time with errors for missing `BazaarPlusPlus.GameInterop.TagTypography` / `ReferenceTagBaseResolver`.

- [ ] **Step 3: Create the pure resolver**

Create `src/BazaarPlusPlus/GameInterop/TagTypography/ReferenceTagBaseResolver.cs`:

```csharp
#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.TagTypography;

internal readonly struct ReferenceTagBase
{
    private ReferenceTagBase(EHiddenTag? hiddenTag, ECardTag? cardTag)
    {
        HiddenTag = hiddenTag;
        CardTag = cardTag;
    }

    public EHiddenTag? HiddenTag { get; }

    public ECardTag? CardTag { get; }

    public bool HasValue => HiddenTag.HasValue || CardTag.HasValue;

    public static ReferenceTagBase ForHiddenTag(EHiddenTag tag) => new(tag, null);

    public static ReferenceTagBase ForCardTag(ECardTag tag) => new(null, tag);
}

internal static class ReferenceTagBaseResolver
{
    public static bool TryResolve(EHiddenTag tag, out ReferenceTagBase baseTag)
    {
        baseTag = tag switch
        {
            EHiddenTag.DamageReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Damage),
            EHiddenTag.HealReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Heal),
            EHiddenTag.BurnReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Burn),
            EHiddenTag.PoisonReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Poison),
            EHiddenTag.JoyReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Joy),
            EHiddenTag.ShieldReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Shield),
            EHiddenTag.RegenReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Regen),
            EHiddenTag.HealthReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Health),
            EHiddenTag.FreezeReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Freeze),
            EHiddenTag.HasteReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Haste),
            EHiddenTag.SlowReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Slow),
            EHiddenTag.EconomyReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Income),
            EHiddenTag.CooldownReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Cooldown),
            EHiddenTag.AmmoReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Ammo),
            EHiddenTag.CritReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Crit),
            EHiddenTag.QuestReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Quest),
            EHiddenTag.FlyingReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Flying),
            EHiddenTag.RageReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Rage),
            EHiddenTag.HeatedReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Heated),
            EHiddenTag.ChilledReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Chilled),
            EHiddenTag.TempoReference => ReferenceTagBase.ForHiddenTag(EHiddenTag.Tempo),
            EHiddenTag.PotionReference => ReferenceTagBase.ForCardTag(ECardTag.Potion),
            _ => default,
        };
        return baseTag.HasValue;
    }
}
```

- [ ] **Step 4: Link the resolver into the executable test project**

Add this item to `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj` inside the existing `<ItemGroup>` with linked source files:

```xml
    <Compile
      Include="..\..\src\BazaarPlusPlus\GameInterop\TagTypography\ReferenceTagBaseResolver.cs"
      Link="ReferenceTagBaseResolver.cs"
    />
```

- [ ] **Step 5: Wire `NativeTagTypography` through the resolver**

In `src/BazaarPlusPlus/GameInterop/TagTypography/NativeTagTypography.cs`, replace the body of `TryResolveReferenceTag` with:

```csharp
    private static bool TryResolveReferenceTag(
        EHiddenTag tag,
        out NativeTagDisplay referenceDisplay
    )
    {
        referenceDisplay = default;
        if (!ReferenceTagBaseResolver.TryResolve(tag, out var referenceBase))
            return false;

        NativeTagDisplay baseDisplay;
        if (referenceBase.HiddenTag.HasValue)
            baseDisplay = Resolve(referenceBase.HiddenTag.Value);
        else if (referenceBase.CardTag.HasValue)
            baseDisplay = Resolve(referenceBase.CardTag.Value);
        else
            return false;

        var label = ReferenceLabel(tag, baseDisplay.Label);
        referenceDisplay = new NativeTagDisplay(
            label,
            baseDisplay.AccentColor,
            baseDisplay.IconName
        );
        return true;
    }
```

Delete the old `TryGetReferenceBaseTag(EHiddenTag tag, out EHiddenTag baseTag)` method from the same file. The switch moved into `ReferenceTagBaseResolver` and now supports `ECardTag.Potion` as a display base.

- [ ] **Step 6: Run focused tests**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

Expected: PASS with `CollectionFilterEngine checks passed.`

- [ ] **Step 7: Build the mod**

Run:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: build succeeds. This catches `NativeTagTypography` integration because the focused test only links the pure resolver.

- [ ] **Step 8: Commit**

```bash
git add \
  src/BazaarPlusPlus/GameInterop/TagTypography/ReferenceTagBaseResolver.cs \
  src/BazaarPlusPlus/GameInterop/TagTypography/NativeTagTypography.cs \
  tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj \
  tests/CollectionFilterEngine.Tests/Program.cs
git commit -m "refactor(collection): resolve reference tag display bases"
```

## Task 2: Expose PotionReference In CollectionPanel Keywords

**Files:**
- Modify: `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs`
- Modify: `tests/CollectionFilterEngine.Tests/Program.cs`

- [ ] **Step 1: Write the failing whitelist and availability tests**

In `tests/CollectionFilterEngine.Tests/Program.cs`, update the expected `CollectionKeywordWhitelist.Ordered` array so the reference section ends with `PotionReference`:

```csharp
        nameof(EHiddenTag.RageReference),
        nameof(EHiddenTag.EconomyReference),
        nameof(EHiddenTag.PotionReference),
```

Remove `EHiddenTag.PotionReference` from the non-curated reference list:

```csharp
    var referenceKeyword in new[]
    {
        EHiddenTag.ChilledReference,
        EHiddenTag.HeatedReference,
        EHiddenTag.JoyReference,
        EHiddenTag.TechReference,
        EHiddenTag.TempoReference,
    }
```

Add `EHiddenTag.PotionReference` to the curated reference list:

```csharp
    var referenceKeyword in new[]
    {
        EHiddenTag.DamageReference,
        EHiddenTag.HealReference,
        EHiddenTag.AmmoReference,
        EHiddenTag.RageReference,
        EHiddenTag.EconomyReference,
        EHiddenTag.PotionReference,
    }
```

Add direct reference-section assertions after the curated/non-curated reference keyword loops:

```csharp
AssertTrue(
    CollectionKeywordWhitelist.IsReferenceKeyword(EHiddenTag.PotionReference),
    "PotionReference should start the keyword reference subsection like other reference keywords."
);
AssertFalse(
    CollectionKeywordWhitelist.IsReferenceKeyword(EHiddenTag.Poison),
    "Base gameplay keywords should not be classified as reference keywords."
);
```

In the existing `availableFacetCards` block, add a non-package item that carries `PotionReference`:

```csharp
    Card(
        "Potion Reference",
        ETier.Bronze,
        hiddenTags: new[] { EHiddenTag.PotionReference }
    ),
```

Update the item keyword availability expectation:

```csharp
    new[]
    {
        nameof(EHiddenTag.Damage),
        nameof(EHiddenTag.DamageReference),
        nameof(EHiddenTag.PotionReference),
    },
```

Add this filter-separation block near the existing item tag and item keyword filter tests, after the `Potion` tag filter assertions:

```csharp
var potionReferenceItem = Card(
    "Potion Reference Item",
    ETier.Bronze,
    hiddenTags: new[] { EHiddenTag.PotionReference }
);
var potionReferenceFilter = new CollectionFilterState();
potionReferenceFilter.Keywords.Add(EHiddenTag.PotionReference);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, potionReferenceItem }, potionReferenceFilter),
    new[] { potionReferenceItem.Id },
    "PotionReference should behave as its own hidden-tag keyword, separate from the Potion item tag."
);

var potionTagOnlyFilter = new CollectionFilterState();
potionTagOnlyFilter.Tags.Add(ECardTag.Potion);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, potionReferenceItem }, potionTagOnlyFilter),
    new[] { potionItem.Id },
    "Potion tag filtering should not match PotionReference-only cards."
);
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

Expected: FAIL because `CollectionKeywordWhitelist.Ordered` and `IsReferenceKeyword` do not yet expose `PotionReference`.

- [ ] **Step 3: Add `PotionReference` to the keyword whitelist order**

Modify `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs`:

```csharp
        EHiddenTag.AmmoReference,
        EHiddenTag.RageReference,
        EHiddenTag.EconomyReference,
        EHiddenTag.PotionReference,
```

- [ ] **Step 4: Mark `PotionReference` as a reference keyword**

Modify `CollectionKeywordWhitelist.IsReferenceKeyword`:

```csharp
                or EHiddenTag.AmmoReference
                or EHiddenTag.RageReference
                or EHiddenTag.EconomyReference
                or EHiddenTag.PotionReference;
```

- [ ] **Step 5: Run focused tests**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

Expected: PASS with `CollectionFilterEngine checks passed.`

- [ ] **Step 6: Commit**

```bash
git add \
  src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs \
  tests/CollectionFilterEngine.Tests/Program.cs
git commit -m "feat(collection): expose potion reference keyword"
```

## Task 3: Verify Build, Boundaries, And Runtime Behavior

**Files:**
- No new source files unless a preceding task failed and required a scoped repair in the listed files.

- [ ] **Step 1: Format scoped C# changes**

Run:

```bash
csharpier format \
  src/BazaarPlusPlus/GameInterop/TagTypography/ReferenceTagBaseResolver.cs \
  src/BazaarPlusPlus/GameInterop/TagTypography/NativeTagTypography.cs \
  src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs \
  tests/CollectionFilterEngine.Tests/Program.cs
```

Expected: command succeeds. If formatting changes unrelated files, do not stage them.

- [ ] **Step 2: Run focused regression tests**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

Expected: PASS with `CollectionFilterEngine checks passed.`

- [ ] **Step 3: Run architecture tests**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
```

Expected: PASS. This confirms the CollectionPanel-to-GameInterop typography seam remains allowed and no pure assembly pulled Unity/game references across the wrong layer.

- [ ] **Step 4: Build the plugin**

Run:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: PASS. The build also copies the Debug plugin into the BepInEx plugins folder when the game install is detected.

- [ ] **Step 5: Runtime smoke through Steam**

Launch through Steam:

```bash
open "steam://run/1617400"
```

Open CollectionPanel after the game reaches static data readiness. Expected behavior:

- The existing `Potion` chip remains in the item tag section when potion-tagged cards are present.
- A `Potion Reference` chip appears in the keyword reference subsection only if the current runtime catalog contains at least one non-package item or skill with `EHiddenTag.PotionReference`.
- Selecting `Potion Reference` narrows to cards whose hidden tags include `PotionReference`.
- Selecting `Potion` narrows to cards whose normal tags include `Potion`.

If the current game data contains no `PotionReference` cards, the chip correctly does not appear because `CollectionFacetAvailability` only returns whitelisted tags that are present in the catalog.

- [ ] **Step 6: Inspect runtime log for errors**

Read:

```bash
tail -n 200 "$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log"
```

Expected: no new `[BPP][CollectionPanel]`, `[BPP][NativeTagTypography]`, or UI Toolkit errors caused by opening the panel or selecting chips.

- [ ] **Step 7: Final self-review**

Run:

```bash
git diff --check
git diff -- src/BazaarPlusPlus/GameInterop/TagTypography/ReferenceTagBaseResolver.cs \
  src/BazaarPlusPlus/GameInterop/TagTypography/NativeTagTypography.cs \
  src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs \
  tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj \
  tests/CollectionFilterEngine.Tests/Program.cs
```

Review points:

- `PotionReference` is exposed only as an `EHiddenTag` keyword/reference chip.
- `Potion` remains an `ECardTag` item tag chip.
- No source offer rules changed.
- No `decompiled/` files changed.
- No string-based card identity or card-text parsing was added.

- [ ] **Step 8: Commit verification-only adjustments**

If Task 3 only formatted files already committed in Tasks 1 and 2, amend the relevant commit. If Task 3 required a small repair, commit it separately:

```bash
git add \
  src/BazaarPlusPlus/GameInterop/TagTypography/ReferenceTagBaseResolver.cs \
  src/BazaarPlusPlus/GameInterop/TagTypography/NativeTagTypography.cs \
  src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs \
  tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj \
  tests/CollectionFilterEngine.Tests/Program.cs
git commit -m "test(collection): verify potion reference filtering"
```

## Implementation Notes

- The filter engine already supports selected hidden tags without special cases: `CollectionFilterEngine.Apply()` calls `MatchesFacet(card.HiddenTags, filter.Keywords, filter.KeywordMatchMode)`.
- `CollectionFacetAvailability` is the gate that prevents unused hidden tags from appearing in UI. Adding `PotionReference` to the whitelist does not force the chip to appear when no current card uses it.
- `NativeTagTypography.Resolve(ECardTag.Potion)` is the correct display base for `PotionReference` because no `EHiddenTag.Potion` exists in the game enum.
- Keep `ReferenceTagBaseResolver` in `GameInterop/TagTypography` because it is a display adapter concern. Do not move it into `Game/CollectionPanel/Data`; CollectionPanel should not need to know how native typography maps references.

## Plan Self-Review

- Spec coverage: the plan covers the existing `Potion` tag, the missing `PotionReference` hidden-tag whitelist entry, reference-section classification, display mapping, and separation between tag and keyword filtering.
- Placeholder scan: this plan contains concrete file paths, code snippets, commands, and expected outcomes for each task.
- Type consistency: `ECardTag.Potion`, `EHiddenTag.PotionReference`, `ReferenceTagBase.CardTag`, and `ReferenceTagBase.HiddenTag` are used consistently across tests and implementation.
