---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Combat Replay 录制：消除卡顿 + 加入游戏音频

- 日期：2026-05-30
- 状态：SUPERSEDED（audio）/ IMPLEMENTED（video）。音频侧的 FMOD tap 决策已被 WASAPI/CoreAudio 输出层采集取代；视频侧的帧缓冲池、墙钟 CFR 与异步 mux 仍是现行实现历史。
- 影响仓库：`bazaarplusplus-mod`

> **更新（2026-05-30）**：本文的**音频抓取决策**（决策 2：从 FMOD 主 channel group 挂直通 tap DSP）**已被取代**——FMOD tap 抓不到 Resonance Audio 空间化的 3D 战斗音效，已改为 **WASAPI loopback 录设备输出**。详见 [2026-05-30-combat-replay-audio-loopback-capture.md](2026-05-30-combat-replay-audio-loopback-capture.md)。本文的视频侧决策（帧缓冲池消卡顿、墙钟 CFR、异步 mux）不受影响，仍是现行实现。

## 背景与动机

Combat Replay 的 MP4 录制在 [2026-05-30-combat-replay-record-button-design.md](2026-05-30-combat-replay-record-button-design.md) 落地为「HistoryPanel 按钮单次触发」后，实测暴露并修复了三个潜伏 bug（录制器从未订阅事件总线；bootstrap 存档回放退出不发 `PublishEnded` 致 ffmpeg 不收尾、文件无 moov atom；抓帧未按图形 API 方向处理致上下颠倒）。**这三处修复是本设计的前置依赖，目前在工作树、待提交。**

录制现已可用，但剩两个体验问题：

1. **录制时游戏掉帧**：[`ReplayVideoCaptureSession`](../../../../src/BazaarPlusPlus/Game/CombatReplay/Video/ReplayVideoCaptureSession.cs) 的 `OnReadbackComplete` 每帧 `new byte[width*height*4]`（2560×1440 ≈ 14MB），30fps 即 ~420MB/s 分配，GC 尖峰拖累主线程。
2. **没有声音**：游戏音频走 FMOD（`FMODUnity.RuntimeManager` / `FMOD.Studio`，见 [`AudioBankWarmer.cs`](../../../../src/BazaarPlusPlus/Game/CombatReplay/Warmup/AudioBankWarmer.cs)），**无 Unity `AudioListener`/`AudioSource` 路径**，故 Unity `OnAudioFilterRead` 抓不到声音。

目标：**保留原生分辨率**消除录制卡顿，并把**游戏音频**合进 MP4，且**音画同步**。

## 决策

1. **卡顿：先测量确认主因，再以帧缓冲池（S1）为主修，保留原生 2K。** 实现第一步先 profile 录制时的 GC / 主线程开销，确认 14MB/帧分配是否为主因（高置信假设）。S1 复用固定大小缓冲消除该分配。若消除 GC 后 `ScreenCapture.CaptureScreenshotIntoRenderTexture` + 每帧 `CopyTo` 的主线程残余仍致顿，再上 command-buffer Blit 复用已渲染帧（S3）。S3 不进第一版，除非实测需要。
2. **音频：只录游戏声，从 FMOD 主 channel group 挂「直通 tap」DSP 抓混音 PCM。** 相比 WASAPI loopback（抓系统全部声、混入 Discord/通知、需 P/Invoke），FMOD 路径输出最干净并复用既有 FMOD 接入。DSP 必须把输入**原样转发到输出**（只旁路一份给我们），绝不影响游戏正常发声。
3. **音视频同步：把视频时间轴做成墙钟对齐的 CFR。** 喂给 ffmpeg 的帧按 `1/fps` 槽位节奏输出「当前最新帧」，缺帧补上一帧、超额丢弃，使**视频时长 == 真实录制时长**，从而与实时音频天然对齐。两段式合流：音频 PCM 落临时 WAV，回放结束再 mux。
   - 取舍：相比「按起点对齐 + `-shortest`」（丢帧即漂移），墙钟 CFR 把漂移从根上消掉；代价是改动 session 内部喂帧节奏（对 ffmpeg 的 CFR 契约不变）。备选 VFR（逐帧真实 PTS）更精确但管线改动更大，列为后备。
