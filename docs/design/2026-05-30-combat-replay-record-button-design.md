# Combat Replay 视频录制改为 HistoryPanel 按钮触发

- 日期：2026-05-30
- 状态：已实现并验证（build 通过 + 评审通过），待提交；合并后移入 `archive/` 并加 `Status:` banner
- 影响仓库：`bazaarplusplus-mod`

## 背景与动机

Combat Replay 的可选 MP4 录制当前是「全局配置开关 + 事件订阅 + 自动录制每一次回放」模型：录制器 [`CombatReplayVideoRecorder`](../../Game/CombatReplay/Video/CombatReplayVideoRecorder.cs) 作为常驻 MonoBehaviour（挂载于 [`BppComposition`](../../BppComposition.cs) `:99`），在 `OnEnable`(`:50`) 无条件订阅 `CombatReplayPlaybackStarting`/`CombatReplayPlaybackEnded`。只要 `CombatReplayVideo/Enabled=true`，凡是经过 `CombatReplayRuntime.StartReplayAsync` 的回放都会被自动录制。

问题：

- 全局开关 + 自动录每场不符合实际使用——用户通常只想录「某一场特定对局」。
- `Enabled` 既是用户 opt-in、又是逐场自动闸（唯一读取点 `CombatReplayVideoRecorder.cs:98`），语义混淆。
- `ForceSpeed1x`（录制时锁 1x 速度）与 `SuppressBppOverlays`（录制时隐藏 BPP overlay）对「全自动录每场」是必要副作用，但作为用户可调 config 价值低、徒增配置面。

目标：把录制改为 **HistoryPanel 上用户显式点击触发的单次行为**——点「录制并回放」按钮 →（ghost 对局先下载）→ 回放该对局 → 回放期间录制 → 回放结束自动收尾。删除全局开关与无脑自动录制路径，精简相关 config，使录制只能从这一个入口发起。

## 决策

1. **触发机制：录制意图作为参数绑定到本次回放 session（方案 A）。** 给 `CombatReplayPlaybackStarting` 事件加 `bool RecordVideo`，从按钮一路透传到 `ReplayPlaybackPublisher`；录制器把原 `Enabled` 闸换成 `if (!evt.RecordVideo) return;`。复用现有 `PublishStarting`（在 `replayState.Replay()` 前一行同步发出）/`PublishEnded`（离开 `ReplayState` 时发出）作为录制起止时序，**零新增时序逻辑**。
   - 取舍：相比「录制器 arm-latch」（全局一次性 flag），参数随 session 走无残留状态、ghost 下载失败天然不录、无需在多处清 flag；相比「录制器不订阅事件、调用方直接驱动」，保留了「出画前一刻就绪、回放结束即收尾」的精确时序。代价是 7 个方法签名各加一个 `bool` 参数（机械改动）。
2. **UI：HistoryPanel 页脚新增独立「录制并回放」按钮**，作用于当前选中对局，与现有纯 Replay 按钮并存。面板在回放开始时关闭（复用现有协调器行为），故**无需逐行「录制中」状态 UI**。
3. **Config 精简：** 删 `Enabled`、`ForceSpeed1x`、`SuppressBppOverlays`；保留 `Fps`/`Width`/`Height`/`Crf`/`Preset`/`MaxQueuedFrames`（编码/输出参数）。录制可用性 = FFmpeg 存在 + 支持 `AsyncGPUReadback`，不再有功能总开关。
4. **副作用固定：** 录制时**始终**隐藏 BPP overlay（原 `SuppressBppOverlays` 行为固定为 true）；**不再**强制 1x 速度（删 `ForceSpeed1x` 整套逻辑），录制速度跟随回放本身。

## 详细设计

### A. 录制意图透传链（方案 A）

依次新增 `recordVideo` 参数 / 字段（默认 `false`，仅录制按钮路径传 `true`）：

