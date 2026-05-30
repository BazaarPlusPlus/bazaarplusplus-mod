# BazaarPlusPlus 功能总览

本文根据当前代码整理模组**已实现**的能力；若与实现不一致，以 `Plugin.cs`、`BppComposition.cs`、`Core/`、`Game/`、`Patches/` 为准。

## 概述

BazaarPlusPlus 是面向《The Bazaar》的 **BepInEx** 插件，在游戏中提供：

- 战斗与 UI 增强：状态条、附魔/升级 tooltip、CardSet preview overlay
- **Run logging**：活跃对局写入本地 SQLite，供 HistoryPanel 和上传队列使用
- **PVP 战斗回放**：本地录制 replay payload，并在 HistoryPanel / ghost replay 路径下条件回放
- **云同步**：在**非 live run** 时后台上传 run-bundle，并从 **`bazaarplusplus-server`**（部署在 `mod-api-v4.bazaarplusplus.com`）同步 ghost battles / replay 下载链接
- **BazaarDB 截图上传**（默认关闭）：开启后将历史与新增的终局截图与摘要 JSON 推到 `bazaarplusplus-server`，BazaarDB 再从服务端拉取
- 大厅与展示类小功能：随机英雄池面板、主菜单版本号、Legendary 段位展示文案、中文术语切换
- **Anonymous Mode**：可选将显示名改为 `Anonymous`
- 终局自动截图：终局 `Continue` 前自动保存主截图和元数据
- **AutoBazaar HTTP 接口**（**当前 parked**）：本地回环 HTTP 服务（默认端口 47900），对外暴露决策上下文（`GET /v1/context`）并接受外部动作（`POST /v1/actions`）；纯传输与校验层，Mod 本身不做策略决策。**注意：该 mount 在 `BppComposition.cs:120` 被注释掉，HTTP 服务当前不启动；wire 契约与源码保留，取消注释即重新启用。** 详见 [auto-bazaar-http-api-v1.md](reference/auto-bazaar-http-api-v1.md)。

## 运行时骨架

| 层次 | 作用 |
| --- | --- |
| `Plugin.cs` | BepInEx 入口：初始化配置、composition、Harmony patches、`CombatReplayRuntime`（bootstrap-special，需在 `composition.Start()` 前构造），随后 `Mountables.MountAll(...)` 一行装好 9 个 `IBppMountable`（第 10 个 `AutoBazaarMount` 在 `BppComposition.cs:120` 注释掉，parked） |
| `BppComposition.cs` | 创建 `IBppServices`，注册 `RunLifecycleModule`、`CombatReplayModule`、`CombatStatusBarModule`，并把所有 `IBppMountable`（feature runtime）和 `ISettingsDockEntry`（设置坞入口）汇总到两个 registry |
| `Core/` | 纯抽象：配置、事件总线、路径、run context、运行时服务接口 |
| `GameInterop/` | 游戏 DLL 耦合层：`GameStateProbe`、`RunContextStore`、`BppClientCacheBridge`、`BppStaticDataAccess`，以及带 game type 的事件 + `IRunContext` 接口 |
| `Patches/` | Harmony 补丁：战斗模拟、回放采集、设置坞、大厅、tooltip、名称覆盖等 |

挂载的 9 个 `IBppMountable`（实际注册见 `BppComposition.cs:95-118`；多数是泛型 `ComponentMount<T>`，仅 `HistoryPanelMount` 为定制类）：`ComponentMount<RunLoggingController>`、`ComponentMount<RunUploadController>`、`ComponentMount<CombatStatusBar>`、`ComponentMount<CardSetPreviewRuntime>`、`ComponentMount<EndOfRunScreenshotController>`、`ComponentMount<BazaarDbScreenshotUploadController>`、`ComponentMount<CombatReplayVideoRecorder>`、`HistoryPanelMount`（用 `Func<>` 延迟解析 online client + combat replay runtime）、`ComponentMount<TooltipModifierRefreshController>`。第 10 个 `AutoBazaarMount` 在 `BppComposition.cs:120` 被注释（parked）。新增 feature 只需在 `BppComposition` 加一行 `_mountables.Register(...)`。

