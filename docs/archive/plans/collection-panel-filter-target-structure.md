---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at HEAD (7a68e8bb) — target structure (schema v4 + offerSegments + per-enchantment facets + source attribution) all shipped; residual "remaining work" is open-ended source-rule validation handled organically via direct collection-sources.json edits.

---
status: active
calibrated: 2026-06-10
note: "Schema v4, offerSegments, per-enchantment facets, and source attribution are already implemented. This plan keeps only the remaining validation and catalog-policy work."
---

# CollectionPanel Source Filter Follow-Up

## Current Code Baseline

- `CollectionSourceCatalog.ExpectedSchemaVersion` is already `4`, and mismatched JSON is rejected during catalog build (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:15`, `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:96-104`).
- Source entries already parse `offerSegments`; rules include hero mode, starting tier, size, tags, hidden tags, hidden-tag groups, enchantability, enchantment type, enchantment tags, and enchantment hidden tags (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:239-330`).
- `CollectionCardVm.From` already projects per-enchantment facets from `TCardItem.Enchantments` (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs:24-45`).
- `CollectionSourceOfferPoolResolver` already returns `OfferMatchesByCardId`, and the grid binds source attribution badges from those matches (`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:46-75`, `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:361-362`).

## Remaining Work

1. Validate the current `collection-sources.json` source rules against authoritative game behavior or an approved external reference. The comparison note in [bazaardb-merchant-filter-comparison.md](bazaardb-merchant-filter-comparison.md) is external research, not code-verified truth.
2. Decide whether any BazaarDB-only source such as Zurphin's Safari should remain out of scope. Current tests intentionally lock the catalog to the current source count after removing Zurphin's Safari; changing that requires a product decision and new game-data evidence.
3. Re-check the rare enchanted specialist merchant mappings against real spawn behavior or equivalent decompiled evidence before changing catalog rules. Do not infer those mappings from public prose alone.
4. If the catalog changes, keep schema v4 as the single path. Do not reintroduce v3 `offerRule` compatibility.

## Verification

- Run `dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj`.
- Run `dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`.
- For any source-count change, update the tests that assert merchant/trainer totals and cite the code/game-data evidence in the commit.
