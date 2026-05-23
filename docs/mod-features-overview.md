# BazaarPlusPlus 功能总览

本文根据当前代码整理模组**已实现**的能力；若与实现不一致，以 `Plugin.cs`、`BppComposition.cs`、`Core/`、`Game/`、`Patches/` 为准。

## 概述

BazaarPlusPlus 是面向《The Bazaar》的 **BepInEx** 插件，在游戏中提供：

- 战斗与 UI 增强：状态条、怪物 tooltip 增强、附魔/升级 tooltip、展示柜 item-board overlay
- **Run logging**：活跃对局写入本地 SQLite，供 HistoryPanel 和上传队列使用
- **PVP 战斗回放**：本地录制 replay payload，并在 HistoryPanel / ghost replay 路径下条件回放
- **云同步**：在**非 live run** 时后台上传 V3 `run-bundle`，并从 **ModCFServerV3** 同步 ghost battles / replay 下载链接
- **BazaarDB 截图上传**（默认关闭）：开启后将历史与新增的终局截图与摘要 JSON 推到 ModCFServerV3，BazaarDB 再从服务端拉取
- 大厅与展示类小功能：随机英雄池面板、主菜单版本号、Legendary 段位展示文案、中文术语切换
- **Anonymous Mode**：可选将显示名改为 `Anonymous`
- 终局自动截图：终局 `Continue` 前自动保存主截图和元数据
- **AutoBazaar HTTP 接口**：本地回环 HTTP 服务（默认端口 47900），对外暴露决策上下文（`GET /v1/context`）并接受外部动作（`POST /v1/actions`）；纯传输与校验层，Mod 本身不做策略决策。详见 [auto-bazaar-http-api-v1.md](reference/auto-bazaar-http-api-v1.md)。

## 运行时骨架

| 层次 | 作用 |
| --- | --- |
| `Plugin.cs` | BepInEx 入口：初始化配置、composition、Harmony patches、身份/网络服务和 `MonoBehaviour` 控制器 |
| `BppComposition.cs` | 创建 `IBppServices`，注册 `RunLifecycleModule`、`CombatReplayModule`、`CombatStatusBarModule` |
| `Core/` | 配置、事件总线、路径、run context、运行时服务接口 |
| `Patches/` | Harmony 补丁：战斗模拟、回放采集、设置坞、大厅、tooltip、名称覆盖等 |

`Plugin.AttachRuntimeComponents()` 当前挂载 `RunLoggingController`、`RunUploadController`、`PlayerObservationController`、`HistoryPanel`、`CombatStatusBar`、怪物预览相关 runtime、`EndOfRunScreenshotController`、`BazaarDbScreenshotUploadController` 和 `TooltipModifierRefreshController`。

## 游戏内功能模块

### 战斗状态条（Combat Status Bar）

- 战斗期间底部 HUD：逻辑战斗时间、已处理帧数、暂停状态和 0.50x / 0.67x / 1.00x 速度档位
- 默认关闭，可在 **Bazaar++ 设置坞** 中开启；开关和速度档位会写入配置
- 逻辑时间基于已处理战斗帧 × 50ms，与墙钟解耦

详见 `docs/combat-status-bar.md`。

### 怪物预览（Monster Preview）

- 默认走游戏原生怪物预览
- Bazaar++ 在原生 tooltip 路径上做局部增强：按需补 monster 上下文、附魔/升级预览注入、showcase tooltip 锁绕过
- `MonsterPreviewItemBoardRuntime` / `CardSetPreviewRuntime` 会复用原生 `MonsterBoardTooltip` 展示 Bazaar++ 组织的 board 内容
- HistoryPanel 预览使用共享 `Game/PreviewSurface` 渲染栈

详见 `docs/monster-preview-design.md`。

### 附魔预览与升级预览（Tooltips）

- **附魔**：在物品 tooltip 上追加附魔说明；可配置「始终显示」或按住 **EnchantPreview** 热键时显示（默认 Ctrl）
- **升级预览**：按住 **UpgradePreview** 热键（默认 Shift）进入原生 upgrade preview 路径
- `TooltipModifierRefreshController` 在配置或 modifier 状态变化时刷新当前 tooltip

### Streamer / Anonymous 名称（Name Override）

