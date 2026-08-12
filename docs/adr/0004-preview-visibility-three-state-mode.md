# ADR-0004: Use three-state enchant visibility and configurable key-only upgrade preview

Status: Accepted

## Decision

Enchant preview has `Off`, `AutoOnPedestalChoice`, and `Always` modes, defaulting to `Always`; holding the enchant-preview key remains an override. Upgrade preview has no visibility mode, but its shared activation key can use `Hold` (default) or `Toggle`. The same active state drives upgrade tooltips and the music-note socket overlay.

## Why

Enchant text can be useful always but can also be noisy or reveal irrelevant outcomes, so users need an automatic relevance mode and explicit off/always choices. Upgrade preview is an on-demand comparison, so a second three-state visibility setting added no value. A two-state activation behavior is separate: `Hold` keeps the transient default while `Toggle` supports users who do not want to keep a modifier depressed.

## Guardrails

- Resolve precedence centrally as upgrade key → enchant key → `Always` → matching enchant pedestal → normal ([policy](../../src/BazaarPlusPlus/Game/Tooltips/TooltipPreviewModePolicy.cs#L52-L74)).
- `AutoOnPedestalChoice` follows the encounter probe’s current enchant-pedestal classification and uses its enchantment-type restrictions when available ([policy](../../src/BazaarPlusPlus/Game/Tooltips/TooltipPreviewModePolicy.cs#L76-L106), [probe](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L48-L96)).
- Persist `[EnchantPreview] Mode` and `[Hotkeys] UpgradePreviewActivationMode`; there is no legacy boolean migration or upgrade visibility-mode config ([config](../../src/BazaarPlusPlus/Core/Config/BppConfig.cs)).
- Reset the latched toggle state when the activation mode or binding changes, and process at most one transition per frame ([state](../../src/BazaarPlusPlus/Game/Input/HotkeyActivationState.cs)).
- Keep the settings ladder data-driven ([dock entry](../../src/BazaarPlusPlus/Game/ItemEnchantPreview/ItemEnchantPreviewSettingsDockEntry.cs#L9-L29)).
