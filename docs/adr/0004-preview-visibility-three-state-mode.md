# ADR-0004: Use three-state enchant visibility and key-only upgrade preview

Status: Accepted; consolidated 2026-07-19

## Decision

Enchant preview has `Off`, `AutoOnPedestalChoice`, and `Always` modes, defaulting to `Always`; holding the enchant-preview key remains an override. Upgrade preview has no persisted visibility mode and appears only while its key is held.

## Why

Enchant text can be useful always but can also be noisy or reveal irrelevant outcomes, so users need an automatic relevance mode and explicit off/always choices. Upgrade preview is an on-demand comparison, so a second three-state setting added no value.

## Guardrails

- Resolve precedence centrally as upgrade key → enchant key → `Always` → matching enchant pedestal → normal ([policy](../../src/BazaarPlusPlus/Game/Tooltips/TooltipPreviewModePolicy.cs#L52-L74)).
- `AutoOnPedestalChoice` follows the encounter probe’s current enchant-pedestal classification and uses its enchantment-type restrictions when available ([policy](../../src/BazaarPlusPlus/Game/Tooltips/TooltipPreviewModePolicy.cs#L76-L106), [probe](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L48-L96)).
- Persist only `[EnchantPreview] Mode`; there is no legacy boolean migration or upgrade-mode config ([config](../../src/BazaarPlusPlus/Core/Config/BppConfig.cs#L9-L15), [binding](../../src/BazaarPlusPlus/Core/Config/BppConfig.cs#L73-L78)).
- Keep the settings ladder data-driven ([dock entry](../../src/BazaarPlusPlus/Game/ItemEnchantPreview/ItemEnchantPreviewSettingsDockEntry.cs#L9-L29)).
