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

- [`2026-05-31-collection-panel-design.md`](2026-05-31-collection-panel-design.md) — 全屏卡牌图鉴面板（仅 Item+Skill）：原生 `CardPreviewBase` + 有界实例池 + 回收式固定规格虚拟化网格 + hidden-tag/manual-rule 商人 chips 已落地；完整 spawner/offline 商人来源、热键重绑 UI、键盘导航、滚动惯性、共享抽象提取仍为后续项（**Implemented with follow-ups**）。
- [`2026-05-31-collection-panel-first-load-performance.md`](2026-05-31-collection-panel-first-load-performance.md) — Collection Panel 首次加载性能优化方案：loading shell、分段测量、无效 art key / negative cache、VM catalog 跨 scene runtime dispose 已落地；默认排序预计算、持久化 snapshot、prewarm 仍按运行日志决定（**Partially implemented**）。
- [`2026-05-31-sell-hotkey-regression-debug-plan.md`](2026-05-31-sell-hotkey-regression-debug-plan.md) — 安装 BazaarPlusPlus 后官方出售物品快捷键偶发失效的生产调试方案：梳理 native SellItem 输入链路、tooltip/keybind/raycast 失效假设、诊断日志和修复验证矩阵（**Draft，未开工**）。
- [`2026-05-31-history-panel-hero-portrait-badge-design.md`](2026-05-31-history-panel-hero-portrait-badge-design.md) — HistoryPanel 英雄文本徽章（`VAN`/`PYG`）改真实头像 Sprite：走 `CollectionManager.GetDefaultHeroSkin` + `SkinAssetDataSO.LoadPortraitSpriteAsync`（异步 Addressables，无硬编码 key），在现有 `Label` 上设 `backgroundImage`，文本徽章降级为 fallback；含 token 防陈旧与逐文件源码锚点（**Draft，未开工**）。
- [`2026-06-01-collection-panel-hero-portrait-chips-plan.md`](2026-06-01-collection-panel-hero-portrait-chips-plan.md) — CollectionPanel hero filter chips use the game's default hero portrait sprites through a shared provider; HistoryPanel badge adoption remains a follow-up. (**✅ 已实现**)
- [`2026-06-02-collection-panel-source-catalog-schema.md`](2026-06-02-collection-panel-source-catalog-schema.md) — CollectionPanel 商人 / 训练师来源筛选改为 BPP 自有结构化 source catalog：schema、规则语义、contract tests、旧 game resolver fallback 删除边界。(**✅ 已实现,schema 现为 v3 + groups 字段**)
- [`2026-06-02-item-board-preview-abstraction.md`](2026-06-02-item-board-preview-abstraction.md) — 抽出单卡 native `CardPreviewBase` primitive 与共享 item-board preview surface；HistoryPanel 与 CardSetPreview 已迁移，live `MonsterBoardTooltip` clone path 已移除（**已落地；HistoryPanel 现直接持有 BppItemBoardPreview，thin wrapper 阶段已合并跳过；manual interaction validation pending**）。
- [`2026-06-03-settings-dock-anchor-and-collection-grid-scroll-plan.md`](2026-06-03-settings-dock-anchor-and-collection-grid-scroll-plan.md) — 两处 playtest 微调：设置 dock 面板生成锚点从按钮上边界改到下边界（仍向上弹出）；CollectionPanel 网格滚轮每格跳跃量 300→~120（保留即时跳转，不加滚动条）。（**B 项已落地：滚轮步长 300→120，`CollectionGridConstants.cs:81`；A 项 anchor Y 改 0f 未实施**）
- [`2026-06-03-localization-module-extraction-design.md`](2026-06-03-localization-module-extraction-design.md) — 把本地化引擎（`LocalizedTextSet`/语言码匹配/简繁地区转换/CJK 判定）从 `Game/Settings` 抽离为零依赖独立程序集 `BazaarPlusPlus.Localization`：依赖反转 `ILanguageProvider`/`ILocaleModeProvider`、译文全量集中为嵌套静态类 `Loc` 目录、`BppChineseLocalization`→`ChineseScriptConverter`、删测试 shim（**P1+P2+P3 缓存键修复已落地；P0、P3 其余(Loc 目录/反射改造)、P4、P5 未做**）。
- [`2026-06-02-merchant-trainer-portrait-plan.md`](2026-06-02-merchant-trainer-portrait-plan.md) — 商人/训练师头像方案（**活计划；实际经 `CollectionSources/` 子系统落地，与原 `Game/CollectionPanel/Encounters/` 设计不同，「实现进展」段已订正**）。
- [`2026-06-03-collection-search-removal-sponsor-tweaks-rail-stability.md`](2026-06-03-collection-search-removal-sponsor-tweaks-rail-stability.md) — CollectionPanel 搜索移除 + sponsor 归因 + 操作 rail 稳定性（**✅ 全部落地（搜索移除/布局/赞助 A–E 各项）**）。
- [`2026-06-03-localization-module-extraction-refactor-prompt.md`](2026-06-03-localization-module-extraction-refactor-prompt.md) — 本地化抽取 executor prompt（**spent；§3 现状描述为抽取前状态，已加注**）。
- [`2026-06-04-collection-panel-day-filter-design.md`](2026-06-04-collection-panel-day-filter-design.md) — CollectionPanel 新增「天数 / Day」筛选：把当前运行天映射为等级上限（硬编码 Day 1=青铜 / 2–5=白银 / 6–7=黄金 / 8+=钻石，Legendary 随 Diamond），保留 `StartingTier ≤ 上限` 的卡，与现有 Tier 行独立 AND；纯过滤逻辑由 exe-runner 单测覆盖。**2026-06-05 改版**：天数选择器 → 顶部紧凑「天数」数字 icon（面显示运行天 / 局外 20，点击切换参与筛选）（**✅ 已实现（代码 + 单测 + Debug 构建通过），待游戏内手测验证**）。
- [`2026-06-05-item-board-slot-grid-layout.md`](2026-06-05-item-board-slot-grid-layout.md) — LiveBuildPanel / HistoryPanel 共享 item-board preview 改为显式全宽 10-slot grid：shop 行先由 planner 计算居中的 display slots，renderer 只按 `DisplaySocketId + DisplaySpan` 居中等比放置 native cards，slot 背板可控显隐（**✅ 已实现（代码 + 单测 + Debug 构建通过），待游戏内手测验证**）。
- [`2026-06-07-live-build-refresh-and-history-health-layout.md`](2026-06-07-live-build-refresh-and-history-health-layout.md) — 把十胜阵容手动拉取入口从 HistoryPanel 归位到 LiveBuildPanel，并把 HistoryPanel 的远端连通性检测按钮移到本地 DB chip 同一 overview 行；保持 DB 本地状态与 server health 远端状态分离（**Draft，待确认**）。
- [`2026-06-07-collection-tag-native-typography.md`](2026-06-07-collection-tag-native-typography.md) — CollectionPanel 标签筛选行的文案/配色切到游戏原生 tooltip typography（keyword 配置 + `LocalizableText`），**无 fallback**：删除手工词典 `CollectionPanelText.Tag(ECardTag)`；顺带结构性修复 locale 切换后 chip 文案滞留 bug；渲染保持 UITK，新增 `GameInterop/TagTypography/` 适配器（**Draft，待红队评审**）。

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
- `2026-05-30-ffmpeg-relocation-to-mod-design.md` → [combat-replay.md](../features/combat-replay.md)
- `2026-05-30-combat-replay-record-button-design.md` → [combat-replay.md](../features/combat-replay.md)
- `2026-05-30-combat-replay-audio-loopback-capture.md` → [combat-replay.md](../features/combat-replay.md)
- `2026-05-31-combat-replay-audio-macos-process-tap.md` → [combat-replay.md](../features/combat-replay.md), `native/mac-audio-tap/`
- `2026-05-30-combat-replay-audio-and-capture-perf-design.md` → superseded(audio)/implemented(video),见 [combat-replay.md](../features/combat-replay.md)
- `2026-06-07-bazaaragent-external-battle-video-recording-design.md` → [ADR-0007](../adr/0007-bazaaragent-external-replay-video-recording.md), [bazaar-agent-http-api-v1.md](../reference/bazaar-agent-http-api-v1.md)

> Earlier aspirational / superseded specs (AutoBazaar agent, HTTP rate-limit bypass, arm-then-record, skill showcase, V3 BazaarDB upload, SFX silent-analysis, the offscreen-RT migration) were pruned once their decisions landed in ADRs or they were confirmed never-built; recover them from git history if needed.
