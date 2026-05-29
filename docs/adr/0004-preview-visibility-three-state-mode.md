# Give enchant/upgrade preview a three-state visibility mode, not a boolean

Enchant and upgrade tooltip previews each have an independent three-state visibility mode — `Off` / `AutoOnPedestalChoice` / `Always`, default `AutoOnPedestalChoice` — replacing the old boolean `EnchantPreviewAlwaysShow`. The hold-key (`HoldEnchantPreview` / `HoldUpgradePreview`) remains the manual override in every mode.

## Context

The old behaviour had no notion of relevance: enchant preview was shown on every eligible tooltip whenever `EnchantPreviewAlwaysShow` (default `true`) was set — noisy and a spoiler when no encounter was offering an enchant — while upgrade preview was invisible unless the player held Shift. Neither reflected whether a pedestal was actually being offered *right now*.

The game already exposes that signal: while the player is on the map choosing (`ChoiceState`), `RunState.SelectionSet` lists the offered encounter template GUIDs, and a pedestal card's `Behavior` (`TPedestalBehaviorUpgrade` vs `TPedestalBehaviorEnchant`/`EnchantRandom`) says which kind it is. A single `SelectionSet` never offers both an upgrade and an enchant pedestal at once, so the state is a plain enum, not flags. This rides the existing `Game/Encounter/EncounterStateProbe` snapshot (see [ADR-0002](0002-mountable-feature-registry.md)) plus `ChoiceScreenPedestalResolver`, with no new event plumbing and no UI patches.

## Consequences

- `AutoOnPedestalChoice` auto-shows the matching preview when hovering an inventory item while the corresponding pedestal is on the choice screen; not inside `PedestalState`, not for `TPedestalBehaviorTransform`.
- Resolution is centralized in `Game/Tooltips/TooltipPreviewModePolicy` (priority `hotkey > Always > AutoOnPedestalChoice > Normal`; upgrade wins ties), shared by the three call sites so behaviour can't drift. See [tooltip-preview.md](../features/tooltip-preview.md).
- **Backward-compatible migration** runs on first launch: `EnchantPreviewAlwaysShow = true` → `Mode = Always`; `false` → `Mode = AutoOnPedestalChoice`; the old key is removed from the config file.
- Per-item filtering inside the preview (only items the pedestal would accept) was explicitly left out of scope — that is a larger change against `ItemEnchantPreviewService` / `UpgradePreviewTooltipPatch`.

Full design history: [docs/design/archive/2026-05-24-pedestal-aware-preview-display-design.md](../design/archive/2026-05-24-pedestal-aware-preview-display-design.md).
