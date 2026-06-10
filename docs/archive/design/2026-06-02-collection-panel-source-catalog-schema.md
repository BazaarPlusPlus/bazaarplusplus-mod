---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Collection Panel Structured Source Catalog

> Status: Implemented(schema 现为 v3,含 groups 字段;本文 JSON 示例仍为 v2 草案形态)
> Date: 2026-06-02
> Supersedes: `docs/design/archive/2026-06-02-collection-panel-offer-source-filtering.md`
> Scope: Replace the merchant/trainer source filtering plan with a BPP-owned structured source catalog and rule resolver. This design covers schema, DTOs, runtime flow, validation, and related cleanup for Collection Panel source filtering.

## Decision

Collection Panel source filtering should be fully owned by BazaarPlusPlus. The mod should not fall back to The Bazaar's old runtime `CardDealer` / `cardRepo` / spawn-pool resolver path for merchant and trainer filters.

The previous plan reused game source-card filtering. That made the result harder to explain and added runtime readiness branches that the user can hit. The new plan treats merchant/trainer source filtering as a curated, versioned BPP catalog: each source entry has a structured `offerRule`, and tests must prove every entry can be parsed and resolved before the feature ships.

This is a breaking change taken deliberately for the cleanest end-state (per repo policy): the old `description`-parsing resolver, the game-runtime fallback, and the v1 `merchant-trainer-portraits.json` shape are all removed in-place. No back-compat shims are kept.

`description` is UI text only. It is not a rule input.

## Current Context

Current source identity data lives at `Data/Encounters/merchant-trainer-portraits.json` (schemaVersion 1) and is loaded by `Game/CollectionPanel/Encounters/MerchantTrainerCatalog.cs`. The file name and directory are now misleading because the catalog is no longer only portrait metadata; it is the source identity catalog for merchant/trainer filtering.

Current source filtering has a BPP rule resolver in `Game/CollectionPanel/Encounters/CollectionSourceRuleOfferPoolResolver.cs`, **but that resolver derives its entire rule by switching on the lowercased source `description` string** (`CollectionSourceRuleOfferPoolResolver.cs:68-287`): ~40 description cases plus a `"charge"` substring special-case (`:182`) and name-based hero resolution via `TryResolveHero` (`:395-438`). The catalog JSON carries no structured rule — the rule for each of the 70 entries lives only as the `description` string this switch pattern-matches. An unrecognized description is the *only* thing that makes the resolver return `Unavailable` (`:285-286`), which is what triggers the game-runtime fallback in `Game/CollectionPanel/CollectionSourceOfferPoolCache.cs:32-38` (`GameInterop/EncounterOffers/EncounterOfferPoolResolver.cs`). The resolver's other non-`Ready` status, `Loading`, is emitted only when the card catalog is empty (`:22-23`) — an orthogonal condition already short-circuited upstream at `CollectionPanel.cs:651`.

This means the migration is a **rewrite, not an edit**: replacing `description` parsing with structured `offerRule` data requires re-authoring every current description case as catalog data and then deleting the description switch. See Migration Sequence. (The `Matches`-style predicate at `:292-319` is largely reusable; it is the per-entry rule *data* that must be re-authored.)

Current card eligibility is a boolean classifier in `Game/CollectionPanel/Data/CollectionCardClassifier.cs` (`IsCatalogCard` returns `bool`, `:44-54`). It rejects non-Item/Skill cards and weak art/template markers, but it does not expose a reason code. This should be hardened while changing the source filtering boundary.

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
- Do not parse merchant/trainer `description` strings at runtime (the new resolver reads structured `offerRule` data; the current description switch is deleted, not preserved).
- Do not keep a manual card-to-merchant table keyed by display text.
- Do not expose raw `ECardTag` or `EHiddenTag` filters in the UI.
- Do not introduce back-compat shims for the old `merchant-trainer-portraits.json` shape once the new catalog is in place.
- Do not localize source `name` / `description`. They are not surfaced as primary on-screen UI text, so English values are acceptable.

## Catalog Location

New data file:

```text
Data/CollectionSources/collection-sources.json
```

New code namespace and directory:

```text
Game/CollectionPanel/Sources/
```

The old `Data/Encounters/merchant-trainer-portraits.json` should be removed after the migration. The catalog is not encounter-generic; it is specifically the Collection Panel's merchant/trainer source catalog. (Confirmed Collection-Panel-owned: the portrait subsystem resolves sprites from game static data via a template id and does **not** read this file, so moving/deleting it does not affect portrait resolution — `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs:49-87`.)

