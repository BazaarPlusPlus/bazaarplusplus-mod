<!-- Generated manifest. Rebuilt by consolidation runs from frontmatter/state. Do not hand-edit. -->
<!-- Last rebuilt: 2026-07-10 (HEAD 7a68e8bb) -->

# Documentation Index

Manifest of all docs by layer, topic, and status. The code is the source of truth; this index maps the doc layer onto it.

## Curated layer (load first)

| Path | Topic | Status | Last verified |
|---|---|---|---|
| [MEMORY.md](MEMORY.md) | dense durable knowledge / rules / gotchas | curated | 2026-07-10 (7a68e8bb) |
| [ARCHITECTURE.md](ARCHITECTURE.md) | living architecture & data flow (= truth/overview) | truth | 2026-07-10 calibration |
| [../CONTEXT.md](../CONTEXT.md) | project vocabulary | truth | current |
| [README.md](README.md) | docs lifecycle map | current | 2026-07-10 |

## Decision records (`adr/`, immutable)

| Path | Topic | Status |
|---|---|---|
| [adr/0001](adr/0001-encounter-status-probe-not-timeline-tracker.md) | encounter status probe, not timeline | accepted |
| [adr/0002](adr/0002-mountable-feature-registry.md) | mountable/feature registry | accepted |
| [adr/0003](adr/0003-history-panel-preview-overlay.md) | HistoryPanel ScreenSpaceOverlay preview | accepted |
| [adr/0004](adr/0004-preview-visibility-three-state-mode.md) | three-state preview visibility | accepted |
| [adr/0005](adr/0005-autobazaar-isolated-transport-core.md) | AutoBazaar transport-only core | superseded by 0006 |
| [adr/0006](adr/0006-bazaaragent-as-its-own-plugin.md) | BazaarAgent as its own plugin | accepted |
| [adr/0007](adr/0007-bazaaragent-external-replay-video-recording.md) | external replay video recording | accepted |
| [adr/0008](adr/0008-replay-continue-as-agent-action.md) | replay continue as agent `Continue` action | accepted |

## Active plans (`plans/`)

Future work or items needing human decision. Curated into here only by consolidation runs.

| Path | Topic | Status | Calibrated |
|---|---|---|---|
| [plans/choice-timeline-run-bundle-plan.md](plans/choice-timeline-run-bundle-plan.md) | choice timeline in run bundles | pending (awaiting sign-off on §11 open decisions) | 2026-07-10 |
| [plans/history-panel-hero-portrait-badge.md](plans/history-panel-hero-portrait-badge.md) | HistoryPanel hero portrait badges | active (unstarted; reuse shipped `HeroPortraitSpriteProvider`) | 2026-07-10 |
| [plans/reverse-engineering/offline-local-run-design.md](plans/reverse-engineering/offline-local-run-design.md) | offline local-run proposal | needs-human-decision (aspirational) | 2026-07-10 |
| [plans/reverse-engineering/predefined-match-and-random-system-design.md](plans/reverse-engineering/predefined-match-and-random-system-design.md) | predefined match + RNG proposal | needs-human-decision (aspirational) | 2026-07-10 |

## Archive (`archive/`, frozen — never current)

Historical session artifacts; each carries a `status:` banner. Treat claims as current only if they also appear in code or `ARCHITECTURE.md`.

| Subtree | Contents |
|---|---|
| [archive/plans/](archive/plans/) | shipped/superseded/abandoned plans (incl. 27 swept 2026-07-10: BazaarDB link ×4, voice subtitles ×3, PTR compat, keybinds, UI font, event preview, tooltips ×2, achievements ×5, five-deepening refactors, collection filter ×3, replay continue, overlay/BazaarAgent loop plans, sources, card proportions, history filters, shortcut tutorial) |
| [archive/design/](archive/design/) | dated point-in-time design specs (+ `design/archive/` finished specs; +3 swept 2026-07-10); see [archive/design/README.md](archive/design/README.md) |
| [archive/audits/](archive/audits/) | doc/code drift, health, feasibility, and performance audits |
| [archive/debugging/](archive/debugging/) | debugging session notes (incl. tooltip-overlay third recurrence, stale sell-hotkey debug plan) |
| [archive/features/](archive/features/) | per-feature historical overviews |
| [archive/reference/](archive/reference/) | API/schema/hotkey/settings reference snapshots |
| [archive/reverse-engineering/](archive/reverse-engineering/) | decompile & protocol notes; shop-entry series 1–5 (old client dealer RE) |
| [archive/superpowers/](archive/superpowers/) | superpowers plans & specs |
| [archive/mod-features-overview.md](archive/mod-features-overview.md) | historical feature overview |
