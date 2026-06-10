---
status: superseded
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---


# BazaarPlusPlus 模组 文档 / 注释 / 代码漂移审计 — 综合报告

> 注(2026-06-07):本审计大部分 P0/P1 建议已由 commits 60c2bb0、854e4e1、11ef848 落地;§7 顺序 7-16 的物理归档与 §9 步骤 22 的注释修正由 2026-06-07 文档对齐批次完成;§9 步骤 23-27 代码任务仍待独立执行。

## 1. 概览与统计

### 1.1 审计范围与方法
- **范围**：24 个审计单元 = 18 个代码子系统 + 6 个文档集（docset）。`decompiled/` 全程排除（只读参考）。
- **方法**：以**代码为唯一事实源**（code-as-source-of-truth）。文档、注释、PRD、计划稿均视为参考，与代码冲突时一律以代码为准并标注漂移点。每一条 `high` 级与每一条 `delete`/`archive` 处置建议都经过**对抗式独立复核**（verdict 字段：confirmed / adjusted），并附 `file:line` 证据。
- **注释审计为"声明导向"（claim-focused）**：只核查**可验证断言型注释**（如 XML summary 中"由 X 调用"、"native board 为 2400x600"、"FMOD mixer 线程为生产者"），**不做逐行注释扫描**。
- 删除/归档风险评估前，已对每个候选文档在 `docs/`、`Game/`、`Core/`、`AutoBazaar/`、`ModApi/`、`Storage/` 全树 ripgrep 入链引用。

### 1.2 数量统计
- **总有效发现：91 条**（去重并应用 adjusted 严重度后）。
- **refuted 丢弃：0 条**（本批无 refuted）。
- 按严重度（去重 + adjusted 后）：
  - **critical：0**
  - **high：12**
  - **medium：31**
  - **low：48**
- 按类别：`doc-drift` 56 · `comment-drift` 11 · `doc-gap` 13 · `obsolete-doc` 9 · `contract-drift` 4 · `deprecated-feature-doc` 1 · `obsolete-doc(去重合并)` 若干。

### 1.3 严重度调整与去重摘要（透明记录）
经对抗复核后**降级**的高危发现（原 high → 实际 low/medium）：
- ghost §4「hour/encounter_id/combat_kind 全为 null/default」→ **low**（仅 `CombatKind` 漏掉 `"PVPCombat"` 兜底，其余属实，但行为良性）。
- `ModApiJsonPost` "upload clients"（复数）→ **low**。
- `2026-05-30-combat-replay-audio-and-capture-perf-design.md` 归档遗漏 → **low**。
- `2026-06-02-settings-dock-clone-button.md` 已落地计划 → **low**。
- 本地化 `FontAtlasSampleCache` 单键缓存 bug → **medium**（确为真实潜伏 bug，但设计文档自身明确将其列为"P3 待修"且 P3 未启动，故非文档漂移，是"有计划的潜伏 bug"）。
- 本地化设计/状态 banner "未开工" → **low**（P1+P2 已落地但 P3–P5 仍未做，文档作为活计划仍有效，仅状态措辞陈旧）。
- `CLAUDE.md` "三个 csproj/三个程序集" → 拆为两条，分别 **high / low**。
- `README.md` 四个 combat-replay spec "待提交" → **low**（纯组织性整理，不误导集成方）。

**跨单元去重合并**（同一底层问题被多个重叠单元各自报告，已合并证据）：
1. **终局截图延迟 8s vs 文档 10s** — 合并 4 条（`screenshots.md` Trigger、`screenshots.md:13`、`mod-features-overview.md:95`、Lobby 单元的 client-flow 重述）。
2. **ghost 响应 hour/encounter_id/combat_kind** — 合并 2 条（doc-drift §4 + comment-drift Step 4）。
3. **CollectionPanel 缺权威文档 / 缺 overview 条目** — 合并 3 条（CollectionPanel 单元 + Core 单元 + docset 单元）。
4. **本地化抽取设计 "未开工" 状态陈旧** — 合并 3 条（Localization 单元 + docset 单元 + design/README 单元）。
5. **AutoBazaar 挂载"注释掉" vs `#if BPP_AUTOBAZAAR_HOST` 编译开关** — 合并 4 条（README、MEMORY.md、isolation spec、isolation plan）。
6. **`PreviewCardLifecyclePolicy.cs` 死代码 + 残留 `Game.PreviewSurface` 命名空间** — 合并 3 条（ADR-0003 视角、obsolete-doc 视角、design 删除清单遗漏视角）。

---

## 2. 文档 ↔ 代码偏移汇总

> 仅列**已确认**的 doc-drift / contract-drift。`代码证据` 一律 `file:line`（不翻译标识符与路径）。

### 2.1 High 级

