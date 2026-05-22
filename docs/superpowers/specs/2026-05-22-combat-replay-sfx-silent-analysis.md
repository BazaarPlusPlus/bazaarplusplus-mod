# CombatReplay 战斗 SFX 失声分析与验证方案

**Status:** Draft for plan authoring
**Date:** 2026-05-22
**Owner:** BazaarPlusPlus mod / CombatReplay
**Related code:**
- [Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs](../../../Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs)
- [Game/CombatReplay/CombatReplayRuntime.Warmup.cs](../../../Game/CombatReplay/CombatReplayRuntime.Warmup.cs)
- [decompiled/TheBazaarRuntime/SoundManager.cs](../../../decompiled/TheBazaarRuntime/SoundManager.cs)
- [decompiled/TheBazaarRuntime/SFXPlayer.cs](../../../decompiled/TheBazaarRuntime/SFXPlayer.cs)
- [decompiled/TheBazaarRuntime/TheBazaar/SoundEventListener.cs](../../../decompiled/TheBazaarRuntime/TheBazaar/SoundEventListener.cs)
- [decompiled/TheBazaarRuntime/Singleton.cs](../../../decompiled/TheBazaarRuntime/Singleton.cs)

---

## 1. 背景

`bazaarplusplus-mod` 的 CombatReplay 功能把已结束的 PvP 战斗序列保存下来，让玩家在大厅或外部导入后回放。

**三条 Replay 入口走的代码路径不同：**

| 入口 | 流程 | 关键差异 |
|---|---|---|
| Live PvP→Replay（原生 ReplayState） | 玩家就在 `PVPCombatState`，直接推入 `ReplayState`，无场景重建 | 游戏运行时所有 manager / listener 已稳定订阅 |
| Saved Replay（从 lobby） | `LoadScene(GameScene)` → `LoadSceneAdditive(GameplayLoading)` → `BootstrapReplayManagersAsync` → 卡牌 rehydrate → `TryPushState<ReplayState>` → `replayState.Replay()` | 需要从零搭出完整 gameplay 运行环境 |
| Imported Replay | 同 Saved Replay，仅 manifest+payload 来源不同 | 同上 |

战斗回放本身通过 `_combatSimHandler.Simulate(_sequence.CombatMessage)` 触发，三条入口在 `Simulate` 调用本身上没差异。

---

## 2. 现象与已知边界

**用户观察：**

- ✅ Live PvP→Replay：BGM 和 SFX 都正常
- ✅ Saved / Imported Replay：BGM 正常
- ❌ Saved / Imported Replay：**SFX（卡牌触发、攻击、命中、tick 等所有战斗音效）全静音**

**已尝试且未解决的方向：**