## JSON Schema

Root:

```json
{
  "schemaVersion": 3,
  "entries": []
}
```

> 注:实际 v3 增加了顶层 `groups` 字段(CollectionSourceCatalog.cs:109-115 强制校验;Data/CollectionSources/collection-sources.json:3-11)。

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

- `sourceKey`: stable UI and cache identity. It is **generated by the catalog loader, not authored in JSON** (from `kind`, `name`, and hero). It does not include source tier. Dropping tier is a deliberate, breaking identity change: every existing `sourceKey` value changes (e.g. `merchant:ande:bronze:global` → `merchant:ande:global`), so `CollectionPanelSelectionState.DefaultMerchantSourceKey` and the tier-bearing assertions in the source/filter test projects must be updated in the same change. If a name/hero collision appears (now that tier is gone), append a short template-id fingerprint; add a test that forces such a collision, since the live 70-entry catalog currently has none and the fingerprint path is otherwise unexercised.
- `kind`: `Merchant` or `Trainer`. `Merchant` filters `ECardType.Item`; `Trainer` filters `ECardType.Skill`.
- `name`: internal display / tooltip label. Not localized (see Non-Goals).
- `availableHeroes`: heroes for which this source's **chip is visible**. An empty array means global. This governs chip visibility only; the offered-card hero scope is owned by `offerRule.heroMode`, and the two are independent (see Resolver Semantics).
- `description`: tooltip text only. Not localized; not a rule input.
- `portraitTemplateId`: one template id used to resolve the source portrait sprite (resolved from game static data, not from this file).
- `sourceTemplateIds`: all template ids that map the current encounter / choice screen back to this source. **Migration invariant: this must remain a superset of the entry's pre-migration `templateIds`**, so encounter→source mapping on open does not regress. `CollectionPanelOpenSelectionResolver` scans this set, not `portraitTemplateId`.
- `offerRule`: structured rule used to compute the offered card whitelist.

Removed fields:

- `tier` / `sourceTier`: source tier is not displayed, sorted, or used for offer filtering. (Today it only seeds the generated `sourceKey` and the roster label; the new key drops it — see above.)
- `offeredCardType`: redundant with `kind`.
- generic `templateIds`: split into `portraitTemplateId` and `sourceTemplateIds` because those are different responsibilities. `sourceTemplateIds` must remain a superset of the old list.

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

- `SelectedHero`: follow the currently selected UI hero, including `Common`. If a hero is selected, match only that hero. If no hero is selected, do not crop by hero.
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

**`FixedHero` migration note:** today there is no explicit fixed-hero field — the fixed hero is computed from the source *name*, including aliases (`cymon`, `nonna`, `kelsa`, `zosima`, `mr. tuskari`, `uncle odi`, `old zane`, …), via `TryResolveHero` (`CollectionSourceRuleOfferPoolResolver.cs:395-438`). Step 2 must port that name/alias → `EHero` table into explicit `hero` values, and validation must confirm every source previously classified as a fixed/source hero now carries a correct explicit `hero`.

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

**`NeutralOnly` migration note (Curio):** the sole `NeutralOnly` source today is **Curio** (`Sells Bronze-tier Neutral items`). This entry is an **intentional, documented behavior change** from the pre-migration `description` switch. The old resolver ANDed the active UI-hero filter on top of the neutral match, so selecting Curio while a concrete hero was selected returned an **empty** pool. The new resolver follows the `NeutralOnly` definition above — it ignores the selected UI hero and returns `Common` / neutral cards regardless of which hero (or none) is selected. This is a deliberate bug-fix, not silent drift: it is the one entry where the equivalence claim below does not hold (see Catalog Validation), and it is pinned by a dedicated resolver test (`NeutralOnly` selected-hero invariance).

`startingTier` modes:

- `AtMost`: candidate `StartingTier` rank is less than or equal to the configured tier.
- `Exact`: candidate `StartingTier` must equal the configured tier.

The first migration should keep the current **description-derived** behavior for tier merchants/trainers by using `AtMost`. Note this matches only the description-based resolver, not the game-data fallback's `SpawningFilters.ItemTierFilters` set behavior, which is being intentionally dropped. Confirm in-game that no current tier source actually needs an exact-tier or multi-tier-set rule before committing to `AtMost`-only. The schema still supports `Exact` if runtime validation later proves a source should only offer exactly one starting tier.