4. **第二趟 mux 异步执行。** 回放结束（`OnPlaybackEnded`，主线程）做视频会话收尾（沿用现有 `WaitForCompletion`，同步关 ffmpeg / 写 moov）+ 关闭 WAV，随后**派发后台 mux 任务**即返回；mux 完成才产出最终 `.mp4` 并写元数据。把对最终件的 remux（秒级）移出主线程，不在「回放→菜单」切换时额外叠加冻结。（第一趟视频收尾的同步开销是既有行为，本设计不扩大处理。）
5. **不新增 config。** 音频随录制默认开启、码率固定（AAC 192 kbps）。与上一版去除 `Enabled`/`ForceSpeed1x`/`SuppressBppOverlays` 的精简方向一致。
6. **音频可降级，绝不毁视频。** FMOD DSP 挂载失败 / 抓不到样本 / mux 失败 → 回退到无声视频（保留第一趟产物）+ 记 Warn。

## 详细设计

### 工作流 1：录制性能与时间轴（纯视频侧，可独立交付）

**S1 帧缓冲池**
- 会话内维护固定大小（`width*height*4`）`byte[]` 池：`OnReadbackComplete` 借出装帧 → 喂帧逻辑用完归还。消除每帧 14MB 分配。
- 线程安全：主线程借出 vs 编码线程归还需并发安全（并发集合或锁）。分辨率变更时重建池。

**墙钟 CFR 喂帧**
- 解耦「抓帧」与「喂帧」：抓帧（`ScreenCapture` + `AsyncGPUReadback`）更新一个「最新帧」槽；一个按 `1/fps` 推进的喂帧节拍把最新帧送入编码队列，缺帧则重复上一帧、超额则丢弃。
- 结果：编码器收到恒定 `fps` 帧/秒，视频时长 == 真实时长。`dropped_frames` 语义保留（抓帧侧丢）。
- 注意：重复帧会让同一缓冲被引用多次——「最新帧」缓冲在被替换前不回收，避免与池冲突。

> 这条工作流不依赖音频，可单独验证（录一段、确认时长 == 真实时长、录制时帧率改善）。

### 工作流 2：音频抓取与合流

**`FmodAudioCaptureTap`（抓音）**
- `RuntimeManager.CoreSystem.getMasterChannelGroup(out var mcg)`；`coreSystem.createDSP(ref desc, out dsp)`，`desc.read` = 静态 `DSP_READ_CALLBACK`（直通：`inbuffer → outbuffer` 原样拷贝，同时旁路一份）；`mcg.addDSP(CHANNELCONTROL_DSP_INDEX.TAIL, dsp)`。
- 回调在 **FMOD 混音线程**触发：把交错 float PCM 推入**无锁环形缓冲**（生产者侧 wait-free）。回调委托用 `GCHandle` 保活、静态方法，规避 GC 回收与 marshaling。
- 格式取 `coreSystem.getSoftwareFormat(...)`（采样率/声道）；后台写线程把环形缓冲落临时 WAV（float PCM，格式交给 mux 阶段转码，无需手动重采样）。

**两段式合流（异步）**
- 视频出 `<...>.recording.mp4`（无声）；音频出 `<...>.audio.wav`。
- `OnPlaybackEnded`（主线程）：收尾视频会话 + 关闭 WAV → 派发后台 mux 任务后立即返回（菜单切换不卡）。
- 后台任务：`ffmpeg -i <video> -i <audio.wav> -c:v copy -c:a aac -b:a 192k -shortest <final>.mp4`；成功 → 删两个临时件 + `SaveFinish` 写最终路径；失败 / 无 WAV → 把无声视频提升为最终件（沿用 `FinalizeOutputFile`）+ `SaveFinish` + Warn。
- mux 进程复用 [`FfmpegLocator`](../../../../src/BazaarPlusPlus/Game/CombatReplay/Video/FfmpegLocator.cs) 的 ffmpeg。需校验该 ffmpeg 含 `aac` 编码器（原生 AAC 基本都有；缺则回退无声）。

