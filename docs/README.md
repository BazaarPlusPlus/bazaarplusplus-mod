# BazaarPlusPlus Docs

The documentation inventory, and the rules for changing it. When to open each document is decided once, in `CLAUDE.md`'s header table — this file does not repeat those triggers.

## Inventory

- [MEMORY.md](MEMORY.md) — invariants, settled decisions, and traps that fail silently.
- [ARCHITECTURE.md](ARCHITECTURE.md) — how the plugin is assembled and what seams are shared, with per-feature detail under [architecture/](architecture/).
- [../CONTEXT.md](../CONTEXT.md) — the glossary.
- [adr/](adr/) — decision records.
- [contracts/](contracts/) — wire and payload formats that outlive any one implementation.
- [agents/](agents/) — per-repo config for the engineering skills.
- [../README.md](../README.md) — the human entry point: features, install, quick start.

## Decision records

Every record below is accepted; each file's own `Status:` line is authoritative for amendments and absorptions. An ADR keeps the decision, its load-bearing rationale, guardrails, and current code evidence, and may be corrected or compressed when the code drifts. A superseded record is collapsed into the one that absorbed it (0005 lives inside 0006; 0008 inside 0007), and numbers are never reused.

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

## Document lifecycle

Each layer owns one thing, and links rather than restating: `CLAUDE.md` owns process, `MEMORY.md` knowledge, `ARCHITECTURE.md` structure, `adr/` rationale, GitHub issues the work.

**Work goes to GitHub issues, not to this tree.** Task plans, feature requests, bugs, acceptance checklists, implementation orders, and "pending confirmation" notes are issues. A document here describes how the system is, not what someone intends to do next.

**`drafts/` is the write buffer for knowledge documents only** — design records, root-cause analyses, decision analyses produced mid-session. A consolidation run promotes each draft's durable outcomes into MEMORY, an ADR, or ARCHITECTURE, moves any remaining work to issues, and then deletes the draft. The directory therefore exists only while unswept drafts are pending. `tests/docs/DocsHygieneTests.cs` fails when a draft goes stale.

**Edit policy.** `ARCHITECTURE.md`, `architecture/`, `contracts/`, and `adr/` may be corrected the moment the code drifts. `MEMORY.md` and this file are curated by consolidation runs, so new knowledge goes to `drafts/` first.

**Budgets are in bytes, not lines.** `CLAUDE.md` and `MEMORY.md` carry dense one-line entries, so a line count says nothing about what they cost an agent. The enforced ceilings live in `tests/docs/DocsHygieneTests.cs`; keeping under them means merging entries, not appending. `MEMORY.md`'s Gotchas section is exempt — that section is the reason the file exists, and compressing it to hit a budget defeats the budget.

## Historical material

Retired documents live only in git history. The last complete `docs/archive/` tree is recoverable at commit `82412f0c` (`git show 82412f0c:docs/archive/<path>`). Recheck any historical claim against current code before acting on it.
