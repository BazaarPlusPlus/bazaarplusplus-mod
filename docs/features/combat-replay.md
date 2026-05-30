# Combat Replay

PVP combat 的本地录制与回放，外加可选的 MP4 视频录制。录制 / 回放是默认能力；视频录制是独立的附加 feature，由 HistoryPanel 上的「录制并回放」按钮对选中对局显式发起单次录制（无全局开关）。

本文是 combat replay 子系统的唯一现行文档。SQLite 列定义统一见 [sqlite-schema-reference.md](../reference/sqlite-schema-reference.md)，不在此重复。

## Capture（录制链路）

完成的 `PVPCombat` 被保存为与游戏原生 replay 流程兼容的三消息 bundle：

1. opening `NetMessageGameSim`
2. `NetMessageCombatSim`
3. closing `NetMessageGameSim`

流程：

1. `Patches/Combat/CombatReplayCapturePatch.cs` 监听相关 net message。
2. `Game/CombatReplay/CombatReplayModule.cs` 转发消息给 `CombatReplayRuntime`。
3. `Game/CombatReplay/CombatReplayCaptureService.cs` 识别三消息窗口，组装 manifest + player/opponent board snapshot + payload。
4. `Game/CombatReplay/CombatReplayRuntime.cs` 经 `CombatReplayPersistenceQueue` 异步持久化 payload 与 manifest。
5. 完成后发布 `PvpBattleRecorded`，供 run logging 等模块消费。

## Storage

- payload 文件：`<GameRoot>/BazaarPlusPlusV4/CombatReplays/<battle_id>.payload.mpack.gz`（由 `CombatReplayPayloadStore` 管理）
- battle metadata：SQLite `battles`（manifest）/ `battle_snapshots`（board snapshot，供 history preview 与 replay bootstrap 使用）

## Playback（回放）

- **HistoryPanel 本地 battle**：payload 存在且当前允许 bootstrap 时，`runtime.ReplaySaved(battleId)`。
- **ghost battle**：服务端声明 replay 可用时，先下载 payload，再 `runtime.ReplayImportedBattle(manifest, payload)`。
- bootstrap：`CombatReplayRuntime.Bootstrap.TryInjectSavedReplayAsync` 重建场景 / 卡牌 / 技能 → `AppState.TryPushState<ReplayState>()` → `replayState.Replay()`。
- 退出：`CombatReplayRuntime.OnStateChanged` 检测离开 `ReplayState`。

## Playback Events

`CombatReplayRuntime` 在 saved replay 真正播放前后发布两个事件，供独立 feature 订阅，不耦合 capture path：

- `CombatReplayPlaybackStarting`（`replayState.Replay()` 之前）：`BattleId`、`Manifest`、`Source`（`LocalSaved` / `ImportedGhost`）。
- `CombatReplayPlaybackEnded`（离开 `ReplayState`，或 start 失败的 catch 分支）：`BattleId`、`Reason`、`Failed`。

视频录制就是这两个事件的唯一订阅者。

## Optional：Video Recording（可选 MP4 录制）

replay 播放期间把 Unity Game View 抓帧编码为 MP4，落到 `<GameRoot>/BazaarPlusPlusV4/CombatReplayVideos/<yyyy-MM-dd>/<battle_id>.<yyyyMMdd-HHmmss>.mp4`，供离线复盘 / 社区分享。Local 与 ghost replay 一视同仁。