| 文档 | 偏移点 | 代码证据(file:line) | 严重度 | 建议 |
| --- | --- | --- | --- | --- |
| `docs/features/history-panel.md:12` | `RunsColumnWidthPercent=30` / `PreviewHeightPercent=28`，实际为 52f / 33f，差距极大 | `Infrastructure/UiTokens/Sizes.cs:12-13`；用于 `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:55,116` | high | update-doc |
| `docs/features/screenshots.md:13`（同 `docs/mod-features-overview.md:95`） | 终局截图窗口标注"满 10 秒"，实际常量为 8s | `Game/Screenshots/EndOfRunScreenshotController.cs:21`（`FirstCaptureDelaySeconds = 8f`，用于 :463/:466/:471/:485） | high | update-doc |
| `docs/design/2026-05-31-collection-panel-design.md:3,659,973,920` | 状态 banner 与 §10.3/§13/§16.5 仍称商人来源筛选"deferred"；实际结构化 source catalog 已全量落地 | `Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:1`；`Game/CollectionPanel/CollectionSourceOfferPoolCache.cs:16-29`；`Game/CollectionPanel/CollectionPanel.cs:60,131,594` | high | update-doc |
| `docs/design/2026-05-31-collection-panel-design.md:10,276-279,294,653` | §4.1 过滤引擎代码草图仍含 `f.Search` 搜索分支 + debounce；搜索已于 2026-06-03 删除 | `Game/CollectionPanel/Data/CollectionFilterEngine.cs:15`（无 search 分支）；`Game/CollectionPanel/Data/CollectionFilterState.cs:17`（无 Search 属性） | high | update-doc |
| `docs/design/2026-06-03-settings-dock-anchor-and-collection-grid-scroll-plan.md:39-42` | 改动 A 要求 anchor Y 由 1f 改 0f；代码仍是 1f（**计划未实施**，但文档标"approved"） | `Game/Settings/BppSettingsDockController.Presentation.cs:15-16`（Y=1f） | high | update-code（独立任务） |
| `docs/design/2026-06-02-merchant-trainer-portrait-plan.md`「实现进展」段 | 声称已实现 5 个文件（`tools/encounter-portraits/build_catalog.py`、`Data/Encounters/...`、`Game/CollectionPanel/Encounters/...`）全部不存在；实际走 `CollectionSources/` 架构 | `Game/CollectionPanel/Sources/CollectionSourceEnums.cs:5-9`；`Game/CollectionPanel/Sources/CollectionSourceEntry.cs:17`；`GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs:1` | high | update-doc |
| `docs/design/2026-06-02-merchant-trainer-portrait-plan.md` §3.1/§3.2 | §3.2 `Game/CollectionPanel/Encounters/` 架构整段未实现（目录不存在；`CollectionCatalogBuildSession` 无 `EncounterCards` 扩展） | `Game/CollectionPanel/Data/CollectionCardClassifier.cs:69`；`Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs:399`（实际消费在 Sources 子系统） | high（doc 自标 §3.2"待做"，故实质 low） | update-doc |
| `docs/reference/sqlite-schema-reference.md:30` | 摘要项"Upload payload schema version: 1"；实际为 5（与本文档 :291 自相矛盾） | `Storage/RunLog/RunLogSchema.cs:14`（`UploadPayloadSchemaVersion => 5`） | high | update-doc |
| `docs/design/README.md:30`（= `2026-06-03-localization-module-extraction-design.md:3`） | 本地化抽取标"Draft，未开工"；P1+P2 已落地（程序集存在、`L.Install` 已接线） | `Localization/L.cs:1`；`BazaarPlusPlus.Localization.csproj:1`；`Plugin.cs:108` | high（实质 low：P3–P5 仍未做，活计划有效） | update-doc |
| `docs/design/README.md:18-21` | 四个 combat-replay spec 仍标"待提交"且未移入 archive/ + 无 `Status:` banner（均已合并数周） | `Game/CombatReplay/Video/FfmpegLocator.cs:1`；`Game/CombatReplay/Audio/WasapiLoopbackCaptureTap.cs:1`；`Game/CombatReplay/Audio/CoreAudioProcessTapCaptureTap.cs:1` | high（实质 low：纯导航整理） | update-doc |
| `CLAUDE.md:48` | "Three assemblies ship as the mod"；实为 4 个无条件 + 1 个条件（Localization 永远随发） | `BazaarPlusPlus.csproj:146,241,319`（无 Condition） | high | update-doc |
| `docs/mod-features-overview.md`（lines 9-117） | CollectionPanel 子系统（49 源文件、已注册 mountable）整篇零提及 | `Game/CollectionPanel/CollectionPanelMount.cs:13`；注册于 `BppComposition.cs:101` | high | update-doc / create-doc |

### 2.2 Medium 级

