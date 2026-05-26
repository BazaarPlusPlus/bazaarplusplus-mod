# Combat Replay Video Recording

## Scope

为 saved combat replay 的回放过程提供视频录制能力。当玩家在 HistoryPanel 触发本地战斗回放或 ghost battle 回放时，将 Unity Game View 录制为 MP4 文件，落到 `<GameRoot>/BazaarPlusPlusV4/CombatReplayVideos/` 下，供玩家离线复盘、社区分享和剪辑使用。

本文档描述目标、关键设计决策、实施阶段和验证逻辑。Phase 1–3 已实现（默认关闭，需要 FFmpeg 在 `BazaarPlusPlusV4/tools/ffmpeg/` 或 PATH 上）；HistoryPanel 录制状态/Open Folder 行动项与 installer 侧 FFmpeg 部署仍未落地。代码入口见 [Game/CombatReplay/Video/](../Game/CombatReplay/Video/)。

## 背景

### 当前 Combat Replay 系统已具备的能力

BPP mod 已有完整的 PVP combat replay 保存和回放系统。

**保存链路**：

- [Patches/CombatReplayCapturePatch.cs](../Patches/CombatReplayCapturePatch.cs) patch 了 `NetMessageProcessor.ReceiveOrQueue`,监听 `NetMessageGameSim` / `NetMessageCombatSim`,发布 `NetMessageObserved`
- [Game/CombatReplay/CombatReplayCaptureService.cs](../Game/CombatReplay/CombatReplayCaptureService.cs) 识别 PVPCombat 的三段消息窗口(opening GameSim → CombatSim → closing GameSim),组装 manifest + snapshots + payload
- Payload 存到 `<GameRoot>/BazaarPlusPlusV4/CombatReplays/<battle_id>.payload.mpack.gz`,由 [Game/CombatReplay/CombatReplayPayloadStore.cs](../Game/CombatReplay/CombatReplayPayloadStore.cs) 管理
- Metadata 存到 SQLite 的 `battles` + `battle_snapshots` 表

**回放链路**：

- 入口在 `HistoryPanelReplayService`:
  - Local battle → `runtime.ReplaySaved(battleId)`
  - Ghost battle → 下载 payload → `runtime.ReplayImportedBattle(manifest, payload)`
- [Game/CombatReplay/CombatReplayRuntime.cs](../Game/CombatReplay/CombatReplayRuntime.cs) `StartReplayAsync` → [Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs](../Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs) `TryInjectSavedReplayAsync` → 重建场景/卡牌/技能 → `AppState.TryPushState<ReplayState>()` → `replayState.Replay()`
- 退出由 `CombatReplayRuntime.OnStateChanged` 检测 `PreviousState is ReplayState && CurrentState is not ReplayState`

### 当前缺口

回放只能在游戏内查看,无法导出。玩家想做赛后复盘、社区分享、剪辑只能依赖外部录屏软件,门槛高且体验不一致。

### 项目约束

- 主项目 `bazaarplusplus-mod`：BepInEx 5 plugin,runtime 行为
- 独立项目 `bazaarplusplus-installer`：Tauri 桌面安装器(Rust + Svelte),负责部署
- 不能修改游戏源码,`decompiled/` 只读
- mod release artifact 不能默认变大(installer 体积是用户痛点)
- 许可证不能被传染(mod LICENSE 是 MIT-类协议,不能强制变 GPL)

## 目标

### 功能目标

- saved replay 播放期间自动录制 Unity Game View 为 MP4
- 输出路径：`<GameRoot>/BazaarPlusPlusV4/CombatReplayVideos/<yyyy-MM-dd>/<battle_id>.<yyyyMMdd-HHmmss>.mp4`
- 默认关闭(`CombatReplayVideoEnabled = false`)
- FFmpeg 未检测到时静默禁用,不报错不弹窗,BPP log 一行 Info 提示放置位置
- Local battle replay 与 ghost battle replay 一视同仁

### 非功能目标

