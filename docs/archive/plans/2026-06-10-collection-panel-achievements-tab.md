---
status: superseded
archived: 2026-06-12
calibrated: 2026-06-10
superseded-by: docs/plans/achievement-service-design.md, docs/plans/achievement-ui-local-mvp.md
---

> Status: SUPERSEDED. This earlier proposal was red-teamed (its `CollectionSelectedCardDto`/left-rail/`placeholderArtKey` shapes were found fictional) and split into the active `achievement-service-design.md` + `achievement-ui-local-mvp.md`. Retained for history.

# CollectionPanel Achievements Tab Plan

## Goal

Add a fourth top-level tab, `成就`, next to `物品 / 包裹 / 技能`. This tab shows Bazaar++-owned custom cards, not normal game-spawnable cards. Each card gets a stable slug `achievementId`, a GUID `templateId`, title, description, art, hero/tier/size facets, and can be selected into a compact left-side selected-card rail.

The first implementation should be display-only. Do not inject these cards into normal run/shop/spawn behavior.

## Current Code Baseline

- The current top-level mode is encoded as `CollectionFilterState.ActiveType` plus `PackagesOnly`, not as an explicit tab enum (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:24-42`). `SelectActiveType()` clears package mode, and `SelectPackagesOnly()` forces `ActiveType = Item` (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:59-77`).
- The operation rail builds the visible top buttons in one row: item tab, package tab, skill tab, then sort/day controls (`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:75-127`).
- The current panel layout adds the grid first and the operation rail second, which puts the operation rail on the right side (`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:15-29`). A strict left-side mini selected-card rail should therefore be inserted before `BuildGrid(panel)`, not inside the existing operation rail.
- Catalog data currently comes from the game's static `JsonGameDataManager.GetCardMap()` path. `CollectionCatalog.BeginCardMapLoad()` calls `BppStaticDataAccess.LoadCardMap()`, which returns `manager.GetCardMap()` (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCatalog.cs:59-72`, `src/BazaarPlusPlus/GameInterop/StaticCards/BppStaticDataAccess.cs:55-64`).
- `CollectionCatalogBuildSession` only accepts entries whose map value is a `TCardBase`, then projects them to `CollectionCardVm` (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCatalogBuildSession.cs:47-63`).
- `CollectionCardVm` is the current immutable grid/filter projection: id, type, size, tier, heroes, tags, hidden tags, display/internal names, art key, enchantment facets, and package identity (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.cs:28-50`).
- Filtering currently runs `type -> packages -> source offer pool -> hero -> tier -> day -> tags -> keywords -> size -> sort` in `CollectionFilterEngine.Apply()` (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:38-86`).
- Native card rendering is not hand-drawn UITK. `CollectionCardFactory.TryBind()` resolves `vm.Id` back to a `TCardBase` from static data, builds a synthetic `TCardInstanceItem` or `TCardInstanceSkill`, then calls native `CardPreviewBase.SetUp` (`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardFactory.cs:36-70`, `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardFactory.cs:75-100`).
- The native preview contract requires a `TCardBase` template and `TCardInstance`; `CardPreviewBase.SetUp()` creates the client card, updates tier data, builds tooltip data, then loads frame and art (`decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewBase.cs:64-86`).
- Template fields available for a BPP synthetic card include `Id`, `InternalName`, `InternalDescription`, `StartingTier`, `Size`, `Type`, `Heroes`, `Tags`, `HiddenTags`, `ArtKey`, and `Localization` (`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards/TCardBase.cs:9-43`). Tooltip title/description read from `Localization.Title` and `Localization.Description` (`decompiled/TheBazaarRuntime/TheBazaar.Tooltips/CardTooltipData.cs:139-161`).
- Current custom art is keyed by `<templateId>.jpg` in `Resources/CustomCardArt`, embedded with logical names under `BazaarPlusPlus.Resources.CustomCardArt.*` (`src/BazaarPlusPlus/BazaarPlusPlus.csproj:22-32`). Runtime lookup also maps `.jpg` filenames by GUID (`src/BazaarPlusPlus/Game/CardArtReplacement/CustomCardArtCatalog.cs:22-44`, `src/BazaarPlusPlus/Game/CardArtReplacement/CustomCardArtImageFormats.cs:13-25`).
- The existing card-art replacement checks package identity before replacing both live item visuals and CollectionPanel preview art (`src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs:45-59`, `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:38-61`).
- The grid currently has hover behavior but no selection behavior. `PollHover()` maps the cursor to a realized cell and dispatches native hover tooltip calls; `TryRealize()` attaches a hover relay and source badge, but no click relay (`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:227-321`, `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:341-380`).

## Target Architecture

Introduce an explicit top-level mode:

```csharp
internal enum CollectionPanelMode
{
    Items,
    Packages,
    Skills,
    Achievements,
}
```

Replace `ActiveType + PackagesOnly` decision points with this mode, while keeping `ECardType` as a property of cards/templates. This avoids adding another special boolean after `PackagesOnly`.

Recommended ownership:

- `Data/AchievementCards/`: embedded BPP achievement-card JSON definitions.
- `Game/CollectionPanel/Achievements/`: JSON loader, template projection, and selection DTOs.
- `Game/CollectionPanel/Data/`: shared catalog/filter VM changes.
- `Game/CollectionPanel/Grid/`: click relay and selected-card callback.
- `Game/CardArtReplacement/` plus `GameInterop/CardArtReplacement/`: custom-art identity expansion so BPP achievement cards can use the same `<templateId>.jpg` path without pretending to be packages.

## Data Model

Store authored cards in a new embedded JSON file:

`src/BazaarPlusPlus/Data/AchievementCards/achievement-cards.json`

Suggested schema:

```json
{
  "schemaVersion": 1,
  "cards": [
    {
      "achievementId": "first_ten_win",
      "templateId": "11111111-1111-4111-8111-111111111111",
      "internalName": "bpp_achievement_first_ten_win",
      "title": "First Ten Win",
      "description": "Win ten battles with this build uploaded to BazaarDB.",
      "flavorText": "",
      "hero": "Vanessa",
      "tier": "Diamond",
      "size": "Small",
      "tags": ["Tool"],
      "hiddenTags": ["Quest"],
      "sortKey": "001_first_ten_win",
      "baseArtKey": "some-known-valid-native-item-art-key"
    }
  ]
}
```

Notes:

- `achievementId` is the semantic key used by server progress and analyzers rules. Use a stable slug and never recycle it for a different rule.
- `templateId` is the card-rendering key. It must be a GUID because current CollectionPanel preview, `TCardBase.Id`, and custom-art lookup all use GUIDs. Generate it once and never change it after release.
- Never use a slug such as `bpp-achievement-first-ten-win` as `templateId`; keep that shape in `internalName` or `InstanceIdPrefix`.
- `internalName` should use a `bpp_achievement_` prefix so logs and diagnostics are searchable.
- `title` and `description` become `TCardLocalization.Title.Text` and `TCardLocalization.Description.Text`. `TLocalizableText` is only `Key + Text` in the current decompiled model (`decompiled/BazaarGameShared/BazaarGameShared.Domain.Core/TLocalizableText.cs:3-8`).
- `baseArtKey` should point to a known valid native item art key unless we add a dedicated base-material fallback. `CardPreviewItem.LoadArt()` only builds a card material when `HasValidArtKey()` and Addressables returns `CardAssetDataSO` (`decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewItem.cs:80-95`).
- Actual BPP art stays as `Resources/CustomCardArt/<templateId>.jpg`, reusing the existing installer/catalog/cache.

## Creating A New Achievement Card

1. Pick a stable slug for `achievementId`.
2. Generate a stable GUID for `templateId`.
3. Add one entry to `achievement-cards.json` with `achievementId`, `templateId`, `internalName`, `title`, `description`, `hero`, `tier`, `size`, `tags`, `hiddenTags`, `sortKey`, and `baseArtKey`.
4. Add `src/BazaarPlusPlus/Resources/CustomCardArt/<templateId>.jpg`.
5. Ensure the `.csproj` embeds the new JSON and existing `*.jpg` resource glob picks up the art.
6. Run an asset validation test that verifies:
   - `achievementId` values are non-empty and unique.
   - GUIDs are non-empty and unique.
   - every card has non-empty title and description.
   - every card has a matching `<templateId>.jpg`.
   - `baseArtKey` is non-empty.
7. In-game validation then confirms that the native card material is created from `baseArtKey` and replaced by the BPP JPEG.

## Template Projection

Do not mutate the game's static card map for v1. Instead, add a resolver used by CollectionPanel rendering:

```csharp
internal interface ICollectionCardTemplateResolver
{
    bool TryResolveTemplate(CollectionCardVm vm, out TCardBase template);
}
```

The resolver should:

- Return game static templates for normal item/package/skill cards.
- Return synthetic `TCardItem` templates for BPP achievements.

The minimal synthetic `TCardItem` for display:

```csharp
new TCardItem
{
    Id = dto.TemplateId,
    Version = "1.0.0",
    InternalName = dto.InternalName,
    InternalDescription = dto.Description,
    Type = ECardType.Item,
    Size = dto.Size,
    StartingTier = dto.Tier,
    Heroes = new HashSet<EHero> { dto.Hero },
    Tags = dto.Tags,
    HiddenTags = dto.HiddenTags,
    ArtKey = dto.BaseArtKey,
    Localization = new TCardLocalization
    {
        Title = new TLocalizableText { Text = dto.Title },
        Description = new TLocalizableText { Text = dto.Description },
        FlavorText = string.IsNullOrWhiteSpace(dto.FlavorText)
            ? null
            : new TLocalizableText { Text = dto.FlavorText },
    },
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [dto.Tier] = new TCardTier(),
    },
}
```

Keep it display-only: no abilities, no auras, no spawn eligibility, and no source-offer rules.

## Filtering Behavior

`Achievements` should be an exclusive mode like `Packages`, but backed by BPP definitions rather than `EHiddenTag.Package`.

Recommended behavior:

- Show only BPP achievement cards.
- Keep hero, quality, size, and keyword filters if product wants them.
- Hide merchant/trainer source filters entirely.
- Disable day gating by default; achievements are not shop availability.
- Sort by `sortKey` first or by existing tier/size sort if we want consistency with the rest of the panel.

Implementation direction:

- Add `CollectionPanelMode Mode` to `CollectionFilterState`.
- Derive `ActiveType` from mode only where native preview needs an item/skill branch.
- Replace `PackagesOnly` checks in `CollectionFilterEngine.Apply()` with a mode branch:
  - `Packages`: filter `card.IsPackage`.
  - `Achievements`: filter `card.Source == CollectionCardSource.Achievement`.
  - `Items`: filter normal item cards excluding packages and achievements.
  - `Skills`: filter normal skill cards.
- Update `CollectionTabProfile` to accept `CollectionPanelMode`, not just `ECardType`, so source/day/tag/keyword/size visibility is mode-specific.
- In `CollectionPanel.ApplyFilters()`, bypass source pool resolution for `Packages` and `Achievements`, matching the existing package bypass at `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:781-822`.

## Left-Side Mini Selected Card

Add click selection as a separate layer from hover.

Grid side:

- Add a `CollectionCardClickRelay` or extend hit-target setup to dispatch `OnCardClicked(CollectionCardVm vm)`.
- Store selection in `CollectionPanel`, not in the virtualizer. The virtualizer recycles card objects, so it must not own durable selected state.
- Selection can be single-select for v1. If clicking the selected card again should clear it, make that explicit.

UI side:

- Add a compact selected-card strip/rail in the left side of the panel, outside the virtualized grid content so it does not scroll away.
- Render either:
  - a tiny native preview using the same template resolver, or
  - a lightweight UITK row with art thumbnail, title, tier, and remove button.

Minimal selected-card DTO:

```csharp
internal readonly record struct CollectionSelectedCardDto(
    Guid TemplateId,
    CollectionCardSource Source,
    ETier Tier,
    ECardSize Size,
    int DisplaySpan,
    string InstanceIdPrefix
);
```

Why this is enough:

- `TemplateId + Source` resolves the full template from either static game data or BPP achievement definitions.
- `Tier` drives the native frame and synthetic instance tier.
- `Size / DisplaySpan` are needed for compact layout without recomputing from a potentially stale VM.
- `InstanceIdPrefix` prevents collisions in synthetic preview instances.
- Do not copy description, tags, heroes, art path, or localization into the selected DTO. Those belong to the catalog definition and should be resolved by `TemplateId`.

For persistence across sessions, add an optional `CatalogVersion` later. For in-memory selection within one open panel, the DTO above is sufficient.

## Implementation Phases

1. Data and validation:
   - Add `src/BazaarPlusPlus/Data/AchievementCards/achievement-cards.json`, `.csproj` embedding, and DTO parser.
   - Add tests for schema, uniqueness, required fields, and matching art files.
2. Mode refactor:
   - Add `CollectionPanelMode`.
   - Convert `CollectionFilterState`, `CollectionTabProfile`, view model, and tab callbacks.
   - Keep `Packages` behavior identical while making it a mode, not a boolean.
3. Catalog merge:
   - Build normal game cards from `GetCardMap()` as today.
   - Append BPP achievement cards from the embedded JSON.
   - Mark each VM with `CollectionCardSource`.
4. Native preview resolver:
   - Replace direct `BppStaticDataAccess.GetCardTemplate(staticData, vm.Id)` inside `CollectionCardFactory` with `ICollectionCardTemplateResolver`.
   - Use synthetic `TCardItem` for achievements.
5. Art pipeline:
   - Generalize `CardArtInjector.IsPackageTemplate()` to a custom-art identity check that returns true for packages and BPP achievements.
   - Keep live item visual replacement package-only unless achievements can appear in a real run.
   - Let CollectionPanel preview replacement cover BPP achievements.
6. Selection rail:
   - Add click relay and `CollectionSelectedCardDto`.
   - Add compact selected-card UI and clear/remove behavior.
   - Ensure virtualizer recycling does not drop selection.
7. Polish:
   - Add `成就` text to `CollectionPanelText`.
   - Include CJK text in font prewarm.
   - Verify responsive layout with four top buttons in the operation row.

## Tests And Verification

Automated:

- `dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`
- `dotnet run --project tests/CollectionGridLayout.Tests/CollectionGridLayout.Tests.csproj`
- `dotnet test tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj`
- New `AchievementCards.Tests` for JSON schema and matching art.
- If shared files are removed or moved, grep test `.csproj` compile includes first; this repo has exe-runner tests that include source files directly.

Build:

- `./run.sh test`
- `./run.sh build`

Manual runtime:

1. Launch through Steam: `open "steam://run/1617400"`.
2. Open CollectionPanel with `Tab`.
3. Confirm top row shows `物品 / 包裹 / 技能 / 成就`.
4. Click `成就`; verify only BPP achievement cards appear.
5. Hover an achievement card; verify title and description tooltips.
6. Click an achievement card; verify the compact selected-card rail appears and survives scroll/recycling.
7. Reopen the panel; verify the expected selection reset/persistence behavior.

## Open Decisions

- Should achievements be single-select or multi-select?
- Should achievement cards be grouped by hero, category, or completion state?
- Does `成就` use normal item frames, or do we eventually need a custom frame?
- Is the left mini card purely visual, or does it feed an export/share DTO later?
- Do we need localized title/description variants now, or is current `Text` enough for v1?
