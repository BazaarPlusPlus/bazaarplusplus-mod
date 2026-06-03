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
- [`2026-05-31-combat-replay-audio-macos-process-tap.md`](2026-05-31-combat-replay-audio-macos-process-tap.md) — macOS 音频采集后端：CoreAudio 进程级 tap（`AudioHardwareCreateProcessTap`，14.2+）+ 薄 `BppMacAudio.dylib`，C# 退化成与 WASAPI 相同的 pull 循环（已实现并验证；合并后移入 `archive/` 并加 `Status:` banner）。
- [`2026-05-31-collection-panel-design.md`](2026-05-31-collection-panel-design.md) — 全屏卡牌图鉴面板（仅 Item+Skill）：原生 `CardPreviewBase` + 有界实例池 + 回收式固定规格虚拟化网格 + hidden-tag/manual-rule 商人 chips 已落地；完整 spawner/offline 商人来源、热键重绑 UI、键盘导航、滚动惯性、共享抽象提取仍为后续项（**Implemented with follow-ups**）。
- [`2026-05-31-collection-panel-first-load-performance.md`](2026-05-31-collection-panel-first-load-performance.md) — Collection Panel 首次加载性能优化方案：loading shell、分段测量、无效 art key / negative cache、VM catalog 跨 scene runtime dispose 已落地；默认排序预计算、持久化 snapshot、prewarm 仍按运行日志决定（**Partially implemented**）。
- [`2026-05-31-sell-hotkey-regression-debug-plan.md`](2026-05-31-sell-hotkey-regression-debug-plan.md) — 安装 BazaarPlusPlus 后官方出售物品快捷键偶发失效的生产调试方案：梳理 native SellItem 输入链路、tooltip/keybind/raycast 失效假设、诊断日志和修复验证矩阵（**Draft，未开工**）。
- [`2026-05-31-history-panel-hero-portrait-badge-design.md`](2026-05-31-history-panel-hero-portrait-badge-design.md) — HistoryPanel 英雄文本徽章（`VAN`/`PYG`）改真实头像 Sprite：走 `CollectionManager.GetDefaultHeroSkin` + `SkinAssetDataSO.LoadPortraitSpriteAsync`（异步 Addressables，无硬编码 key），在现有 `Label` 上设 `backgroundImage`，文本徽章降级为 fallback；含 token 防陈旧与逐文件源码锚点（**Draft，未开工**）。
- [`2026-06-01-collection-panel-hero-portrait-chips-plan.md`](2026-06-01-collection-panel-hero-portrait-chips-plan.md) — CollectionPanel hero filter chips use the game's default hero portrait sprites through a shared provider; HistoryPanel badge adoption remains a follow-up.
- [`2026-06-02-collection-panel-source-catalog-schema.md`](2026-06-02-collection-panel-source-catalog-schema.md) — CollectionPanel 商人 / 训练师来源筛选改为 BPP 自有结构化 source catalog：schema、规则语义、contract tests、旧 game resolver fallback 删除边界。
- [`2026-06-02-item-board-preview-abstraction.md`](2026-06-02-item-board-preview-abstraction.md) — 抽出单卡 native `CardPreviewBase` primitive 与共享 item-board preview surface；HistoryPanel 与 CardSetPreview 已迁移，live `MonsterBoardTooltip` clone path 已移除（startup/resource runtime validation complete, manual interaction validation pending）。
- [`2026-06-03-settings-dock-anchor-and-collection-grid-scroll-plan.md`](2026-06-03-settings-dock-anchor-and-collection-grid-scroll-plan.md) — 两处 playtest 微调：设置 dock 面板生成锚点从按钮上边界改到下边界（仍向上弹出）；CollectionPanel 网格滚轮每格跳跃量 300→~120（保留即时跳转，不加滚动条）。（**Draft，未开工**）

## Archived specs

Archived specs are historical; each file carries its own `Status:` banner.

- `2026-06-02-collection-panel-offer-source-filtering.md` → superseded by [`2026-06-02-collection-panel-source-catalog-schema.md`](2026-06-02-collection-panel-source-catalog-schema.md)
- `2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md` → [ADR-0002](../adr/0002-mountable-feature-registry.md)
- `2026-05-23-combat-replay-sfx-impl.md` → [combat-replay.md](../features/combat-replay.md)
- `2026-05-24-pedestal-aware-preview-display-design.md` → [ADR-0004](../adr/0004-preview-visibility-three-state-mode.md), [tooltip-preview.md](../features/tooltip-preview.md)
- `2026-05-27-mod-ui-typography-token-foundation.md` → `Infrastructure/Fonts/`, `Infrastructure/UiTokens/`
- `2026-05-29-historypanel-fullscreen-responsive-design.md` → [history-panel.md](../features/history-panel.md)
- `2026-05-31-bpp-supporters-design.md` → `Game/Supporters/`, `tests/Supporters.Tests`
- `2026-05-31-history-collection-right-operation-rail-layout.md` → `Game/CollectionPanel/Ui/`, `Game/HistoryPanel/Ui/`, `Infrastructure/UiTokens/Sizes.cs`

> Earlier aspirational / superseded specs (AutoBazaar agent, HTTP rate-limit bypass, arm-then-record, skill showcase, V3 BazaarDB upload, SFX silent-analysis, the offscreen-RT migration) were pruned once their decisions landed in ADRs or they were confirmed never-built; recover them from git history if needed.