| 维度 | 目标 |
|---|---|
| Live combat 性能 | 零影响(不在 capture path 上加任何逻辑) |
| 其他 BPP 功能 | 零影响(独立 MonoBehaviour,独立事件订阅,独立失败域) |
| Replay 体验 | 录制失败不阻塞回放;录制成功不可感知卡顿 |
| 平台 | Windows + macOS(含 arm64) |
| mod release 体积 | 不变(不打包 FFmpeg 二进制) |
| 许可证 | 零分发风险(mod artifact 不含 ffmpeg) |

### 职责切分

| 项目 | 职责 |
|---|---|
| `bazaarplusplus-mod` | runtime 只做**检测 + 抓帧 + 调用 ffmpeg**。绝不下载,绝不写 `tools/ffmpeg/`。 |
| `bazaarplusplus-installer` | install/repair flow 可选步骤：下载平台对应 FFmpeg minimal build → 校验 SHA256 → 写到 `<GameRoot>/BazaarPlusPlusV4/tools/ffmpeg/` → 验证 `ffmpeg -version`。Binary 托管在 R2 上(LGPL minimal + OpenH264,避开 GPL 传染)。 |

### 明确不做

防止 scope creep,以下事项明确不在本项目范围内：

- live combat 实时录制(**只录** saved replay 回放)
- Phase 1-3 不做音频
- 视频编辑、剪辑、水印、上传
- 多 codec、HDR、自定义帧率插值
- mod 内自动下载 FFmpeg

## 关键设计决策

### 挂载点

- **录制启动**：在 `CombatReplayRuntime.Bootstrap.TryInjectSavedReplayAsync` 中,`replayState.Replay()` 调用前
- **录制停止**：在 `CombatReplayRuntime.OnStateChanged` 中,检测离开 `ReplayState` 时
- **不挂在** `CombatReplayCapturePatch` / `CombatReplayCaptureService` / `NetMessageObserved` —— 那是消息保存窗口,还没有视觉播放帧

挂错位置的代价：会在 live combat 期间空转,且没有可见画面可录。

### 事件解耦

新增两个事件类,Recorder 只订阅这两个事件,不耦合 HistoryPanel、CombatReplayController 或任何 caller：

```csharp
internal sealed class CombatReplayPlaybackStarting
{
    public string BattleId { get; set; }
    public PvpBattleManifest Manifest { get; set; }
    public CombatReplayPlaybackSource Source { get; set; }  // LocalSaved / ImportedGhost
}

internal sealed class CombatReplayPlaybackEnded
{
    public string BattleId { get; set; }
    public string Reason { get; set; }
    public bool Failed { get; set; }
}
```

`TryInjectSavedReplayAsync` 当前是 `static`,不强行改 instance method —— 给静态方法多传 2 个参数(`IBppEventBus`, `CombatReplayPlaybackSource`)即可,改动最小。

### 抓帧 + 编码技术栈

| 层 | 技术 | 理由 |
|---|---|---|
| 抓帧 | `ScreenCapture.CaptureScreenshotIntoRenderTexture` + `AsyncGPUReadback` | Unity 2022.3 game view 异步抓帧标准。`Texture2D.ReadPixels` 同步等 GPU,30fps 会卡死。 |
| 帧间解耦 | `BlockingCollection<byte[]>`,main thread 只 enqueue | 编码慢时不阻塞主线程 |
| 编码 | FFmpeg subprocess + rawvideo stdin pipe → libx264 / openh264 → MP4 | 跨平台、稳定、零 native 依赖 |
| 音频 | 暂不做 | 跨平台 PCM 捕获 + mux 风险高,Phase 4 再评估 |

### FFmpeg 检测

两级 fallback,**只检测,不下载**：

```text
1. <GameRoot>/BazaarPlusPlusV4/tools/ffmpeg/ffmpeg(.exe)   ← installer 部署
2. 系统 PATH 里的 ffmpeg                                  ← 用户手动装
都未命中 → 功能静默禁用,Info log 一行
```

