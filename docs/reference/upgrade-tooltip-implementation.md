# Upgrade Tooltip Implementation

## Current Behavior

- Normal hover shows the native primary tooltip.
- `Ctrl + hover` appends Bazaar++ enchant preview text to the passive block.
- `Shift + hover` shows the native upgrade preview tooltip.
- While hovering, pressing or releasing `Ctrl` or `Shift` refreshes the tooltip immediately.
- Priority is `Shift > Ctrl`.
- Upgrade preview is restricted to `ItemCard` only.

## Why Upgrade Tooltip Works

Bazaar++ does not build a separate upgrade tooltip UI.
It reuses the game's native upgrade preview path:

1. normal hover shows the main card tooltip
2. Bazaar++ detects `Shift`
3. Bazaar++ calls `TooltipParentComponent.DisplayUpgradeTooltips(...)`
4. the native UI creates a secondary tooltip
5. the native tooltip pipeline switches the card into upgrade preview mode

Once the card is in upgrade preview mode, the game's own tooltip rendering starts showing next-tier values.

## Native Game Path

Relevant native files:

- `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs`
- `decompiled/TheBazaarRuntime/CardController.cs`
- `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipTypeHandler.cs`
- `decompiled/TheBazaarRuntime/TheBazaar.Tooltips/CardTooltipData.cs`

Important native behavior:

- `TooltipParentComponent.DisplayUpgradeTooltips(...)` creates the secondary tooltip.
- `HandleUpgradePreview(...)` calls `cardController.EnterUpgradePreview()`.
- `CardController.CanFuse()` returns `true` while upgrade preview is active.
- `CardTooltipTypeHandler` copies that state into `tooltipData.CanFuse`.
- `CardTooltipData` then renders current values and next-tier values using the native fusion/upgrade formatting.

## Bazaar++ Hook Points

### Shift upgrade preview

File:

- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`

Behavior:

- patches `CardController.ShowTooltips()`
- only continues when:
  - `Shift` is pressed
  - the card is an `ItemCard`
  - the card can upgrade
  - tooltip data is `CardTooltipData`

The patch does not force upgrade preview in the same frame.
Instead it waits briefly for the native primary tooltip controller to exist, then calls:

- `Data.TooltipParentComponent.DisplayUpgradeTooltips(...)`

That wait is necessary because the native primary tooltip is created asynchronously.

### Ctrl enchant preview

File:

- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`

Behavior:

- patches `CardTooltipData.GetPassiveTooltipBlock()`
- appends Bazaar++ enchant preview lines to the passive tooltip text

### Shift suppresses enchant preview

Also in:

- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`

If `Shift` is pressed, the Bazaar++ enchant-text append path returns early.
This prevents mixed output where upgrade preview and enchant preview are shown together.

## Runtime Refresh

File:

- `Game/Tooltips/TooltipModifierRefreshController.cs`

Behavior:

- watches `Ctrl` and `Shift`
- when modifier state changes during hover:
  - finds the currently hovered `ItemCard`
  - hides the current card tooltip
  - shows the native primary tooltip again
  - if `Shift` is pressed and the item can upgrade, shows native upgrade preview again

This is what makes modifier changes feel live while already hovering.

## Scope

Upgrade tooltip is intentionally limited to `ItemCard`.

This excludes:

- skills
- monster cards
- encounter cards
- other non-item card types

## Bazaar++ Files

- `Game/Input/KeyBindings.cs`
- `Game/Tooltips/TooltipModifierRefreshController.cs`
- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- `Plugin.cs`
