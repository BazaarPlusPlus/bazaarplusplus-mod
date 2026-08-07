# Collection size-filter layout root cause

## Background

The Collection panel renders Size and Quality as two adjacent segmented controls. Size chips were
changed from native `Button.text` content to a custom icon + label layout so the aspect-ratio icon
would not overlap the built-in text element.

Two attempted fixes did not restore the Size group in game:

1. Hide the native text element and add custom icon/label children.
2. Give each custom Size button an explicit width.

The runtime screenshot still shows only a thin leading edge before the Quality group, which means
the outer Size segment exists but its effective width remains approximately zero.

## Current problem

`CreateCombinedFilterChipSegment` is an overflow-hidden flex container with no explicit width. The
Size buttons are UI Toolkit `Button` controls whose native text measure node is deliberately hidden.
Although the child buttons now carry explicit widths, the parent segment's auto/intrinsic width is
still collapsing in the runtime Yoga layout. Because the parent has `Overflow.Hidden`, all custom
content is clipped; only the segment's border remains visible.

The Quality segment does not collapse because its buttons still use native `Button.text`, so the
native measure path supplies an intrinsic width to the parent.

## Candidate approaches

1. **Explicitly size the outer Size segment (chosen).** Make the segment width deterministic from
   the number of Size buttons, button width, and divider widths. This removes dependence on native
   Button text measurement and preserves the existing custom icon/label implementation.
2. Re-enable native text for measurement but make it transparent. Rejected because it risks a
   second overlapping text layer and couples layout to invisible presentation.
3. Replace the segmented control with a hand-measured custom container. Rejected as unnecessary
   while a deterministic outer width is sufficient.

## Verification method

- Build with zero warnings/errors and deploy through the normal Debug build copy step.
- In game, confirm all three Size chips are visible before the Quality group.
- Confirm icon and text do not overlap in Chinese.
- Confirm all Size icons share one height while widths represent 1:2, 1:1, and 3:2.
- Confirm selecting each hero produces a two-pixel border in that hero's theme color, including
  hover state.
