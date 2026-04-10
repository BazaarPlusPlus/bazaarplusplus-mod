# BazaarPlusPlus 功能总览

本文根据当前代码整理模组**已实现**的能力；若与实现不一致，以 `Plugin.cs`、`Core/`、`Game/`、`Patches/` 为准。

## 概述

BazaarPlusPlus 是面向《The Bazaar》的 **BepInEx** 插件，在游戏中提供：

- 战斗与 UI 增强（状态条、怪物预览、附魔/升级 tooltip、展示柜 tooltip 互通等）
- **Run logging**：活跃对局写入本地 SQLite，供历史面板与离线脚本使用
- **PVP 战斗回放**：本地录制与在 HistoryPanel / Debug 下条件回放
- **云同步**：在**非 live run** 时后台上传 V3 `run-bundle` 与 ghost/replay 数据，对接 **ModCFServerV3**（Cloudflare Worker）
- 大厅与展示类小功能（随机英雄池面板、主菜单版本号、Legendary 段位展示文案等）
- **Anonymous Mode**：可选将显示名改为 `Anonymous`

## 运行时骨架

| 层次 | 作用 |
| --- | --- |
| `Plugin.cs` | BepInEx 入口：`Harmony.PatchAll()`，创建 `BppRuntimeHost`，挂载各类 `MonoBehaviour` 控制器 |
| `Core/Runtime/BppRuntimeHost.cs` | 统一初始化配置、事件总线、路径、`MonsterDatabase`、Run 上下文探测；注册 **RunLifecycle**、**CombatReplay**、**CombatStatusBar**、**EncounterTracking** 等模块 |
| `Patches/` | Harmony 补丁：战斗模拟、回放采集、设置坞、大厅、tooltip、名称覆盖等 |

主要挂在游戏对象上的组件见 `Plugin.AttachRuntimeComponents()`：`RunStateSyncController`、`RunLoggingController`、`RunUploadController`、`HistoryPanel`、`CombatStatusBar`、怪物预览相关、`TooltipModifierRefreshController` 等。

## 游戏内功能模块

### 战斗状态条（Combat Status Bar）

- 战斗期间底部 HUD：逻辑战斗时间、已处理帧数与 **暂停**
- 默认关闭，可在 **Bazaar++ 设置坞** 中开启；开关会写入配置
- 逻辑时间基于已处理战斗帧 × 50ms，与墙钟解耦

详见 `docs/combat-status-bar.md`。

### 怪物预览（Monster Preview）

- 右键锁定怪物或遭遇牌时，可显示 Bazaar++ 自定义预览（物品与技能面板）
- 优先 `MonsterDatabase` 静态数据，缺失时回退 **EncounterTracker** 运行时缓存
- 配置项 **Use native preview** 为 true 时走游戏原生预览（历史面板内战斗预览不受此项单独约束）
- 与 **Showcase** 卡片 tooltip 锁、预览板表面等补丁协同（`Patches/Showcase/`）

详见 `docs/monster-preview-design.md`。

### 遭遇与选项追踪（Encounter Tracking）

- 在地图遭遇、Choice、Loot、Pedestal 等状态下，根据发牌与选项集更新「当前可选卡/怪」快照
- 向 run logging / 怪物预览等特性提供 **SelectionObserved** 等事件数据源

### 附魔预览与升级预览（Tooltips）

- **附魔**：在物品 tooltip 上追加附魔说明；可配置「始终显示」或按住 **EnchantPreview** 热键时显示（默认 Ctrl）
- **升级预览**：按住 **UpgradePreview** 热键（默认 Shift）进入原生 upgrade preview 路径
- `TooltipModifierRefreshController` 在配置变化时刷新 tooltip 展示

### Streamer / Anonymous 名称（Name Override）

- 可选将游戏内显示名替换为 `Anonymous`（配置 + 设置坞）
- 通过 `Patches/NameOverride/` 与相关 UI 刷新衔接

### 历史面板（History Panel）

- 浏览本地 `runs`、关联 **PVP battles**、**ghost battles**（自服务端同步的摘要）
- 预览保存的棋盘快照；条件满足时可启动**本地 replay**
- 支持删除 run 及关联 battle 记录
- 大厅通过设置坞 **Game History** 打开；另有面板内 preview 调试热键（见 `docs/reference/hotkeys-reference.md`）

