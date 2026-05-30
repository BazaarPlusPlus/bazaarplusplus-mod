# Combat Replay 音频录制：从 FMOD tap 到 WASAPI loopback 的排查与决策

- 日期：2026-05-30
- 状态：**IMPLEMENTED**（已落地并验证）。现行实现的「活真相」是 [combat-replay.md](../features/combat-replay.md) + 代码；本文记录**排查历程、踩过的坑与决策依据**。
- 影响仓库：`bazaarplusplus-mod`
- 取代：[2026-05-30-combat-replay-audio-and-capture-perf-design.md](2026-05-30-combat-replay-audio-and-capture-perf-design.md) 的**音频抓取决策**（该文选择「FMOD 主 channel group 直通 tap」，已被本文推翻——见下）。

---

## 0. TL;DR

视频录制录不到卡牌**打击 / 棋盘音效**（只有 BGM 和结算音乐）。根因：游戏用 **Google Resonance Audio** 对 3D 音效做空间化，**解码后的声床不出现在任何可 tap 的 FMOD 通道组上**（任何 Studio bus 都没挂 `Resonance Audio Listener` DSP，连 FMOD 核心 master 的输出也只有音乐）。因此**任何挂在 FMOD 通道组上的 DSP tap 都抓不到打击声**，而 2D 音乐正常走通道组、能被抓到。

解决：**改录「设备输出」(WASAPI loopback)**——录默认渲染端点 = 玩家听到什么就录什么，与 FMOD 内部路由无关，必然包含 Resonance 声床。抓取后端抽象为 `IReplayAudioCaptureTap` + 工厂选择（Windows = WASAPI，其它平台留占位，为 macOS 预留）。

最大的方法论教训：**FMOD 的诊断标志（`captured=True`、`playback=PLAYING`、channelGroup 树终于 `FMOD master`）全都"正确"，但都不代表 PCM 真的进了那个节点。唯一可信的是核对真正录出来的 WAV 内容。** 我们一度被这些标志误导，得出过"已经录到了"的错误结论。

---

## 1. 问题

最初症状（视频录制功能）：

1. ✅ 背景音乐、结算音乐能录到。
2. ❌ **卡牌打击、棋盘卡牌音效录不到**——这些声音在直接回放时听得到，但视频里没有。
3. ⚠️ 录制**开始时明显卡顿**（后查实是诊断代码导致）。
4. （后续）英雄语音也想要——最终**放弃**（见 §6.3）。

## 2. 背景：游戏音频架构

- 音频走 **FMOD Studio + FMOD Core**（经 `FMODUnity.RuntimeManager` / `FMOD.Studio`），**没有 Unity `AudioListener`/`AudioSource` 路径**，所以 Unity `OnAudioFilterRead` 抓不到。
- 运行时实测的 bus 拓扑（来自诊断日志）：

  | SoundManager 字段 | bus 路径 |
  |---|---|
  | `MasterBusPath` | `bus:/` |
  | `CombatBusPath` | `bus:/SFX/Combat` |
  | `BoardDiegeticBusPath` | `bus:/SFX/Board/Board_Diegetic` |
  | `BoardPresentationBusPath` | `bus:/SFX/Board/Board_Presentation` |
  | `MonsterNonVerbalBusPath` | `bus:/SFX/Monster_NonVerbal` |
  | `VOBusPath` | `bus:/VO/NonTutorial` |
  | Environment | `bus:/SFX/Environment/*` |

- 卡牌打击/棋盘音效是 **3D 定位**事件（`SFXPlayer.PlayOneShotSfx(eventRef, position)` / `PlayOneShotAttached` → `RuntimeManager.AttachInstanceToGameObject`）。

> 原始设计（[2026-05-30-combat-replay-audio-and-capture-perf-design.md](2026-05-30-combat-replay-audio-and-capture-perf-design.md)）据此选择「从 FMOD 主 channel group 挂直通 tap DSP 抓混音 PCM」，理由是「比 WASAPI loopback 干净、复用 FMOD 接入」。**这个前提对 2D 音频成立，对 Resonance 空间化的 3D 音频不成立**——这正是本次踩坑的根源。

## 3. 排查历程与踩过的坑

> 用了两轮多 agent workflow（`.claude/workflows/combat-replay-audio-capture-*.js`）做并行调查 + 对抗式验证，但**真正破局的是核对实际录出的 WAV/日志，而不是静态分析**。

### 坑 1 — "captured=True" 误判（最关键的方法论错误）
第一轮 workflow 看到运行日志里：两路 tap（`bus:/` + `bus:/SFX`）都 `captured=True`、~1.7M samples、rms ≈ −25 dB；打击事件 `playback=PLAYING virtual=False volume=1/1`；channelGroup 树终于 `… <- Master Bus <- FMOD master`——于是**误判"打击声其实已经录到了，问题只是 +6 dB 双计"**。
**错在哪**：这些都只证明"有信号流过 / 事件在播 / 逻辑上路由到 master"，**没有任何一项证明打击声的 PCM 真的进了被 tap 的那个缓冲**。`captured=True` 只是说 ring 写过样本（那是音乐 + 环境声）。

