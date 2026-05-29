> **Status: IMPLEMENTED (historical).** As-built home is Game/CombatReplay/Warmup/AudioBankWarmer.EnsureAudioReadyForPlayback (renamed/moved from this doc). Living feature: [combat-replay.md](../../features/combat-replay.md).

# CombatReplay 战斗 SFX 失声：补充诊断与分层修复

**Status:** Implemented (commit `1ce7c38`，等待 saved replay 实跑验证 §5)
**Date:** 2026-05-23
**Owner:** BazaarPlusPlus mod / CombatReplay
**Supersedes:** —
**Builds on:** [2026-05-22-combat-replay-sfx-silent-analysis.md](2026-05-22-combat-replay-sfx-silent-analysis.md)
**Related code:**
- [Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs](../../../Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs)
- [Game/CombatReplay/CombatReplayRuntime.Warmup.cs](../../../Game/CombatReplay/CombatReplayRuntime.Warmup.cs)
- [Patches/Combat/ReplayStateAudioDiagnosticPatch.cs](../../../Patches/Combat/ReplayStateAudioDiagnosticPatch.cs)
- [decompiled/TheBazaarRuntime/SoundManager.cs](../../../decompiled/TheBazaarRuntime/SoundManager.cs)
- [decompiled/TheBazaarRuntime/SFXPlayer.cs](../../../decompiled/TheBazaarRuntime/SFXPlayer.cs)
- [decompiled/TheBazaarRuntime/SoundSettings.cs](../../../decompiled/TheBazaarRuntime/SoundSettings.cs)
- [decompiled/TheBazaarRuntime/TheBazaar/GameServiceManager.cs](../../../decompiled/TheBazaarRuntime/TheBazaar/GameServiceManager.cs)
- [decompiled/TheBazaarRuntime/TheBazaar/ReplayState.cs](../../../decompiled/TheBazaarRuntime/TheBazaar/ReplayState.cs)
- [decompiled/TheBazaarRuntime/TheBazaar/VFXManager.cs](../../../decompiled/TheBazaarRuntime/TheBazaar/VFXManager.cs)
- [decompiled/TheBazaarRuntime/VOPlayer.cs](../../../decompiled/TheBazaarRuntime/VOPlayer.cs)
- [decompiled/TheBazaarRuntime/TheBazaar.AppFramework/AppLoader.cs](../../../decompiled/TheBazaarRuntime/TheBazaar.AppFramework/AppLoader.cs)

---

## 1. 为什么需要再写一份

[2026-05-22 分析文档](2026-05-22-combat-replay-sfx-silent-analysis.md) §4.5 给出的根因假设——“`CombatBus` 等 FMOD bus 卡在 `paused=true`，因为 `SoundEventListener` 重订阅有时序竞争”——方向是对的。但**改造前的** `EnsureReplayAudioUnpaused` 已经实现了那份文档 §5 Method A 的关键一步：

```csharp
Services.Get<SoundManager>()?.PauseBusses(isPausing: false);
```

直接调到 `SoundManager.PauseBusses(false)`。**这就是文档推荐的“绕过订阅链直接 setPaused(false)”修复，但用户报告 SFX 仍然失声。**

