# Collection Panel Structured Source Catalog

> Status: Draft
> Date: 2026-06-02
> Supersedes: `docs/design/archive/2026-06-02-collection-panel-offer-source-filtering.md`
> Scope: Replace the merchant/trainer source filtering plan with a BPP-owned structured source catalog and rule resolver. This design covers schema, DTOs, runtime flow, validation, and related cleanup for Collection Panel source filtering.

## Decision

Collection Panel source filtering should be fully owned by BazaarPlusPlus. The mod should not fall back to The Bazaar's old runtime `CardDealer` / `cardRepo` / spawn-pool resolver path for merchant and trainer filters.

The previous plan reused game source-card filtering. That made the result harder to explain and added runtime readiness branches that the user can hit. The new plan treats merchant/trainer source filtering as a curated, versioned BPP catalog: each source entry has a structured `offerRule`, and tests must prove every entry can be parsed and resolved before the feature ships.

`description` is UI text only. It is not a rule input.

## Current Context

Current source identity data lives at `Data/Encounters/merchant-trainer-portraits.json` and is loaded by `Game/CollectionPanel/Encounters/MerchantTrainerCatalog.cs`. The file name and directory are now misleading because the catalog is no longer only portrait metadata; it is the source identity catalog for merchant/trainer filtering.

Current source filtering has a BPP rule resolver in `Game/CollectionPanel/Encounters/CollectionSourceRuleOfferPoolResolver.cs`, but `Game/CollectionPanel/CollectionSourceOfferPoolCache.cs` still falls back to `GameInterop/EncounterOffers/EncounterOfferPoolResolver.cs` when the local rule resolver reports unavailable. That fallback should be removed once structured rules are in the catalog.

Current card eligibility is a boolean classifier in `Game/CollectionPanel/Data/CollectionCardClassifier.cs`. It rejects non-Item/Skill cards and weak art/template markers, but it does not expose a reason code. This should be hardened while changing the source filtering boundary.

## Goals

- Define a source catalog schema that separates source identity, portrait lookup, current-encounter lookup, and offered-card filtering.
- Make `kind` the source of truth for whether a source filters Items or Skills: `Merchant` means Item, `Trainer` means Skill.
- Remove unused source metadata such as source tier from the runtime contract.
- Make missing or malformed source rules fail in tests, not at runtime.
- Keep source filter results explainable: a selected source produces a template-id whitelist by applying one structured rule to the already-built `CollectionCardVm` catalog.
- Keep existing user-facing filter semantics: source whitelist AND package, tier, size, search, sort, and other active filters.
- Make opening the panel loading-first: show the shell and loading animation before heavy catalog/source work proceeds.
- Review collection card eligibility and grid cache memory behavior as part of the same change family.

## Non-Goals

- Do not predict the exact cards generated in the current run.
- Do not call game runtime `CardDealer`, `cardRepo`, or `FilterCards` for source filtering.
- Do not parse merchant/trainer `description` strings at runtime.
- Do not keep a manual card-to-merchant table keyed by display text.
- Do not expose raw `ECardTag` or `EHiddenTag` filters in the UI.
- Do not introduce back-compat shims for the old `merchant-trainer-portraits.json` shape once the new catalog is in place.

## Catalog Location

New data file:

```text
Data/CollectionSources/collection-sources.json
```

New code namespace and directory:

```text
Game/CollectionPanel/Sources/
```

The old `Data/Encounters/merchant-trainer-portraits.json` should be removed after the migration. The catalog is not encounter-generic; it is specifically the Collection Panel's merchant/trainer source catalog.

## JSON Schema

Root:

```json
{
  "schemaVersion": 2,
  "entries": []
}
```

Entry:

```json
{
  "sourceKey": "merchant:ande:global",
  "kind": "Merchant",
  "name": "Ande",
  "availableHeroes": [],
  "description": "Sells Small items",
  "portraitTemplateId": "60e67364-7aab-4ffa-9751-db5ec0caf452",
  "sourceTemplateIds": [
    "60e67364-7aab-4ffa-9751-db5ec0caf452",
    "705c0d8e-8513-49ce-82e8-81782cdac316"
  ],
  "offerRule": {
    "heroMode": "SelectedHero",
    "sizesAny": ["Small"]
  }
}
```

Field rules:

- `sourceKey`: stable UI and cache identity. It should not include source tier. If a name/hero collision appears, append a short template-id fingerprint.
- `kind`: `Merchant` or `Trainer`. `Merchant` filters `ECardType.Item`; `Trainer` filters `ECardType.Skill`.
- `name`: display label and tooltip label.
- `availableHeroes`: heroes for which this source is visible. An empty array means global.
- `description`: tooltip text only.
- `portraitTemplateId`: one template id used to resolve the source portrait sprite.
- `sourceTemplateIds`: all template ids that should map the current encounter / choice screen back to this source.
- `offerRule`: structured rule used to compute the offered card whitelist.

Removed fields:

- `tier` / `sourceTier`: source tier is not displayed, sorted, or used for filtering.
- `offeredCardType`: redundant with `kind`.
- generic `templateIds`: split into `portraitTemplateId` and `sourceTemplateIds` because those are different responsibilities.

## Offer Rule Schema

All rule fields are ANDed together. Array fields are ANY within that field.

```json
{
  "heroMode": "SelectedHero",
  "startingTier": {
    "mode": "AtMost",
    "tier": "Gold"
  },
  "sizesAny": ["Small", "Medium"],
  "tagsAny": ["Weapon"],
  "tagsNone": ["Weapon"],
  "hiddenTagsAny": ["Burn", "BurnReference"],
  "enchantableOnly": true
}
```

Supported `heroMode` values:

- `SelectedHero`: follow the currently selected UI hero. If a concrete hero is selected, match that hero plus `Common`. If no concrete hero is selected, do not crop by hero.
- `AllHeroes`: ignore the current UI hero and allow all heroes to pass into the remaining conditions.
- `FixedHero`: ignore the current UI hero and match only the `hero` field.
- `NeutralOnly`: ignore the current UI hero and match only `Common` / neutral cards.

`FixedHero` example:

```json
{
  "heroMode": "FixedHero",
  "hero": "Jules"
}
```

`NeutralOnly` example:

```json
{
  "heroMode": "NeutralOnly",
  "startingTier": {
    "mode": "AtMost",
    "tier": "Bronze"
  }
}
```

`startingTier` modes:

- `AtMost`: candidate `StartingTier` rank is less than or equal to the configured tier.
- `Exact`: candidate `StartingTier` must equal the configured tier.

The first migration should keep the current behavior for tier merchants/trainers by using `AtMost`. The schema still supports `Exact` if runtime validation later proves that a source should only offer exactly one starting tier.

## Runtime Models

DTO classes:

- `CollectionSourceCatalogDto`
- `CollectionSourceEntryDto`
- `CollectionSourceOfferRuleDto`
- `CollectionSourceStartingTierRuleDto`

Runtime classes/enums:

- `CollectionSourceCatalog`
- `CollectionSourceEntry`
- `CollectionSourceKind`
- `CollectionSourceOfferRule`
- `CollectionSourceHeroMode`
- `CollectionSourceStartingTierMode`
- `CollectionSourceOfferPoolResolver`

`CollectionSourceEntry` should expose:

- `SourceKey`
- `Kind`
- `Name`
- `AvailableHeroes`
- `Description`
- `PortraitTemplateId`
- `SourceTemplateIds`
- `OfferRule`
- `AppliesToHero(EHero hero)`

`AppliesToHero` should preserve the current semantics: empty `AvailableHeroes` means global and should be visible for every concrete hero.

## Source Filtering Flow

