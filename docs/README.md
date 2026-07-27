# BazaarPlusPlus Docs

The code is the source of truth. Current implementation guidance lives in one architecture document plus compact decision records. Historical working documents live in git history, not in the active tree. This file is the single documentation map.

## Current Docs

- [MEMORY.md](MEMORY.md) — dense, agent-facing durable knowledge. **Load this first.**
- [ARCHITECTURE.md](ARCHITECTURE.md) — living architecture and data-flow summary for the current code (the structure/overview layer).
- [../CONTEXT.md](../CONTEXT.md) — project vocabulary (glossary only).
- [../README.md](../README.md) — project entry point, quick start, build commands, and high-level feature list.
- [agents/](agents/) — per-repo config for the engineering skills (issue tracker, triage labels, domain-doc consumer rules).

## Decision records (`adr/`)

ADRs retain only the decision, its load-bearing rationale, guardrails, and current code evidence. They may be corrected or compressed during consolidation when the code has drifted. Superseded ADRs are collapsed into their successors (ADR-0005 lives on inside ADR-0006); retired files remain recoverable from git history and numbering is never reused.

| Path | Topic | Status |
|---|---|---|
| [adr/0001](adr/0001-encounter-status-probe-not-timeline-tracker.md) | encounter status probe, not timeline | accepted |
| [adr/0002](adr/0002-mountable-feature-registry.md) | mountable/feature registry | accepted |
| [adr/0003](adr/0003-history-panel-preview-overlay.md) | HistoryPanel ScreenSpaceOverlay preview | accepted |
| [adr/0004](adr/0004-preview-visibility-three-state-mode.md) | three-state preview visibility | accepted |
| [adr/0006](adr/0006-bazaaragent-as-its-own-plugin.md) | BazaarAgent as its own plugin (absorbs 0005) | accepted |
| [adr/0007](adr/0007-bazaaragent-external-replay-video-recording.md) | external replay video recording | accepted |
| [adr/0008](adr/0008-replay-continue-as-agent-action.md) | replay continue as agent `Continue` action | accepted |
| [adr/0009](adr/0009-preserve-behavior-specific-boundaries.md) | rejected cosmetic unifications / preserved behavior boundaries | accepted |
| [adr/0010](adr/0010-merged-destroy-collection-filter.md) | one Destroy chip covers the destroy-mechanic cluster | accepted |
| [adr/0011](adr/0011-pure-decision-cores-for-timing-invariants.md) | timing invariants in pure decision cores, not MonoBehaviour glue | accepted |

## Future Work

Task plans, feature requests, and bugs are tracked as **GitHub issues** (see [agents/issue-tracker.md](agents/issue-tracker.md)), not repo docs.

`drafts/` is the write buffer for **knowledge documents only** — design records, root-cause analyses, and decision/option analyses produced mid-session. Task plans do not go there. Consolidation promotes durable outcomes into MEMORY/ADR/ARCHITECTURE, moves actionable work to GitHub Issues, and deletes the spent draft; the directory therefore exists only while unswept drafts are pending.

## Historical material

Retired documents live only in git history. The last complete `docs/archive/` tree is recoverable at commit `82412f0c` (for example, `git show 82412f0c:docs/archive/<path>`). Historical claims must be rechecked against current code before use.

## Agent Rules

Agent rules and project-specific operating constraints live in [../CLAUDE.md](../CLAUDE.md). `AGENTS.md` is a symlink to that file.
