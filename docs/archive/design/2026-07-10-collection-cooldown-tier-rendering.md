---
status: implemented
archived: 2026-07-11
calibrated: 2026-07-11
superseded-by: code
---

> Status: IMPLEMENTED. Approach 1 shipped with PR#17: two-tier cooldowns use the native fusing layout (`CollectionTierTooltipPatch.cs:79` `SetCooldown(values[0], canFuse: true, values[1])`), longer chains use the compact merged string (`:83` `MergeCooldown`). Retained as the design-decision record.

# Collection cooldown tier rendering

## Background

Collection tooltips merge values from every available card tier. Body text can render the
game's `Fusion` TMP sprite, but the cooldown clock uses a separate `TooltipClockController`
with dedicated normal and fusing labels.

## Current problem

The cooldown patch initially sent the body-text tier chain to `CooldownRenderer.SetCooldown`.
The clock label does not have the body tooltip's sprite asset, so `<sprite name=Fusion>` rendered
as a question-mark box. Replacing the sprite with `>` fixed the missing glyph, but the normal
single-value font still renders a four-tier chain far too large and wraps it over the card title.

## Candidate approaches

1. Use `CooldownRenderer.SetCooldown(before, true, after)` for two tiers, which activates the
   game's native fusing layout. For three or more tiers, remove trailing `.0`, use a compact ASCII
   separator, and wrap the chain in a relative TMP size tag. This reuses native UI where it fits
   and keeps longer chains inside the clock.
2. Add the body tooltip sprite asset to the clock labels at runtime. This couples the patch to
   private TMP fields and still leaves the wrapping problem.
3. Move all cooldown tier values into a new body row and leave the clock unchanged. This is
   visually clean but changes where users look for cooldown and duplicates native content.

## Proposed fix

Use approach 1. Two-tier cards, including the reported `7.0 -> 5.0` case, get the native
before/after display. Longer chains retain all values as a compact `8>7>6>5`-style string at 36%
of the native single-value font, with no unsupported rich-text sprite.

## Verification

- Regression-test that cooldown merging contains no `Fusion` sprite markup.
- Preserve the existing body-text merger test with the `Fusion` sprite.
- Run CollectionGridLayout tests, architecture tests, and the main project build.