| 文档 | 偏移点 | 代码证据(file:line) | 严重度 | 建议 |
| --- | --- | --- | --- | --- |
| `docs/features/ghost-battle-data-flow.md` §1/§6 | 漏列 `PlayerPrestige/PlayerVictories/OpponentPrestige/OpponentVictories`（已落地并 projection 翻转） | `Game/PvpBattles/PvpBattleParticipants.cs:22-35`；`GhostBattleLocalProjector.cs:20-29,45-57` | medium | update-doc |
| `docs/features/screenshots.md` UI Suppression | 仅列"设置坞、combat status bar"，漏 `CollectionPanelDockButtonController` | `Game/Screenshots/EndOfRunScreenshotController.cs:334-339` | medium | update-doc |
| `docs/reference/auto-bazaar-http-api-v1.md` §6 | `SelectEncounter` 标 group `Route`；代码发 `Offer`（`Route` 枚举从未被赋值） | `Game/AutoBazaarHost/AutoBazaarGameContextReader.cs:714`；`AutoBazaar/Contract/AutoBazaarDecision.cs:31` | medium | update-doc |
| `docs/reference/auto-bazaar-http-api-v1.md` §6 Dispatches 列 | 称 `Cmd.GetInstance().Xxx()`；实为 `AppState.CurrentState.{Method}()` 反射（仅 `StartOrContinueRun` 相符） | `Game/AutoBazaarHost/AutoBazaarGameActionDispatcher.cs:71-127` | medium | update-doc |
| `docs/reference/auto-bazaar-decision-surface.md:46` | `InteractableTemplateIds` 归因于已删类 `AutoBazaarInteractionFilterProbe` | `Game/AutoBazaarHost/AutoBazaarGameContextReader.cs:159-161` | medium | update-doc |
| `docs/features/combat-status-bar.md` Runtime Flow step 2 | 称 HUD 展示 frame index；实际只渲染逻辑时间（无 frame-index 部件） | `Game/CombatStatusBar/CombatStatusBar.State.cs:133-138`（`GetCurrentCombatFrameIndex` 仅算毫秒，不渲染） | medium | update-doc |
| `docs/design/2026-06-02-collection-panel-source-catalog-schema.md:67-94,265` | 标 `schemaVersion 2`；代码/数据为 3，且强制 `groups` 字段（文档完全未提） | `Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:15`；`Data/CollectionSources/collection-sources.json:2` | medium | update-doc |
| `docs/design/2026-06-01-collection-panel-hero-portrait-chips-plan.md`（Task 2/3） | 计划的 `_heroChipLabels`/`HeroChipMinWidth`/带文字的 chip 均未落地；实际为方形纯图标 chip，`RefreshHeroChip` 为 stub | `Game/CollectionPanel/Ui/CollectionPanelView.cs:101`；`CollectionPanelView.Filters.cs:238`；`Infrastructure/UiTokens/Sizes.cs:32` | medium | update-doc |
| `docs/design/2026-05-31-collection-panel-design.md` §16.1 | as-built 文件清单（~25）严重过期；实际 49 个 .cs（含整个 `Sources/`） | `Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:1` | medium | update-doc |
| `docs/design/2026-05-31-collection-panel-design.md` §2.3 | 草图 `OpenFromDockEntry()`/`CloseFromExternalRequest()`；实为 `OpenFromDockButton()` + `HistoryPanelHost.Instance?.ToggleFromHotkey()` | `Game/CollectionPanel/CollectionPanel.cs:106,218` | medium | update-doc |
| `docs/design/2026-06-02-merchant-trainer-portrait-plan.md` §3.1 | 规定用反射调 `LoadAssetAsyncByAddress`；实际直接泛型调用 | `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs:75` | medium | update-doc |
| `docs/design/2026-06-02-merchant-trainer-portrait-plan.md` §5 | 引用 `CollectionFilterEngine.AnyHeroMatch(:90-101)` 不存在；实为 `CollectionHeroScope` | `Game/CollectionPanel/Data/CollectionHeroScope.cs:28` | medium | update-doc |
| `docs/adr/0002-mountable-feature-registry.md:14` | 称"仅 `HistoryPanelMount` 为定制类"；`CollectionPanelMount` 亦为 bespoke | `Game/CollectionPanel/CollectionPanelMount.cs:13,25` | medium | update-doc |
| `docs/mod-features-overview.md:29` | mountables 列表缺 `CollectionPanelMount` 与 `ComponentMount<MainMenuVersionCheckController>` | `BppComposition.cs:101,109-111` | medium | update-doc |
| `docs/reference/settings-and-debug-surfaces.md:13` | 标"BazaarDB Upload"；实际 UI 英文 label 为"Upload screenshots to BazaarDB" | `Game/Screenshots/Upload/BazaarDbSnapshotUploadSettingsMenuLabel.cs:10` | medium | update-doc |
| `docs/reference/settings-and-debug-surfaces.md:5-6` | 称定义来自 `BppSettingsDockCatalog.cs`；实为 7 个 `ISettingsDockEntry` 经 registry 物化（catalog 仅排序存储） | `BppComposition.cs:89-95` | medium | update-doc |
| `docs/design/archive/2026-05-31-bpp-supporters-design.md` Sampling Policy | tier-4 权重写 `4 => 6`；实际 `4 => 9` | `Game/Supporters/BPPSupporterSampler.cs:89` | medium | update-doc |
| `docs/design/archive/2026-05-31-bpp-supporters-design.md` Proposed Module | 仅记 `Sample()`；已增 `SampleMany(int)` 且为主调用点 | `Game/Supporters/BPPSupporters.cs:13`（CollectionPanel.cs:231 / HistoryPanel.cs:204 调用） | medium | update-doc |
| `docs/superpowers/plans/2026-06-03-exclusive-skill-health-sponsor.md`（多处） | 全篇用陈旧 `BazaarDbScreenshot...` 命名；实际 `BazaarDbSnapshot...` | `Game/Screenshots/Upload/BazaarDbSnapshotUploadController.cs:16` | medium | update-doc |
| `docs/superpowers/plans/2026-06-03-exclusive-skill-health-sponsor.md:33,1109` | `LanguageCodeMatcher` 路径/命名空间错（写 `Game/Settings`，实为 `Localization/`） | `Localization/LanguageCodeMatcher.cs:1,4` | medium | update-doc |
| `docs/features/screenshots.md:40` | Client Flow 漏 health probe 步骤、错置 `player_account_id` 检查（实为循环前一次性短路） | `Game/Screenshots/Upload/BazaarDbSnapshotUploadService.cs:50-57,78-97` | medium | update-doc |
| `docs/design/2026-06-03-localization-module-extraction-refactor-prompt.md` §3 | 称 `GetLanguageCode()` 已全删；`BPPSupporterAttributionRow.cs:193-203` 仍有，且多处直读 `PlayerPreferences.Data.LanguageCode` | `Game/Supporters/Ui/BPPSupporterAttributionRow.cs:193`；`Game/Settings/BppSettingsDockController.cs:328`；`Game/Input/BppHotkeyService.cs:189` | medium | update-doc |
| `docs/design/2026-06-03-localization-module-extraction-design.md`「公共接口契约」 | `L` 契约漏 `CurrentLanguageCode`/`CurrentMode`（已被调用方直接使用） | `Localization/L.cs:22-24`；`Game/HistoryPanel/HistoryPanelText.cs:832,849` | medium | update-doc |
| `docs/reverse-engineering/data-structure-catalog.md:264` | `BattleProjection` 漏 `WinnerCombatantId`/`LoserCombatantId`（wire 字段） | `ModApi/Models/RunBundleUploadRequest.cs:144-148` | medium | update-doc |
| `docs/reverse-engineering/data-structure-catalog.md:264` | `BattleProjection` 漏 `PlayerPrestige/PlayerVictories/OpponentPrestige/OpponentVictories`（commit 7b8c380 新增 wire 字段） | `ModApi/Models/RunBundleUploadRequest.cs:111` | medium | update-doc |
| `CLAUDE.md:54` | "All three csproj files…"；实为 5 个 csproj / 4 个源码树（AutoBazaar+Localization 同样 `<Compile Include>`） | `BazaarPlusPlus.AutoBazaar.csproj:17-18`；`BazaarPlusPlus.Localization.csproj:16-17`；`Directory.Build.props:21-29` | medium | update-doc |
| `CLAUDE.md:62-68` | Layer boundaries 五层列表整体漏 `Localization/` 源码层 | `BazaarPlusPlus.Localization.csproj:17`；`BazaarPlusPlus.csproj:229` | medium | update-doc |
| `docs/superpowers/specs/2026-06-02-autobazaar-module-isolation-design.md:4,8` | Context 把迁移前状态当现状（`Game/AutoBazaar` 已不存在）；Status 仍"pending review" | `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj:27`（ProjectReference）；`tests/Architecture.Tests/CoreLayeringTests.cs:253` | medium | update-doc |
| `docs/design/README.md` Active proposals | 缺 3 个实存于 `docs/design/` 的文档（merchant-trainer-portrait-plan、collection-search-removal、localization-refactor-prompt） | `Game/CollectionPanel/Ui/CollectionPanelView.cs:1` | medium | update-doc |
| `docs/design/archive/2026-05-23-combat-replay-sfx-impl.md` §7 | 声称交付 `Patches/Combat/ReplayStateAudioDiagnosticPatch.cs`（不存在）；`LogReplayAudioState` 亦不在 `AudioBankWarmer.cs` | `Game/CombatReplay/Warmup/AudioBankWarmer.cs:92` | medium | update-doc |
| `docs/design/archive/2026-05-24-pedestal-aware-preview-display-design.md` §4.2 | 声称新增 `UpgradePreviewModeConfig`；`IBppConfig` 从未加（升级预览仅 hold-Shift） | `Core/Config/IBppConfig.cs:11`；`Game/Tooltips/TooltipPreviewModePolicy.cs:29` | medium | update-doc |

### 2.3 Low 级（节选 — 完整清单见各单元；此处列要点）

