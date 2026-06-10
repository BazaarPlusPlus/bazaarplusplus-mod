# BazaarPlusPlus Docs

The code is the source of truth. Current implementation guidance lives in one architecture document plus immutable decision records; older designs, audits, feature notes, and prompts are archived for history.

## Current Docs

- [ARCHITECTURE.md](ARCHITECTURE.md) — living architecture and data-flow summary for the current code.
- [../README.md](../README.md) / [../README_en.md](../README_en.md) — project entry points, quick start, build commands, and high-level feature list.
- [../CONTEXT.md](../CONTEXT.md) — project vocabulary.
- [adr/](adr/) — architecture decision records. This repo keeps the existing ADR convention instead of adding a duplicate `decisions/` directory.

## Active Plans

Only future work or items needing human confirmation belong in [plans/](plans/):

- [plans/sell-hotkey-regression-debug-plan.md](plans/sell-hotkey-regression-debug-plan.md) — native sell-hotkey regression investigation.
- [plans/history-panel-hero-portrait-badge.md](plans/history-panel-hero-portrait-badge.md) — replace HistoryPanel text hero badges with portraits.
- [plans/collection-panel-filter-target-structure.md](plans/collection-panel-filter-target-structure.md) — remaining CollectionPanel source-filter validation after schema v4 landed.
- [plans/collection-panel-achievements-tab.md](plans/collection-panel-achievements-tab.md) — proposed CollectionPanel Achievements tab backed by BPP-owned card definitions.
- [plans/achievement-service-design.md](plans/achievement-service-design.md) — proposed achievement catalog, server status API, parser, and storage design.
- [plans/package-card-art-settings-dock-toggle.md](plans/package-card-art-settings-dock-toggle.md) — proposed SettingsDock toggle for package-card art replacement.
- [plans/bazaardb-merchant-filter-comparison.md](plans/bazaardb-merchant-filter-comparison.md) — external BazaarDB comparison input; not code-verified.
- [plans/reverse-engineering/offline-local-run-design.md](plans/reverse-engineering/offline-local-run-design.md) and [plans/reverse-engineering/predefined-match-and-random-system-design.md](plans/reverse-engineering/predefined-match-and-random-system-design.md) — unimplemented reverse-engineering proposals.

## Archive

[archive/](archive/) contains implemented, superseded, abandoned, or historical documents. Archive frontmatter states why each file moved and, when applicable, points to [ARCHITECTURE.md](ARCHITECTURE.md).

Archived files are preserved for context. Do not treat archived implementation claims as current unless they also appear in [ARCHITECTURE.md](ARCHITECTURE.md) or current code.

## Agent Rules

Agent rules and project-specific operating constraints live in [../CLAUDE.md](../CLAUDE.md). `AGENTS.md` is a symlink to that file.