也就是说：原假设至少不够完整。还有别的“静音器”没有被列举/验证。继续按假设去“再多写一层修复”就是在重复 [WarmReplayAudioBanksAsync](../../../Game/CombatReplay/CombatReplayRuntime.Warmup.cs#L492) 走过的弯路：方向感觉对、改完仍然没声音、反复 churn。

这份补充文档要做两件事：

1. 把对 decompiled 原游戏代码的二次审计结论写下来——加上已经确认过、文档原版没有覆盖的事实。
2. 明确这次修复的策略：**先加诊断、再加分层防御**——并在每一层都打日志，让证据告诉团队哪一层才是真正担当的，下一次清理时可以放心删除多余的层。

---

## 2. 二次审计确认的事实

| 事实 | 验证位置 | 含义 |
|---|---|---|
| `SoundManager` 在整个 app 生命周期内是单例且持久化（reparent 到 DontDestroyOnLoad 的 `SystemRoot`） | [AppLoader.cs:138-148](../../../decompiled/TheBazaarRuntime/TheBazaar.AppFramework/AppLoader.cs#L138) | `Services.Get<SoundManager>()` 在 saved replay 流程里能拿到“同一个、原始的” SoundManager，bus 字段已被一次性 `GetBuses()` 填好 |
| `GameServiceManager.PauseOrUnpauseGame` 是边沿触发——`(false)` 在 `GamePaused == false` 时不触发 `GamePausedOrUnpaused`，等于 no-op | [GameServiceManager.cs:432-456](../../../decompiled/TheBazaarRuntime/TheBazaar/GameServiceManager.cs#L432) | 仅靠 `EnsureReplayAudioUnpaused` 早先版本只调 `PauseOrUnpauseGame(false)` 完全可能打水漂——这正是当前实现也直接补打 `PauseBusses(false)` 的原因 |
| `SoundEventListener : Singleton<SoundEventListener>` 没有 `DontDestroyOnLoad` —— 场景重载即销毁 | [SoundEventListener.cs:26](../../../decompiled/TheBazaarRuntime/TheBazaar/SoundEventListener.cs#L26) + [Singleton.cs:3](../../../decompiled/TheBazaarRuntime/Singleton.cs#L3) | 旧 listener `OnDestroy`（[L169](../../../decompiled/TheBazaarRuntime/TheBazaar/SoundEventListener.cs#L169)）**不**解订阅 `GamePausedOrUnpaused` 也**不**调 `OnGameplayPause(false)`，跨场景之后 bus 状态完全可能落在“pause snapshot 在持续播、bus paused 也没解、新 listener 还没接管”的混合态 |
| `SFXPlayer.PlayOneShotSfx` 的唯一“静默丢弃”闸门是 `IsEventInLoadedBank`，FMOD 事件本身被路由到由 FMOD studio 工程预先指定的 bus | [SFXPlayer.cs:17-31](../../../decompiled/TheBazaarRuntime/SFXPlayer.cs#L17) + [SoundManager.cs:615](../../../decompiled/TheBazaarRuntime/SoundManager.cs#L615) | bank 已加载 → 调用必发出；要让 SFX 静音，只可能是 bus paused、bus 上叠 ducking snapshot、`SfxVCA` 体积为 0、或上游 `MasterBus` 出问题之一 |
| Music 路由**不在** `PauseBusses` 名单内 | [SoundManager.cs:625-630](../../../decompiled/TheBazaarRuntime/SoundManager.cs#L625) | 完美解释 “BGM 正常 / SFX 静音” 现象；同时也说明：任何同时关掉 BGM 的修复假设都是错的 |
| `VFXManager.GenerateCombatVFX` 只在 `eventData.Data.VFXShouldPlay == false` 时返回，默认 true，**没有全局 replay 静音开关** | [VFXManager.cs:119-126](../../../decompiled/TheBazaarRuntime/TheBazaar/VFXManager.cs#L119) | item-triggered SFX 沿 `Events.EffectTriggered → GenerateCombatVFX → InstantiateVFX → AbilityVFXController.Play → prefab 上的 FMOD StudioEventEmitter` 路径走；它不会被 ReplayState 直接抑制 |
| `VOPlayer.PlayVO` 显式在 `AppState.CurrentState.IsReplayActive()` 时早返，**所有 replay 路径下 VO 都静音** | [VOPlayer.cs:108](../../../decompiled/TheBazaarRuntime/VOPlayer.cs#L108) | 用户感知到的“item 触发的声音”不是 VO（VO 在 live replay 里也是没声的），是 prefab 上 FMOD emitter 发出的 SFX |
| `SoundSettings.SetSfxVolume(0)` 不仅把 `SfxVCA` 设为 0，还会把 `SFXSnapshotBypassParameter` 全局参数置为 `true` | [SoundSettings.cs:48-62](../../../decompiled/TheBazaarRuntime/SoundSettings.cs#L48) | VCA 体积 = 0 是一条独立于 bus paused / snapshot 的静音通道，必须单独验证 |

---

## 3. 还没排除的“静音器”候选

把现有 fix 还没覆盖、又能解释“仅 SFX 静音 / BGM 正常 / 仅 saved replay 出现 / live replay 正常”的所有机制列全：

### 候选 A：残留的 `PauseSnapshot`（最可疑）

[SoundManager.PauseBusses](../../../decompiled/TheBazaarRuntime/SoundManager.cs#L615) 行为：

```csharp
public void PauseBusses(bool isPausing) {
    if (isPausing) SFXPlayer.PlaySfx(PauseSnapshot);
    else           SFXPlayer.StopSfx(PauseSnapshot);   // ← 见下
    ...bus.setPaused(isPausing) ×6...
}
```

[SFXPlayer.StopSfx](../../../decompiled/TheBazaarRuntime/SFXPlayer.cs#L171)：

```csharp
public void StopSfx(EventReference eventRef, string instanceName = "") {
    string eventInstanceName = GetEventInstanceName(eventRef, instanceName);
    if (sfxEventInstances.ContainsKey(eventInstanceName)) {    // ← 字典里没有就 no-op
        ...
    }
}
```

PauseSnapshot 是个 FMOD mixer snapshot——一旦 `start()` 之后，它会持续 duck 多个 bus 的输出，**直到对应的 EventInstance 被 stop**。`StopSfx` 只在 `sfxEventInstances` 字典里能找到对应 key 时才做 stop。

如果 PauseSnapshot 是在某条**不由当前 SoundManager.SFXPlayer 字典追踪**的路径上启动的（例如：FMOD studio 工程里有其他动作触发了它、或者历史上的某个生命周期里启动后字典又被清空），那么 `PauseBusses(false)` 走过来的 `StopSfx` 找不到 key，no-op。snapshot 继续 duck SFX。Music 不在 snapshot 影响的 mixer 路径上，BGM 不受影响。**完美匹配现象。**

修复手法：直接遍历 `SFXPlayer.sfxEventInstances` 字典，把任何还在追踪的 EventInstance 主动 stop+release（限定在 saved replay bootstrap 这一个点位上，避免影响其它流程）。同时通过反射把 `SoundManager.PauseSnapshot` 字段的 GUID 拿出来，作为日志判定依据。

### 候选 B：残留的 `LoadingScreenSnapshot` / 类似 snapshot

[SoundEventListener.cs:107](../../../decompiled/TheBazaarRuntime/TheBazaar/SoundEventListener.cs#L107) 持有 `LoadingScreenSnapshot`，会在加载场景时启动用来 duck 玩法 SFX。saved replay bootstrap 里走的就是 `LoadScene` + `LoadSceneAdditive(GameplayLoading)`，加载途中或之后这个 snapshot 没被对应清理是非常合理的。

判定依据和 A 一样：字典里只要有非空 EventInstance 残留，就有嫌疑。

### 候选 C：`SfxVCA` 体积被压成 0

谁会把 SFX VCA 设到 0？目前**没有**找到 saved replay bootstrap 路径上明确这样做的代码。但是因为 SfxVCA = 0 也能完美匹配“仅 SFX 静音”的现象，作为一条便宜的“最后保险”——在 `EnsureReplayAudioReadyForPlayback` 里从 `PlayerPreferences.Data.VolumeSfx` 重新写一遍 `SoundManager.SetVolume(VolumeType.SFX, ...)`，并在日志里 dump 当前 `SfxVCA.getVolume()` 给后续排查留证据。

### 候选 D：bank 没加载（基本可以排除）

`SFXPlayer` 走的所有 `PlayOneShotSfx` 都先过 `IsEventInLoadedBank`；如果 bank 缺失会有 warning 日志：

```
[SFXPlayer] Event {guid} does not exist in any loaded banks
```

让用户在 BepInEx 日志里搜这条字符串。**如果没出现，说明 bank 已经全部加载、bank 不是这次问题的根因**——也就坐实了真正阻断 SFX 输出的是更下游的 bus/snapshot/VCA。

### 候选 E：`MasterBus` 被 setPaused(true)

`MasterBus` 不在 `PauseBusses` 的处理名单里，但它在 [SoundManager.cs:605](../../../decompiled/TheBazaarRuntime/SoundManager.cs#L605) 通过 `GetBuses()` 拿到了 handle。如果有第三方调 `MasterBus.setPaused(true)`，会同时杀掉 SFX 和 BGM。**用户报告 BGM 正常 → 此候选可以排除。**

但保留作为诊断日志里的一项，万一证据指过去（例如：发现 BGM 实际上是因为另一条路径才得以发声，并不真的“正常”），就能马上识别。

---

## 4. 实施方案

策略：**“证据驱动的分层防御”**——一次性把所有候选静音器都做一遍治理，每一层进入和退出都打日志。下次维护看日志就知道哪一层真正起到了作用。

### 4.1 加诊断：`LogReplayAudioState(string label)`

放在 [CombatReplayRuntime.Warmup.cs](../../../Game/CombatReplay/CombatReplayRuntime.Warmup.cs) 里，对外只有一个签名 `LogReplayAudioState(string label)`。内部以反射读：

- `SoundManager` 的 6 个 `*BusPath` 私有字段 + `MasterBusPath`——通过 `FMODUnity.RuntimeManager.GetBus(path)` 拿到 Bus，调用 `getPaused()` 和 `getVolume()`
- `SoundManager.SoundSettings` 的 `SfxVCA / MusicVCA / VoVCA` 三个 VCA 体积
- `SoundManager.SFXPlayer` 上的私有 `sfxEventInstances` 字典——dump 当前 key 集合（也即“正在被追踪的 SFX EventInstance”），便于检测残留 snapshot
- 反射拿到 `SoundManager.PauseSnapshot` / `SoundEventListener.LoadingScreenSnapshot` 等 `EventReference` 的 GUID，看是否落在字典 key 集合里
- `Singleton<GameServiceManager>.Instance.GamePaused`

日志格式以 `[ReplayAudioDiag/{label}]` 开头，方便用户在 BepInEx 日志里 grep。

### 4.2 改 `EnsureReplayAudioUnpaused` → `EnsureReplayAudioReadyForPlayback`

由 5 层组成，**全部执行**（不再做“只在条件满足时才修”那种早返）：

| Layer | 内容 | 用意 |
|---|---|---|
| 0 | `LogReplayAudioState("pre-fix")` | 把进入这一刻的 FMOD 实际状态钉死 |
| 1 | 若 `GameServiceManager.GamePaused == true`：`PauseOrUnpauseGame(false)` | 解开 GameServiceManager 视角下的“暂停” |
| 2 | `SoundManager.PauseBusses(false)`（已有） | 直接 `setPaused(false)` 六个 SFX bus，并通过 `SFXPlayer.StopSfx(PauseSnapshot)` 尝试停掉自家追踪的 PauseSnapshot |
| 3 | 反射遍历 `SFXPlayer.sfxEventInstances`，stop + release + 清空，**针对 saved replay bootstrap 这一时刻**——这是干掉残留 PauseSnapshot / LoadingScreenSnapshot 的最稳手段 | 候选 A、B 的兜底 |
| 4 | 用 `PlayerPreferences.Data.VolumeSfx` 重新喂一次 `SoundManager.SetVolume(VolumeType.SFX, ...)` | 候选 C 兜底 |
| 5 | `LogReplayAudioState("post-fix")` | 让日志变成“前/后对比”，下次维护可立刻判断哪一层有效 |

每个层级在执行前后都加一行 `BppLog.Info`，明确写“是否真的做了事”（例如 Layer 1 只有 `GamePaused == true` 时才会 log "had to unpause"）。

被替换的旧函数从 Bootstrap.cs 里改名字调用即可——Bootstrap.cs:139 那一行。

### 4.3 加 Harmony 基线：live replay 路径

为了让两条路径可比，给 `ReplayState.Replay` 加 `[HarmonyPrefix]`，在每次 replay 真正开始前都打一次 `LogReplayAudioState("ReplayState.Replay")`：

- 在 live PvP→Replay 路径下，这是**唯一**的诊断点
- 在 saved replay 路径下，这是“在 `EnsureReplayAudioReadyForPlayback` 的 post-fix 之后”的第三次快照，用于校验“离开 mod 代码进入 game 代码时是否仍是健康的”

放在 [Patches/Combat/ReplayStateAudioDiagnosticPatch.cs](../../../Patches/Combat/ReplayStateAudioDiagnosticPatch.cs)。

### 4.4 不做的事

- **不动** `WarmReplayAudioBanksAsync` 的任何逻辑。bank 加载不是这次 bug 的根因，文档原版结论保持不变。
- **不修改** 原游戏 decompiled 代码——按 `.rules`，全部从 mod 侧通过反射 + Harmony 处理。
- **不加单元测试**——FMOD 运行时副作用没有有效的自动化测试 seam，按 `.rules` 允许直接 ship。
- **不预设**“某一层会有效就只做某一层”——这次必须把所有候选都覆盖，让日志告诉我们答案，而不是反过来。

---

## 5. 验证流程（landed 后必须跑）

1. **跑 saved replay**，BepInEx 日志里搜 `[ReplayAudioDiag/`：
   - 看 `pre-fix` 这一份：哪些 bus `paused=true`？哪些 EventInstance 还在字典里？`SfxVCA` 体积是多少？
   - 看 `post-fix`：是否所有 bus 都 `paused=false`、字典清空、`SfxVCA` 体积非 0？
   - 听一下 saved replay 期间的 item SFX 是否真的有声音了。
2. **跑 live PvP→Replay**，作对照：在 `ReplayState.Replay` 那条日志里，bus 状态/VCA/字典应该天然就健康。
3. **跑“两次 saved replay 连放”** 这种边界场景：第二次应该和第一次一样正常发声，避免本次修复留下副作用（例如把字典清空后影响后续的 SFX 追踪）。

**“声音真的回来了”** 是这次问题修没修好的唯一硬性判据；日志是用于决定下次能不能删层的依据。

---

## 6. 后续可能的清理

如果验证后日志显示某些 layer 进入时已经是“无事可做”状态（例如 bus 永远不 paused、`sfxEventInstances` 永远只剩 user UI snapshot 一类应保留项），那么对应 layer 在下一轮 PR 里可以删掉，回归到“仅保留真正有效的一层”。**不要在这次 PR 里提前删——证据没采全之前就预判会再次踩坑。**

---

## 7. 实施结果

commit `1ce7c38` 已落地，文件清单：

- 修改：[Game/CombatReplay/CombatReplayRuntime.Warmup.cs](../../../Game/CombatReplay/CombatReplayRuntime.Warmup.cs) — 加 `LogReplayAudioState` (L970)、`EnsureReplayAudioReadyForPlayback` (L923)、`StopAllTrackedSfxEventInstances` (L1179)、`ReassertSfxVolumeFromPreferences` (L1239)
- 修改：[Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs:139](../../../Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs#L139) — 调用点更名
- 新增：[Patches/Combat/ReplayStateAudioDiagnosticPatch.cs](../../../Patches/Combat/ReplayStateAudioDiagnosticPatch.cs) — Harmony prefix on `ReplayState.Replay`
- 修改：[BazaarPlusPlus.csproj](../../../BazaarPlusPlus.csproj) — `FMODUnity` 加进 `<Reference>`
- 修改：[run.sh](../../../run.sh) — `FMODUnity` 加进 `decompile_all`

下一步是按 §5 跑 saved replay + live PvP→Replay，拉日志对比，决定哪些 layer 在本环境下实际生效、哪些可以在后续 PR 里精简。