| 文档 | 偏移点 | 代码证据(file:line) | 建议 |
| --- | --- | --- | --- |
| `docs/features/run-logging-and-upload.md` 关键文件:70 | `HistoryPanelRepository.cs` 路径错（实为 `Game/HistoryPanel/Storage/`） | `Game/HistoryPanel/Storage/HistoryPanelRepository.cs` | update-doc |
| `docs/features/run-logging-and-upload.md` wire 表:53-58 | metadata 表漏 `artifact_codec` 行（常量 MIME） | `ModApi/Models/RunBundleUploadRequest.cs:18-19` | update-doc |
| `docs/superpowers/plans/2026-06-04-upload-dto-performance.md:724,932,97/103` | 动态 artifact 文件名 / "lower dimensions" 描述 / `player_position` 非 null 示例，均与代码不符 | `ModApi/Http/RunBundleMultipartContent.cs:12`；`Game/Screenshots/Upload/BazaarDbSnapshotImagePreparer.cs:104-108`；`Game/RunLogging/Upload/RunBundleUploadStore.cs:201-202` | update-doc |
| `docs/features/ghost-battle-data-flow.md` §4 | 称 hour/encounter_id/combat_kind 全 null/default；`CombatKind` 实有 `"PVPCombat"` 兜底 | `ModApi/Clients/GhostBattleClient.cs:187-205` | update-doc |
| `docs/features/ghost-battle-data-flow.md` §3 | result 写 `"Win"/"Loss"`；实际 recorder 写小写 `"win"/"loss"` | `Game/PvpBattles/PvpBattleSnapshotCollector.cs:468-470` | update-doc |
| `docs/features/screenshots.md` Client Flow | 漏 health probe 前置门 | `Game/Screenshots/Upload/BazaarDbSnapshotUploadService.cs:79-97` | update-doc |
| `docs/features/monster-preview.md` Runtime Entry | `CardSetPreviewRuntime` 注册归因 `Plugin.cs`；实为 `BppComposition.cs:100` | `BppComposition.cs:100` | update-doc |
| `docs/features/tooltip-preview.md:21` | `UpgradePreviewTooltipPatch` 列为 policy 直接调用方；实为经 `UpgradeTooltipScheduler` 间接 | `Game/Tooltips/UpgradeTooltipScheduler.cs:23` | update-doc |
| `docs/features/monster-preview.md` History Panel | 称直用 `Data.GetStatic().GetCardById(Guid)`；实经 `BppStaticDataAccess` 间接层 | `GameInterop/StaticCards/BppStaticDataAccess.cs:22` | update-doc |
| `docs/adr/0004-preview-visibility-three-state-mode.md:18` | "upgrade wins ties"；实为有序 if-chain，无 tie 场景 | `Game/Tooltips/TooltipPreviewModePolicy.cs:30` | update-doc |
| `docs/reference/auto-bazaar-http-api-v1.md:166` | "12 action kinds"；枚举仅 11 | `AutoBazaar/Contract/AutoBazaarDecision.cs:12-24` | update-doc |
| `docs/reference/auto-bazaar-http-api-v1.md` §4 错误表 | 漏 500 / `internal`（dispatch 失败返回） | `AutoBazaar/Runtime/AutoBazaarRuntimeController.cs:185` | update-doc |
| `docs/reference/auto-bazaar-decision-surface.md:44` | `CurrentEncounterType` 漏静态数据先查步骤 | `GameInterop/Encounter/EncounterTypeResolver.cs:39-47` | update-doc |
| `docs/reference/sqlite-schema-reference.md:78-82` | `runs` 写路径列为 `SqliteRunLogStore.*`；类名实为 `RunLogStore` | `Storage/RunLog/RunLogStore.cs:11` | update-doc |
| `docs/design/2026-06-03-collection-search-removal-sponsor-tweaks-rail-stability.md` Part B/S1 | 批准 NoWrap 两行布局；`primaryControlsRow` 仍 `Wrap.Wrap`，Reset 按钮未落地 | `Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:165` | update-doc |
| `docs/design/2026-06-02-merchant-trainer-portrait-plan.md`（多处行号） | `CollectionPanelView.Filters.cs`、`Sizes.cs`、`BppComposition.cs:98`、`CollectionMerchantKind.cs:8-20` 行号大面积漂移 | `CollectionPanelView.Filters.cs:257`；`Sizes.cs:32`；`BppComposition.cs:101`；`CollectionMerchantKind.cs:22` | update-doc |
| `docs/design/archive/2026-05-27-mod-ui-typography-token-foundation.md:1,109,116-117` | banner 称"fonts 全落地"；G1 准则1（grep `LegacyRuntime.ttf` 零命中）+准则4 未达成 | `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:707,611` | update-doc |
| `docs/design/archive/2026-05-29-historypanel-fullscreen-responsive-design.md:7` | 引用 `history-panel-known-issues.md §1`（死链，文件不存在；无 §1 标题） | （无对应文件） | update-doc |
| `docs/reference/hotkeys-reference.md` Rebindable | 暗示 KeyCode 默认；实为 Input System binding path | `Game/Input/BppHotkeyService.cs:17,50-51` | update-doc |
| `README.md:25,59` / `README_en.md` | 仓库结构漏 `Localization/`+`AutoBazaar/`；手动安装漏 `BazaarPlusPlus.Localization.dll` | `BazaarPlusPlus.csproj:146,241` | update-doc |
| `README.md`（AutoBazaar bullet）/ `MEMORY.md` | 称挂载"注释掉于 BppComposition.cs:120"；实为 `#if BPP_AUTOBAZAAR_HOST`（:126-128），:120 是无关行 | `BppComposition.cs:126-128` | update-doc |
| `docs/superpowers/plans/2026-06-02-autobazaar-module-isolation.md:64` / spec:197 | 称挂载"commented-out"；代码用编译开关 | `BppComposition.cs:126` | update-doc |
| `docs/superpowers/plans/2026-06-03-exclusive-skill-health-sponsor.md:33` | Chinese 码列表漏 `zh-SG`；服务类名 `BazaarDbScreenshotUploadService` 不存在 | `Localization/LanguageCodeMatcher.cs:14`；`Game/Screenshots/Upload/BazaarDbSnapshotUploadService.cs:60` | update-doc |
| `docs/superpowers/plans/2026-06-04-upload-dto-performance.md:723` vs :48 | 文档内部不一致：per-run 文件名 vs 固定 `run-bundle.mpack.gz` | `ModApi/Http/RunBundleMultipartContent.cs:12` | update-doc |
| `docs/mod-features-overview.md:114,117` | 主菜单版本号漏"update available"探测；dock 列表含不存在的"Upgrade Preview"条目、漏 BazaarDB upload | `Game/Lobby/MainMenuVersionLabelFormatter.cs:20-21`；`BppComposition.cs:89-95` | update-doc |
| `docs/mod-features-overview.md:26` | GameInterop 描述漏 `EncounterPortraits/` 与 `GameLanguageProvider.cs` | `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs:1` | update-doc |
| `CLAUDE.md:65` | GameInterop 描述提"hero portrait assets"漏 `EncounterPortraits/` | `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs:1` | update-doc |
| `docs/design/archive/2026-05-22-autobazaar...design.md` §4.1 | 探针路径 `Game/Encounter/`；as-built 在 `GameInterop/Encounter/`（ADR-0002 已正确记） | `GameInterop/Encounter/EncounterStateProbe.cs:1` | keep（ADR 已纠正） |
| `docs/design/archive/2026-05-27-mod-ui-typography...md:276-281` | Spacing/DayBubbleSize/Borders 草图值与 shipped 不符（spec 自标"暂定"） | `Infrastructure/UiTokens/Spacing.cs:11-12`；`Sizes.cs:67`；`Borders.cs:1-10` | keep |

---

## 3. 注释 ↔ 代码不同步（comment-drift，已确认）