**`enchantableOnly` semantics caveat:** `enchantableOnly: true` maps to `CollectionCardVm.IsEnchantable`, which means "the item template defines enchantment slots" (true for the large majority of items) — *not* "this source sells pre-enchanted variants." This coarse semantic is carried over verbatim from current behavior. If a source genuinely needs "actually enchanted" semantics, model it with a hidden tag or a curated id list instead, and confirm in-game which entries truly need `enchantableOnly`.

## Runtime Models

DTO classes:

- `CollectionSourceCatalogDto`
- `CollectionSourceEntryDto`
- `CollectionSourceOfferRuleDto`
- `CollectionSourceStartingTierRuleDto`

The catalog is deserialized with Newtonsoft JSON (`MerchantTrainerCatalog.cs`), **not** MessagePack, so the public-DTO-graph MessagePack trap does not apply here — DTOs may stay narrow / internal. The four-DTO + runtime-model split is only justified if the runtime models enforce invariants the DTOs do not (enum parsing, required-field validation, `AppliesToHero`); if they are pure mirrors, collapse to one validated type. Enum fields parse from strings; an unknown enum value is a catalog-validation failure, not a silent default.

Runtime classes/enums:

- `CollectionSourceCatalog`
- `CollectionSourceEntry`
- `CollectionSourceKind`
- `CollectionSourceOfferRule`
- `CollectionSourceHeroMode`
- `CollectionSourceStartingTierMode`
- `CollectionSourceOfferPoolResolver`
- `CollectionSourceOfferPoolResult` and `CollectionSourceOfferPoolStatus` — the new resolver's result/status type, **defined under `Game/CollectionPanel/Sources/`**. Do *not* reuse `GameInterop/EncounterOffers/EncounterOfferPoolResult` / `EncounterOfferPoolStatus`; those belong to the game-runtime fallback path being removed. Post-removal the only meaningful statuses are `Ready` and `NoneSelected` — catalog-pending is gated upstream at `CollectionPanel.cs:651`, and `Unavailable` becomes a catalog-validation (test-time) failure rather than a runtime state.

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

`AppliesToHero` should preserve the current semantics: empty `AvailableHeroes` means global and should be visible for every selected hero, including `Common`.

## Source Filtering Flow

1. The user opens Collection Panel.
2. The panel resolves the initial selection from current run hero and current encounter ids.
3. The source catalog maps current encounter template ids through `sourceTemplateIds` to a `sourceKey`.
4. The UI shows source chips for the active tab:
   - Item tab: `CollectionSourceKind.Merchant`.
   - Skill tab: `CollectionSourceKind.Trainer`.
5. The UI filters visible source chips by selected hero, including `Common`, using `availableHeroes`.
6. The user selects one source chip.
7. `CollectionSourceOfferPoolResolver` applies the selected entry's `offerRule` to the already-built `CollectionCardVm` catalog and returns a set of candidate template ids.
8. `CollectionFilterEngine` receives that set as a source whitelist and ANDs it with the rest of the active filters.
9. The virtual grid receives the ordered visible list and renders only visible-window cards.

With a source selected, the normal hero filter should not crop the result a second time (the engine receives `ApplyHeroFilter = false`). The source rule owns hero semantics through `heroMode`. The resolver still receives the currently selected hero, including `Common`, on its **own** input (via `CollectionFilterState.SelectedHero`); this is separate from `CollectionFilterContext`, which only governs the post-resolution engine pass and carries no hero.

`availableHeroes` (chip visibility) and `heroMode` (offered-card scope) are independent by design and can legitimately differ. Concretely in the live catalog: a `FixedHero` merchant's chip is **shown to other heroes** (its fixed `hero` is deliberately **absent** from `availableHeroes`), and some `AllHeroes`-rule sources carry a non-empty `availableHeroes`. The pool only runs when a chip is selectable, and a chip is only selectable when visible, so this is not a user-facing contradiction. Because the two are genuinely independent, catalog validation deliberately does **not** assert a relationship between them (see Catalog Validation).

When the active tab or the selected hero changes such that the selected source no longer applies, the selected source must be cleared / re-validated. The existing clear-on-mismatch logic (`CollectionPanel.cs:826-841`) must be preserved through the refactor.

## Resolver Semantics

