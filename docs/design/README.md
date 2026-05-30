# Design Specs

Dated, point-in-time design documents (`YYYY-MM-DD-<slug>.md`). A spec captures *how a change was planned*; once the work lands or dies it becomes immutable history.

## Convention

- **Active proposals** (work not yet started) live at this top level.
- **Finished specs** (implemented / superseded / abandoned) live under [`archive/`](archive/), each with a `Status:` banner at the very top:
  - `ASPIRATIONAL` — never implemented; the world may have moved on.
  - `IMPLEMENTED (historical)` — shipped; the living truth is now a feature doc or code.
  - `SUPERSEDED by <doc>` — replaced by a later spec or decision.
- When a spec records a decision that outlives it (an abstraction choice, a rejected approach, a tradeoff), promote that decision to a [docs/adr/](../adr/) entry **before** archiving — ADRs are the canonical "why", specs are the historical "how we planned it".

## Active proposals

Work designed but not yet (fully) landed; lives at this top level until implemented, then moves to `archive/` with a status banner.

- [`2026-05-30-ffmpeg-relocation-to-mod-design.md`](2026-05-30-ffmpeg-relocation-to-mod-design.md) — FFmpeg 改为随 mod 分发的兄弟二进制（已实现并验证，待提交；合并后移入 `archive/` 并加 `Status:` banner）。
- [`2026-05-30-combat-replay-record-button-design.md`](2026-05-30-combat-replay-record-button-design.md) — Combat Replay 视频录制从全局开关自动录每场改为 HistoryPanel 按钮单次触发（已实现并验证，待提交；合并后移入 `archive/` 并加 `Status:` banner）。
- [`2026-05-30-combat-replay-audio-loopback-capture.md`](2026-05-30-combat-replay-audio-loopback-capture.md) — Combat Replay 录制音频改用 WASAPI loopback（设备输出）采集 + 跨平台 adapter；记录从 FMOD tap 到 loopback 的排查历程与踩坑（已实现并验证）。

## Archived specs

All remaining archived specs are `IMPLEMENTED (historical)` — shipped; the living truth is the linked ADR / feature doc / code:

- `2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md` → [ADR-0002](../adr/0002-mountable-feature-registry.md)
- `2026-05-23-combat-replay-sfx-impl.md` → [combat-replay.md](../features/combat-replay.md)
- `2026-05-24-pedestal-aware-preview-display-design.md` → [ADR-0004](../adr/0004-preview-visibility-three-state-mode.md), [tooltip-preview.md](../features/tooltip-preview.md)
- `2026-05-27-mod-ui-typography-token-foundation.md` → `Infrastructure/Fonts/`, `Infrastructure/UiTokens/`
- `2026-05-29-historypanel-fullscreen-responsive-design.md` → [history-panel.md](../features/history-panel.md)

> Earlier aspirational / superseded specs (AutoBazaar agent, HTTP rate-limit bypass, arm-then-record, skill showcase, V3 BazaarDB upload, SFX silent-analysis, the offscreen-RT migration) were pruned once their decisions landed in ADRs or they were confirmed never-built; recover them from git history if needed.