| 注释位置(file:line) | 注释陈述 | 纠正事实 | 状态 |
| --- | --- | --- | --- |
| `ModApi/Http/ModApiJsonPost.cs:11` | "used by the upload clients"（复数） | 仅 `BazaarDbSnapshotClient.cs:31` 调用；`RunBundleClient` 走 `RunBundleMultipartContent`，从不用它 | inaccurate（应改单数） |
| `Game/HistoryPanel/HistoryPanel.cs:358` | "fits the 2400x600 native board" | 常量 `NativeBoardWidth = 2600`（`HistoryPanelPreviewTextureGeometry.cs:10`），宽度过期 | outdated（2400→2600） |
| `Game/CombatReplay/Audio/AudioRingBuffer.cs:10,34` | 生产者为"the FMOD mixer thread (DSP read callback)" | FMOD DSP tap 路径已删；该类生产代码零引用，仅单测用；现链路 `WasapiLoopbackCaptureTap.cs:298` 直写 `WavStreamWriter` | outdated（架构已更替） |
| `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs:60`（亦 :68,:82） | "using text fallback" | 消费方 `ApplySourceChipIcon`（`CollectionPanelView.Filters.cs:456-462`）回退为 initials Label，非自由文本 | inaccurate（应作"initials fallback"） |
| `Game/HistoryPanel/HistoryPanelText.cs:794,881` | `PreviewTuneStatus()` / `PreviewTuneHelp()` 描述调参工作流 | 两方法**零调用方**（grep 确认），描述的热键调参功能在代码中不存在 | dead（死代码，无 deprecation 标记） |
| `Game/HistoryPanel/HistoryPanelText.cs:884` | 死字符串 hint："board spacing"/"zoom"/裸"Home/End" | 与 `hotkeys-reference.md` 语义不一致；且该串本身无调用方 | dead + inconsistent |
| `AutoBazaar/Contract/AutoBazaarDecision.cs:109` | `TargetSockets` XML summary 称"informational hint"，引导用 option 版 | 与 `data-structure-catalog.md:289` 列法不一致（catalog 未标 non-authoritative） | low（in-code 已澄清，文档侧待补） |

> 说明：`HeroPortraitSpriteProvider.cs` / `EncounterPortraitSpriteProvider.cs` 缺类级 summary 属既有风格一致，不计为漂移（低置信观察，建议作可选 add-comment）。

---

## 4. 老旧 / 废弃文档识别（obsolete-doc / deprecated-feature-doc）

> **关键区分**：parked（停泊）≠ obsolete（废弃）。已 spent 的 superpowers 计划（工作全落地、清单全勾选为未勾、无剩余项）才是合法删除/归档候选。

| 文档 | 性质 | 已不存在/已落地的事物 | 移除 vs PARKED | 处置 |
| --- | --- | --- | --- | --- |
| `docs/reference/hotkeys-reference.md` "HistoryPanel Preview Tuning" 整段 | **功能从未实现**（非 parked） | Ctrl+方向/Q/E/PageUp… 无任何键盘轮询；`BattleBoardPreview` 仅 `SetPosition/SetClipSize/SetCardScale`；`PreviewTuneHelp/Status` 为死字符串 | **从未交付**（无 disabled mount、无 parked 设计） | delete-doc（删该段，文档其余有效） |
| `docs/superpowers/plans/2026-06-02-autobazaar-module-isolation.md` | spent transient（31 复选框全未勾） | 工作全落地：`AutoBazaar/` 存在、`Game/AutoBazaar` 已删、ProjectReference、`#if BPP_AUTOBAZAAR_HOST`、架构测试 | 已落地（非 parked） | archive-doc |
| `docs/superpowers/plans/2026-06-02-settings-dock-clone-button.md` | spent transient（26 未勾） | `BppSettingsDockPlacement.cs`/`BppNativeSettingsButtonClone.cs` 已落地，死符号已删 | 已落地 | archive-doc |
| `docs/superpowers/plans/2026-06-03-dock-clone-button-sprites-hover.md` | spent transient（26 未勾） | `BppDockButtonIconKind/SpriteProvider/Visuals.cs` 已落地，PNG 已 embed | 已落地 | archive-doc |
| `docs/superpowers/plans/2026-06-03-exclusive-skill-health-sponsor.md` | spent transient（34 未勾） | `CollectionHeroScope`、`ModApiHealthClient`、health probe、`BPPSupporterLinks` 全落地 | 已落地 | archive-doc |
| `docs/superpowers/plans/2026-06-04-upload-dto-performance.md` | spent transient（全未勾） | `RunBundleMultipartContent`、schema=5、3 个上传索引、`BazaarDbSnapshotImagePreparer` 全落地，version 5.0.0 | 已落地 | archive-doc |
| `docs/superpowers/specs/2026-06-02-autobazaar-module-isolation-design.md` | 已落地决策的 spec（Context 误述现状） | `Game/AutoBazaar` 已不存在；迁移完成 | 已落地；**被 ADR-0005:20 引用** | update-doc + archive（**不可删**，先改 banner/Context） |
| `Game/CardSetPreview/GameObjectFactory/PreviewCardLifecyclePolicy.cs:2` | 死代码 + 残留 `BazaarPlusPlus.Game.PreviewSurface` 命名空间 | ADR-0003 称 `Game/PreviewSurface/` 已删（目录确无），但该命名空间在此文件存活；`PreviewCardLifecyclePolicy`/`PreviewCardKind` 零调用方 | 死代码（应删源文件，非文档） | update-code（独立任务） |

**对抗复核降级的归档候选**（保留 archive 处置，但确认严重度低）：
- `docs/design/2026-05-30-combat-replay-audio-and-capture-perf-design.md`（音频侧 SUPERSEDED、视频侧 IMPLEMENTED，缺顶部 banner，仍在顶层）。
- 四个 combat-replay spec（见 §2.1，归档 + 加 banner）。

---

## 5. 文档缺口（doc-gap：已上线代码缺权威文档 / 契约缺失于 reference/）