| 层 | 位置 | 改动 |
|---|---|---|
| DTO | [`CombatReplayPlaybackStarting.cs`](../../Game/CombatReplay/CombatReplayPlaybackStarting.cs) | 加 `public bool RecordVideo { get; set; }` |
| publisher | [`ReplayPlaybackPublisher.cs`](../../Game/CombatReplay/ReplayPlaybackPublisher.cs) `BeginSession`(`:24`) | 加 `bool recordVideo` 参数并存字段；`PublishStarting`(`:36`) 写进 DTO |
| runtime | [`CombatReplayRuntime.cs`](../../Game/CombatReplay/CombatReplayRuntime.cs) `StartReplayAsync`(`:235`)、`ReplaySaved`(`:179`)、`ReplayImportedBattle`(`:206`) | 各加 `bool recordVideo`，透传到 `BeginSession`(`:244`)；`ReplayLatest`(`:170`) 传 `false` |
| UI service | [`HistoryPanelReplayService.cs`](../../Game/HistoryPanel/HistoryPanelReplayService.cs) `ReplayBattleAsync`(`:77`) | 加 `bool recordVideo`，透传到 `ReplaySaved`(`:97`)/`ReplayImportedBattle`(`:165`) |
| coordinator | [`HistoryPanelCoordinator.cs`](../../Game/HistoryPanel/HistoryPanelCoordinator.cs) `TryReplaySelectedBattleAsync`(`:290`) | 加 `bool recordVideo`，透传到 `ReplayBattleAsync`；复用现有 `ReplayActionInProgress` 单飞锁与成功后关面板 |
| controller | [`HistoryPanelController.cs`](../../Game/HistoryPanel/HistoryPanelController.cs) `TryReplaySelectedBattle`(`:69`) | 加 `bool recordVideo = false` |

ghost 路径的下载（`HistoryPanelReplayService.cs:131` 的 `await DownloadReplayAsync`）发生在 `ReplayImportedBattle`(`:165`) **之前**；`recordVideo` 随 session 走，下载失败/取消时根本到不了 `ReplayImportedBattle`，因此不会误录、也无残留状态。

### B. 录制器改动（`CombatReplayVideoRecorder.cs`）

- **触发闸**：`OnPlaybackStarting`(`:79`) 把 `if (config.CombatReplayVideoEnabled?.Value != true) return;`(`:98`) 换成 `if (!evt.RecordVideo) return;`。保留 `AsyncGPUReadback`(`:101`)/FFmpeg(`:111`)/视频目录(`:115`) 三道闸。
- **删 ForceSpeed1x**：删 `BeginRecording`(`:211-212`) 的 `if (request.ForceSpeed1x) ApplyForcedCombatSpeed();`、`ApplyForcedCombatSpeed`/`RestoreCombatSpeed` 方法、`_savedCombatSpeed` 字段、`BuildCaptureRequest`(`:521`) 的读取、`DisposeUiAndSpeedState`(`:484` 附近) 里的 `RestoreCombatSpeed()` 调用，以及 [`ReplayVideoCaptureRequest`](../../Game/CombatReplay/Video/ReplayVideoCaptureRequest.cs) 的 `ForceSpeed1x` 字段。
- **SuppressBppOverlays 固定 true**：删 `BuildCaptureRequest`(`:522`) 的读取与 `ReplayVideoCaptureRequest.SuppressBppOverlays` 字段；`BeginRecording`(`:208`) 改为无条件 `_uiSuppressionScope = BeginUiSuppression();`。

### C. UI 入口（HistoryPanel 页脚按钮）

- [`HistoryPanelUiToolkitView.Tree.cs`](../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs) 的 `BuildFooter`：在 Replay 按钮旁用现有 `CreateButton`/`StyleButton`（[`Style.cs`](../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Style.cs) `:79`）新增「录制并回放」按钮，绑到视图新增 Action `_recordAndReplay`。
- 视图构造函数（[`HistoryPanelUiToolkitView.cs`](../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs) `:60`）新增 `Action _recordAndReplay` 注入位，由 [`HistoryPanel.UiToolkit.cs`](../../Game/HistoryPanel/HistoryPanel.UiToolkit.cs) `:20` 的 `EnsureUi` 提供：`_replay → controller.TryReplaySelectedBattle(false)`、`_recordAndReplay → controller.TryReplaySelectedBattle(true)`。
- 文案走 [`HistoryPanelText`](../../Game/HistoryPanel/HistoryPanelText.cs)：新增 `RecordAndReplay()` 与「不可录」提示文案。

### D. FFmpeg 可用性与按钮状态

- 录制按钮可用态 = `CanReplayBattle(选中对局)`（`HistoryPanelReplayService.cs:34`）**且** 录制可行：`FfmpegLocator.Resolve(services.Paths.PluginsDirectoryPath) != null` **且** `SystemInfo.supportsAsyncGPUReadback`。
- [`FfmpegLocator.Resolve`](../../Game/CombatReplay/Video/FfmpegLocator.cs) 进程级缓存，首次含 2s 探活（`ffmpeg -version`、`ExitCode==0`）。为避免阻塞 UI 线程：在 feature 初始化或面板首次打开时**异步预热一次**，按钮可用态读缓存结果。
- 不可录时：按钮置灰 + 状态行提示（如「未检测到 FFmpeg，无法录制」），**不**误启一个无视频的回放。
- 按钮可用态/文案随选中变化刷新——挂在现有「选中变化 → 刷新页脚」逻辑上（与 Replay 按钮同源，具体位置实现计划阶段确认）。