## 游戏内功能模块

### 战斗状态条（Combat Status Bar）

- 战斗期间底部 HUD：逻辑战斗时间、已处理帧数、暂停状态和 0.50x / 0.67x / 1.00x 速度档位
- 默认关闭，可在 **Bazaar++ 设置坞** 中开启；开关和速度档位会写入配置
- 逻辑时间基于已处理战斗帧 × 50ms，与墙钟解耦

详见 [features/combat-status-bar.md](features/combat-status-bar.md)。

### 怪物预览（Monster Preview）

- 完全走游戏原生怪物预览，Bazaar++ 不再 patch 或 augment 原生 tooltip
- `CardSetPreviewRuntime` 会复用原生 `MonsterBoardTooltip` 作为宿主展示 Bazaar++ 组织的 CardSet board 内容
- HistoryPanel 预览是独立栈（`Game/HistoryPanel/Preview/`），与怪物预览解耦
- 附魔/升级预览注入由独立的 patch 提供（见后续小节 / `Patches/Tooltips/`），不属于 monster preview 路径

详见 [features/monster-preview.md](features/monster-preview.md)。

### 附魔预览与升级预览（Tooltips）

- **可视性模式**：附魔与升级各有独立的 3 态配置（`Off` / `AutoOnPedestalChoice` / `Always`），默认 `AutoOnPedestalChoice`
- **自动触发**：在 `ChoiceState` 选择屏遇到对应种类 pedestal 时，hover 物品自动展示匹配的预览；非 pedestal 选项或非 ChoiceState 不会自动触发
- **手动覆盖**：按住 `HoldEnchantPreview`（默认 Ctrl）/ `HoldUpgradePreview`（默认 Shift）总是显示对应预览，等级最高，覆盖所有模式
- **共享决策**：`TooltipModifierRefreshController`、`ItemEnchantPreviewPatch`、`UpgradePreviewTooltipPatch` 共同调用 `Game/Tooltips/TooltipPreviewModePolicy.Resolve`，保证三处行为一致；模式由 `Game/Encounter/ChoiceScreenPedestalResolver` 从 `RunState.SelectionSet` 推导
- **迁移**：首次启动会把旧的 `[EnchantPreview] AlwaysShow = true/false` 自动迁移到 `[EnchantPreview] Mode = Always / AutoOnPedestalChoice`，并从配置文件移除旧键

详见 [features/tooltip-preview.md](features/tooltip-preview.md) 与 [ADR-0004](adr/0004-preview-visibility-three-state-mode.md)。

### Streamer / Anonymous 名称（Name Override）

- 可选将游戏内显示名替换为 `Anonymous`（配置 + 设置坞）
- 通过 `Patches/NameOverride/` 与相关 UI 刷新衔接

### Run Logging 与 HistoryPanel

- Live run 期间写入 `bazaarplusplus.db`：`runs`、`run_events`、`battles`、`battle_snapshots`、`run_sync_state` 等
- `RunLifecycleModule` 维护 `IsInGameRun` 和当前服务端 `run_id`；`RunInitializedPatch` 捕获 `NetMessageRunInitialized`
- HistoryPanel 浏览本地 runs、本地 PVP battles、ghost battles 和保存的棋盘快照
- 条件满足时可从 HistoryPanel 启动本地 replay 或下载并回放 ghost replay
- 支持删除 run 及关联 battle 记录

详见 [features/run-logging-and-upload.md](features/run-logging-and-upload.md)。

### 后台上传与 Ghost Battles

- `RunUploadController` 仅在非 live run 时扫描待上传 completed runs
- `RunBundleUploadStore` 组装 run projection、battle projections 和 gzip MessagePack artifact
- `RunBundleUploadService` 上传到 `POST /run-bundles`
- HistoryPanel 的 ghost sync 用 `?player_account_id=` 调用 `GET /ghost-battles`（不鉴权），按需申请 `POST /ghost-battles/:battleId/replay-link`

详见 [features/run-logging-and-upload.md](features/run-logging-and-upload.md)。