| 缺口 | 代码证据(file:line) | 影响 | 建议 |
| --- | --- | --- | --- |
| **CollectionPanel 子系统无 `docs/features/` 权威文档**（最大 Game 子系统，49 源文件） | `Game/CollectionPanel/CollectionPanel.cs:1`；注册 `BppComposition.cs:101` | 唯一文档是一串 dated design spec；无 as-shipped 契约（入口/生命周期/过滤维度/source catalog schema/网格常量/缓存层） | create-doc（`docs/features/collection-panel.md`） |
| **CollectionSources 子系统（实际头像实现）无设计文档** | `Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:1`；`CollectionSourceOfferPoolResolver.cs:57-58`（Merchant→Item / Trainer→Skill） | 唯一相邻文档（portrait-plan）描述了**未采用**的方案；offer-rule 映射为 shipped 契约无锚 | create-doc |
| **`collection-sources.json` schema v3 契约无文档** | `Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:15`（`ExpectedSchemaVersion = 3`，不匹配即抛） | 手写嵌入资源，schema 漂移无编译期守卫；v1/v2/v3 差异 + `groups`/`offerRule` 语义无记录 | create-doc |
| **mod 版本检查 HTTP 端点缺于网络清单** | `Game/Lobby/MainMenuVersionCheckController.cs:16`（每次启动 GET `https://bppinstaller.bazaarplusplus.com/latest.json`） | `network-interface-inventory.md` 自称权威网络面，遗漏此出站调用 | update-doc |
| **CollectionPanel 整体缺于 `mod-features-overview.md`** | `BppComposition.cs:101` | feature hub 静默遗漏（与 §2.1 末条合并） | update-doc / create-doc |
| **CollectionPanel dock 按钮缺于 `settings-and-debug-surfaces.md`** | `Patches/Settings/BppSettingsDockPatch.cs:46-52,77-85` | 第二个原生克隆 dock 按钮（AboveSettingButton）无文档 | update-doc |
| **Supporters 模块缺于 `mod-features-overview.md`** | `Game/Supporters/BPPSupporterLinks.cs:8`（被 CardSetPreviewRuntime/CollectionPanel/HistoryPanel 使用） | 语言路由 sponsor URL + attribution row 行为无 overview 条目 | create-doc |
| **`MainMenuVersionCheckController` 的 update-available 行为缺记** | `Game/Lobby/MainMenuVersionLabelFormatter.cs:20` | shipped 用户可见行为（append " | update available"）overview 仅说"展示版本" | update-doc |
| **`BazaarDbSnapshotUploadSettingsDockEntry` 缺于 overview dock 列表** | `BppComposition.cs:89` | 控制截图是否推送服务器的 dock 项漏列 | update-doc |
| **`RunLogEvent.Options/Selected*/RunLogOptionSnapshot` 移除无文档；`EncounterTracking` 重设计亦不存在** | `Storage/RunLog/RunLogEvent.cs:1-55`（无相关字段；repo 全 grep `RunLogOptionSnapshot` 零命中） | 死脚手架已清但去向无记录 | keep（记于 MEMORY，无需新文档） |
| **P3–P5 本地化工作包仅活在 design 文档，无独立追踪** | `Game/HistoryPanel/HistoryPanelText.cs:12`（FontAtlas 缓存键 bug 即 P3 一项） | design 一旦归档，剩余工作有丢失风险 | create-doc / 改 design 状态为分阶段 |

---

## 6. 不一致程度评级

### 6.1 评级标准（rubric）
- **critical** — 误导**集成方/外部**的稳定契约 / wire 协议 / 持久化 schema（如对外 API 字段、SQLite schema、JSONL 协议错误）。
- **high** — 误述**活功能**的可观察行为或签名（用户可见行为、方法名、布局常量、程序集清单）。
- **medium** — 陈旧状态 / 标签 / 默认值 / 缺字段（不致命，但读者会被带偏）。
- **low** — 纯措辞 / 行号 / cosmetic / 文档内部历史整理。

### 6.2 按区域评级

| 审计区域 | 最高级别 | 评级理由 |
| --- | --- | --- |
| CollectionPanel | **high** | 主设计文档状态/§4.1/§16.1 与已删搜索、已发 source catalog 大幅背离；feature 整体缺权威文档 |
| HistoryPanel | **high** | 布局常量 30/28 实为 52/33（用户可见）；2400→2600 注释；preview-tuning 整段为未实现功能 |
| Screenshots + BazaarDB | **high** | 8s vs 10s 在两处文档（用户可见 blocker 窗口）；client-flow 漏 health probe |
| Storage（SQLite schema） | **high** | schema version 摘要写 1 实为 5（自相矛盾，近似 schema 契约） |
| Core + 组合根 | **high** | overview 整体漏 CollectionPanel；mountables 列表过期 |
| CLAUDE.md / CONTEXT.md | **high** | "三程序集/三 csproj"双重过期（影响新人对构建产物的理解） |
| design/README 状态索引 | **high** | 本地化标"未开工"实已落地；4 个 combat-replay spec 错位（导航/真相源混淆） |
| Localization 模块 | **medium** | 状态陈旧 + 契约漏属性 + FontAtlas 缓存键潜伏 bug（有计划） |
| AutoBazaar（parked + host） | **medium** | API 文档 group/dispatch/错误码多处错；但子系统 parked，无活集成方 |
| CombatReplay | **medium** | AudioRingBuffer 注释架构过期；4 spec 待归档 |
| ModApi（cloud client+DTO） | **medium** | ghost combat_kind 兜底无文档；wire 表漏字段 |
| GameInterop 探针+头像 | **medium** | portrait-plan 整体描述未采用架构；CollectionSources 无文档 |
| 逆向工程笔记 | **medium** | BattleProjection wire 字段漏列；版本检查端点缺 |
| Settings dock | **medium** | 定义源归因错（catalog vs registry）；CollectionPanel dock 按钮缺 |
| Lobby + Supporters | **medium** | 版本检查/Supporters 模块缺 overview；plan 命名陈旧 |
| Tooltip/Enchant/CardSet preview | **low** | 多为间接调用/命名空间残留，行为正确 |
| CombatStatusBar | **medium** | HUD frame index 误述；Key Files 漏 4 文件（含主 UI 文件） |
| PvpBattles + ghost flow | **medium** | participant 字段漏列；result 大小写 |
| Patches + Input + hotkeys | **medium** | preview-tuning 死段 + 死方法 |
| Infrastructure（fonts/tokens） | **low** | 多为归档草图"暂定"值；G1 准则未达成属 low |

---

## 7. 清理删除计划

> 处置含义：delete=删整文件 / delete-section=删文档内某段 / archive=移入 archive/ 并加 `Status:` banner / merge=并入活文档 / rewrite=改写状态与 Context / keep=保留（不删）。
> 顺序原则：无入链的 leaf 先处理；被链接文档须**先修链/改 banner** 再动；ADR 与 reference/ 契约无替代不删。

