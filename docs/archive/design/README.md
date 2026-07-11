---
status: superseded
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Design Specs

Dated, point-in-time design documents (`YYYY-MM-DD-<slug>.md`). A spec captures *how a change was planned*; once the work lands or dies it becomes immutable history.

## Convention

- Every file carries its own status in frontmatter and/or a `> Status:` banner at the top (`implemented` / `superseded` / `abandoned` / `parked`). Per-file banners are the source of status truth — this README keeps no per-file roster (the old roster drifted and was dropped in the 2026-07-11 cleanup).
- The nested [`archive/`](archive/) subdirectory holds the pre-2026-06-10 finished set; later sweeps place finished specs at this top level with banners. Both layers are equally frozen.
- When a spec records a decision that outlives it (an abstraction choice, a rejected approach, a tradeoff), promote that decision to a [docs/adr/](../../adr) entry — ADRs are the canonical "why", specs are the historical "how we planned it".
- The 2026-07-11 archive cleanup deleted low-value specs (executor prompts, checkbox twins, fully-code-superseded plans); links to them from surviving frozen docs are intentionally left dangling — recover any target from git history.