### 破局 — 核对真实 WAV 内容
用户提供地面真相（"就是没有打击声"），改用 `ffmpeg volumedetect` 量**各子总线 debug stem**：

| 直接 tap 的 bus | mean | 结论 |
|---|---|---|
| `bus:/`（master） | −24.6 dB | 有内容（音乐+环境） |
| `bus:/SFX` | −25.5 dB | 有内容（环境） |
| **`bus:/SFX/Combat`（打击）** | **−44.6 dB** | **近静音** |
| `bus:/SFX/Board/*` | **−91.0 dB** | **纯数字静音** |

打击事件触发了 167 次、可听见，却在它**自己的总线**上近乎静音。

### 坑 2 — 误判为 FMOD 对象输出（Atmos）
一度以为是 FMOD 原生 object spatialiser 在输出级渲染（绕过 master）。排除：`RuntimeManager` 初始化时 `setSoftwareFormat(rate, mode, 0)`，**numrawspeakers = 0 → FMOD 对象输出关闭**。所以不是 Atmos 对象路径。

### 坑 3 — 发现 Resonance，但 TAIL 读早了
游戏装了 **Google Resonance Audio**（`TheBazaar_Data/Plugins/x86_64/resonanceaudio.dll` + `FMODUnityResonance.dll`）。Resonance 是**环境声场**架构：每个源的 `Resonance Audio Source` DSP 把干声编码进一个全局声场（源自己的总线输出近乎静音），一个 `Resonance Audio Listener` DSP 把声场解码混回图里。
当前 tap 挂在 `bus:/` 的 **TAIL（输入端，在 Listener 解码之前）** → 只读到音乐。改挂**核心 master 的 HEAD**（解码之后）。

### 坑 4 — 连核心 master HEAD 都抓不到
实测核心 master HEAD 的 stem：rms −28 dB、仍只有音乐（比之前更安静，约等于音乐叠加了 master 音量衰减）。**声床连核心 master 都不经过。**

### 坑 5 — 扫描 Listener 的 bug + 决定性证据
改成「扫描所有 bank/bus，定位 `Resonance Audio Listener` DSP 所在的通道组，挂它的 HEAD」（镜像游戏自己的 `FmodResonanceAudio.Initialize`）。第一次**没找到**——因为我在 `lockChannelGroup() != OK` 时 `continue` 跳过了总线，而游戏代码不检查该返回值。修掉 bug + 加**全总线 DSP dump** 后：

> **40 条总线的 DSP 清单里，没有任何一条挂着 `Resonance Audio Listener` DSP**（全是 `ChanGroup Fader / FMOD 3-EQ / Compressor / Send / Return / Reverb / Loudness Meter` 等标准 DSP）。

至此实锤：**Resonance 解码后的声床根本不在 Studio bus 图里**，任何通道组 DSP tap 都不可能抓到。

## 4. 根因

> 3D 战斗/棋盘音效由 **Resonance Audio** 空间化；其解码声床经由一条**任何 FMOD 通道组都观察不到**的路径到达扬声器（无 `Resonance Audio Listener` DSP 在任何 Studio bus；核心 master 输出仅音乐）。`bus:/` TAIL、核心 master HEAD、各子总线——逐层验证后**全部抓不到**。2D 音乐没有 Resonance Source、正常走通道组，所以能被抓到——这就是"音乐能录、打击不能录"的根本不对称。

为什么排查这么久：**FMOD 的所有中间标志都"正确"却具误导性**（见坑 1）。能采集 = PCM 真的出现在录出来的 WAV 里，别的都不算数。

## 5. 解决方案：录设备输出（WASAPI loopback）

既然声床不在任何 FMOD 通道组、却确实到达扬声器，就**录扬声器（设备输出）本身**：

- **WASAPI loopback** 抓默认渲染端点的 PCM = 玩家听到什么就录什么，与 FMOD 内部路由无关 → 必然包含 Resonance 声床、音乐、结算、以及任何在播的语音。
- 只读、不改游戏发声；任何 COM/WASAPI 失败都优雅降级为静音视频。

实测验证（7.1 设备）：loopback stem rms −30 dB、**打击声可听见**；用户确认 WAV 里有打击声。采集问题解决。

## 6. 次要问题与修复

### 6.1 7.1 AAC 无法播放 → 下混立体声
loopback 按**设备混音格式**抓取，用户设备是 **7.1（8 声道）float 48 kHz**。ffmpeg 把它编成 **7.1 AAC**，而 Windows 播放器（媒体播放器/照片）**不支持 >2 声道 AAC**，报"编码设置不支持"。
修复：muxer 加 **`-ac 2 -ar 48000`**，下混成立体声 48 kHz AAC（通用可播）。本地用现有 7.1 WAV 重封装验证：输出 `aac, 48000 Hz, stereo`。

