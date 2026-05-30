# Combat Replay 音频录制：macOS CoreAudio 进程级 tap 设计

- 日期：2026-05-31
- 状态：**IMPLEMENTED（2026-05-31 落地）/ 设计定稿，直接实现（不设前置探针门）**。两处 Mono 运行时假设已被 §4 架构从根上规避，列在 §10 作为落地后按症状回查的点，而非阻塞项。
- 落地说明：代码见 `native/mac-audio-tap/`（`BppMacAudio.{m,h}` + `build.sh`，产物 `libBppMacAudio.dylib`）、`Game/CombatReplay/Audio/CoreAudioProcessTapCaptureTap.cs`（纯 pull 镜像 WASAPI）、`ReplayAudioCaptureFactory.cs`（macOS 分支 + `IsSupported` 门 + `DllNotFoundException` 静默降级）、`BazaarPlusPlus.csproj`（仿 `libe_sqlite3.dylib` 打包）。两处与本文字面不同的实现决策：**①** 产物名取 `libBppMacAudio.dylib`（lib 前缀，匹配 `libe_sqlite3.dylib` 的 Mono `lib{name}.dylib` 解析；C# 侧 `[DllImport("BppMacAudio")]` 不变）——§8 写的 `BppMacAudio.dylib` 为简写；**②** 不在 Release target 另加 `<Copy>`（sqlite 同样没有，提交进 `SourceForBuild/macos/.../plugins/` 的 dylib 由 `ZipDirectory` 自动打包）——§8「Release installer target 各并排加一条」与真实 sqlite 机制不符，故未照做。已验证：dylib 零警告编译、四符号导出、`IsSupported()→1`、`Start/Stop` 全生命周期在 macOS 26 跑通（`rate=48000 ch=2`；静默进程读到 0 帧属预期）、`dotnet build -c Debug` 绿且 dylib 经新 `<Copy>` 落进游戏 `BepInEx/plugins/`。剩余 §10「采集正确性」（in-game `ffmpeg volumedetect` + 听感）需游戏运行时验收；§12 的 archive 迁移 / 并入 [../features/combat-replay.md](../features/combat-replay.md) 留作后续。
- 影响仓库：`bazaarplusplus-mod`
- 承接：[2026-05-30-combat-replay-audio-loopback-capture.md](2026-05-30-combat-replay-audio-loopback-capture.md)（Windows WASAPI loopback 已落地，其 §7/§9 把「macOS 采集后端」列为后续；本文是该后续的完整设计）。活真相见 [../features/combat-replay.md](../features/combat-replay.md) + 代码。

---

## 0. TL;DR

Windows 用 WASAPI loopback 录设备输出，绕开 FMOD/Resonance 的内部路由（[前文](2026-05-30-combat-replay-audio-loopback-capture.md)）。macOS 的等价物是 **CoreAudio 进程级 tap**（`AudioHardwareCreateProcessTap`，macOS 14.2+）：tap 游戏进程自己的输出 PCM，处在 FMOD/Resonance 所有内部混音的**下游**，必然包含空间化声床。比 loopback 还**更优**——按进程采集、设备无关、不录别的 app 的声。

两条核心设计决策:

1. **把全部 CoreAudio 交互收进一个薄 `BppMacAudio.dylib`,C# 侧退化成与 `WasapiLoopbackCaptureTap` 相同的 pull 循环。** CoreAudio 的 IOProc 是**实时线程 push**;让它直接回调进 C# 委托 = 把 RT 线程拖进 Mono(GC 挂起 / 线程 attach / 首调 JIT → 丢帧)。WASAPI 之所以稳,正因为它是 mod 自有线程的 pull、没有外部 RT 线程进托管。把 IOProc + 无锁 FIFO + planar→interleave 全放原生,C# 只 `Read` 拉数据,才是真·镜像 WASAPI。
2. **版本门用 dylib 内 `NSProcessInfo` 判定(≥ macOS 15),不用 `Environment.OSVersion`。**

> **为什么能"一把梭"直接做、不用先探针**:原本要探的两处 Mono 运行时假设(`Environment.OSVersion` 是否返回 Darwin 号、`[DllImport("CoreAudio")]` 能否解析),已被上面两条决策**从架构上规避**——C# 既不读 `OSVersion`、也从不直接 P/Invoke 系统 framework。所以它们不再是阻塞项,只在 §10 留作"出症状再回查"。

