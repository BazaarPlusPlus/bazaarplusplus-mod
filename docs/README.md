# BazaarPlusPlus 文档索引

所有文档按**受众**与**生命周期**组织。这里是入口；每个文档至少能从本页或 [mod-features-overview.md](mod-features-overview.md) 经一个具名链接到达。

## 终端用户 / 贡献者

- [../README.md](../README.md)（中文，仓库布局权威）、[../README_en.md](../README_en.md)：安装、配置、从源码构建、数据与网络行为。

## 在用功能（`features/`）

每个功能在此只有一篇权威文档；共享事实（SQLite 列、热键默认值）只在 `reference/` 写一次并被链接。

- [mod-features-overview.md](mod-features-overview.md) — **功能枢纽**：代码派生的功能总览、运行时骨架、配置摘要，链接到下面每篇深入文档。
- [features/run-logging-and-upload.md](features/run-logging-and-upload.md) — 活跃 run 采集、HistoryPanel 数据、后台上传与信任模型。
- [features/history-panel.md](features/history-panel.md) — HistoryPanel 布局 / 预览渲染 / ghost 出局行级指示。
- [features/collection-panel.md](features/collection-panel.md) — 全屏卡牌图鉴（Item+Skill）：入口/生命周期、过滤维度、CollectionSources 来源 catalog（含 v3 schema）、虚拟化网格与首屏性能。
- [features/combat-replay.md](features/combat-replay.md) — PVP 录制 / 回放 + 可选 MP4 视频录制。
- [features/screenshots.md](features/screenshots.md) — 终局自动截图 + 可选 BazaarDB 上传。
- [features/tooltip-preview.md](features/tooltip-preview.md) — 附魔 / 升级预览的 3 态可视性与 pedestal 自动触发。
- [features/combat-status-bar.md](features/combat-status-bar.md) — 战斗底部 HUD。
- [features/monster-preview.md](features/monster-preview.md) — 原生怪物预览 + CardSet overlay。
- [features/ghost-battle-data-flow.md](features/ghost-battle-data-flow.md) — ghost battle 录制→上传→同步→渲染的视角翻转数据流。
- [features/autobazaar.md](features/autobazaar.md) — AutoBazaar 概览（**当前 parked**）。

## 稳定契约 / 清单（`reference/`）

只放低漂移的契约与清单，被功能文档链接、不被复述。

- [reference/sqlite-schema-reference.md](reference/sqlite-schema-reference.md) — 本地表 / 列 / 版本的唯一真相源。
- [reference/hotkeys-reference.md](reference/hotkeys-reference.md) — 快捷键默认值与可绑按键的唯一真相源。
- [reference/settings-and-debug-surfaces.md](reference/settings-and-debug-surfaces.md) — 设置坞条目清单。
- [reference/auto-bazaar-http-api-v1.md](reference/auto-bazaar-http-api-v1.md) — AutoBazaar wire 契约（parked）。
- [reference/auto-bazaar-decision-surface.md](reference/auto-bazaar-decision-surface.md) — AutoBazaar 字段推导（parked）。

## 领域决策与术语

- [../CONTEXT.md](../CONTEXT.md) — 领域术语表（Encounter / Pedestal / Encounter status probe …）。
- [adr/](adr/) — 架构决策记录：[0001](adr/0001-encounter-status-probe-not-timeline-tracker.md) 状态探针、[0002](adr/0002-mountable-feature-registry.md) mountable 注册表、[0003](adr/0003-history-panel-preview-overlay.md) 预览 overlay、[0004](adr/0004-preview-visibility-three-state-mode.md) 3 态预览模式、[0005](adr/0005-autobazaar-isolated-transport-core.md) AutoBazaar 隔离核心。

## 设计历史（`design/`）

- [design/README.md](design/README.md) — spec 生命周期约定 + 按状态索引的 [design/archive/](design/archive/)（已实现 / 已废弃 / 未实现的 dated 设计 spec，每篇带状态横幅）。

## 排障记录（`debugging/`）

- [debugging/README.md](debugging/README.md) — 已修复 / 已验证问题的排障记录：现象、日志与代码证据、根因、修复和验证。

## 游戏逆向工程（`reverse-engineering/`）

- [reverse-engineering/README.md](reverse-engineering/README.md) — 事实型 RE 参考（网络面、DTO、session 协议、反编译记录；2026-05-21 快照口径）；[reverse-engineering/proposals/](reverse-engineering/proposals/) 下是**尚未实现**的离线模式设计提案。

## 新文档去哪（约定）

- 已上线功能 → `features/<feature>.md`（先扩展已有文档，再考虑新建）
- 稳定契约 / schema / 清单 → `reference/`
- 开工前 / 进行中的提案 → `design/<date>-<slug>.md`（落地或废弃后移入 `design/archive/` 并加状态横幅）
- 已修复 bug 的排障复盘 / debug session → `debugging/<date>-<slug>.md`
- 值得记住的设计决策 → `adr/`（在归档对应 spec 前先提升）
- 新领域术语 → `../CONTEXT.md`
- 游戏行为逆向 → `reverse-engineering/`（未实现提案放 `proposals/`）

> agent 规则与 PR 规范在仓库根的 [../CLAUDE.md](../CLAUDE.md)（`AGENTS.md` 是它的 symlink）。