- 可选将游戏内显示名替换为 `Anonymous`（配置 + 设置坞）
- 通过 `Patches/NameOverride/` 与相关 UI 刷新衔接

### Run Logging 与 HistoryPanel

- Live run 期间写入 `bazaarplusplus.db`：`runs`、`run_events`、`battles`、`battle_snapshots`、`run_sync_state` 等
- `RunLifecycleModule` 维护 `IsInGameRun` 和当前服务端 `run_id`；`RunInitializedPatch` 捕获 `NetMessageRunInitialized`
- HistoryPanel 浏览本地 runs、本地 PVP battles、ghost battles 和保存的棋盘快照
- 条件满足时可从 HistoryPanel 启动本地 replay 或下载并回放 ghost replay
- 支持删除 run 及关联 battle 记录

详见 `docs/run-logging.md`。

### 后台上传与 Ghost Battles

- `RunUploadController` 仅在非 live run 时扫描待上传 completed runs
- `RunBundleUploadStore` 组装 run projection、battle projections 和 gzip MessagePack artifact
- `RunBundleUploadService` 上传到 `POST /run-bundles`
- HistoryPanel 的 ghost sync 用 `?player_account_id=` 调用 `GET /ghost-battles`（不鉴权），按需申请 `POST /ghost-battles/:battleId/replay-link`

详见 `docs/run-upload.md`。

### 战斗回放（Combat Replay）

- 本地录制 PVP replay payload，保存到 `<GameRoot>/BazaarPlusPlus/CombatReplays`
- battle metadata 和 board snapshot 写入 SQLite `battles` / `battle_snapshots`
- `CombatReplayRuntime` + `CombatReplayCapturePatch` 负责采集；HistoryPanel 在条件满足时回放
- **可选 MP4 录制**：默认关闭（`CombatReplayVideo / Enabled`）。开启后会在 saved replay 播放期间把 Game View 抓帧、调用外部 FFmpeg 写到 `<GameRoot>/BazaarPlusPlus/CombatReplayVideos/`，元数据进 SQLite `combat_replay_videos`。FFmpeg 未检测到时静默禁用，不影响 replay 本身

详见 `docs/reference/combat-replay-recording.md`、`docs/combat-replay-video-recording.md`。

### 终局自动截图（End-of-run Screenshot）

- 终局界面出现后先等待 10 秒，等待窗口内吞掉鼠标点击
- 第一次合法 `Continue` 会先保存主截图，再放行原始按钮动作
- PNG 保存到 `<GameRoot>/BazaarPlusPlus/Screenshots`，元数据写入 SQLite `run_screenshots`

详见 `docs/reference/end-of-run-screenshot-flow.md`。

### BazaarDB 截图上传

- `BazaarDB / UploadScreenshots` 默认关闭；开启后 `BazaarDbScreenshotUploadController` 在非 live run 时按 180s 间隔扫描待上传截图
- Sidecar 表 `bazaardb_screenshot_uploads` 用 `INSERT OR IGNORE` 回填 `run_screenshots` 中所有 `capture_source = 'end_of_run_auto'` 的行——开关从关切到开就能把历史截图一并补传
- 数据流：模组 `POST /bazaardb-screenshots`（无鉴权）→ Worker 写 R2 + D1 → BazaarDB 用 Bearer token 调 `GET /bazaardb/manifest` 与 `GET /bazaardb/image/{id}` 拉取
- 4xx（除 408 / 429）落 `permanent_failure`，不再重试；5xx / 网络错误保留 `pending` 自动重试；翻开开关 / run 退出时立刻触发一次

详见 `docs/bazaardb-screenshot-upload.md`。

### 大厅、设置与本地化

- **随机英雄池 / 皮肤池**：英雄选择界面附加面板逻辑
- **主菜单版本号**：在游戏版本字符串旁展示模组版本
- **Legendary 位置展示**：可按配置保留原值、隐藏、固定 `999999` 或显示 `#position | rating`
- **中文术语模式**：可在 Mainland / Taiwan / HongKong 术语间切换
- **Bazaar++ 设置坞**：注入 Game History、Anonymous、Legendary Position、Enchant Preview、Combat Status Bar、Chinese Locale 等入口

## 云同步与 ModCFServerV3

