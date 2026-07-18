# BazaarPlusPlus Docs

The code is the source of truth. Current implementation guidance lives in one architecture document plus immutable decision records; older designs, audits, feature notes, and prompts are archived for history. This file is the single documentation map (it absorbed the former `INDEX.md` on 2026-07-11).

## Current Docs

- [MEMORY.md](MEMORY.md) — dense, agent-facing durable knowledge. **Load this first.**
- [ARCHITECTURE.md](ARCHITECTURE.md) — living architecture and data-flow summary for the current code (the structure/overview layer).
- [../CONTEXT.md](../CONTEXT.md) — project vocabulary (glossary only).
- [../README.md](../README.md) — project entry point, quick start, build commands, and high-level feature list.
- [agents/](agents/) — per-repo config for the engineering skills (issue tracker, triage labels, domain-doc consumer rules).

## Decision records (`adr/`, immutable)

This repo keeps the existing ADR convention instead of adding a duplicate `decisions/` directory. ADR-0005 (AutoBazaar transport-only core, superseded) was collapsed into ADR-0006 on 2026-07-11; the file is recoverable from git history and numbering is never reused.

| Path | Topic | Status |
|---|---|---|
| [adr/0001](adr/0001-encounter-status-probe-not-timeline-tracker.md) | encounter status probe, not timeline | accepted |
| [adr/0002](adr/0002-mountable-feature-registry.md) | mountable/feature registry | accepted |
| [adr/0003](adr/0003-history-panel-preview-overlay.md) | HistoryPanel ScreenSpaceOverlay preview | accepted |
| [adr/0004](adr/0004-preview-visibility-three-state-mode.md) | three-state preview visibility | accepted |
| [adr/0006](adr/0006-bazaaragent-as-its-own-plugin.md) | BazaarAgent as its own plugin (absorbs 0005) | accepted |
| [adr/0007](adr/0007-bazaaragent-external-replay-video-recording.md) | external replay video recording | accepted |
| [adr/0008](adr/0008-replay-continue-as-agent-action.md) | replay continue as agent `Continue` action | accepted |

## Future Work

Task plans, feature requests, and bugs are tracked as **GitHub issues** (see [agents/issue-tracker.md](agents/issue-tracker.md)), not repo docs. The former `docs/plans/` directory was retired on 2026-07-11; its two remaining plans migrated verbatim to [#32](https://github.com/cauyxy/bazaarplusplus-mod/issues/32) (HistoryPanel hero portrait badges, `ready-for-agent`) and [#33](https://github.com/cauyxy/bazaarplusplus-mod/issues/33) (choice timeline in run bundles, `needs-info` pending §11 sign-off).

`drafts/` remains the write buffer for **knowledge documents only** — design records, root-cause analyses, decision/option analyses produced mid-session. Task plans do not go there. Consolidation runs sweep drafts into MEMORY/ADR/archive, so the directory only exists while unswept drafts are pending (its `.gitkeep` placeholder was dropped on 2026-07-11 — just create the directory when writing a draft). (Sweeps: 2026-07-18, 2026-07-11, 2026-07-10, 2026-06-12.)

## Archive (`archive/`, frozen — never current)

Historical session artifacts; frontmatter states why each file moved. Treat archived implementation claims as current only if they also appear in [ARCHITECTURE.md](ARCHITECTURE.md) or current code.

Cleanup on 2026-07-11: 71 low-value archived docs were **deleted** (executor prompts, checkbox twins of kept design docs, deleted-feature plans, stale reference snapshots, resolved audits, retired superpowers-workflow artifacts). What survives is either referenced by ADRs/MEMORY/plans or carries unique root-cause / decompiled-evidence content. Deleted docs — and any dangling links to them inside surviving frozen docs — are recoverable from git history (commit message lists the criteria).

| Subtree | Contents |
|---|---|
| [archive/plans/](archive/plans/) | plans retained for their root-cause records, decision registers, or MEMORY citations (e.g. PTR 216-API diff base, shader-keyword flicker root cause, five-deepening refactors verdicts) |
| [archive/design/](archive/design/) | design specs retained for decompiled-evidence and rejected-approach analysis (incl. the 2026-07-11 architecture-review batch, the PR#17 search/cooldown records, and the 2026-07-13 native-game-fonts RFC with its font-coverage audit tables); see [archive/design/README.md](archive/design/README.md) |
| [archive/audits/](archive/audits/) | ADR-pinned feasibility audit + the 2026-07-08 performance audit (open GC backlog + probe-refuted items) |
| [archive/debugging/](archive/debugging/) | root-cause debugging records (tooltip-overlay third recurrence, enchant auto-preview state detection, end-of-run raw-reveal-graph screenshot regression, remote-data IPv6 download stall, issue-96 recording-button diagnosis) |
| [archive/features/](archive/features/) | ADR-pinned feature overviews + the ghost-battle cross-repo data-flow trace |
| [archive/reference/](archive/reference/) | BazaarAgent HTTP API v1 snapshot (ADR-0007) |
| [archive/reverse-engineering/](archive/reverse-engineering/) | decompile & protocol notes; shop-entry series 1–5 (old client dealer RE); parked offline-run / predefined-match proposals — most re-derivation-expensive class, kept whole |
| [archive/superpowers/](archive/superpowers/) | the two ADR-pinned superpowers artifacts (module-isolation spec, dependency-inversion prompt) |
| [archive/mod-features-overview.md](archive/mod-features-overview.md) | historical feature overview |
| [archive/2026-06-15-collection-panel-shop-probability-design.md](archive/2026-06-15-collection-panel-shop-probability-design.md) | shop-probability rejected-approach analysis (series #6 of the shop-entry RE docs) |

## Agent Rules

Agent rules and project-specific operating constraints live in [../CLAUDE.md](../CLAUDE.md). `AGENTS.md` is a symlink to that file.