### 6.2 录制开始卡顿 → 删诊断
`e68217b` 加的诊断 Harmony 补丁 hook 了每一个 `SFXPlayer.*`，**每事件调 `RuntimeManager.StudioSystem.flushCommands()`**（与混音线程的同步屏障）+ 协程探针（167+ 事件 → 数百次屏障 + 分配）。实测 ~19.5% 丢帧。删除 `CombatReplayAudioEventDiagnosticPatch.cs` + `ReplayStateAudioDiagnosticPatch.cs` + `AudioBankWarmer` 的诊断转储。

### 6.3 英雄结束语音 → 放弃
诊断显示：回放中英雄语音内容（`HeroCardAudio`）其实**已加载**；缺的是**触发**——开场 `OnPvPIntro` / 结束 `OnPvPVictoryDefeat` 由 **UI 过渡事件**（`Events.PvpTransitionBegan` 等，见 `SoundEventListener`）驱动，而存档回放只跑战斗模拟、不发这些 UI 事件。
尝试过：解除 `VOPlayer.PlayVO` 的 `IsReplayActive` 屏蔽（作用域补丁）+ postfix `OnCombatantDied` 在战斗结束补发英雄胜/负语音。但实测仍未发声（疑似 hook 对应的语音事件缺失或被播放概率卡住）。**用户决定放弃英雄语音**，相关补丁已移除。

## 7. 最终架构：跨平台采集 adapter

```
IReplayAudioCaptureTap                  采集契约（TryStart / Stop / CapturedAnySamples / Rms…）
  ├─ WasapiLoopbackCaptureTap           Windows：WASAPI loopback（录默认输出设备）
  └─ UnsupportedPlatformAudioCapture    其它平台占位：TryStart 返回 false → 静音视频
ReplayAudioCaptureFactory.Create(wav)   按 OS 选择后端
```

- 录制器 `CombatReplayVideoRecorder.StartAudioTaps` → 走工厂、单 stem。
- muxer 单 WAV 路径 + `-ac 2 -ar 48000` 立体声下混。
- **macOS 接入点**：在 `ReplayAudioCaptureFactory` 里加 `if (IsOSPlatform(OSX)) return new MacLoopbackAudioCapture(...)`，实现一个 `IReplayAudioCaptureTap`（CoreAudio 聚合/环回设备 或 ScreenCaptureKit 音频）。录制 / 封装管线**完全不用动**。

涉及代码：
- `Game/CombatReplay/Audio/IReplayAudioCaptureTap.cs`、`WasapiLoopbackCaptureTap.cs`、`ReplayAudioCaptureFactory.cs`
- `Game/CombatReplay/Video/CombatReplayVideoRecorder.cs`、`ReplayVideoAudioMuxer.cs`、`ReplayVideoAudioTapPlan.cs`
- 删除：`Audio/FmodAudioCaptureTap.cs`（被取代）、两个诊断补丁

## 8. 经验教训

1. **核对最终产物，别信中间标志。** `captured=True` / `PLAYING` / channelGroup 树都可以全"对"而音频仍不在文件里。对音频问题，`ffmpeg volumedetect` 量实际 stem 是地面真相。
2. **空间化音频（Resonance / 类似全局声场插件）无法从通道组采集。** 解码声床不在 bus 图里 → 通道组 DSP tap（无论 TAIL/HEAD/哪条总线）都抓不到。设备级 loopback 是通用解。
3. **对抗式验证 + 找真实运行日志**很值：它推翻了第一轮的错误结论，并最终用全总线 DSP dump 一锤定音。
4. **loopback 按设备格式抓**——多声道 / 非常规采样率要在封装时显式下混到 stereo/48k，否则播放器不认。

## 9. 验证 / 后续

- **已验证**：录设备输出后打击声进入 WAV；下混后 MP4 立体声 AAC、通用可播；删诊断后开始卡顿缓解。`tests/CombatReplayAudioVideo.Tests` 绿。
- **未实现 / 后续**：
  - **macOS 采集后端**（CoreAudio / ScreenCaptureKit）——adapter 已就位，按 §7 接入。
  - **音画微同步**：loopback 自由跑设备时钟、视频走墙钟 CFR，恒定缓冲偏移；若听感漂移可在 mux 加一次性 `-itsoffset`（目前未见需要）。
  - **loopback 录进系统其它声**：默认端点 loopback 会录到同设备上其它程序的声；后续可换 **进程级 loopback**（`AUDCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`）只录游戏 PID。
  - 英雄语音：已放弃，如将来重启需解决"回放里补发触发"。