检测方法 = 起 `ffmpeg -version` 子进程,2 秒超时,`ExitCode == 0` 才算通过。**禁止只检测 `File.Exists`** —— 权限、损坏、错误架构(mac arm64 跑 x64 binary)都会被漏掉。

检测结果缓存到 game session 内,不每次 replay 重新探测。

不提供显式 `CombatReplayVideoFfmpegPath` config —— 两级 fallback 已覆盖 99% 场景,真有自定义需求可以手动 symlink 到标准位置。

### 数据落地

- 文件：`<GameRoot>/BazaarPlusPlusV4/CombatReplayVideos/<yyyy-MM-dd>/<battle_id>.<yyyyMMdd-HHmmss>.mp4`(同 battle 多次录制由 timestamp 区分)
- Metadata：新增 SQLite 表 `combat_replay_videos`,schema version 从 11 升级到 12,不污染现有 `battles` 表

```sql
CREATE TABLE IF NOT EXISTS combat_replay_videos (
    video_id TEXT PRIMARY KEY,
    battle_id TEXT NOT NULL,
    source TEXT NOT NULL,             -- LocalSaved / ImportedGhost
    video_relative_path TEXT NOT NULL,
    width INTEGER NOT NULL,
    height INTEGER NOT NULL,
    fps INTEGER NOT NULL,
    codec TEXT NOT NULL,
    crf INTEGER NULL,
    preset TEXT NULL,
    started_at_utc TEXT NOT NULL,
    ended_at_utc TEXT NULL,
    duration_ms INTEGER NULL,
    captured_frames INTEGER NOT NULL DEFAULT 0,
    dropped_frames INTEGER NOT NULL DEFAULT 0,
    file_size_bytes INTEGER NULL,
    status TEXT NOT NULL,             -- RECORDING / COMPLETED / FAILED
    error TEXT NULL,
    FOREIGN KEY (battle_id) REFERENCES battles(battle_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_combat_replay_videos_battle
    ON combat_replay_videos(battle_id, started_at_utc DESC);
```

### Config 列表

8 项,均通过 `IBppConfig` 暴露：

```csharp
CombatReplayVideoEnabled              // 默认 false
CombatReplayVideoFps                  // 默认 30
CombatReplayVideoWidth                // 默认 0 = Screen.width
CombatReplayVideoHeight               // 默认 0 = Screen.height
CombatReplayVideoCrf                  // 默认 23
CombatReplayVideoPreset               // 默认 "veryfast"
CombatReplayVideoForceSpeed1x         // 默认 true
CombatReplayVideoSuppressBppOverlays  // 默认 true
CombatReplayVideoMaxQueuedFrames      // 默认 90  (= 3s @ 30fps)
```

## 实施阶段

### Phase 0：立项 Spike

在临时分支跑一个最小验证,控制在 1-2 小时内：

1. 任意 MonoBehaviour 的 `Update` 中调 `CaptureScreenshotIntoRenderTexture` + `AsyncGPUReadback`
2. 连续抓 30 帧,每帧 `byte[]` 落到本地,肉眼检查是否完整 RGBA
3. 起 ffmpeg 子进程喂 stdin,看能否产出可播 MP4
4. Unity Profiler 看主线程单帧增量耗时

**Spike 不通过则方案需要重新评估,不进入 Phase 1。**

### Phase 1：最小可跑

| 步骤 | 内容 |
|---|---|
| 1 | 加 `CombatReplayPlaybackStarting` / `Ended` 事件类;Bootstrap 发 starting;OnStateChanged 发 ended;catch 分支发失败 ended |
| 2 | 加 `CombatReplayVideoRecorder` MonoBehaviour + `ReplayVideoCaptureSession` + `FfmpegRawVideoEncoder` |
| 3 | **ffmpeg path 硬编码**(mac: `/opt/homebrew/bin/ffmpeg`)、**输出路径硬编码**、**无 config 项** |
| 4 | 在 `Plugin.AttachRuntimeComponents` 挂载,`DetachRuntimeComponents` 销毁 |