### E. 与「FFmpeg 迁移」设计的关系（重要）

[2026-05-30-ffmpeg-relocation-to-mod-design.md](2026-05-30-ffmpeg-relocation-to-mod-design.md) 的 **mod 侧改动（A1–A4）已落地**：`FfmpegLocator.Resolve(string? pluginsDirectoryPath)` 现已从 `<plugins>/ffmpeg(.exe)` + 系统 `PATH` 两级查找，[`IPathProvider`](../../Storage/Paths/IPathProvider.cs) 的 `PluginsDirectoryPath` 已存在、`ToolsDirectoryPath` 已删，录制器(`:110`) 已传 `PluginsDirectoryPath`。本设计 D 节直接复用该现状，且与「二进制从哪来」正交——录制可用性只看「能否定位到一个可用 ffmpeg」。

两处需协调：

1. 迁移设计 D 节计划「更新 `BppConfig.cs` 的 `Enabled` 帮助文案」——本设计**删除** `Enabled`，使该子任务作废。
2. [`docs/features/combat-replay.md`](../features/combat-replay.md) 当前**两处过时**：(a) FFmpeg 路径仍写 `tools/ffmpeg/`（代码已改 plugins/，属迁移设计 D 节待办）；(b) 整个录制触发模型（`Enabled` 自动录每场）。本设计重写该节时一并修正 (a)、覆盖 (b)。

### F. 文档同步

- （实现阶段）重写 `docs/features/combat-replay.md` 的「Optional：Video Recording」节：触发从「全局开关自动录每场」→「HistoryPanel 按钮单次录制」；删 config 表里 `Enabled`/`ForceSpeed1x`/`SuppressBppOverlays` 三行；修正 FFmpeg 路径为 `BepInEx/plugins/`；更新「当前状态」。
- （随本 spec 已落地）`docs/design/README.md` 已把本 spec 与 ffmpeg-relocation 列入「Active proposals」。

## 非目标（Out of Scope）

- 不改抓帧/编码引擎（`ReplayVideoCaptureSession`、`FfmpegRawVideoEncoder`）与 `FfmpegLocator` 的定位逻辑。
- 不动 FFmpeg 的部署/分发（属 ffmpeg-relocation 设计的范畴）。
- 不新增录制完成后的「打开文件夹 / 视频列表」UI（combat-replay 文档原列为未落地项，保持未落地）。
- 不引入 codec 回退（当前固定 `libx264`）。
- 不新增逐行「录制中」状态 UI（面板回放时关闭，无此需要）。

## 验证

- 这条链路主要是 Unity 运行时接线，缺乏干净的单测 seam。
- `dotnet build BazaarPlusPlus.csproj`（targeted）：确认编译通过、删除 config 后无悬空引用（grep `CombatReplayVideoEnabled`/`ForceSpeed1x`/`SuppressBppOverlays` 确认清理干净）。
- 若 `HistoryPanelReplayService` / config 存在现成可断言 seam，补最小测试验证 `recordVideo` 透传；否则不为覆盖率造测试（遵循项目规则）。
- 游戏内手测：local + ghost 对局各点一次「录制并回放」→ 确认生成 MP4 + `combat_replay_videos` 元数据 + overlay 隐藏 + 速度不再强制 1x；FFmpeg 缺失时按钮置灰且不误启回放。

## 风险 / 待确认

- **签名透传面**：7 个方法加 `bool recordVideo`，须确保所有现有调用点都显式传或落到默认 `false`（页脚 Replay、`ReplayLatest`，以及任何其他 `ReplaySaved`/`ReplayImportedBattle` 调用者）。实现时 grep 全部调用点。
- **按钮可用态刷新位置**：页脚按钮 enabled/label 随选中变化刷新的确切位置，待实现计划阶段在 coordinator/controller 里定位（与 Replay 同源）。
- **FFmpeg 预热时机**：首次 `Resolve` 的 2s 探活须在 UI 关键路径外触发；选 feature 初始化或面板首次打开时异步预热。