---

## 1. 背景与约束

- 为什么不能挂 FMOD tap：游戏用 **Google Resonance Audio** 做 3D 空间化，解码声床不出现在任何可 tap 的 FMOD 通道组上（完整排查见[前文](2026-05-30-combat-replay-audio-loopback-capture.md) §3–4）。必须在**系统/进程输出层**截取。
- Windows 已用 WASAPI loopback 解决；前文 §9 已预见 macOS 对应物，并指出「进程级 loopback 只录游戏 PID」是更干净的方向。CoreAudio 进程 tap 天然就是进程级。
- 约束:mod 跑在 **BepInEx 5 = Unity Mono** 里;只需支持 **arm64**(Apple Silicon,macOS 11+)。

## 2. API 确认

```c
// AudioHardwareTapping.h（macOS 14.2+，导出的 C 符号）
OSStatus AudioHardwareCreateProcessTap(CATapDescription* desc, AudioObjectID* outTapID);
OSStatus AudioHardwareDestroyProcessTap(AudioObjectID tapID);
```

`CATapDescription` 是 ObjC 类（`initStereoMixdownOfProcesses:` / `muteBehavior` / `privateTap`），整个 header 被 `#ifdef __OBJC__` 包裹——**这是必须要有原生 wrapper 的唯一硬理由**：C# 构造不出这个 ObjC 对象。读取 tap 流需要把 tap 包进一个 **aggregate device**，在其上装 IOProc（标准做法，见 Apple "Capturing system audio with Core Audio taps" 示例）。

## 3. 权限

self-tap（tap 自己进程，BepInEx 已在游戏进程内，`getpid()` = 游戏 PID）是最无权限的情形:

| 检查项 | 预期 | 备注 |
|---|---|---|
| TCC 麦克风 / 屏幕录制 | 无 | tap 的是输出 PCM，不走麦克风、非 ScreenCaptureKit |
| Entitlement / Hardened Runtime | 无 | API header 无权限注解 |
| `muteBehavior` | `CATapUnmuted` | 游戏照常发声，tap 只读 |

> ⚠️ "无权限"按 self-tap 推断成立,但 Apple 对音频采集持续收紧——见 §10,落地后在干净机器实测一次即可。门定 15(§7)后,早期 14.x tap 的稳定性顾虑自动消解。

## 4. 架构：把 CoreAudio 整个收进 dylib

```
BppMacAudio.dylib                         承担全部 CoreAudio 交互
  ├─ IsSupported   NSProcessInfo ≥ 15.0
  ├─ Start         translate getpid()→AudioObjectID → CATapDescription
  │                → AudioHardwareCreateProcessTap → AudioHardwareCreateAggregateDevice
  │                → 读 kAudioTapPropertyFormat（ASBD）→ 装 IOProc → AudioDeviceStart
  ├─ IOProc        【原生 RT 线程】planar→interleave → 无锁 SPSC FIFO（wait-free push）
  ├─ Read          从 FIFO 抽走交错 float（C# 线程调，wait-free）
  └─ Stop          AudioDeviceStop → DestroyIOProcID → DestroyAggregateDevice → DestroyProcessTap

CoreAudioProcessTapCaptureTap.cs          只 P/Invoke BppMacAudio，无任何 CoreAudio 类型
  ├─ TryStart   BppMacAudio_Start → 拿 rate/channels → 起后台 pull 线程
  ├─ CaptureLoop  BppMacAudio_Read → WavStreamWriter.WriteSamples + AccumulateStats
  └─ Stop       BppMacAudio_Stop + WAV.Dispose
```

**为什么切分线在这里（核心决策）：**

