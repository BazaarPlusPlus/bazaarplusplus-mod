# Combat Replay

PVP combat 的本地录制与回放，外加可选的 MP4 视频录制。录制 / 回放是默认能力；视频录制是独立、默认关闭的附加 feature（`CombatReplayVideo / Enabled = false`）。

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

saved replay 播放期间把 Unity Game View 抓帧编码为 MP4，落到 `<GameRoot>/BazaarPlusPlusV4/CombatReplayVideos/<yyyy-MM-dd>/<battle_id>.<yyyyMMdd-HHmmss>.mp4`，供离线复盘 / 社区分享。Local 与 ghost replay 一视同仁。

- **默认关闭**，由 `CombatReplayVideo / Enabled` 控制；关闭时录制器不订阅、零开销。
- **FFmpeg 两级检测，只检测不下载**：`<GameRoot>/BazaarPlusPlusV4/tools/ffmpeg/ffmpeg(.exe)` → 系统 `PATH`。检测方式是起 `ffmpeg -version`（2s 超时，`ExitCode==0`），结果缓存到 session；都未命中则功能静默禁用并打一行 Info log，不影响 replay 本身。
- **抓帧 / 编码**：`ScreenCapture.CaptureScreenshotIntoRenderTexture` + `AsyncGPUReadback`（main thread 只 enqueue 到 bounded queue）→ FFmpeg subprocess（rawvideo stdin → libx264 / openh264 → MP4）。`SystemInfo.supportsAsyncGPUReadback` 为 false 时禁用。
- **元数据**：SQLite `combat_replay_videos`（列定义见 schema 参考）。
- **职责切分**：mod runtime 只做检测 + 抓帧 + 调 ffmpeg，绝不下载、绝不写 `tools/ffmpeg/`；可选的 FFmpeg 部署由 `bazaarplusplus-installer` 负责（minimal LGPL build + OpenH264，避开 GPL 传染），保证 mod release artifact 不变大、无许可证分发风险。

### Config（`CombatReplayVideo` 段，权威见 `Core/Config/BppConfig.cs`）

| Key | 默认 | 含义 |
|---|---|---|
| `Enabled` | `false` | 总开关 |
| `Fps` | `30` | 帧率 |
| `Width` / `Height` | `0` | `0` = 跟随 `Screen` |
| `Crf` | `23` | x264 质量 |
| `Preset` | `veryfast` | x264 预设 |
| `ForceSpeed1x` | `true` | 录制期间锁 1x 速度 |
| `SuppressBppOverlays` | `true` | 录制期间隐藏 BPP 浮层 |
| `MaxQueuedFrames` | `90` | 抓帧队列上限（满则 drop，记 `dropped_frames`） |

### 当前状态

Phase 1–3 已落地：录制链路、稳定性（fallback / 检测缓存 / bounded queue / 资源闭环 / speed 恢复 / overlay 抑制）、SQLite `combat_replay_videos` 元数据。**未落地**：HistoryPanel 内的视频状态 / “Open Folder” 行动项、installer 侧 FFmpeg 自动部署、音频（原 Phase 4）。SFX 修复历史归档在 [docs/design/archive/2026-05-23-combat-replay-sfx-impl.md](../design/archive/2026-05-23-combat-replay-sfx-impl.md)。

## 关键文件

- `Patches/Combat/CombatReplayCapturePatch.cs`
- `Game/CombatReplay/CombatReplayModule.cs`
- `Game/CombatReplay/CombatReplayRuntime.cs`、`CombatReplayRuntime.Bootstrap.cs`
- `Game/CombatReplay/CombatReplayCaptureService.cs`
- `Game/CombatReplay/CombatReplayPersistenceQueue.cs`、`CombatReplayPayloadStore.cs`、`CombatReplayLoader.cs`
- `Game/CombatReplay/CombatReplayPlaybackStarting.cs`、`CombatReplayPlaybackEnded.cs`
- `Game/CombatReplay/Video/CombatReplayVideoRecorder.cs`、`CombatReplayVideoMetadataStore.cs`（+ `ReplayVideoCaptureSession.cs`、`FfmpegRawVideoEncoder.cs`）
- `Game/HistoryPanel/HistoryPanelReplayService.cs`
