# Collection size-filter layout root cause

## Background

The Collection panel renders Size and Quality as two adjacent segmented controls. Size chips were
changed from native `Button.text` content to a custom icon + label layout so the aspect-ratio icon
would not overlap the built-in text element.

Three attempted fixes did not restore the Size group in game:

1. Hide the native text element and add custom icon/label children.
2. Give each custom Size button an explicit width.
3. Give the outer Size segment an explicit width and flex basis.

The latest runtime screenshot shows a correctly widened but completely empty Size segment. This
disproves the outer-width hypothesis and confirms that the child buttons themselves are hidden.

## Current problem

Unity UI Toolkit `Button` inherits from `TextElement`. `chip.Q<TextElement>()` therefore returns the
Button itself when there is no separate descendant text node. The custom Size-chip path treated that
result as a built-in child label and assigned `display: none`, hiding the entire Button. The existing
generic `CreateButton` code already guards this exact trap with `!ReferenceEquals(textElement,
button)`; the Size-chip customization omitted that guard.

The outer-width change made the failure clearer: the empty segment now has the intended width while
all three hidden children remain absent.

## Candidate approaches

1. **Do not hide the queried `TextElement` (chosen).** The Button text is already empty; custom icon
   and label children can be added without changing the Button's own display state.
2. Guard with `!ReferenceEquals(defaultText, chip)`. Safe but unnecessary while the native Button
   text is empty; removing the faulty block is simpler.
3. Keep the explicit outer segment width as deterministic layout protection for the custom content.

## Verification method

- Build with zero warnings/errors and deploy through the normal Debug build copy step.
- In game, confirm all three Size chips are visible before the Quality group.
- Confirm icon and text do not overlap in Chinese.
- Confirm all Size icons share one height while widths represent 1:2, 1:1, and 3:2.
- Confirm selecting each hero produces a two-pixel border in that hero's theme color, including
  hover state.