1. **WASAPI 端没有 ring buffer，是纯 pull。** `WasapiLoopbackCaptureTap.CaptureLoop` 在 mod 自有后台线程上 `GetBuffer`→直接写 WAV→`Sleep(8)`。代码库里的 `AudioRingBuffer` **当前只被一个测试**（`tests/CombatReplayAudioVideo.Tests`）引用，是上一版废弃的 FMOD-DSP tap 留下的孤儿——别被它误导成"Windows 用了 ring buffer"。
2. **WASAPI 稳，是因为没有外部 RT 线程进 Mono。** CoreAudio IOProc 相反——它是 CoreAudio 实时线程 push 进回调。若回调是 C# 委托（reverse P/Invoke），等于把硬截止时间的 RT 线程拖进 Mono：首调 JIT、线程被 attach、attach 后参与 GC（并发 GC 可挂起它）→ 采集线程 stall → WAV 丢帧。问题**不在那次拷贝是否 wait-free，而在「跨进 Mono」本身**。
3. 因此把 IOProc + FIFO + planar 全放原生，C# 退回 pull——既消除 RT→Mono，又让 C# 不必直接 P/Invoke `CoreAudio` 系统 framework（C# 只按名字解析 `BppMacAudio`、和 `libe_sqlite3.dylib` 一样；CoreAudio 在 dylib 编译期就 `-framework CoreAudio` 链好了）。这一步同时规避了 §10 的两处 Mono 假设。

## 5. 原生 wrapper：C ABI（`native/mac-audio-tap/BppMacAudio.m`，~180 行）

```c
// 任意 macOS 可调，只碰 NSProcessInfo，不触 tap 符号。1=支持(≥15)。
int32_t BppMacAudio_IsSupported(void);

// self-tap 当前进程（内部 getpid()→AudioObjectID）。失败返回 NULL。
// 成功填回 mixdown 后实际格式（通常 48000 / 2）。
void*   BppMacAudio_Start(int32_t* outSampleRate, int32_t* outChannels);

// 从 FIFO 抽至多 maxFloats 个交错 float；返回实际拷贝数（0=暂无）。非阻塞、wait-free。
int32_t BppMacAudio_Read(void* handle, float* dst, int32_t maxFloats);

// 顺序：AudioDeviceStop → DestroyIOProcID → DestroyAggregateDevice → DestroyProcessTap → free。幂等。
void    BppMacAudio_Stop(void* handle);
```

要点:

- **句柄式**(返回 `void*` ctx),与 WASAPI 每实例独立一致;不要用全局单例。
- **aggregate device 用文档化的 `AudioHardwareCreateAggregateDevice(CFDictionaryRef, AudioObjectID*)`**,不要在 system object 上设 `kAudioPlugInCreateAggregateDevice`(那是 plugin 对象的属性)。device 描述里 `kAudioAggregateDeviceTapListKey` 挂 sub-tap(`kAudioSubTapUIDKey` = 读 `kAudioTapPropertyUID` 得到的字符串),`...IsPrivateKey` / `...TapAutoStartKey` 按需。
- **IOProc 必须处理 planar。** CoreAudio canonical 多是**非交错** float（`mNumberBuffers == 声道数`，每 buffer 一个声道平面）。直接当单块交错喂 = "LLLL…RRRR…" 垃圾音。就地交错后再 push：

```c
// 非交错：mNumberBuffers==ch，每 plane frames 个 float；交错则单 buffer 顺序 push
UInt32 frames = io->mBuffers[0].mDataByteSize / sizeof(float);
for (UInt32 f = 0; f < frames; f++)
    for (UInt32 c = 0; c < ch; c++)
        fifo_push(ctx->fifo, ((const float*)io->mBuffers[c].mData)[f]);
```

