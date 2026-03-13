# Item Enchant Preview Redesign

**Date:** 2026-03-13

**Goal**

Refactor item enchant preview into a feature-scoped module that isolates preview computation from live game state, fixes cache correctness, and keeps the Harmony patch as a thin integration layer.

## Current Problems

1. `Game/ItemEnchantPreviewBuilder.cs` combines feature gating, candidate selection, preview rendering, formatting, and caching in one static class.
2. `RenderTooltipText` temporarily mutates the live `ItemCard` by changing `Enchantment` and `Attributes`, then restores it. This is fragile around re-entrancy, observers, and any code that reads the card during tooltip rendering.
3. The cache key only includes instance identity, section, current enchant, and candidate list. It omits rendered attribute inputs and any runtime values that affect tooltip resolution, so the cache can return stale text.
4. The patch layer and domain logic are only partially separated. The patch is thin, but the feature itself is not modular.
5. There is no local automated coverage around preview eligibility, candidate filtering, formatting, or cache invalidation.

## Design Goals

1. Keep `Patches/Tooltips/ItemEnchantPreviewPatch.cs` limited to Harmony integration.
2. Move item enchant preview logic into `Game/ItemEnchantPreview/`.
3. Eliminate mutation of the live `ItemCard` during preview rendering.
4. Make cache keys depend on render inputs rather than object identity alone.
5. Create seams for targeted tests around pure logic.
6. Preserve current user-facing behavior unless the current behavior is clearly incorrect.

## Recommended Architecture

### Module Layout

Create a feature folder:

- `Game/ItemEnchantPreview/ItemEnchantPreviewService.cs`
- `Game/ItemEnchantPreview/ItemEnchantPreviewEligibility.cs`
- `Game/ItemEnchantPreview/ItemEnchantPreviewFormatting.cs`
- `Game/ItemEnchantPreview/ItemEnchantPreviewCache.cs`
- `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewSnapshot.cs`
- `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewSnapshotFactory.cs`
- `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewRenderer.cs`

Keep the existing patch at:

- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`

Delete or replace:

- `Game/ItemEnchantPreviewBuilder.cs`

### Responsibilities

`ItemEnchantPreviewService`

- Entry point used by the patch.
- Accepts a `Card` or `ItemCard` plus available enchantments.
- Applies feature gating and candidate selection.
- Builds a preview snapshot for each candidate.
- Consults the cache and returns `List<TooltipSegment>`.

`ItemEnchantPreviewEligibility`

- Encodes whether preview should render for a given card and state.
- Checks item type, supported sections, combat guard, and any future feature flags.
- Keeps gating logic separate from rendering logic.

`ItemEnchantPreviewSnapshot`

- Represents the immutable render input for a single previewed enchant.
- Contains current enchant, target enchant, resolved preview attributes, display label/color metadata, and references needed for tooltip rendering.
- Never exposes mutable state from the live item.

`ItemEnchantPreviewSnapshotFactory`

- Reads the live `ItemCard` once.
- Produces snapshot instances by combining current item data with enchantment template data.
- Owns merge rules for preview attributes.

`ItemEnchantPreviewRenderer`

- Converts a snapshot into tooltip text.
- Uses a preview context or cloned card instance so tooltip token resolution sees preview values without mutating the original card.
- Handles fallback behavior when localization or tooltip rendering fails.

`ItemEnchantPreviewFormatting`

- Maps enchantment types to labels and colors.
- Produces `TooltipSegment` rich text lines from rendered body text.
- Keeps presentation formatting out of business logic.

`ItemEnchantPreviewCache`

- Stores final rendered segments for a short duration.
- Cache key is based on render inputs, not only object identity.
- Encapsulates expiration and capacity policy.

## Rendering Strategy

The critical design change is to stop mutating the live `ItemCard`.

There are two viable implementations:

1. Create a preview-only cloned `ItemCard` and point `TooltipBuilder` at the clone.
2. Create a thin preview context object that supplies preview enchant and attributes to tooltip resolution.

The preferred path is option 1 because it keeps compatibility with existing tooltip APIs and reduces the amount of Bazaar-specific logic we need to replicate. The clone should be local to rendering, populated only with the fields needed by `TooltipBuilder`, and discarded after use. If cloning the concrete card type is too expensive or awkward, the fallback is a preview context adapter, but that should be a second choice.

## Cache Design

The cache should key on the values that affect final text. At minimum:

- Item template identity
- Card instance identity
- Card section
- Current enchant
- Preview enchant
- Preview attributes used for token resolution
- Candidate set ordering

If `Data.Run` contributes to resolved values, the cache must either include a run-scoped discriminator or avoid caching those cases. Correctness matters more than the current two-second reuse window.

The cache remains short-lived because tooltips are UI-driven and repetitive within a single hover session, but stale output is worse than recomputation.

## Data Flow

1. `ItemEnchantPreviewPatch` receives the base passive tooltip block.
2. The patch asks `ItemEnchantPreviewService` for preview segments.
3. The service checks eligibility and gets available enchantment candidates.
4. For each candidate, the snapshot factory creates a preview snapshot.
5. The cache is consulted using the snapshot-based key.
6. On miss, the renderer produces text from the snapshot.
7. Formatting converts rendered text into `TooltipSegment` instances.
8. The patch appends the final strings to the passive tooltip block.

## Error Handling

1. The patch should keep its top-level catch and logging, because Harmony patch failures must not break the game UI.
2. Inside the module, broad `catch` blocks should be narrowed where practical.
3. Fallbacks are acceptable for localization and formatting, but failures should be logged at the feature layer when they indicate bad assumptions rather than missing text.
4. Snapshot creation and rendering should fail closed by skipping a broken preview candidate, not by corrupting the base tooltip.

## Testing Strategy

This feature needs test seams even if the repository currently lacks an active test project.

Priority coverage:

1. Eligibility rules for section/type/combat state.
2. Candidate filtering, including dedupe and current-enchant exclusion.
3. Snapshot attribute merge rules.
4. Cache key correctness when preview-relevant attributes change.
5. Formatting output for labels, colors, and segment text.

If Bazaar runtime types are too difficult to construct in unit tests, keep pure logic in testable helper classes and minimize direct game-object dependencies.

## Migration Plan

1. Introduce the new `Game/ItemEnchantPreview/` module alongside the existing builder.
2. Move pure logic first: eligibility, formatting, cache key creation.
3. Add snapshot and renderer path, then switch the patch to the new service.
4. Remove the old builder after parity verification.
5. Verify tooltip behavior manually in stash/hand and non-render states.

## Non-Goals

1. Changing the visual copy or heading text beyond what is required for parity.
2. Broad tooltip system refactors outside the item enchant preview path.
3. Reworking unrelated patch organization in this change.