面向 **ModCFServerV3**（`ModCFServerV3/`，Cloudflare Workers + D1 + R2）：

- **共享身份目录**：`<GameRoot>/BazaarPlusPlus/Identity/`，当前只写 `observation.v1.json`；旧 `auth.v1.json` 与 `identity.db*` 在 mod 启动时会被一次性清理
- **Run Bundle 上传**：已完成 run 与关联 replay artifact 合并上传到 `POST /run-bundles`（不鉴权；服务端只把 `seen_player_accounts` 注册过的玩家或上传者本人作为可投影 opponent）
- **Player Observation**：mod 把观察到的 `player_account_id` 写入 `observation.v1.json`
- **Ghost 战斗**：`GET /ghost-battles?player_account_id=…` 查询 against-me 列表（不鉴权）；按需签发 `POST /ghost-battles/:battleId/replay-link`
- **Bundle-final 标记**：服务端把上传 bundle 中最后一场 battle 在被投影时标记为 `is_bundle_final_battle`；HistoryPanel 在 ghost 视角下用它提示“这场后对手出局”
- **BazaarDB 截图上传**：`POST /bazaardb-screenshots`（不鉴权）写 R2 + D1，BazaarDB 用 `BAZAARDB_PULL_TOKEN` 拉 `GET /bazaardb/manifest` / `GET /bazaardb/image/{id}`

模组侧仅在**非 live run** 时执行上传扫描；`RunUploadController` 统一调度 run-bundle 上传。信任模型与安全限制见 `docs/run-upload.md`。

## 配置摘要（BazaarPlusPlus.cfg）

与功能直接相关的条目包括（完整见 `Core/Config/BppConfig.cs`）：

| Section / Key | 含义 |
| --- | --- |
| `ItemBoard / AnchoredPosition` | item-board overlay 位置覆盖 |
| `StreamerMode / EnableNameOverride` | Anonymous 显示名 |
| `EnchantPreview / AlwaysShow` | 附魔 tooltip 是否始终显示 |
| `CombatStatusBar / Enabled` | 战斗状态条开关 |
| `CombatStatusBar / SpeedMultiplier` | 默认战斗速度档位 |
| `Hotkeys / EnchantPreview`, `Hotkeys / UpgradePreview` | 附魔/升级预览按键路径 |
| `Localization / ChineseLocaleMode` | 中文术语模式 |
| `LegendaryPositionDisplay / Mode` | Legendary 位置展示模式 |
| `CombatReplayVideo / Enabled` | 可选战斗回放视频录制总开关（默认 false） |
| `CombatReplayVideo / Fps`, `Width`, `Height`, `Crf`, `Preset` | 视频编码参数；`Width=0` / `Height=0` 表示跟随 `Screen` |
| `CombatReplayVideo / ForceSpeed1x`, `SuppressBppOverlays`, `MaxQueuedFrames` | 录制期间锁定 1x 速度、隐藏 BPP overlay、抓帧队列上限 |
| `BazaarDB / UploadScreenshots` | 是否将终局截图上传到 ModCFServerV3 供 BazaarDB 拉取，默认 `false` |

HistoryPanel 的预览相关另有独立配置段（`HistoryPanelPreviewSettings`）。

## 测试与脚本

- `tests/` 下按特性拆分测试项目（run logging、upload、combat replay、UI 格式化等）
- `run.sh` 提供 `build`、`all`、`test`、`format`、`decompile` 等本地入口

## 进一步阅读

- 仓库总览：`README.md`
- Run / History / SQLite：`docs/run-logging.md`
- Run bundle 上传与信任模型：`docs/run-upload.md`
- BazaarDB 截图上传链路：`docs/bazaardb-screenshot-upload.md`
- 战斗状态条 / 怪物预览 / 终局截图 / CF 部署：`docs/combat-status-bar.md`、`docs/monster-preview-design.md`、`docs/reference/end-of-run-screenshot-flow.md`、`docs/mod-cf-server-deploy.md`
- 热键、设置表面、SQLite schema、tooltip 实现细节：`docs/reference/`
- AutoBazaar HTTP API 规范：`docs/reference/auto-bazaar-http-api-v1.md`
- AutoBazaar 决策表面字段推导：`docs/reference/auto-bazaar-decision-surface.md`