- **FIFO** = `AudioRingBuffer` 的 C 版(单生产者 IOProc、单消费者 C# 线程,无锁、无 malloc,~40 行)。

## 6. C# 实现（`Game/CombatReplay/Audio/CoreAudioProcessTapCaptureTap.cs`，~120 行）

```csharp
[DllImport("BppMacAudio")] static extern int    BppMacAudio_IsSupported();
[DllImport("BppMacAudio")] static extern IntPtr BppMacAudio_Start(out int rate, out int ch);
[DllImport("BppMacAudio")] static extern int    BppMacAudio_Read(IntPtr h, float[] dst, int max);
[DllImport("BppMacAudio")] static extern void   BppMacAudio_Stop(IntPtr h);

public bool TryStart() {
    _handle = BppMacAudio_Start(out var rate, out var ch);
    if (_handle == IntPtr.Zero) { BppLog.Warn(Component, "CoreAudio tap start failed."); return false; }
    _wav = new WavStreamWriter(_wavFilePath, rate, ch);  // 已是 32-bit float WAV，无需任何格式转换
    _running = true;
    _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "BPP.CombatReplayAudio.CoreAudioTap" };
    _thread.Start();
    IsCapturing = true; return true;
}

private void CaptureLoop() {
    var scratch = new float[16384];
    while (_running) {
        int n = BppMacAudio_Read(_handle, scratch, scratch.Length);  // float[] 直接 pin 传指针，无拷贝
        if (n > 0) { _wav!.WriteSamples(scratch, 0, n); AccumulateStats(scratch, n); Interlocked.Add(ref _totalFloats, n); }
        else Thread.Sleep(8);
    }
    // 收尾：_running=false 后 drain 一次 FIFO，别丢最后 ~8ms
}
```

- 结构与 `WasapiLoopbackCaptureTap.CaptureLoop` 一模一样——真·镜像。
- **格式更省**：`WavStreamWriter` 本就写 32-bit IEEE float；tap 原生就是 float32 → macOS 路径**不需要 `ConvertToFloat`**（Windows 那边还要处理 int16/int32）。
- **诊断字段照抄 WASAPI**:`AccumulateStats` / `RmsAmplitude` / `PeakAmplitude` / `CapturedSampleFloats`——`CombatReplayVideoRecorder.StopAudioTaps` 的日志**和 mux 判定**(`CapturedAnySamples` 决定 WAV 进不进封装)全靠它们,别丢。

## 7. 工厂与版本门

```csharp
// ReplayAudioCaptureFactory.Create
if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && IsSupported())
    return new CoreAudioProcessTapCaptureTap(wavFilePath);
return new UnsupportedPlatformAudioCapture(wavFilePath);

private static bool IsSupported() {
    try { return BppMacAudio_IsSupported() != 0; }   // ≥15 由 dylib 内 NSProcessInfo 判定
    catch (DllNotFoundException) { return false; }    // 极旧系统连 dylib 都 load 不了 → 静音降级
}
```

- **门定 15**:策略上更干净,彻底不纠结 14.2/14.3/14.4 哪个点版本可靠,Sequoia 上 tap 已成熟。
- **不用 `Environment.OSVersion`**:它在 Unity Mono 下可能返回 **Darwin 内核号**(macOS 15 = Darwin 24),`major >= 15` 会在 Darwin 空间误判。`NSProcessInfo.isOperatingSystemAtLeastVersion:{15,0,0}` 读真·产品版本,免疫该怪癖,也免疫旧 SDK 二进制读 `kern.osproductversion` 的版本封顶。

## 8. 构建与打包

```bash
# native/mac-audio-tap/build.sh
clang -arch arm64 -fobjc-arc \
  -framework CoreAudio -framework Foundation \
  -mmacosx-version-min=11.0 \          # 不是 14.2：压到 arm64 起点，让 dylib 在任何 Apple Silicon macOS 都能 load 来回答 IsSupported
  -dynamiclib -o BppMacAudio.dylib BppMacAudio.m
```

- `-mmacosx-version-min=11.0` 让 14.2 引入的 tap 符号被**弱链接**(靠 header `API_AVAILABLE` 注解):dylib 在 13/14 也能加载、`IsSupported` 干净返回 false;tap 符号只在 §7 门通过后才被 `Start` 调用(弱符号 NULL 直调会崩,门保证不发生)。
- 集成同 `libe_sqlite3.dylib`:预编译放进 `bazaarplusplus-installer/src-tauri/resources/SourceForBuild/macos/BepInEx/plugins/`,在 `BazaarPlusPlus.csproj` 的 Debug copy + Release installer target 各并排加一条 `<Copy>`(与 `libe_sqlite3.dylib` 那条并列,见 csproj:218)。签名/加载行为直接继承 sqlite(clang arm64 默认 ad-hoc 签名)。

## 9. 降级行为

| macOS | 行为 |
|---|---|
| 15.0+ | `CoreAudioProcessTapCaptureTap`，有声视频 |
| < 15（含 13/14） | `UnsupportedPlatformAudioCapture`，静音视频 + Warn |

录制 / 封装管线**完全不用动**——已核对:`CombatReplayVideoRecorder.TryStartAudioTap` 对 `TryStart()` 返回 false 是 dispose+删 WAV;`StopAudioTaps` 只把 `CapturedAnySamples` 为真的 WAV 送 mux。新 tap 只要诚实实现接口契约,recorder 一行不改。

## 10. 假设与待验证点（直接实现，按症状回查）

本方案**直接实现,不设前置探针门**——因为原本要探的两处 Mono 运行时假设,已被 §4 架构从根上规避,不构成阻塞:

| 原假设 | 为何不再阻塞 | 万一出症状 → 回查 |
|---|---|---|
| `Environment.OSVersion` 在 Mono 下可能是 Darwin 号 | 版本门走 dylib 内 `NSProcessInfo`（§7），C# 根本不读 `OSVersion` | macOS 15 用户拿到静音视频 → 查 `IsSupported`/`NSProcessInfo` 是否返回 true；pre-15 不是静音降级而是崩 → 查弱链接 + `DllNotFound` catch |
| `[DllImport("CoreAudio")]` 在 Mono 下可能解析不了 | C# 只 P/Invoke `BppMacAudio`（§4/§6），从不直碰系统 framework；CoreAudio 在 dylib 编译期链好 | `DllNotFoundException: BppMacAudio` → 查 dylib 是否在 `BepInEx/plugins/`、签名是否同 `libe_sqlite3.dylib` |

落地后真正要验的是**采集正确性**（按[前文](2026-05-30-combat-replay-audio-loopback-capture.md) §8 同款方法——核对产物，别信中间标志）：

1. **打击声 PCM 真进 WAV 吗** → `ffmpeg volumedetect` 量录出的 stem（这是整件事成败的根本；self-tap 在 FMOD/Resonance 下游，理论上必然抓到，但必须实测）。
2. **planar 交错对不对** → 听感声道不串、不变速、采样率正常。
3. **dylib 在游戏 Mono 里 load + 签名 OK 吗** → 与 `libe_sqlite3.dylib` 同路径，若它能加载则同理。
4. **RT/IOProc 有没有丢帧** → WAV 无断续、无周期性爆音。
5. **权限**（§3）→ 干净 TCC 机器上确认 self-tap 不弹任何授权框。

## 11. 工作量估算

| 部分 | 行数 | 说明 |
|---|---|---|
| `BppMacAudio.m` + `.h` | ~180 | tap+aggregate 创建/销毁 + IOProc + C 版 FIFO + planar 交错 + IsSupported |
| `CoreAudioProcessTapCaptureTap.cs` | ~120 | 纯 pull 循环,只 P/Invoke BppMacAudio;无 reverse-P/Invoke / GCHandle / 裸 CoreAudio 绑定 |
| `ReplayAudioCaptureFactory.cs` 改动 | ~12 | 加 macOS 分支,走 `IsSupported` |
| csproj 打包 + `build.sh` | ~25 | 复用 libe_sqlite3 模式 |

## 12. 关联

- 代码（将新增/改动）：`Game/CombatReplay/Audio/CoreAudioProcessTapCaptureTap.cs`（新）、`ReplayAudioCaptureFactory.cs`（加分支）、`native/mac-audio-tap/`（新）、`BazaarPlusPlus.csproj`（打包）。
- 既有契约（不变）:`IReplayAudioCaptureTap`、`WavStreamWriter`(float32)、`CombatReplayVideoRecorder`、`ReplayVideoAudioMuxer`。
- 文档：本文落地后移入 `design/archive/` 并加 IMPLEMENTED 横幅；活真相并入 [../features/combat-replay.md](../features/combat-replay.md)。

## 13. 验证 / 后续

- 落地后**按前文同款方法验收**:`ffmpeg volumedetect` 量录出的 WAV,确认打击声 PCM 真在文件里(别信中间标志)——见 §10 的 5 项。
- mux 仍走单 WAV + `-ac 2 -ar 48000`(若 tap 给非立体声),与 Windows 一致。
- 音画微同步:tap 自由跑设备时钟、视频走墙钟 CFR,与 loopback 同样的恒定偏移;若漂移可在 mux 加一次性 `-itsoffset`(目前未见需要)。