| 文档 | 处置 | 理由 | 风险 | 入链引用 | 顺序 |
| --- | --- | --- | --- | --- | --- |
| `docs/superpowers/plans/2026-06-02-settings-dock-clone-button.md` | archive | 工作全落地，26 复选框全未勾，纯 spent transient | low | none found（全树 grep 无引用） | 1 |
| `docs/superpowers/plans/2026-06-03-dock-clone-button-sprites-hover.md` | archive | 同上，PNG/visuals 全落地 | low | none found | 2 |
| `docs/superpowers/plans/2026-06-04-upload-dto-performance.md` | archive | schema=5/multipart/索引全落地 | low | none found | 3 |
| `docs/superpowers/plans/2026-06-03-exclusive-skill-health-sponsor.md` | archive | health probe/hero scope/supporter links 全落地（含陈旧命名） | low | none found | 4 |
| `docs/superpowers/plans/2026-06-02-autobazaar-module-isolation.md` | archive | 迁移全落地；ADR-0005 仅链 spec 不链此 plan | low | none found（ADR-0005:20 链的是 spec） | 5 |
| `docs/reference/hotkeys-reference.md`（"HistoryPanel Preview Tuning" 段） | delete-section | 该段描述从未实现的功能；文档其余（活热键）有效，**不可删整文件** | medium | `docs/README.md:29`、`docs/features/history-panel.md`、`tooltip-preview.md` 均链此文件（链向整文件，删段不破链） | 6 |
| `docs/design/2026-05-30-combat-replay-audio-and-capture-perf-design.md` | archive（split banner：音频 SUPERSEDED / 视频 IMPLEMENTED） | 已部分被取代、视频侧已落地，缺顶部 banner | medium | 被兄弟 spec `2026-05-30-combat-replay-audio-loopback-capture.md` 反向引用（`n.md`）；**先确认链锚再移动** | 7 |
| `docs/design/2026-05-30-ffmpeg-relocation-to-mod-design.md` | archive + `Status: IMPLEMENTED` | 已合并数周 | medium | `docs/design/README.md:18`、`record-button-design.md:66` 引用——**先重指 README 链** | 8 |
| `docs/design/2026-05-30-combat-replay-record-button-design.md` | archive + banner | 已合并 | medium | `docs/design/README.md:19`、兄弟 spec 互链——先修链 | 9 |
| `docs/design/2026-05-30-combat-replay-audio-loopback-capture.md` | archive + banner | 已实现 | medium | `README.md:20`、`features/combat-replay.md:70`、macos-tap spec 多处互链——先修链 | 10 |
| `docs/design/2026-05-31-combat-replay-audio-macos-process-tap.md` | archive + banner + merge §living-truth 入 `combat-replay.md` | 已实现；§12 自标待归档 | medium | `README.md:21`、`features/combat-replay.md:70`、`native/mac-audio-tap/README.md`——先修链 | 11 |
| `docs/design/2026-06-03-localization-module-extraction-design.md` | rewrite 状态为"P1+P2 done / P0,P3–P5 pending"（**暂不归档**，活计划） | 仍有大量未做工作（`Loc` 目录/字面量迁移/缓存键修复） | medium | `docs/design/README.md:30`、`...refactor-prompt.md:3`——改 README 标签即可 | 12 |
| `docs/design/2026-06-03-localization-module-extraction-refactor-prompt.md` | rewrite（标注已落地 P1/P2，§3 现状清单加"以下为抽取前状态"） | spent executor prompt，§3 现状已不符 | low | `docs/design/README.md`（未列）、被 design 文档:182 引用 | 13 |
| `docs/superpowers/specs/2026-06-02-autobazaar-module-isolation-design.md` | rewrite Context + Status，可后续 archive | Context 误述现状；但记录已落地决策 | **medium（被 ADR 引用）** | `docs/adr/0005-...:20`（"Full design detail"）——**先把 ADR 链重指或保留，再改 banner；绝不删** | 14 |
| `docs/design/2026-06-02-merchant-trainer-portrait-plan.md` | rewrite「实现进展」段（标注实际走 CollectionSources，原 5 文件未建） | 活计划但进展段误述 | low | `docs/design/README.md`（未列，应补列） | 15 |
| `docs/design/2026-06-03-collection-search-removal-sponsor-tweaks-rail-stability.md` | archive + banner 或补列 README | 搜索删除已落地（commit f701dce） | low | `docs/design/README.md`（未列） | 16 |
| 四个 combat-replay spec 在 `docs/design/README.md:18-21` 的条目 | update-doc（移至 Archived 段 + 去"待提交") | 链接整理（配合顺序 7–11） | low | README 自身 | 17（随 7–11） |
| `docs/design/archive/2026-05-23-...sfx-impl.md` §7 | rewrite（删 `ReplayStateAudioDiagnosticPatch` 误称） | 归档 banner 正确，仅 §7 文件清单 overclaim | low | `combat-replay.md:70` 链向整文件 | 18 |
| `docs/design/archive/2026-05-24-pedestal-aware...md` §4.2 | rewrite（标注 `UpgradePreviewModeConfig` 未实现） | 归档 banner 正确，仅 §4.2 overclaim | low | ADR-0004 / tooltip-preview.md | 19 |
| `docs/design/archive/2026-05-27-mod-ui-typography...md` banner | rewrite（fonts 半落地：CombatStatusBar 仍用 LegacyRuntime.ttf） | banner 称全落地，G1 准则1/4 未达成 | low | `Infrastructure/Fonts/`、`UiTokens/` | 20 |
| `docs/design/archive/2026-05-29-historypanel-fullscreen...md:7` | rewrite（修死链 `history-panel-known-issues.md §1`） | 死链 | low | 无外链 | 21 |
| `docs/adr/0003-history-panel-preview-overlay.md` | **keep** | 目录删除声明属实；命名空间残留是源码问题（顺序 22） | — | 多文档引用 ADR | — |
| `Game/CardSetPreview/GameObjectFactory/PreviewCardLifecyclePolicy.cs` | **delete-code（独立任务）** | 死代码 + 残留 `Game.PreviewSurface` 命名空间，零调用方 | low | 仅 csproj 通配 include；无 .cs 引用 | 22（非文档任务） |

---

## 8. 清理优先级

### P0 — 误导稳定契约 / 集成方（先修）
- `docs/reference/sqlite-schema-reference.md:30` schema version 1→5（近似 schema 契约，自相矛盾）。
- `docs/reference/auto-bazaar-http-api-v1.md` §6 `SelectEncounter` group（Route→Offer）、Dispatches 列（Cmd→AppState 反射）、500/`internal` 缺失、"12 kinds"→11（wire/HTTP 契约面，即便 parked 也是对外协议真相源）。
- `docs/reverse-engineering/data-structure-catalog.md` BattleProjection / BattleParticipantsArtifact 漏 wire 字段（WinnerCombatantId/LoserCombatantId + prestige/victories）。
- `docs/reverse-engineering/network-interface-inventory.md` 补 `bppinstaller.bazaarplusplus.com/latest.json`（权威网络面）。
- `collection-sources.json` schema v3 契约文档（手写资源、无编译守卫，schema 漂移高危）。

理由：这些是**对外/集成层稳定契约**，错误会直接误导集成方或运维诊断。

### P1 — 活功能漂移 + 可安全清除的 spent transient
- **活功能行为错**：`history-panel.md:12`（52/33）、`screenshots.md:13` + `mod-features-overview.md:95`（8s）、CollectionPanel 主设计文档状态/§4.1/§16.1、`combat-status-bar.md` HUD frame index、`CLAUDE.md` 三程序集/三 csproj、overview/ADR-0002 mountables 与 CollectionPanel 缺失。
- **新建权威文档**：`docs/features/collection-panel.md`（含 CollectionSources + source-catalog schema）。
- **归档 spent transient（顺序 1–5）**：5 个 superpowers plan（工作全落地、无剩余清单项、无入链）。
- **删未实现功能段**：`hotkeys-reference.md` Preview Tuning 段。
- **修 design/README 状态索引**：本地化"未开工"标签、4 个 combat-replay spec 错位。