- 在 [CombatReplayRuntime.Warmup.cs:490](../../../Game/CombatReplay/CombatReplayRuntime.Warmup.cs#L490) `WarmReplayAudioBanksAsync` 里反复迭代「warm SFX bank / Music bank / soundtrack bank」
- Git 记录里有 `cb849a7 Restore CombatReplay audio warmup`，说明该方向曾经被删过又恢复，本身就反复

---

## 3. 为什么 "warm bank" 方向永远修不好（关键洞察）

### 3.1 SFX 实际播放链路

```
CombatSimHandler.Simulate
  → Events.EffectTriggered
  → VFXManager.GenerateCombatVFX        # 实例化 VFX prefab
  → prefab 上的 FMOD StudioEventEmitter / animation event
  → SFXPlayer.PlayOneShotSfx*
  → IsEventInLoadedBank(eventRef) 把关  ← 唯一的静默跳过条件
  → RuntimeManager.PlayOneShot/CreateInstance(...)
  → 通过 FMOD bus 出声
```

[SFXPlayer.cs](../../../decompiled/TheBazaarRuntime/SFXPlayer.cs) 里 SFX 被静默丢弃的判断**只有一个**：

```csharp
if (!IsEventInLoadedBank(eventRef)) {
    AppLogger.LogWarning(...);
    return;
}
```

### 3.2 反证

**BGM 能正常播放 → bank 加载路径本身是通的。** BGM 走 `MusicPlayer.Play(...)`，同样要求音乐 bank 已被 `LoadBankAsync` 注入。如果 saved replay 路径下真的有 bank 没加载，BGM 也会一起失声。

**结论：bank 加载不是瓶颈，"warm bank" 是在治不相干的器官。** 这就是为什么这条路径反复改、反复试都没用。

---

## 4. 根因假设

### 4.1 关键证据：FMOD bus 的 pause 名单

[SoundManager.cs:615-631](../../../decompiled/TheBazaarRuntime/SoundManager.cs#L615)：

```csharp
public void PauseBusses(bool isPausing) {
    if (isPausing) SFXPlayer.PlaySfx(PauseSnapshot);
    else           SFXPlayer.StopSfx(PauseSnapshot);

    BoardDiegeticBus.setPaused(isPausing);
    CombatBus.setPaused(isPausing);            // ← 战斗 SFX
    MonsterNonVerbalBus.setPaused(isPausing);
    VOBus.setPaused(isPausing);
    EnvironmentSpecificBus.setPaused(isPausing);
    EnvironmentFocusBus.setPaused(isPausing);
}
```

**注意名单里没有 `MasterBus`，也没有 Music 路由。** 这恰好和现象一一对应：

| 路由 | 是否在 pause 名单 | 用户观察 |
|---|---|---|
| Music 路由 | 否 | BGM ✅ |
| `CombatBus` / `BoardDiegeticBus` 等 | 是 | SFX ❌ |

### 4.2 触发 `PauseBusses` 的唯一入口

[SoundEventListener.cs:163-167](../../../decompiled/TheBazaarRuntime/TheBazaar/SoundEventListener.cs#L163)：

```csharp
private void Start() {
    GameServiceManager instance = Singleton<GameServiceManager>.Instance;
    instance.GamePausedOrUnpaused = Delegate.Combine(
        instance.GamePausedOrUnpaused,
        new Action<bool>(OnGameplayPause));   // ← 唯一订阅者
}

private void OnGameplayPause(bool isPausing) {
    _soundManager.PauseBusses(isPausing);
}
```

整个游戏代码里 `PauseBusses` 没有第二个调用入口。**Bus 的 paused 状态完全由 `GamePausedOrUnpaused` Action 驱动，能否触发它，取决于 `SoundEventListener` 当时是否已经订阅。**

### 4.3 SoundEventListener 的生命周期问题

[Singleton.cs](../../../decompiled/TheBazaarRuntime/Singleton.cs)：

```csharp
public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance;
    // ⚠️ 没有 DontDestroyOnLoad —— 随场景销毁
}
```

`SoundEventListener : Singleton<SoundEventListener>` ⇒ **场景重载时会被销毁并重建，订阅链断裂后由新实例的 `Start()` 重新订阅。**

### 4.4 时序竞争假设

Saved Replay 的 bootstrap（[Bootstrap.cs:25-58](../../../Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs#L25)）涉及：

1. `LoadScene(GameScene, shouldUnloadCurrentScene: true)` — 销毁旧 `SoundEventListener`，新实例 `Awake`
2. `LoadSceneAdditive(GameplayLoading)`
3. `BootstrapReplayManagersAsync` → `gameServiceManager.Init(boardManager)` — **该过程可能调用 `PauseOrUnpauseGame(true)`**
4. 等待 `IsReplayBootstrapReady`
5. Unload `GameplayLoading` 场景
6. 后续 inject、warm、`replayState.Replay()`

**只要在 (3) 的某次 `PauseOrUnpauseGame(true)` 边沿到来时，新的 `SoundEventListener.Start()` 还没跑完订阅注册** —— 那次 pause 不会落到 `PauseBusses(true)` 上。但 FMOD bus 的实际 paused 状态由谁决定？由 `setPaused()` 调用决定。如果**别处**的代码（包括 FMOD studio 自身在某些事件中产生的快照、或者其他 bootstrap 内联代码）让某些 bus 进入了 paused，订阅迟到的 `SoundEventListener` 永远不会执行对应的解除。

更糟的是，[Warmup.cs:909-920](../../../Game/CombatReplay/CombatReplayRuntime.Warmup.cs#L909) 的现有"补救"：

```csharp
private static void EnsureReplayAudioUnpaused() {
    if (gameServiceManager == null || !gameServiceManager.GamePaused) return;
    gameServiceManager.PauseOrUnpauseGame(toPauseOrUnpause: false);
}
```

只能修复"`GamePaused == true` 且监听已就位"的同步情形。**对"bus 实际 paused，但 GameServiceManager 视角下 `GamePaused == false`"这种半状态完全无效** —— 它直接 `return`。

### 4.5 假设总结

> Saved Replay bootstrap 在 `SoundEventListener` 重新订阅 `GamePausedOrUnpaused` 的过程中存在时序竞争，导致至少一个 SFX bus（最可能是 `CombatBus`）卡在 `paused=true`，而 `GameServiceManager.GamePaused` 已为 `false`。现有 `EnsureReplayAudioUnpaused` 走 `PauseOrUnpauseGame(false)` 这条订阅链触达不了"已脱钩"的 bus，所以再怎么 warm bank 也无济于事。

**这个假设满足所有现象：**

- ✅ 只 SFX 缺，BGM 正常（音乐路由不在 pause 名单）
- ✅ 只在 saved/imported 出（这两条路才经历场景重建 + 订阅竞争）
- ✅ Live PvP→Replay 正常（`SoundEventListener` 全程存活）
- ✅ Warm bank 永远无效（治错了器官）

---

## 5. 潜在方案

按介入程度从浅到深排列。**所有方案都先做 §6 的验证后再选定**。

### 方案 A：绕过订阅链，直接操作 bus（推荐）

在 `TryInjectSavedReplayAsync` 内、`replayState.Replay()` 之前，对那 6 个 bus 直接 `setPaused(false)`。

```csharp
// 通过反射拿 SoundManager 的 *BusPath 私有字段
foreach (var path in new[] {
    BoardDiegeticBusPath, CombatBusPath, MonsterNonVerbalBusPath,
    VOBusPath, EnvironmentSpecificBusPath, EnvironmentFocusBusPath })
{
    var bus = FMODUnity.RuntimeManager.GetBus(path);
    if (bus.isValid()) bus.setPaused(false);
}
```

- **优点**：直击 FMOD 实际状态，不依赖 `SoundEventListener` 的订阅是否就位
- **代价**：要通过反射拿 `SoundManager` 的 `*BusPath` 私有字段；或反射调 `SoundManager.PauseBusses(false)`

### 方案 B：修复 SoundEventListener 订阅时序

强制等待 `SoundEventListener.Instance != null` 并主动调用一次"基线解除"（通过反射）：

```csharp
await WaitUntilAsync(
    () => Singleton<SoundEventListener>.Instance != null,
    TimeSpan.FromSeconds(5));
// 然后通过反射触发 OnGameplayPause(false)，或直接调 PauseBusses(false)
```

- **优点**：贴近原生游戏语义
- **代价**：绕不开"如果 bus 已经独立 paused、Listener 不知情"的边界情况；比方案 A 更绕

### 方案 C：在 `replayState.Replay()` 启动前调一次 `PauseBusses(true)` 再 `false`

强制一次完整的 pause→unpause 周期来"扶正"两侧状态。

- **优点**：对所有可能的半状态都收敛
- **代价**：会触发一次 `PauseSnapshot` 的 play/stop，可能有 audible 副作用（一瞬的 SFX duck）

### 方案 D（次要假设）：处理 PauseSnapshot 残留

如果根因是 `PauseSnapshot` 这个 FMOD 快照实例残留：

```csharp
soundManager.SFXPlayer.StopSfx(/* PauseSnapshot ref */);
```

- 需要反射拿那个私有 `EventReference`，比较脏
- 优先级靠后，先验证 §4.5 主假设

---

## 6. 验证方法（执行顺序固定）

按 systematic-debugging 流程，**fix 之前先采证据**。

### Step 1 — 加诊断日志

在 [Bootstrap.cs:137](../../../Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs#L137) `replayState.Replay()` 调用前插一段诊断代码：

```csharp
// 通过反射拿到 SoundManager 的 6 个 *BusPath 私有字段
foreach (var (name, path) in busPaths) {
    var bus = FMODUnity.RuntimeManager.GetBus(path);
    bus.getPaused(out var paused);
    BppLog.Info("CombatReplayRuntime",
        $"[ReplayBusDiag] bus={name} valid={bus.isValid()} paused={paused}");
}
BppLog.Info("CombatReplayRuntime",
    $"[ReplayBusDiag] GameServiceManager.GamePaused={gameServiceManager.GamePaused}");

// 同时检查 PauseSnapshot 是否在 SFXPlayer 的 sfxEventInstances 里
// （通过反射拿 sfxEventInstances 字段，看 key 集合里是否有任何 snapshot 痕迹）
```

同样的日志在 Live PvP→Replay 进入 `replayState.Replay()` 时也打一份（用 Harmony prefix 到 `ReplayState.Replay`），作为 baseline。

### Step 2 — 跑两条路径，对比日志

| 路径 | 期待诊断结果（若假设成立）|
|---|---|
| Live PvP→Replay | 所有 6 个 bus `paused=false`，`GamePaused=false` |
| Saved Replay 从 lobby | 至少 1 个 bus `paused=true`（最可能是 `CombatBus`），但 `GamePaused=false` |

### Step 3 — 根据证据决定下一步

- **若日志确认 `CombatBus paused=true` 且 `GamePaused=false`** → §4.5 假设钉死 → 上**方案 A**：在那一行之前直接 `setPaused(false)` 那 6 个 bus；同时**移除**或精简现有 `WarmReplayAudioBanksAsync` 里大部分逻辑（保留必要的 bank 加载，去掉无效的"治错器官"代码）
- **若所有 bus 都 `paused=false`** → §4.5 假设不成立，根因在别处。回到 Phase 1 重新调查：
  - 优先怀疑 `PauseSnapshot` 残留（快照仍在 ducking SFX 总线）
  - 其次怀疑 VFX prefab 上的 FMOD emitter 在 bootstrap 后未绑定（用 `Resources.FindObjectsOfTypeAll<FMODUnity.StudioEventEmitter>()` 抽查典型 VFX prefab）
  - 最后才怀疑 `ReplayState` / `CombatSimHandler` 有针对 saved-replay 的抑制路径（grep `IsReplaying`、`SuppressVFX`、`VFXShouldPlay`）

### Step 4 — Fix 后再验证

加完 fix 后**不删诊断日志**，用同样的 saved replay → 确认日志显示 `paused=false`，且能听到 SFX。**Evidence before assertion** —— 听见声音 + 日志双重确认才算修好。

---

## 7. 落地建议

1. **不要再碰 `WarmReplayAudioBanksAsync`** —— 它本身没错（暖 bank 对其它边缘情形可能仍然必要），但它**不是这个 bug 的根因**，继续在那里改就是再走一遍死循环。
2. **先做 §6 Step 1 + Step 2 的诊断**，把 bus pause 状态作为客观证据钉下来。
3. 根据证据落具体方案。**强烈倾向方案 A 的直接 `setPaused(false)`**，因为它绕开了那条已被证明脆弱的 `SoundEventListener` 订阅链路。

---

## 8. Non-goals

- **不**在这次修复中重构 `CombatReplayRuntime.Warmup.cs` 的整体结构；仅删除被证伪为无用的代码。
- **不**修改游戏原生 `SoundEventListener` 或 `SoundManager` —— 它们在 decompiled 目录下，按 `.rules` 是只读参考。所有 fix 通过 mod 侧反射 + Harmony 完成。
- **不**给 saved replay 的 BGM 路径做任何改动 —— 那条路用户已确认正常。
- **不**为这次修复加新的单元测试。改动是 FMOD 运行时副作用，没有有效的自动化测试 seam，按 `.rules` 允许直接 ship；验证完全依赖 §6 Step 4 的人工 + 日志双重确认。