1. The user opens Collection Panel.
2. The panel resolves the initial selection from current run hero and current encounter ids.
3. The source catalog maps current encounter template ids through `sourceTemplateIds` to a `sourceKey`.
4. The UI shows source chips for the active tab:
   - Item tab: `CollectionSourceKind.Merchant`.
   - Skill tab: `CollectionSourceKind.Trainer`.
5. The UI filters visible source chips by selected concrete hero using `availableHeroes`.
6. The user selects one source chip.
7. `CollectionSourceOfferPoolResolver` applies the selected entry's `offerRule` to the already-built `CollectionCardVm` catalog and returns a set of candidate template ids.
8. `CollectionFilterEngine` receives that set as a source whitelist and ANDs it with the rest of the active filters.
9. The virtual grid receives the ordered visible list and renders only visible-window cards.

With a source selected, the normal hero filter should not crop the result a second time. The source rule owns hero semantics through `heroMode`.

## Resolver Semantics

The resolver should work over `CollectionCardVm`, not raw game DTOs. This keeps the rule engine independent from game runtime resolver availability and lets tests construct rule inputs without booting the game.

Candidate card type is derived from source `kind`:

- `Merchant`: candidate `card.Type == ECardType.Item`.
- `Trainer`: candidate `card.Type == ECardType.Skill`.

Hero matching:

- `SelectedHero`: if the UI has a selected concrete hero, accept cards whose `Heroes` contains that hero or `EHero.Common`; otherwise accept all heroes.
- `AllHeroes`: accept all heroes.
- `FixedHero`: accept cards whose `Heroes` contains the configured fixed hero.
- `NeutralOnly`: accept cards whose `Heroes` contains `EHero.Common`.

Other filters:

- `startingTier`: rank comparison using the same explicit tier ranking already used by `CollectionFilterEngine`.
- `sizesAny`: candidate size must be in the set.
- `tagsAny`: candidate `Tags` must overlap.
- `tagsNone`: candidate `Tags` must not overlap.
- `hiddenTagsAny`: candidate `HiddenTags` must overlap.
- `enchantableOnly`: candidate `IsEnchantable` must be true.

The resolver should return `Ready` for every valid catalog entry after the card catalog is available. Missing rule fields that are required for a mode, invalid enum names, or impossible schema combinations are catalog validation failures.

## Catalog Validation

Add a catalog contract test that reads the full signed-in JSON and checks:

- `schemaVersion` is the expected version.
- Every entry has a non-empty `sourceKey`, `kind`, `name`, `portraitTemplateId`, `sourceTemplateIds`, and `offerRule`.
- Every `sourceKey` is unique.
- `portraitTemplateId` is valid and appears in `sourceTemplateIds`, unless there is a documented exception.
- Every id in `sourceTemplateIds` is a non-empty GUID.
- `kind` and `heroMode` enum values are valid.
- `FixedHero` always has a valid `hero`.
- `NeutralOnly` does not also specify `hero`.
- Every `startingTier` has a valid `mode` and `tier`.
- Rule arrays contain valid enum values.
- Every current catalog entry can be resolved by the BPP resolver against a representative card catalog.

The test should fail loudly if a future data update introduces an unexpressed merchant/trainer.

## Card Eligibility Cleanup

Replace boolean-only catalog eligibility with a reasoned classification result.

Proposed enum:

```csharp
internal enum CollectionCardEligibilityReason
{
    Accepted,
    UnsupportedType,
    MissingArtKey,
    InvalidArtKey,
    PlaceholderArtKey,
    MaterialArtKey,
    DebugTemplate,
    TemplateInternalName,
}
```

Proposed result:

```csharp
internal sealed class CollectionCardClassification
{
    public bool IsCatalogCard { get; init; }
    public CollectionCardEligibilityReason EligibilityReason { get; init; }
    public bool IsPackage { get; init; }
    public IReadOnlyCollection<CollectionMerchantKind> Merchants { get; init; }
}
```

Packages should not be removed from the catalog. They should be marked by `IsPackage` and hidden by default through `IncludePackages == false`.