理由：活功能漂移会让开发者按错误行为/签名编码；spent transient 留着会让未来 agent 误以为有未完成工作而重做。

### P2 — Cosmetic + 历史整理
- 全部 low 级行号漂移（portrait-plan、refactor-prompt、Sizes.cs 锚点等）。
- 归档 spec 的 banner/overclaim 修正（sfx-impl §7、pedestal §4.2、typography banner、fullscreen 死链）。
- 注释纠正（`ModApiJsonPost` 单复数、`HistoryPanel.cs:358` 2400→2600、`AudioRingBuffer` FMOD 描述、`EncounterPortraitSpriteProvider` text→initials）。
- README/MEMORY/spec/plan 中"注释掉"→"编译开关"统一表述。

理由：不影响行为与契约理解，可批量收尾。

---

## 9. 执行步骤（有序清单）

> 标注 ⚠️ 的步骤需**人工决策**；标注 🔧 的为 **update-code（代码 bug 修复）属本次文档清理范围之外，应另立任务**。

**阶段 A：P0 契约修正（不动文件结构，仅改内容）**
1. 改 `docs/reference/sqlite-schema-reference.md:30` → schema version 5；同步修 `runs` 写路径类名 `RunLogStore`、补 metadata `artifact_codec` 行。
2. 改 `docs/reference/auto-bazaar-http-api-v1.md`：SelectEncounter group=Offer、Dispatches 列改 AppState 反射、补 500/`internal` 错误行、"12"→"11"。
3. 改 `docs/reverse-engineering/data-structure-catalog.md`：BattleProjection/BattleParticipantsArtifact 补 4+2 wire 字段。
4. 改 `docs/reverse-engineering/network-interface-inventory.md`：新增 version-check 端点行。
5. 新建 `collection-sources.json` schema v3 契约文档（可并入 step 12 的 collection-panel 文档或独立）。

**阶段 B：P1 活功能 + 新文档**
6. 修 `docs/features/history-panel.md:12`（52/33）。
7. 修两处 8s：`docs/features/screenshots.md:13` + `docs/mod-features-overview.md:95`；并补 screenshots client-flow 的 health probe + account_id 时序、UI suppression 补 CollectionPanel dock。
8. 修 `docs/design/2026-05-31-collection-panel-design.md`：状态 banner / §4.1（删 search）/ §10.x/§13/§16.1 文件清单 / §2.3 方法名。
9. 修 `docs/mod-features-overview.md`（mountables 补 2 项、Supporters/版本检查/BazaarDB dock/EncounterPortraits）+ `docs/adr/0002` Consequences（补 CollectionPanelMount）。
10. 修 `CLAUDE.md`（程序集 4+1、5 csproj/4 源码树、Layer 补 Localization、GameInterop 补 EncounterPortraits）。
11. 修 `docs/features/combat-status-bar.md`（删 frame index 描述、Key Files 补 4 文件）。
12. **新建** `docs/features/collection-panel.md`（as-shipped 契约：入口/生命周期/过滤/source catalog/网格/缓存）；并在 overview 增 CollectionPanel + Supporters 章节。

**阶段 C：spent transient 归档（先无入链，再有入链）**
13. 归档顺序 1–5 的 5 个 superpowers plan（移入 `docs/superpowers/plans/archive/` 或加 `Status:` banner；均无入链，低风险）。
14. ⚠️ **人工决策：delete vs archive** — `hotkeys-reference.md` Preview Tuning 段。建议 **delete-section**（功能从未实现），但因 README/history-panel/tooltip-preview 链向整文件，确认删段不破链后执行。

**阶段 D：design/README 状态索引 + combat-replay spec 归档（先修链后移动）**
15. 先在 `docs/design/README.md` 把 4 个 combat-replay spec 从 Active 移到 Archived 段并去"待提交"，把本地化标签从"未开工"改为"P1+P2 done / P3–P5 pending"，补列 merchant-trainer-portrait-plan / collection-search-removal / localization-refactor-prompt。
16. ⚠️ 逐个把 4 个 combat-replay spec + `2026-05-30-combat-replay-audio-and-capture-perf-design.md` 移入 `archive/` 并加 `Status:` banner（音频 SUPERSEDED / 视频 IMPLEMENTED 用 split banner）；**移动前确认兄弟 spec 与 `combat-replay.md:70`、`native/mac-audio-tap/README.md` 的相对链锚仍有效**。

**阶段 E：rewrite 不删（被引用 / 仍是活计划）**
17. rewrite `docs/superpowers/specs/2026-06-02-autobazaar-module-isolation-design.md` 的 Context + Status（被 ADR-0005 引用，**绝不删**）。
18. rewrite 本地化 design + refactor-prompt 的状态/现状段（活计划）。
19. rewrite `2026-06-02-merchant-trainer-portrait-plan.md`「实现进展」段；归档或补列 `collection-search-removal` spec。

**阶段 F：P2 cosmetic 收尾**
20. 批量修 low 级行号漂移（portrait-plan、refactor-prompt 等）。
21. 修归档 spec 的 banner/overclaim：sfx-impl §7、pedestal §4.2、typography banner、fullscreen 死链。
22. 修注释（claim-focused）：`ModApiJsonPost.cs:11`、`HistoryPanel.cs:358`、`AudioRingBuffer.cs:10/34`、`EncounterPortraitSpriteProvider.cs:60/68/82`；README/MEMORY/plan/spec 的"注释掉→编译开关"统一。

**阶段 G：独立代码任务（🔧 非文档清理范围，应另立 task）**
23. 🔧 删死代码 `Game/CardSetPreview/GameObjectFactory/PreviewCardLifecyclePolicy.cs`（+ 残留 `Game.PreviewSurface` 命名空间）。
24. 🔧 删死方法 `HistoryPanelText.PreviewTuneHelp()` / `PreviewTuneStatus()`（:794/:881）。
25. 🔧 修 `Game/Settings/BppSettingsDockController.Presentation.cs:15-16` anchor Y（计划要求 0f）— 需确认是否仍要该 playtest 微调。
26. 🔧 修本地化 `FontAtlasSampleCache` 单键→(languageCode, mode) 元组键（`HistoryPanelText.cs:12`）— CN→TW 模式切换返回陈旧样本的潜伏 bug；属 P3 工作包。
27. 🔧 修 CombatStatusBar 字体路径 `CombatStatusBar.Canvas.cs:707`（LegacyRuntime.ttf → BppUiFont.Default）以满足 typography G1。

> **范围声明**：步骤 23–27 是代码缺陷/死代码修复，**不在本次"文档清理"范围**，列出仅供追踪，应作为独立任务执行（避免文档 PR 夹带代码改动）。

