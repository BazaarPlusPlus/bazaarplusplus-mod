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

- [plans/choice-timeline-run-bundle-plan.md](plans/choice-timeline-run-bundle-plan.md) — choice timeline in run bundles; awaiting sign-off on its §11 open decisions.
- [plans/history-panel-hero-portrait-badge.md](plans/history-panel-hero-portrait-badge.md) — replace HistoryPanel text hero badges with portraits (reuse the shipped `GameInterop/HeroPortraits` provider).
- [plans/reverse-engineering/offline-local-run-design.md](plans/reverse-engineering/offline-local-run-design.md) and [plans/reverse-engineering/predefined-match-and-random-system-design.md](plans/reverse-engineering/predefined-match-and-random-system-design.md) — unimplemented reverse-engineering proposals.

Swept on 2026-07-10: 34 drafts + 6 stale plans classified against HEAD 7a68e8bb — implemented/abandoned/superseded ones moved to [archive/](archive/) with status banners, 2 zero-value executor briefs deleted, 1 pending draft promoted here. The achievement system and package custom-art subsystem were deleted from the code on 2026-06-30; their plans are archived as abandoned. (Earlier sweep: 2026-06-12.)

## Archive

[archive/](archive/) contains implemented, superseded, abandoned, or historical documents. Archive frontmatter states why each file moved and, when applicable, points to [ARCHITECTURE.md](ARCHITECTURE.md).

Archived files are preserved for context. Do not treat archived implementation claims as current unless they also appear in [ARCHITECTURE.md](ARCHITECTURE.md) or current code.

## Agent Rules

Agent rules and project-specific operating constraints live in [../CLAUDE.md](../CLAUDE.md). `AGENTS.md` is a symlink to that file.