**成功标准**：HistoryPanel 点击任意 local battle 回放,产出能在 VLC / QuickTime 正常播放的 MP4。

### Phase 2：稳定性

- 两级 FFmpeg fallback + 检测缓存 + `SystemInfo.supportsAsyncGPUReadback` 兜底
- 接入 8 个 config 项
- Path service 加 `CombatReplayVideoDirectoryPath` + `ToolsDirectoryPath`
- Bounded queue 满则 drop,记录 `_droppedFrames`
- RenderTexture `.Create()` / `.Release()` / `Destroy()` 闭环
- `ForceSpeed1x` 保存 entry speed,结束恢复
- 接入 `ScreenshotUiSuppressionScope`
- Frame sequence number(防 readback 乱序)
- 临时文件 `.recording.mp4` → 正常结束才 `File.Move` 到最终名

### Phase 3：产品化

- SQLite `combat_replay_videos` 表 + schema v11 → v12
- HistoryPanel 显示视频状态 + "Open Folder" 动作
- 失败状态 + error 落库
- installer 项目独立 PR：可选 FFmpeg 部署步骤

### Phase 4(可选)：音频

- Unity AudioListener PCM 捕获 → 临时 WAV → replay 结束后第二次 ffmpeg mux
- 跨平台音频抓取风险高,仅在 Phase 3 稳定后评估

## 文件结构

预计新增和修改：

| 文件 | 责任 | 状态 |
|---|---|---|
| `Game/CombatReplay/Video/CombatReplayVideoRecorder.cs` | MonoBehaviour,订阅 playback 事件,管理 session | 新增 |
| `Game/CombatReplay/Video/ReplayVideoCaptureSession.cs` | Capture loop + AsyncGPUReadback 调度 | 新增 |
| `Game/CombatReplay/Video/ReplayVideoCaptureRequest.cs` | Capture 配置快照(width/height/fps/crf/...) | 新增 |
| `Game/CombatReplay/Video/ReplayVideoCaptureResult.cs` | Capture 结果(frames/duration/status/error) | 新增 |
| `Game/CombatReplay/Video/FfmpegRawVideoEncoder.cs` | FFmpeg subprocess + stdin pipe + 后台 writer thread | 新增 |
| `Game/CombatReplay/Video/CombatReplayVideoMetadataStore.cs` | `combat_replay_videos` 表读写 | Phase 3 |
| `Game/CombatReplay/Video/ReplayVideoFrameTransforms.cs` | RGBA flip 等帧变换 | 新增 |
| `Game/CombatReplay/CombatReplayPlaybackStarting.cs` | 事件类 | 新增 |
| `Game/CombatReplay/CombatReplayPlaybackEnded.cs` | 事件类 | 新增 |
| `Plugin.cs` | 挂载 / 卸载 recorder | 修改 |
| `Core/Paths/IPathService.cs` + `BppPathService.cs` | 加 `CombatReplayVideoDirectoryPath` 等字段 | Phase 2 修改 |
| `Core/Config/IBppConfig.cs` + `BppConfig.cs` | 加 8 个 config 项 | Phase 2 修改 |
| `Game/CombatReplay/CombatReplayRuntime.cs` | 发 playback events | 修改 |
| `Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs` | `replayState.Replay()` 前发 starting | 修改 |
| `Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs` | schema v11 → v12 | Phase 3 修改 |

## 验证逻辑

### Phase 1 验证(hook 时机 + 基础链路)

| 检查项 | 方法 | 通过条件 |
|---|---|---|
| 事件时机正确 | BPP log 时间戳对比 replay 播放时间 | starting 早于第一可见帧;ended 在 ReplayState 退出瞬间 |
| 事件不重复 | 连续回放同一 battle 3 次 | 每次精确 1 个 starting + 1 个 ended |
| MP4 生成 | 检查输出目录 | 文件存在,大小 > 0 |
| MP4 可播 | VLC / QuickTime / `ffprobe` | 视频正常播放,时长接近 replay 实际播放时长 |
| 主线程耗时 | Unity Profiler | `CaptureScreenshotIntoRenderTexture` + readback callback 主线程单帧增量 < 2ms |
| AsyncGPUReadback 真异步 | Profiler 看发起到 callback 间隔 | 跨多帧返回而非同帧立即返回 |
| 失败不影响 replay | 故意改错 ffmpeg path | Replay 正常播放,BPP log 报错但无 throw |
| 中途退出 | 回放中按 ESC / 切场景 | ended 事件发布,session 正常 stop,部分文件可播 |