The resolver should work over `CollectionCardVm`, not raw game DTOs. This keeps the rule engine independent from game runtime resolver availability and lets tests construct rule inputs without booting the game.

No `CollectionCardVm` extension is required: the VM already exposes `Type`, `Size`, `StartingTier`, `Heroes`, `Tags`, `HiddenTags`, and `IsEnchantable` (`CollectionCardVm.cs:16-24`). The new resolver re-expresses the existing `Matches`-style predicate with structured `offerRule` inputs replacing `description` parsing.

Candidate card type is derived from source `kind`:

- `Merchant`: candidate `card.Type == ECardType.Item`.
- `Trainer`: candidate `card.Type == ECardType.Skill`.

Hero matching:

- `SelectedHero`: if the UI has a selected hero, including `EHero.Common`, accept cards whose `Heroes` contains that hero; otherwise accept all heroes.
- `AllHeroes`: accept all heroes.
- `FixedHero`: accept cards whose `Heroes` contains the configured fixed hero.
- `NeutralOnly`: accept cards whose `Heroes` contains `EHero.Common`.

Other filters:

- `startingTier`: rank comparison using a **shared** tier-ranking helper. `TierRank` is currently a private method duplicated in `CollectionFilterEngine.cs:135-144`, `CollectionSourceRuleOfferPoolResolver.cs:465-474`, and `EncounterOfferStaticPoolResolver.cs:261`; extract one shared helper (e.g. under `Data/`) and have the new resolver consume it rather than copying a fourth time.
- `sizesAny`: candidate size must be in the set.
- `tagsAny`: candidate `Tags` must overlap.
- `tagsNone`: candidate `Tags` must not overlap.
- `hiddenTagsAny`: candidate `HiddenTags` must overlap.
- `enchantableOnly`: candidate `IsEnchantable` must be true (see semantics caveat in Offer Rule Schema).

The resolver should return `Ready` for every valid catalog entry after the card catalog is available, and `NoneSelected` when no source is selected. Once `description` parsing is gone, `Unavailable` is no longer reachable at runtime: missing rule fields required for a mode, invalid enum names, or impossible schema combinations become catalog-validation (test-time) failures, not runtime statuses.

## Catalog Validation

Validation has two tiers with different migration-step dependencies.

**Schema / shape validation** — authorable as soon as the v2 JSON and DTOs exist (Migration steps 2-3):

- `schemaVersion` is the expected version (3).
- Every entry has a non-empty `sourceKey`, `kind`, `name`, `portraitTemplateId`, `sourceTemplateIds`, and `offerRule`.
- Every `sourceKey` is unique; a forced name+hero collision test exercises the fingerprint suffix.
- `portraitTemplateId` is a valid GUID and appears in `sourceTemplateIds`, unless a per-entry documented-exception flag is set.
- Every id in `sourceTemplateIds` is a non-empty GUID, and the set is a superset of the entry's pre-migration `templateIds`.
- `kind` and `heroMode` enum values are valid; rule arrays contain valid enum values.
- `FixedHero` always has a valid `hero`; `NeutralOnly` does not also specify `hero`.
- Every `startingTier` has a valid `mode` and `tier`.
- `availableHeroes` (chip visibility) and `heroMode` (offer-pool hero gate) are **independent by design** — do **not** assert a coherence relationship between them (see Source Filtering Flow). In the live catalog a `FixedHero` merchant's chip is **intentionally shown to other heroes**, so its fixed `hero` is deliberately **absent** from `availableHeroes`; and some `AllHeroes`-rule sources legitimately carry a non-empty `availableHeroes` for chip-visibility reasons. An earlier draft suggested asserting "`FixedHero`'s hero should appear in `availableHeroes`" — that is **backwards** for the live data and would false-reject real entries, so it is intentionally not validated.

**Resolver-coverage validation** — gated on the new resolver (Migration step 5+):

