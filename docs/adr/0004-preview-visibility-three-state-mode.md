# Give enchant/upgrade preview a three-state visibility mode, not a boolean

> **Update 2026-05-30:** Upgrade preview later reverted to hold-Shift-only — its three-state mode, `UpgradePreviewModeConfig`, and settings-dock entry were removed, because an upgrade preview is an on-demand check rather than a spoiler, so the auto-on-pedestal layer added no value. Enchant preview keeps the three-state mode described below, and additionally now filters the preview to the *specific* enchant type(s) the offered pedestal applies — the per-type filtering called out as out of scope below — taking the union across every enchant pedestal when several are offered at once. The text below records the original decision.

> **Update 2026-06-04:** Fresh installs now default `EnchantPreview / Mode` to `Always`; the original `AutoOnPedestalChoice` mode remains available from the settings dock.

> **Update 2026-06-05:** The legacy `[EnchantPreview] AlwaysShow` migration bridge was removed. Current builds bind `[EnchantPreview] Mode` directly and do not rewrite orphaned `AlwaysShow` entries.

The original decision introduced an independent three-state visibility mode — `Off` / `AutoOnPedestalChoice` / `Always` — to replace the old boolean `EnchantPreviewAlwaysShow`. The hold-key (`HoldEnchantPreview` / `HoldUpgradePreview`) remains the manual override in every mode.

## Context

The old behaviour had no notion of relevance: enchant preview was shown on every eligible tooltip whenever `EnchantPreviewAlwaysShow` (default `true`) was set — noisy and a spoiler when no encounter was offering an enchant — while upgrade preview was invisible unless the player held Shift. Neither reflected whether a pedestal was actually being offered *right now*.

The game already exposes that signal: while the player is on the map choosing (`ChoiceState`), `RunState.SelectionSet` lists the offered encounter template GUIDs, and a pedestal card's `Behavior` (`TPedestalBehaviorUpgrade` vs `TPedestalBehaviorEnchant`/`EnchantRandom`) says which kind it is. A single `SelectionSet` never offers both an upgrade and an enchant pedestal at once, so the state is a plain enum, not flags. This rides the existing `GameInterop/Encounter/EncounterStateProbe` read adapter (see [ADR-0002](0002-mountable-feature-registry.md)) plus `ChoiceScreenPedestalResolver`, with no new event plumbing and no UI patches.

## Consequences

- `AutoOnPedestalChoice` auto-shows the matching preview when hovering an inventory item while the corresponding pedestal is on the choice screen; not inside `PedestalState`, not for `TPedestalBehaviorTransform`.
- Resolution is centralized in `Game/Tooltips/TooltipPreviewModePolicy` (priority `hold-upgrade-hotkey > hold-enchant-hotkey > Always > AutoOnPedestalChoice > Normal`; upgrade preview is hold-Shift only and has no visibility mode), shared by the three call sites so behaviour can't drift. See [tooltip-preview.md](../features/tooltip-preview.md).
- The initial implementation included a first-launch migration from `EnchantPreviewAlwaysShow` to `Mode`; current builds no longer run that migration.
- Per-item filtering inside the preview (only items the pedestal would accept) was explicitly left out of scope — that is a larger change against `ItemEnchantPreviewService` / `UpgradePreviewTooltipPatch`.

Full design history: [docs/design/archive/2026-05-24-pedestal-aware-preview-display-design.md](../design/archive/2026-05-24-pedestal-aware-preview-display-design.md).
