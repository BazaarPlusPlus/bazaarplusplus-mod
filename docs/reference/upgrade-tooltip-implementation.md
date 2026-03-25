# Upgrade Tooltip Implementation

## Current Behavior

- Normal hover shows the native primary tooltip.
- Enchant preview text is appended when `EnchantPreviewAlwaysShow` is enabled or the configured
  enchant-preview hotkey is held. The default binding is `Ctrl`.
- Upgrade preview refreshes the native primary tooltip into upgrade preview mode while the
  configured upgrade-preview hotkey is held. The default binding is `Shift`.
- While hovering, pressing or releasing either modifier hotkey refreshes the tooltip immediately.
- Priority is `upgrade > enchant`.
- Upgrade preview is restricted to `ItemCard` only.

## Why Upgrade Tooltip Works

Bazaar++ does not build a separate upgrade tooltip UI.
It reuses the game's native upgrade-preview state on the hovered card:

1. normal hover shows the main card tooltip
2. Bazaar++ detects the configured upgrade-preview hotkey
3. Bazaar++ waits for the native primary tooltip controller to exist
4. Bazaar++ hides the current primary tooltip
5. Bazaar++ calls `cardController.EnterUpgradePreview()`
6. Bazaar++ rebuilds `CardTooltipData` and shows the native primary tooltip again

Once the card is in upgrade preview mode, the game's own tooltip rendering starts showing next-tier
values through the normal primary tooltip.

## Native Game Path

Relevant native files:

- `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs`
- `decompiled/TheBazaarRuntime/CardController.cs`
- `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipTypeHandler.cs`
- `decompiled/TheBazaarRuntime/TheBazaar.Tooltips/CardTooltipData.cs`

Important native behavior:

- `CardController.CanFuse()` returns `true` while upgrade preview is active.
- `CardTooltipTypeHandler` copies that state into `tooltipData.CanFuse`.
- `CardTooltipData` then renders current values and next-tier values using the native fusion/upgrade formatting.

## Bazaar++ Hook Points

### Hotkey resolution

Files:

- `Game/Input/BppHotkeyActionId.cs`
- `Game/Input/BppHotkeyService.cs`
- `Game/Input/BppKeyBindRowController.cs`
- `Patches/Settings/BppKeybindSettingsPatch.cs`

Behavior:

- `BppHotkeyService` resolves keyboard and mouse bindings from BazaarPlusPlus config
- the default aliases remain `Ctrl` and `Shift`
- `BppKeybindSettingsPatch` injects native-settings rows so the user can rebind the two tooltip
  actions without editing config manually

### Upgrade preview refresh

File:

- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`

Behavior:

- patches `CardController.ShowTooltips()`
- only continues when:
  - the upgrade-preview hotkey is held
  - the card is an `ItemCard`
  - the card can upgrade
  - tooltip data is `CardTooltipData`

The patch does not force upgrade preview in the same frame.
Instead it waits briefly for the native primary tooltip controller to exist, then:

- hides the current primary tooltip
- calls `cardController.EnterUpgradePreview()`
- rebuilds `CardTooltipData` for the same card/template
- reopens the native primary tooltip via `ShowCardTooltipController(...)`

That wait is necessary because the native primary tooltip is created asynchronously.

### Enchant preview append

File:

- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`

Behavior:

- patches `CardTooltipData.GetPassiveTooltipBlock()`
- appends Bazaar++ enchant preview lines to the passive tooltip text

### Upgrade preview suppresses enchant preview

Also in:

- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`

If the upgrade-preview hotkey is held, the Bazaar++ enchant append path returns early. This
prevents mixed output where upgrade preview and enchant preview are shown together.

## Runtime Refresh

File:

- `Game/Tooltips/TooltipModifierRefreshController.cs`

Behavior:

- watches the current resolved tooltip modifier mode
- when modifier state changes during hover:
  - finds the currently hovered `ItemCard`
  - hides the current card tooltip
  - shows the native primary tooltip again
  - if the upgrade-preview hotkey is held and the item can upgrade, lets the patch refresh the
    primary tooltip into upgrade preview mode

This is what makes modifier changes feel live while already hovering.

## Scope

Upgrade tooltip is intentionally limited to `ItemCard`.

This excludes:

- skills
- monster cards
- encounter cards
- other non-item card types

## Bazaar++ Files

- `Game/Input/BppHotkeyActionId.cs`
- `Game/Input/BppHotkeyService.cs`
- `Game/Input/BppKeyBindRowController.cs`
- `Game/Tooltips/TooltipModifierRefreshController.cs`
- `Patches/Settings/BppKeybindSettingsPatch.cs`
- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- `Plugin.cs`