- **触发：单次、显式，由 HistoryPanel 页脚的「录制并回放」按钮发起**，作用于当前选中对局，与现有纯 Replay 按钮并存。点按钮 →（ghost 对局先下载 payload）→ 回放该对局 → 回放期间录制 → 离开 `ReplayState` 自动收尾。没有全局开关、也不会自动录每场回放：录制意图作为参数绑定到本次 session（`CombatReplayPlaybackStarting.RecordVideo`），随 session 走、无残留状态；纯 Replay 按钮与 `ReplayLatest` 不录制。
- **录制可用性 = FFmpeg 存在 + 支持 `AsyncGPUReadback`，没有功能开关。** 二者任一不满足时「录制并回放」按钮置灰并给出状态提示（如「未检测到 FFmpeg，无法录制」），且不会误启一个无视频的回放。
- **FFmpeg 两级检测，只检测不下载（随 mod 分发）**：`<GameRoot>/BepInEx/plugins/ffmpeg(.exe)`（与 mod 同目录的 bundled 二进制）→ 系统 `PATH`。FFmpeg 随 mod 一起分发，无需单独下载或安装。检测方式是起 `ffmpeg -version`（2s 超时，`ExitCode==0`），结果缓存到 session；为避免阻塞 UI 线程，首次探活异步预热，按钮可用态读缓存结果。
- **抓帧 / 编码**：`ScreenCapture.CaptureScreenshotIntoRenderTexture` + `AsyncGPUReadback`（main thread 只 enqueue 到 bounded queue）→ FFmpeg subprocess（rawvideo stdin → libx264 / openh264 → MP4）。`SystemInfo.supportsAsyncGPUReadback` 为 false 时录制不可用。
- **录制期间的副作用**：**始终**隐藏 BPP 浮层（不可配置）；**不**强制回放速度——速度跟随回放本身。
- **元数据**：SQLite `combat_replay_videos`（列定义见 schema 参考）。
- **二进制分发**：FFmpeg 现在作为逐平台的兄弟二进制随 mod payload 一起分发（与 SQLite native lib 同构），落在 `BepInEx/plugins/` 下、运行时由 mod 相对自身定位；本项目是公开 GPL 源码项目，接受 GPL，无许可证顾虑。

### Config（`CombatReplayVideo` 段，权威见 `Core/Config/BppConfig.cs`）

仅编码 / 输出参数可配；录制的开 / 关由按钮触发，不在 config 里（详见上文「触发」与「录制可用性」）。

| Key | 默认 | 含义 |
|---|---|---|
| `Fps` | `30` | 帧率 |
| `Width` / `Height` | `0` | `0` = 跟随 `Screen` |
| `Crf` | `23` | x264 质量 |
| `Preset` | `veryfast` | x264 预设 |
| `MaxQueuedFrames` | `90` | 抓帧队列上限（满则 drop，记 `dropped_frames`） |

### 当前状态

录制链路、稳定性（fallback / 检测缓存 / bounded queue / 资源闭环 / overlay 抑制）、SQLite `combat_replay_videos` 元数据均已落地；录制由 HistoryPanel「录制并回放」按钮单次触发。FFmpeg 已随 mod 分发（见上文「二进制分发」），无需 installer 单独部署。**未落地**：HistoryPanel 内的视频状态 / “Open Folder” 行动项、音频。SFX 修复历史归档在 [docs/design/archive/2026-05-23-combat-replay-sfx-impl.md](../design/archive/2026-05-23-combat-replay-sfx-impl.md)。

## 关键文件

- `Patches/Combat/CombatReplayCapturePatch.cs`
- `Game/CombatReplay/CombatReplayModule.cs`
- `Game/CombatReplay/CombatReplayRuntime.cs`、`CombatReplayRuntime.Bootstrap.cs`
- `Game/CombatReplay/CombatReplayCaptureService.cs`
- `Game/CombatReplay/CombatReplayPersistenceQueue.cs`、`CombatReplayPayloadStore.cs`、`CombatReplayLoader.cs`
- `Game/CombatReplay/CombatReplayPlaybackStarting.cs`、`CombatReplayPlaybackEnded.cs`
- `Game/CombatReplay/Video/CombatReplayVideoRecorder.cs`、`CombatReplayVideoMetadataStore.cs`（+ `ReplayVideoCaptureSession.cs`、`FfmpegRawVideoEncoder.cs`）
- `Game/HistoryPanel/HistoryPanelReplayService.cs`