### Phase 2 验证(检测 + 稳定性)

| 检查项 | 方法 | 通过条件 |
|---|---|---|
| FFmpeg 缺失静默禁用 | 清除两级路径下的 ffmpeg | 无 throw,无弹窗,1 行 Info log |
| 标准位置优先 PATH | 两处都装不同版本 | 命中 `<GameRoot>/.../tools/ffmpeg/` |
| 检测结果缓存 | game lifecycle 计数 | 整个 session 只起 1 次 `ffmpeg -version` |
| Config 全生效 | 改 FPS=60 / preset=fast | `ffprobe` 报 60fps;size 变化合理 |
| Speed 恢复 | replay 前选 0.67x | replay 完速度档回到 0.67x |
| BPP overlay 抑制 | 视觉对比开/关 | 关掉时 dock + status bar 不在视频里 |
| Queue 满 drop | 人为塞慢编码(preset=placebo) | `_droppedFrames` 增加,无 OOM,主线程不 block |
| AsyncGPUReadback fallback | 实测 mac/win | 不支持时禁用,不静默退化到同步 |
| GPU 资源不泄露 | 连续 10 次回放 | GPU memory 不持续增长 |
| 跨平台 | Win + macOS 各跑一次 | 两个平台都生成 MP4 |

### Phase 3 验证(数据落地)

| 检查项 | 方法 | 通过条件 |
|---|---|---|
| SQLite migration | 用 v11 旧库启动 | 自动 bump v12,旧数据保留 |
| Metadata 完整 | 回放完查 SQLite | battle_id / duration / frames / status 全填 |
| 失败状态记录 | 杀 ffmpeg 进程 | status=FAILED,error 字段有内容 |
| 临时文件清理 | 失败/中断后看磁盘 | `.recording.mp4` 不残留 |
| HistoryPanel 状态 | 录制中/录制完查 | UI 与 SQLite 一致 |

### 跨 Phase 兜底

每次合并前必跑：

1. 按 `.rules` 要求做最小相关构建(`dotnet build BazaarPlusPlus.csproj -c Debug`);只有 packaging 改动才跑 `BuildAll`
2. 关闭 `CombatReplayVideoEnabled` 后整个 mod 行为与改动前完全一致
3. 故意删 `<GameRoot>/BazaarPlusPlusV4/tools/` 整个目录,启动游戏 → 不崩、不报错、其他功能正常

## 风险与回滚

| 风险 | 缓解 |
|---|---|
| AsyncGPUReadback 在某些图形 API 退化为同步 | `SystemInfo.supportsAsyncGPUReadback` 检测,false 即禁用 |
| FFmpeg pipe 阻塞 | Bounded queue + 后台线程写 stdin,main thread 只 enqueue |
| FFmpeg 子进程泄露 | `OnDestroy` 强制 stop;session supersede 时强制 stop;2 阶段 wait + kill |
| 帧乱序 | Phase 1 信任 FIFO;Phase 2 加 sequence number 兜底 |
| 磁盘暴涨 | 1080p 30fps CRF 23 ≈ 5-10MB/min;默认 `Enabled=false` 即足够;清理策略留以后 |

**回滚**：整个功能受 `CombatReplayVideoEnabled` 开关控制,默认 false。即使打开后崩了,关掉 config 即可禁用,不影响 replay 本身。Recorder 完全独立 component,删 `gameObject.AddComponent<CombatReplayVideoRecorder>()` 一行即可彻底移除。SQLite 新表无破坏性,回滚 schema 只需保留旧表不读。
