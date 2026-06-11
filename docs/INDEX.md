<!-- Generated manifest. Rebuilt by consolidation runs from frontmatter/state. Do not hand-edit. -->
<!-- Last rebuilt: 2026-06-12 (HEAD 9784325) -->

# Documentation Index

Manifest of all docs by layer, topic, and status. The code is the source of truth; this index maps the doc layer onto it.

## Curated layer (load first)

| Path | Topic | Status | Last verified |
|---|---|---|---|
| [MEMORY.md](MEMORY.md) | dense durable knowledge / rules / gotchas | curated | 2026-06-12 (9784325) |
| [ARCHITECTURE.md](ARCHITECTURE.md) | living architecture & data flow (= truth/overview) | truth | 2026-06-10 calibration |
| [../CONTEXT.md](../CONTEXT.md) | project vocabulary | truth | current |
| [README.md](README.md) | docs lifecycle map | current | 2026-06-12 |

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

## Active plans (`plans/`)

Future work or items needing human decision. Curated into here only by consolidation runs.

| Path | Topic | Status | Calibrated |
|---|---|---|---|
| [plans/sell-hotkey-regression-debug-plan.md](plans/sell-hotkey-regression-debug-plan.md) | native sell-hotkey regression | active (investigation, unstarted) | 2026-06-10 |
| [plans/history-panel-hero-portrait-badge.md](plans/history-panel-hero-portrait-badge.md) | HistoryPanel hero portrait badges | active (CollectionPanel done; HistoryPanel pending) | 2026-06-10 |
| [plans/collection-panel-filter-target-structure.md](plans/collection-panel-filter-target-structure.md) | CollectionPanel source-filter follow-up validation | active | 2026-06-10 |
| [plans/achievement-service-design.md](plans/achievement-service-design.md) | achievement catalog/server/analyzers design | active (proposed) | 2026-06-11 |
| [plans/achievement-ui-local-mvp.md](plans/achievement-ui-local-mvp.md) | achievement local UI MVP (4-PR plan) | active (confirmed, unstarted) | 2026-06-11 |
| [plans/shortcut-tutorial-settings-dock-link.md](plans/shortcut-tutorial-settings-dock-link.md) | hotkey-tutorial settings dock row | active (unstarted) | — |
| [plans/bazaardb-merchant-filter-comparison.md](plans/bazaardb-merchant-filter-comparison.md) | external BazaarDB filter comparison | needs-human-decision | 2026-06-10 |
| [plans/reverse-engineering/offline-local-run-design.md](plans/reverse-engineering/offline-local-run-design.md) | offline local-run proposal | needs-human-decision (aspirational) | 2026-06-10 |
| [plans/reverse-engineering/predefined-match-and-random-system-design.md](plans/reverse-engineering/predefined-match-and-random-system-design.md) | predefined match + RNG proposal | needs-human-decision (aspirational) | 2026-06-10 |

## Archive (`archive/`, frozen — never current)

Historical session artifacts; each carries a `status:` banner. Treat claims as current only if they also appear in code or `ARCHITECTURE.md`.

| Subtree | Contents |
|---|---|
| [archive/plans/](archive/plans/) | shipped/superseded plans (incl. 6 swept 2026-06-12: package-art ×3, settings-dock-toggle, live-build-rail-polish, unused-code-deletion, achievements-tab) |
| [archive/design/](archive/design/) | dated point-in-time design specs (+ `design/archive/` finished specs); see [archive/design/README.md](archive/design/README.md) |
| [archive/audits/](archive/audits/) | doc/code drift, health, and feasibility audits |
| [archive/debugging/](archive/debugging/) | debugging session notes |
| [archive/features/](archive/features/) | per-feature historical overviews |
| [archive/reference/](archive/reference/) | API/schema/hotkey/settings reference snapshots |
| [archive/reverse-engineering/](archive/reverse-engineering/) | decompile & protocol notes |
| [archive/superpowers/](archive/superpowers/) | superpowers plans & specs |
| [archive/mod-features-overview.md](archive/mod-features-overview.md) | historical feature overview |
