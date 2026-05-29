# Design Specs

Dated, point-in-time design documents (`YYYY-MM-DD-<slug>.md`). A spec captures *how a change was planned*; once the work lands or dies it becomes immutable history.

## Convention

- **Active proposals** (work not yet started) live at this top level.
- **Finished specs** (implemented / superseded / abandoned) live under [`archive/`](archive/), each with a `Status:` banner at the very top:
  - `ASPIRATIONAL` — never implemented; the world may have moved on.
  - `IMPLEMENTED (historical)` — shipped; the living truth is now a feature doc or code.
  - `SUPERSEDED by <doc>` — replaced by a later spec or decision.
- When a spec records a decision that outlives it (an abstraction choice, a rejected approach, a tradeoff), promote that decision to a [docs/adr/](../adr/) entry **before** archiving — ADRs are the canonical "why", specs are the historical "how we planned it".

There are currently no active proposals; everything is under `archive/`.

## Archived specs

**Implemented (historical)** — shipped; see the linked living doc / ADR:

- `2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md` → [ADR-0002](../adr/0002-mountable-feature-registry.md)
- `2026-05-23-combat-replay-sfx-impl.md` → [combat-replay.md](../features/combat-replay.md)
- `2026-05-24-bazaardb-upload-in-mod-design.md` → [screenshots.md](../features/screenshots.md)
- `2026-05-24-pedestal-aware-preview-display-design.md` → [ADR-0004](../adr/0004-preview-visibility-three-state-mode.md), [tooltip-preview.md](../features/tooltip-preview.md)
- `2026-05-27-mod-ui-typography-token-foundation.md` → `Infrastructure/Fonts/`, `Infrastructure/UiTokens/`
- `2026-05-29-historypanel-fullscreen-responsive-design.md` → [history-panel.md](../features/history-panel.md)

**Superseded** — kept as a reasoning trail:

- `2026-05-22-combat-replay-sfx-silent-analysis.md` → superseded by the `2026-05-23` SFX impl
- `2026-05-27-history-panel-native-rendering-migration.md` → RT approach reverted, see [ADR-0003](../adr/0003-history-panel-preview-overlay.md)

**Aspirational** — never implemented; kept for design reasoning:

- `2026-05-17-autobazaar-agent-design.md` (decision agent now in the `bazaarplusplus-agent` repo)
- `2026-05-17-bypass-http-rate-limit-design.md`
- `2026-05-24-arm-combat-replay-recording-design.md` (contradicts the shipped auto-record behaviour)
- `2026-05-24-combat-replay-skill-showcase-design.md`
