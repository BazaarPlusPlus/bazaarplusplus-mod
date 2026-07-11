# BazaarPlusPlus Docs

The code is the source of truth. Current implementation guidance lives in one architecture document plus immutable decision records; older designs, audits, feature notes, and prompts are archived for history. This file is the single documentation map (it absorbed the former `INDEX.md` on 2026-07-11).

## Current Docs

- [MEMORY.md](MEMORY.md) — dense, agent-facing durable knowledge. **Load this first.**
- [ARCHITECTURE.md](ARCHITECTURE.md) — living architecture and data-flow summary for the current code (the structure/overview layer).
- [../CONTEXT.md](../CONTEXT.md) — project vocabulary (glossary only).
- [../README.md](../README.md) / [../README_en.md](../README_en.md) — project entry points, quick start, build commands, and high-level feature list.
- [agents/](agents/) — per-repo config for the engineering skills (issue tracker, triage labels, domain-doc consumer rules).

## Decision records (`adr/`, immutable)

This repo keeps the existing ADR convention instead of adding a duplicate `decisions/` directory.

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

## Active Plans

Only future work or items needing human confirmation belong in [plans/](plans/). Curated into here by consolidation runs; write new specs/designs/plans to [drafts/](drafts/):

- [plans/choice-timeline-run-bundle-plan.md](plans/choice-timeline-run-bundle-plan.md) — choice timeline in run bundles; awaiting sign-off on its §11 open decisions.
- [plans/history-panel-hero-portrait-badge.md](plans/history-panel-hero-portrait-badge.md) — replace HistoryPanel text hero badges with portraits (reuse the shipped `GameInterop/HeroPortraits` provider).

Swept on 2026-07-11: 4 implemented drafts (PR#17 collection search + cooldown tier rendering) archived; the 2 parked reverse-engineering proposals moved to [archive/reverse-engineering/](archive/reverse-engineering/). Earlier sweeps: 2026-07-10 (34 drafts + 6 stale plans against HEAD 7a68e8bb; the achievement system and package custom-art subsystem were deleted from the code on 2026-06-30, their plans archived as abandoned), 2026-06-12.

## Archive (`archive/`, frozen — never current)

Historical session artifacts; frontmatter states why each file moved. Treat archived implementation claims as current only if they also appear in [ARCHITECTURE.md](ARCHITECTURE.md) or current code.

| Subtree | Contents |
|---|---|
| [archive/plans/](archive/plans/) | shipped/superseded/abandoned plans (incl. 27 swept 2026-07-10: BazaarDB link ×4, voice subtitles ×3, PTR compat, keybinds, UI font, event preview, tooltips ×2, achievements ×5, five-deepening refactors, collection filter ×3, replay continue, overlay/BazaarAgent loop plans, sources, card proportions, history filters, shortcut tutorial) |
| [archive/design/](archive/design/) | dated point-in-time design specs (+ `design/archive/` finished specs; +3 swept 2026-07-10; +10 swept 2026-07-11: the architecture-review batch design docs PRs #22–#31 and the settings-dock PR#18 rediagnosis; +3 more 2026-07-11: the PR#17 collection search/IME/hash and cooldown docs — all implemented); see [archive/design/README.md](archive/design/README.md) |
| [archive/audits/](archive/audits/) | doc/code drift, health, feasibility, and performance audits |
| [archive/debugging/](archive/debugging/) | debugging session notes (incl. tooltip-overlay third recurrence, stale sell-hotkey debug plan) |
| [archive/features/](archive/features/) | per-feature historical overviews |
| [archive/reference/](archive/reference/) | API/schema/hotkey/settings reference snapshots |
| [archive/reverse-engineering/](archive/reverse-engineering/) | decompile & protocol notes; shop-entry series 1–5 (old client dealer RE); parked offline-run / predefined-match proposals |
| [archive/superpowers/](archive/superpowers/) | superpowers plans & specs |
| [archive/mod-features-overview.md](archive/mod-features-overview.md) | historical feature overview |

## Agent Rules

Agent rules and project-specific operating constraints live in [../CLAUDE.md](../CLAUDE.md). `AGENTS.md` is a symlink to that file.
