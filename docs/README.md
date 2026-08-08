# BazaarPlusPlus Docs

The code is the source of truth. Current implementation guidance lives in one architecture document plus compact decision records. Historical working documents live in git history, not in the active tree. This file is the single documentation map.

## Current Docs

- [MEMORY.md](MEMORY.md) — dense, agent-facing durable knowledge. **Load this first.**
- [ARCHITECTURE.md](ARCHITECTURE.md) — living architecture and data-flow summary for the current code (the structure/overview layer).
- [../CONTEXT.md](../CONTEXT.md) — project vocabulary (glossary only).
- [../README.md](../README.md) — project entry point, quick start, build commands, and high-level feature list.
- [agents/](agents/) — per-repo config for the engineering skills (issue tracker, triage labels, domain-doc consumer rules).

## Decision records (`adr/`)

ADRs retain only the decision, its load-bearing rationale, guardrails, and current code evidence. They may be corrected or compressed during consolidation when the code has drifted. Superseded or absorbable ADRs are collapsed into the surviving record (ADR-0005 lives on inside ADR-0006; ADR-0008 inside ADR-0007); retired files remain recoverable from git history and numbering is never reused.

All records below are accepted; each file's own `Status:` line is authoritative for amendments and absorptions.

| Path | Topic |
|---|---|
| [adr/0001](adr/0001-encounter-status-probe-not-timeline-tracker.md) | encounter status probe, not timeline |
| [adr/0002](adr/0002-mountable-feature-registry.md) | mountable/feature registry |
| [adr/0003](adr/0003-history-panel-preview-overlay.md) | HistoryPanel ScreenSpaceOverlay preview |
| [adr/0004](adr/0004-preview-visibility-three-state-mode.md) | three-state preview visibility |
| [adr/0006](adr/0006-bazaaragent-as-its-own-plugin.md) | BazaarAgent as its own plugin (absorbs 0005) |
| [adr/0007](adr/0007-bazaaragent-external-replay-video-recording.md) | explicit replay exit through the agent `Continue` action (absorbs 0008) |
| [adr/0009](adr/0009-preserve-behavior-specific-boundaries.md) | rejected cosmetic unifications / preserved behavior boundaries |
| [adr/0010](adr/0010-merged-destroy-collection-filter.md) | one Destroy chip covers the destroy-mechanic cluster |
| [adr/0011](adr/0011-pure-decision-cores-for-timing-invariants.md) | timing invariants in pure decision cores, not MonoBehaviour glue |
| [adr/0012](adr/0012-outbound-network-ownership.md) | outbound Mod API protocol and persistence owners |
| [adr/0013](adr/0013-remote-data-and-release-boundaries.md) | runtime catalogs, release manifest, and build seed fetch are three lifecycles |

## Future Work

Task plans, feature requests, and bugs are tracked as **GitHub issues** (see [agents/issue-tracker.md](agents/issue-tracker.md)), not repo docs.

`drafts/` is the write buffer for **knowledge documents only** — design records, root-cause analyses, and decision/option analyses produced mid-session. Task plans do not go there. Consolidation promotes durable outcomes into MEMORY/ADR/ARCHITECTURE, moves actionable work to GitHub Issues, and deletes the spent draft; the directory therefore exists only while unswept drafts are pending.

## Historical material

Retired documents live only in git history. The last complete `docs/archive/` tree is recoverable at commit `82412f0c` (for example, `git show 82412f0c:docs/archive/<path>`). Historical claims must be rechecked against current code before use.

## Agent Rules

Agent rules and project-specific operating constraints live in [../CLAUDE.md](../CLAUDE.md). `AGENTS.md` is a symlink to that file.
