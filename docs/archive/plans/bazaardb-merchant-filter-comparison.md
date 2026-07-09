---
status: superseded
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: docs/ARCHITECTURE.md
---

> Status: SUPERSEDED: external-BazaarDB decision note (calibrated 2026-06-10) overtaken by direct game-grounded catalog curation; catalog is now 71/49/22 (CollectionSourceFiltering.Tests Program.cs:298-311), schema-v4-only locked, Zurphin's Safari still excluded.

---
status: needs-human-decision
calibrated: 2026-06-10
note: "External BazaarDB observations are research input only. Refresh and verify them before changing code or catalog policy."
---

# BazaarDB Merchant Filter Comparison

## Purpose

This note preserves the unresolved decisions from the BazaarDB comparison pass. It is not an implementation reference, and its external observations must not be copied into `ARCHITECTURE.md` unless they are rechecked against current code, decompiled game behavior, or an explicit product decision.

## Current Code Baseline

- The source-catalog DTO already uses `offerSegments`; each segment has a key, kind, rarity label, and rule (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceDtos.cs:45-61`).
- Rules already include hero mode, starting tier, sizes, tags, hidden tags, hidden-tag groups, enchantability, enchantment types, enchantment tags, and enchantment hidden tags (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceDtos.cs:64-101`, `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferRule.cs:21-70`).
- The resolver now iterates every source segment, returns the union set of offered card ids, and preserves per-card source matches for attribution badges (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:22-33`, `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:361-362`).
- Enchanted segments are first-class: `CollectionSourceOfferPoolResolver` matches enchantment constraints against projected per-enchantment facets (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:64-83`, `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:123-149`, `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs:24-45`).
- The current catalog count is intentionally locked to 69 total sources, 47 merchants, and 22 trainers after removing Zurphin's Safari (`tests/CollectionSourceFiltering.Tests/Program.cs:289-311`).
- `SelectedHero` rules match the selected concrete hero only; Common cards are not included unless Common is selected (`tests/CollectionSourceFiltering.Tests/Program.cs:653-667`).

## External Research Inputs To Recheck

- A prior manual BazaarDB snapshot reported 47 merchant results and 23 trainer results, including Zurphin's Safari. Treat that as a stale external observation until refreshed.
- The comparison suggested BazaarDB source pools are tag-driven rather than tooltip-text-driven, with base/reference hidden tags commonly matched explicitly.
- The comparison suggested rare enchanted source segments for merchants such as Knightshade, Hef, Chronos, Freiya, and Cobweb. Current code can represent those segments; the remaining question is whether the catalog rules are authoritative.

## Decisions Needed

1. Choose the authority for source-pool truth: decompiled/current game behavior, an approved external reference, or an explicit product rule.
2. Decide whether Zurphin's Safari should remain excluded from the BPP source catalog.
3. Revalidate the rare enchanted specialist mappings before changing `collection-sources.json`.
4. Keep schema v4 as the single path for any future catalog edits; do not restore v3 `offerRule` compatibility.

## Verification

- Run `dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj`.
- Run `dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`.
- If source counts or source rules change, update the count/rule tests in the same change and cite the code or game-data evidence that justified it.