### 战斗回放（Combat Replay）

- 本地录制 PVP replay payload，保存到 `<GameRoot>/BazaarPlusPlusV4/CombatReplays`
- battle metadata 和 board snapshot 写入 SQLite `battles` / `battle_snapshots`
- `CombatReplayRuntime` + `CombatReplayCapturePatch` 负责采集；HistoryPanel 在条件满足时回放
- **可选 MP4 录制**：由 HistoryPanel「录制并回放」按钮对选中对局单次触发（无全局开关）。回放期间把 Game View 抓帧、调用外部 FFmpeg 写到 `<GameRoot>/BazaarPlusPlusV4/CombatReplayVideos/`，元数据进 SQLite `combat_replay_videos`。可录条件 = 检测到 FFmpeg + 支持 `AsyncGPUReadback`；不满足时按钮置灰，不影响普通 replay

详见 [features/combat-replay.md](features/combat-replay.md)。

### 终局自动截图（End-of-run Screenshot）

- 终局界面出现后先等待 10 秒，等待窗口内吞掉鼠标点击
- 第一次合法 `Continue` 会先保存主截图，再放行原始按钮动作
- PNG 保存到 `<GameRoot>/BazaarPlusPlusV4/Screenshots`，元数据写入 SQLite `run_screenshots`

详见 [features/screenshots.md](features/screenshots.md)。

### BazaarDB 截图上传

- `BazaarDB / UploadScreenshots` 默认关闭；开启后 `BazaarDbScreenshotUploadController` 在非 live run 时按 180s 间隔扫描待上传截图
- Sidecar 表 `bazaardb_screenshot_uploads` 用 `INSERT OR IGNORE` 回填 `run_screenshots` 中所有 `capture_source = 'end_of_run_auto'` 的行——开关从关切到开就能把历史截图一并补传
- 数据流：模组 `POST /bazaardb-screenshots`（无鉴权）→ Worker 写 R2 + D1 → BazaarDB 用 Bearer token 调 `GET /bazaardb/manifest` 拿到 row 列表（行内带公开 `image_url`，指向 `bazaardb-assets-v4.bazaarplusplus.com`）直接走公开桶下载
- 4xx（除 408 / 429）落 `permanent_failure`，不再重试；5xx / 网络错误保留 `pending` 自动重试；翻开开关 / run 退出时立刻触发一次

详见 [features/screenshots.md](features/screenshots.md)。

### 大厅、设置与本地化

- **随机英雄池 / 皮肤池**：英雄选择界面附加面板逻辑
- **主菜单版本号**：在游戏版本字符串旁展示模组版本
- **Legendary 位置展示**：可按配置保留原值、隐藏、固定 `999999` 或显示 `#position | rating`
- **中文术语模式**：可在 Mainland / Taiwan / HongKong 术语间切换
- **Bazaar++ 设置坞**：注入 Game History、Anonymous、Legendary Position、Enchant Preview、Upgrade Preview、Combat Status Bar、Chinese Locale 等入口

## 云同步与 bazaarplusplus-server

面向 **`bazaarplusplus-server`**（独立仓库，Cloudflare Workers + D1 + R2，部署 `mod-api-v4.bazaarplusplus.com`）：

- **玩家身份**：上传所需的 `player_account_id` 在上传时从客户端 profile 解析（`BppClientCacheBridge.TryGetProfileAccountId`），解析不到则跳过上传；当前实现没有持久化的身份文件
- **Run Bundle 上传**：已完成 run 与关联 replay artifact 合并上传到 `POST /run-bundles`（不鉴权；服务端只把 `seen_player_accounts` 注册过的玩家或上传者本人作为可投影 opponent）。`player_account_id` 必填，缺失则 400 —— V3 时代的 `"anonymous-player"` sentinel 已删除,mod 在没拿到本机 account id 时直接跳过上传
- **Ghost 战斗**：`GET /ghost-battles?player_account_id=…` 查询 against-me 列表（不鉴权）；按需签发 `POST /ghost-battles/:battleId/replay-link`（返回 5 分钟有效的 R2 预签 URL）
- **Final-battle 标记**：服务端把上传 bundle 中最后一场 battle 在投影时标记 `is_final_battle`（V3 叫 `is_bundle_final_battle`,V4 删掉冗余前缀,sticky 语义:一旦 1 永远 1）；HistoryPanel 在 ghost 视角下用它提示"这场后对手出局"
- **BazaarDB 截图上传**：`POST /bazaardb-screenshots`（不鉴权）写 R2 + D1，BazaarDB 用 `BAZAARDB_PULL_TOKEN` 拉 `GET /bazaardb/manifest`(行内带公开 `image_url`); image 文件本身走公开桶 `bazaardb-assets-v4.bazaarplusplus.com`,不再经过 Worker proxy