This change makes the current `Invalid`, `Placeholder`, `.mat`, `[DEBUG]`, and `TEMPLATE` rules auditable and testable by reason, not just by final boolean.

## Loading-First Open

The panel should show a visible shell and loading animation immediately after F9 or settings dock activation.

Target behavior:

1. `Open()` applies selection and makes the panel visible.
2. The first rendered frame shows the shell, count/status/loading UI, and empty grid.
3. Source catalog load, catalog VM build, initial filter application, and source chip/icon binding happen in subsequent frames.

The code already has `LoadPanelAsync`, `CollectionPanelText.CatalogLoading()`, and a loading label. The implementation should audit the first-open path for remaining synchronous work:

- first `EnsureView()` tree creation,
- source catalog embedded JSON load,
- first source chip creation,
- first source portrait async load scheduling,
- initial `RefreshView()` work.

If the shell itself is heavy, split shell creation from deferred source controls and portrait binding.

## Memory And Virtualization

Grid virtualization remains the right rendering model. `CollectionGridVirtualizer` realizes only the current scroll window plus overscan; `CollectionCardPool` caps pooled cards per native card kind.

The memory review should focus on caches:

- `CollectionCardArtCache` has an LRU capacity.
- `CollectionCardMaterialCache` currently accumulates one material per art key until panel runtime teardown.
- `CollectionPanelOwnedMarker` and the Harmony destroy/load-art patches must release art references on rebind and destroy.

Add a bounded material strategy:

- either make material cache follow art cache eviction,
- or add a material LRU that only evicts materials with no active users,
- and always release all cache state on panel runtime teardown.

The target is that scrolling through the full catalog does not grow memory without a bound inside a single panel lifetime.

## CollectionFilterEngine Boundary

Keep `CollectionFilterEngine` as a pure function, but replace the loose `offerPool` plus `applyHeroFilter` pair with a context object.

Proposed shape:

```csharp
internal sealed class CollectionFilterContext
{
    public IReadOnlyCollection<Guid>? SourceTemplateIds { get; init; }
    public bool ApplyHeroFilter { get; init; } = true;
}
```

Source whitelist still ANDs with ordinary filters. A selected source sets `ApplyHeroFilter = false` because source `heroMode` already decided how hero filtering works.

## Migration Sequence

1. Add the new source catalog design and tests without changing runtime behavior.
2. Create `Data/CollectionSources/collection-sources.json` from the current merchant/trainer catalog.
3. Add the new DTOs and runtime source models under `Game/CollectionPanel/Sources/`.
4. Add catalog validation tests.
5. Implement the structured source offer resolver over `CollectionCardVm`.
6. Update `CollectionPanel` source selection flow to use the new catalog and resolver.
7. Remove the fallback to `EncounterOfferPoolResolver`.
8. Delete or shrink unused `GameInterop/EncounterOffers` code.
9. Replace old catalog resource references in `BazaarPlusPlus.csproj`.
10. Refactor card eligibility to reasoned classification.
11. Audit and improve loading-first open behavior.
12. Add bounded material cache behavior.
13. Run targeted tests and perform in-game validation through Steam.

## Verification

Automated targets:

- `tests/CollectionSourceFiltering.Tests`
- `tests/CollectionFilterEngine.Tests`
- `tests/CollectionGridLayout.Tests`
- new source catalog / source rule tests
- new card classifier reason tests

Runtime validation:

- Launch The Bazaar through Steam.
- Open Collection Panel from settings dock and F9.
- Confirm the panel shell appears immediately with loading text on first open.
- Verify Item tab merchant sources and Skill tab trainer sources.
- Verify `SelectedHero`, `AllHeroes`, `FixedHero`, and `NeutralOnly` source rules using representative entries.
- Verify source chip selection, deselection, tab switching, hero switching, reset, search, tier, size, package toggle, and scrolling.
- Read `BepInEx/LogOutput.log` for `[BPP][CollectionPanel]`, source catalog, and source resolver warnings.