- Every current catalog entry resolves to `Ready` (never `Unavailable`) against a representative card catalog.
- The "representative card catalog" must be a fixture that, per rule shape, contains at least one matching and one non-matching card; assert the resolved whitelist is non-empty and excludes the non-match. (A resolver can return `Ready` with an *empty* whitelist if the fixture happens to contain no matching card, so `!= Unavailable` alone is too weak.)
- One-time equivalence check: for every current entry, the new structured `offerRule` produces the same template-id whitelist as today's `description` switch — so the migration does not silently change filter results. **Known, documented exception:** the sole `NeutralOnly` source (**Curio**) intentionally diverges — with a concrete hero selected the old switch returned an empty pool, the new resolver returns `Common` / neutral cards (see the `NeutralOnly` migration note). It is the only entry whose output changed, and it is a deliberate bug-fix pinned by a resolver test rather than a silent change.
- **Status (post-implementation):** this equivalence check was never committed as an executable test, and the old `description` switch was deleted in the same change, so it can no longer be run as designed. A post-implementation review replayed all 70 entries and found **Curio** to be the only behavioral divergence; treat the migration as equivalence-verified-by-review with that one documented exception, not by an automated test.

This validation guarantees internal consistency of the entries that are **present**; it does not prove completeness against game content. Detecting a merchant/trainer that exists in-game but is absent from the catalog would require an independent encounter roster to diff against (e.g. an extractor-produced index checked into `Data/`, regenerated per game version). If that completeness guarantee is wanted, specify the oracle explicitly; otherwise "an unexpressed merchant/trainer fails loudly" means a *present* entry whose rule the resolver cannot express (a malformed `offerRule`), which the resolver-coverage test catches.

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

**Owner / overlap:** loading-first open is already substantially shipped — see `docs/design/2026-05-31-collection-panel-first-load-performance.md` (P0/P1/P2 marked done). `Open()` already paints the shell before catalog work (`CollectionPanel.cs:236-244`; `LoadPanelAsync` at `:549-557`), and `CollectionPanelText.CatalogLoading()` + the animated loading label already exist. Do **not** re-plan loading-first here as new work. Instead close the sibling doc's known measurement gap: `EnsureView()` builds the full UITK tree synchronously before first paint (`CollectionPanelView.cs:201`, a ~900-line view) and is not yet separately timed. Add `EnsureView()` + first-N-`TryBind` instrumentation, gate any shell-splitting behind a logged baseline (mirroring the sibling doc's "don't claim fixed from build success alone" rule), and keep this work owned by the sibling doc rather than duplicated here.

## Memory And Virtualization

Grid virtualization remains the right rendering model. `CollectionGridVirtualizer` realizes only the current scroll window plus overscan; `CollectionCardPool` caps pooled cards per native card kind.

The memory review should focus on caches:

- `CollectionCardArtCache` has an LRU capacity (and already implements `AddRef`/`Release` + refcount-0 `Evict()`).
- `CollectionCardMaterialCache` currently accumulates one material per art key until panel runtime teardown (unbounded; cleared only by `DisposeAll`).
- `CollectionPanelOwnedMarker` and the Harmony destroy/load-art patches must release art references on rebind and destroy.

Add a bounded material strategy:

- either make material cache follow art cache eviction,
- or add a material LRU that only evicts materials with no active users — **this is the low-risk default, not an uncertain alternative**: the refcount + `CurrentArtKey` release plumbing already exists in `CollectionCardArtCache` and `CollectionItemLoadArtPatch` / `CollectionCardPreviewDestroyPatch`, and the material cache shares the same `artKey` at the same call site (`CollectionItemLoadArtPatch.cs:66/75`), so a material LRU can mirror the art cache's refcount with near-zero new mechanism,
- and always release all cache state on panel runtime teardown.

The target is that scrolling through the full catalog does not grow memory without a bound inside a single panel lifetime.

## CollectionFilterEngine Boundary

Keep `CollectionFilterEngine` as a pure function, but replace the loose `offerPool` plus `applyHeroFilter` pair with a context object.

Proposed shape:

```csharp
internal sealed class CollectionFilterContext
{
    public IReadOnlyCollection<Guid>? OfferedCardIds { get; init; }
    public bool ApplyHeroFilter { get; init; } = true;
}
```