模组侧仅在**非 live run** 时执行上传扫描；`RunUploadController` 统一调度 run-bundle 上传。信任模型与安全限制见 [features/run-logging-and-upload.md](features/run-logging-and-upload.md)。

## 配置摘要（BazaarPlusPlus.cfg）

与功能直接相关的条目包括（完整见 `Core/Config/BppConfig.cs`）：

| Section / Key | 含义 |
| --- | --- |
| `StreamerMode / EnableNameOverride` | Anonymous 显示名 |
| `EnchantPreview / Mode` | 附魔预览可视性模式：`Off` / `AutoOnPedestalChoice` / `Always`（默认 Auto） |
| `UpgradePreview / Mode` | 升级预览可视性模式：同上（默认 Auto） |
| `CombatStatusBar / Enabled` | 战斗状态条开关 |
| `CombatStatusBar / SpeedMultiplier` | 默认战斗速度档位 |
| `Hotkeys / EnchantPreview`, `Hotkeys / UpgradePreview` | 附魔/升级预览按键路径 |
| `Localization / ChineseLocaleMode` | 中文术语模式 |
| `LegendaryPositionDisplay / Mode` | Legendary 位置展示模式 |
| `BazaarDB / UploadScreenshots` | 是否将终局截图上传到 `bazaarplusplus-server` 供 BazaarDB 拉取，默认 `false` |

Combat Replay 录像的编码参数（帧率 / 分辨率 / CRF / preset / 队列上限）为固定默认值，不再暴露为 cfg。

## 测试与脚本

- `tests/` 下按特性拆分测试项目（run logging、upload、combat replay、UI 格式化等）
- `run.sh` 提供 `build`、`all`、`test`、`format`、`decompile` 等本地入口

## 进一步阅读

- **文档总索引**：[docs/README.md](README.md)
- 仓库总览：[../README.md](../README.md)
- Run / History / 上传：[features/run-logging-and-upload.md](features/run-logging-and-upload.md)；SQLite schema：[reference/sqlite-schema-reference.md](reference/sqlite-schema-reference.md)
- Ghost battle 数据流（视角翻转）：[features/ghost-battle-data-flow.md](features/ghost-battle-data-flow.md)
- 战斗回放（含可选 MP4 视频）：[features/combat-replay.md](features/combat-replay.md)
- 终局截图与 BazaarDB 上传：[features/screenshots.md](features/screenshots.md)
- 战斗状态条 / 怪物预览 / 附魔升级预览：[features/combat-status-bar.md](features/combat-status-bar.md)、[features/monster-preview.md](features/monster-preview.md)、[features/tooltip-preview.md](features/tooltip-preview.md)
- 热键 / 设置表面：[reference/hotkeys-reference.md](reference/hotkeys-reference.md)、[reference/settings-and-debug-surfaces.md](reference/settings-and-debug-surfaces.md)
- AutoBazaar（parked）：[features/autobazaar.md](features/autobazaar.md)（→ [reference/auto-bazaar-http-api-v1.md](reference/auto-bazaar-http-api-v1.md)、[reference/auto-bazaar-decision-surface.md](reference/auto-bazaar-decision-surface.md)）
- 设计决策（ADR）：[adr/](adr/)；逆向工程笔记：[reverse-engineering/](reverse-engineering/)
- 服务端部署：见独立仓库 `bazaarplusplus-server/README.md`