详见 `docs/run-logging.md`。

### Run Logging

- Live run 期间持续采集并写入 **SQLite**（runs、事件流、checkpoint、终态、PVP battle 清单、ghost 摘要、上传同步状态等）
- `RunStateSyncController` 周期性触发同步请求；`RunInitializedPatch` 结合服务端 `run_id`
- Run 结束时写入 completion；若 replay 仍在异步落盘会短暂延迟关闭 session
- 提供 `scripts/export_run_log.py` 离线导出

### 战斗回放（Combat Replay）

- 本地录制 PVP **replay payload**，与 run / battle 元数据一并入库
- `CombatReplayRuntime` + `CombatReplayCapturePatch` 等负责采集；HistoryPanel / Debug 在条件满足时回放
- 更深细节见 `docs/reference/combat-replay-recording.md`

### 大厅与 UI 附加行为

- **随机英雄池**：在英雄选择界面附加面板逻辑（`Patches/Lobby/RandomHeroPoolPatches.cs`、`Game/Lobby/RandomHeroPool/`）
- **主菜单版本号**：在游戏版本字符串旁展示模组版本（`MainMenuVersionLabelPatches`）
- **Legendary 大厅展示**：在资料加载/段位更新时刷新排行榜位置与 rating 文案（`LegendaryLobbyRatingPatches`、`LegendaryLobbyRatingFormatter`）

### 设置与键位

- **Bazaar++ 设置坞**注入游戏选项菜单（`BppSettingsDockPatch`、`BppSettingsDockCatalog`）：游戏历史、Anonymous、附魔始终显示、战斗状态条、原生怪物预览等
- 原生键位文案补丁、`BppKeybindSettingsPatch` 等与 **Hotkeys** 配置条目配合
- 选项语言刷新补丁（`OptionsDialogLanguageRefreshPatch`）

### Debug 构建

- `BppBuild.IsDebug` 时额外挂载 **DebugPanel**，用于开发调试（非 Release 行为）

## 云同步与 ModCFServerV3

面向 **ModCFServerV3**（`ModCFServerV3/`，Cloudflare Workers + D1 + R2）：

- **Installation 身份**：installer 写入 `installation.bpp` / `installation.key`，mod 直接读取并签名请求
- **Run Bundle 上传**：已完成 run 与关联 replay artifact 合并上传到 `POST /run-bundles`
- **Observation 上传**：installer / mod 可写 `POST /installations/observations`
- **Ghost 战斗**：`GET /ghost-battles` 查询 against-me 列表；按需签发 `POST /ghost-battles/:battleId/replay-link`

模组侧仅在**非 live run** 时执行上传扫描；`RunUploadController` 统一调度 run-bundle 上传。信任模型与安全限制见 `docs/run-upload.md`。

## 配置摘要（BazaarPlusPlus.cfg）

与功能直接相关的条目包括（完整见 `Core/Config/BppConfig.cs`）：

| Section | 含义 |
| --- | --- |
| MonsterPreview / UseNativePreview | 是否使用原生怪物预览 |
| StreamerMode / EnableNameOverride | Anonymous 显示名 |
| EnchantPreview / AlwaysShow | 附魔 tooltip 是否始终显示 |
| Hotkeys / EnchantPreview, UpgradePreview | 附魔/升级预览按键路径 |
| CombatStatusBar / Enabled | 战斗状态条开关 |

HistoryPanel 的预览相关另有独立配置段（`HistoryPanelPreviewSettings`）。

## 测试与脚本

- `tests/` 下按特性拆分测试项目（run logging、upload、combat replay、UI 格式化等）
- `scripts/` 含 run log 导出等辅助脚本

## 进一步阅读

- 仓库总览：`README.md`
- 文档索引：`docs/README.md`
- Run / History / SQLite：`docs/run-logging.md`
- 上传与风险说明：`docs/run-upload.md`
- 战斗状态条 / 怪物预览 / CF 部署：`docs/combat-status-bar.md`、`docs/monster-preview-design.md`、`docs/mod-cf-server-deploy.md`
- 热键、设置表面、SQLite schema、tooltip 实现细节：`docs/reference/`