### 临时文件生命周期

| 路径 | 无声视频 temp | 音频 WAV | 最终 .mp4 |
|---|---|---|---|
| 正常（有音、mux 成功） | 删 | 删 | 重命名/产出 |
| mux 失败 / 无 ffmpeg-aac | 提升为最终 | 删 | = 无声视频 |
| 无音频（tap 失败） | 提升为最终 | 不存在 | = 无声视频 |
| Abort / OnDisable | 删 | 删 | 不产出 |
| supersede（新录制） | 各自带时间戳，互不冲突；旧 mux 任务独立完成 | 同左 | 同左 |

- DSP 在 finalize / abort / disable / 场景切换都要可靠 `removeDSP` + `release`，绝不残留在 master 总线。
- 后台 mux 任务需被追踪，应用退出时尽量收尾；未尽则留临时件由下次启动清理（best-effort）。

### 集成点

- 音频 tap 与视频会话**并列**，挂在 [`CombatReplayVideoRecorder`](../../../../src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs) 的 `BeginRecording` / `OnPlaybackEnded` / `AbortActiveSession` 生命周期上。
- `SaveStart`（RECORDING 行）仍在 `BeginRecording`；`SaveFinish` 改在 mux 任务完成时（异步，略晚）。

## 非目标（Out of Scope）

- 不做 WASAPI / 系统级音频；不做单进程双输入实时混。
- 不降录制分辨率；不改 ffmpeg 的 CFR 编码契约（仅改 session 内部喂帧节奏）。
- VFR（逐帧真实 PTS）不进第一版，墙钟 CFR 够用则不做。
- S3（command-buffer Blit）不进第一版，除非 S1 后实测仍致顿。
- 不新增任何音频相关 config。

## 验证

- `dotnet build BazaarPlusPlus.csproj`（targeted）。
- 工作流 1：录一段已知时长的回放 → 确认输出视频时长 ≈ 真实时长（验证墙钟 CFR）；对比 S1 前后录制时帧率（验证 GC 顿消除）；2K 下 profile 主线程开销以决定是否需要 S3。
- 工作流 2：`ffprobe` 确认含 AAC 音轨；录一段有明确音画对应的片段（如技能音效对上动画）确认**全程不漂移**；确认录制结束后游戏声正常、无残留 DSP；模拟 mux 失败确认回退到无声视频。
- 纯逻辑可单测的部分（环形缓冲、WAV 头、墙钟节拍的补帧/丢帧计数）抽出补最小单测；其余按项目规则不为覆盖率造测试。

## 风险 / 待确认

- **FMOD 原生 DSP 回调**（最高风险）：直通转发正确性（别掐声）、混音线程线程安全、委托保活、Mono marshaling。
- **墙钟 CFR 喂帧改动**：触及 session 核心节奏（与现有 `_orderingBuffer` 乱序重排、池/队列协同），需保证补帧/丢帧计数正确；若过于侵入，退回「起点对齐 + `-shortest`」并接受丢帧时的轻微漂移。
- **固定音频延迟**：FMOD 输出缓冲可能带来一个固定音画偏移（非漂移）；若手测可感知，mux 时用 ffmpeg `-itsoffset` 补一个常量偏移。
- **异步 mux 生命周期**：后台 Process + 跨线程写 SQLite 元数据 + 应用退出收尾，是新的失败面；若复杂度不划算，退回「同步 mux + 有界超时」（接受短暂冻结）。
- **S1 是否足够**：保 2K 时 `ScreenCapture` + `CopyTo` 残余可能仍需 S3——实现期先测量。
- **ffmpeg aac 可用性**：随包 ffmpeg 若无 aac 编码器则回退无声（启动期或首次 mux 前探测）。
