# BazaarPlusPlus Docs

The code is the source of truth. Current implementation guidance lives in one architecture document plus immutable decision records; older designs, audits, feature notes, and prompts are archived for history.

## Current Docs

- [MEMORY.md](MEMORY.md) — dense, agent-facing durable knowledge. **Load this first.**
- [INDEX.md](INDEX.md) — generated manifest of every doc by layer / topic / status.
- [ARCHITECTURE.md](ARCHITECTURE.md) — living architecture and data-flow summary for the current code (the structure/overview layer).
- [../README.md](../README.md) / [../README_en.md](../README_en.md) — project entry points, quick start, build commands, and high-level feature list.
- [../CONTEXT.md](../CONTEXT.md) — project vocabulary.
- [adr/](adr/) — architecture decision records. This repo keeps the existing ADR convention instead of adding a duplicate `decisions/` directory.

## Active Plans

Only future work or items needing human confirmation belong in [plans/](plans/). Curated into here by consolidation runs; write new specs/designs/plans to [drafts/](drafts/):

- [plans/sell-hotkey-regression-debug-plan.md](plans/sell-hotkey-regression-debug-plan.md) — native sell-hotkey regression investigation.
- [plans/history-panel-hero-portrait-badge.md](plans/history-panel-hero-portrait-badge.md) — replace HistoryPanel text hero badges with portraits (CollectionPanel side already shipped).
- [plans/collection-panel-filter-target-structure.md](plans/collection-panel-filter-target-structure.md) — remaining CollectionPanel source-filter validation after schema v4 landed.
- [plans/achievement-service-design.md](plans/achievement-service-design.md) — proposed achievement catalog, server status API, parser, and storage design.
- [plans/achievement-ui-local-mvp.md](plans/achievement-ui-local-mvp.md) — confirmed local achievement-tab UI MVP (4-PR plan, no server).
- [plans/shortcut-tutorial-settings-dock-link.md](plans/shortcut-tutorial-settings-dock-link.md) — proposed hotkey-tutorial settings dock row.
- [plans/bazaardb-merchant-filter-comparison.md](plans/bazaardb-merchant-filter-comparison.md) — external BazaarDB comparison input; not code-verified.
- [plans/reverse-engineering/offline-local-run-design.md](plans/reverse-engineering/offline-local-run-design.md) and [plans/reverse-engineering/predefined-match-and-random-system-design.md](plans/reverse-engineering/predefined-match-and-random-system-design.md) — unimplemented reverse-engineering proposals.

Shipped/superseded plans (package-card art ×3, settings-dock toggle, live-build rail polish, unused-code deletion, achievements-tab) were swept to [archive/plans/](archive/plans/) on 2026-06-12.

## Archive

[archive/](archive/) contains implemented, superseded, abandoned, or historical documents. Archive frontmatter states why each file moved and, when applicable, points to [ARCHITECTURE.md](ARCHITECTURE.md).

Archived files are preserved for context. Do not treat archived implementation claims as current unless they also appear in [ARCHITECTURE.md](ARCHITECTURE.md) or current code.

## Agent Rules

Agent rules and project-specific operating constraints live in [../CLAUDE.md](../CLAUDE.md). `AGENTS.md` is a symlink to that file.