The field is named `OfferedCardIds` (the resolver's **output** — resolved candidate card ids), deliberately distinct from the catalog entry's `sourceTemplateIds` (source identity ids). They are different GUID sets: do not wire source identity ids into the engine whitelist, or the grid collapses to the merchant card itself / empty.

Source whitelist still ANDs with ordinary filters. A selected source sets `ApplyHeroFilter = false` because source `heroMode` already decided how hero filtering works (the resolver applied it upstream).

## Migration Sequence

1. Add the new source catalog design, DTOs, and runtime source models under `Game/CollectionPanel/Sources/` — including the new `CollectionSourceOfferPoolResult` / `CollectionSourceOfferPoolStatus` type (no dependency on `GameInterop/EncounterOffers`) — without wiring them into the live panel.
2. Create `Data/CollectionSources/collection-sources.json` by **authoring a new structured `offerRule` per entry** — a hand-translation of the current `description` switch in `CollectionSourceRuleOfferPoolResolver.TryCreate` (`:68-287`), including the `"charge"` special-case (`:182`) and the `TryResolveHero` name/alias → `EHero` table (`:395-438`). This is not a copy of the v1 file (which has no `offerRule`). Generate `sourceKey` without tier; ensure `sourceTemplateIds` ⊇ the old `templateIds`.
3. Add schema / shape validation tests (steps 1-2 are sufficient for these).
4. Implement the structured `CollectionSourceOfferPoolResolver` over `CollectionCardVm`, returning the new result type. Add resolver-coverage + equivalence tests (gated on this step).
5. Update the `CollectionPanel` source selection flow to use the new catalog, resolver, result type, and `CollectionFilterContext.OfferedCardIds`. Update `CollectionPanelSelectionState.DefaultMerchantSourceKey` to the tier-less form, and update the tier-bearing assertions in `tests/CollectionSourceFiltering.Tests` and `tests/CollectionFilterEngine.Tests`.
6. **Delete** the description-parsing `CollectionSourceRuleOfferPoolResolver` (do not supplement it).
7. Remove the game-runtime fallback to `EncounterOfferPoolResolver` (the `Unavailable` branch at `CollectionSourceOfferPoolCache.cs:32-38`).
8. Delete the now fallback-only `GameInterop/EncounterOffers` code — `EncounterOfferPoolResolver`, `EncounterOfferStaticPoolResolver`, `EncounterOfferHeroMapper`, `EncounterOfferPoolRules`. The result/status contract lives under `Game/CollectionPanel/Sources/` (step 1), so deleting the directory does not orphan the panel. Update `tests/CollectionSourceFiltering.Tests.csproj` Compile-Includes (it currently hard-links these source files).
9. **Remove the now-dead source-pool retry path**: `CollectionSourcePoolRetryState`, `TickSourcePoolRetry` / `ScheduleSourcePoolRetry` / `ResetSourcePoolRetry`, `_isResolvingSourcePool`, the `Loading` / `Unavailable` UI branches (`CollectionPanel.cs:670-693`), and the `SourcePoolLoading` / `SourcePoolUnavailable` copy. With description parsing gone and the fallback removed, none of these are reachable (the empty-catalog `Loading` is already gated at `CollectionPanel.cs:651`).
10. Replace the catalog resource reference in `BazaarPlusPlus.csproj`. The embed is a per-file `EmbeddedResource` Include (not a `Data/**/*.json` glob) and the loader resolves by manifest-name suffix, so **add an explicit include for `Data/CollectionSources/collection-sources.json`**, remove the `merchant-trainer-portraits.json` include, and delete the old JSON file. A forgotten embed yields a *silent empty catalog at runtime* (the disk-reading validation test would not catch it — verify the embed). Also update the two hard-coded JSON paths in `tests/CollectionSourceFiltering.Tests/Program.cs`.
11. Refactor card eligibility to reasoned classification.
12. Add bounded material cache behavior. (Loading-first open is owned by the sibling first-load doc — only add the `EnsureView` / first-bind instrumentation gap there.)
13. Run targeted tests and perform in-game validation through Steam.

## Verification

Automated targets — all three existing projects are `Exe`-runners; run with `dotnet run --project …`, **not** `dotnet test` (which builds but does not execute them):

- `dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj`
- `dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`
- `dotnet run --project tests/CollectionGridLayout.Tests/CollectionGridLayout.Tests.csproj`
- new source catalog / source rule tests (inherit the exe-runner shape)
- new card classifier reason tests

Runtime validation:

- Launch The Bazaar through Steam.
- Open Collection Panel from settings dock and F9.
- Confirm the panel shell appears immediately with loading text on first open.
- Verify Item tab merchant sources and Skill tab trainer sources.
- Verify `SelectedHero`, `AllHeroes`, `FixedHero`, and `NeutralOnly` source rules using representative entries.
- Verify source chip selection, deselection, tab switching, hero switching, reset, search, tier, size, package toggle, and scrolling.
- Read `BepInEx/LogOutput.log` for `[BPP][CollectionPanel]`, source catalog, and source resolver warnings.
