> **Status: IMPLEMENTED (historical).** Landed 2026-06-08（分支 `feat/bazaaragent-replay-video-http`）。三原语路由、`IBazaarAgentReplayRecorder` facade、显式 continue sink、host tick 自动退出删除均按本设计实现；偏差：① `replayBattleId` 改从 playback session（`ReplayPlaybackPublisher.ActiveSessionBattleId`）读取——设计引用的 `CombatReplayRuntime.ActiveBattleId` 旧实现对 imported battle 不赋值；② facade 签名用 status-enum 结果结构体替代 `bool + out string`（HTTP 400/409/503 映射需要区分失败类别）；③ body cap 先定 32 MB（待 p99 实测，§11 仍开放）；④ 守卫为 `CanRecordReplay` 三条 + recorder 的视频目录检查的并集（共四条）。耐久决策已提升为 [ADR-0007](../../adr/0007-bazaaragent-external-replay-video-recording.md)。§11 的两条「上线前必验」（端到端 mp4 完整可播；不发 continue 则 ReplayState 不退出）在归档时尚未在游戏内执行。

# BazaarAgent 外部驱动战斗回放视频批量录制

Status: Draft — 方案要点已与维护者确认（每 Battle 一个 mp4 / 外部脚本编排 / agent 驱动「起播 + 点继续收尾」/ CombatReplay 仍属主 mod、仅暴露接口）。已经一轮独立红队评审（含反编译实证）修订，并补入维护者约束：**snapshot tick 后不得自动调用 `ReplayState.Exit()`，必须由显式 continue sink 触发**。**待开工**；落地后移入 `archive/` 并加 `Status:` banner，其中「主 mod 暴露原语 facade、显式 continue sink 驱动 ReplayState.Exit 收尾、纯核心二进制录制路由」等决策在归档前提升为 ADR。

> **红队评审并入摘要：** ① 经反编译确认回放跑完**不会自动退出**（只显示 Replay/Recap/继续 按钮等人点），原「mod 内部 `Events.ReplayEnded` 自动退」方案改为**由 agent 显式 `continue` sink 驱动「继续」按钮**收尾；同时现有 `BazaarAgentUiPlumbing.TryAdvanceReplay` 的 tick 自动退出路径必须删除或永久关闭，不能在 snapshot tick 后自动 `Exit()`；② 预检守卫必须**逐字复用** `HistoryPanelReplayService.CanRecordReplay` 的全三条（含 `CombatReplayVideoDirectoryPath`），否则会返回「已受理」却什么都不录；③ body 上限须实测后定、倾向流式落盘；④ 组合根委托须 **lazy** 读 `Runtime`（发布 facade 时它尚为 null）；⑤ 录制完成不可观测 → HTTP 契约为 **sync-accept(202)**，完成由外部按 battleId 轮询产物文件。

本文记录在**现有 BazaarAgent 架构**上新增「外部 HTTP 触发、批量把已存在的战斗回放自动录制成视频」能力的目标架构、对外接口、技术难点与实施方案。Combat Replay 的实现仍完全留在主 mod；BazaarAgent 只是多暴露一个程序化自动化入口（手动 HistoryPanel 录制不受影响）。

---

## 1. 背景与目标

- **目标用户故事：** 「我后续拿到一批 Battle，能自动把每场录制成一个 mp4。」
- **现状：** 录制已是 mod 功能，但只能从 HistoryPanel 的录制按钮**手动**触发一场（`HistoryPanelReplayService.ReplayBattleAsync(..., recordVideo)` → `CombatReplayRuntime.ReplaySaved/ReplayImportedBattle`，[HistoryPanelReplayService.cs:120-149,189-213](../../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelReplayService.cs)）。无程序化批量入口。
- **本设计：** 在 BazaarAgent 的 loopback HTTP 面（`127.0.0.1:47900`）上暴露**三个原语**（起播+录制 / 查回放阶段 / 点继续收尾），由**外部脚本**编排串行循环；每场产出一个 mp4。
- **不做：** 不在 mod 内做批量状态机；不做整批拼接（每 Battle 一个 mp4，需要合并时外部 `ffmpeg concat` 后处理）；不改动 HistoryPanel 手动路径。

### 关键洞察：用户给的「msgpack」就是现成格式

外部要 POST 的 battleId + msgpack，正是现有的 `GhostBattlePayload`（`BattleId` + `PvpBattleManifest` + `PvpReplayPayload`），经 `GhostBattlePayloadCodec`（MessagePack + gzip）编解码（[GhostBattlePayload.cs:6-13](../../../src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattlePayload.cs)、[GhostBattlePayloadCodec.cs:8-15](../../../src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattlePayloadCodec.cs)，codec 实现在零游戏依赖的 `BazaarPlusPlus.ModApi`，[MessagePackGzipCodec.cs:15](../../../src/BazaarPlusPlus.ModApi/MessagePackGzipCodec.cs)）。它解出的 manifest+payload 正是 `CombatReplayRuntime.ReplayImportedBattle(manifest, payload, recordVideo)` 所需（[CombatReplayRuntime.cs:212-244](../../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs)）——与 ghost 路径喂给它的完全同形。

---

## 2. 已确认的决策

| # | 决策 | 依据 |
|---|---|---|
| 1 | **每 Battle 一个 mp4**；不做整批拼接（需要时外部后处理）。 | recorder 单飞、每次退出 finalize 一个文件、按 battleId 命名（[CombatReplayVideoRecorder.cs:821-823](../../../src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs)）。 |
| 2 | **外部脚本编排批量循环**；BazaarAgent 只暴露原语，保持薄。 | 贴合现有 action-server 哲学（外部决策、agent 执行）。 |
| 3 | **agent 驱动「起播 + 点继续收尾」**；不在 mod 内自动退出。 | 回放跑完不自动退（§4 反编译实证）；继续按钮 = `ReplayState.Exit()`。 |
| 4 | Combat Replay 实现**留在主 mod**；通过 `GameInterop/BazaarAgent` facade 暴露原语接口供 host 消费。 | ADR-0006 facade 模式；mod 不引用 agent 模块。 |
| 5 | 录制起播复用 **`ReplayImportedBattle`**（内存 manifest+payload，`recordVideo:true`）。 | recorder 由事件总线自动开录，零额外接线（§5）。 |
| 6 | HTTP 契约 **sync-accept(202)**；完成由外部按 battleId 轮询产物文件。 | 完成不可观测（§7-f）。 |
| 7 | wire blob 复用**现有全 public 的 `GhostBattlePayload` 图**；不新增任何 msgpack DTO。 | 规避 MessagePack public-graph 陷阱（§7-h）。 |
| 8 | **显式 continue sink 是唯一 agent 退出路径**；host tick/snapshot 发布后不得自动 `ReplayState.Exit()`。 | 当前 `BazaarAgentUiPlumbing.TryAdvanceReplay` 会在 `ReplayState && !IsReplaying` 时直接 `replay.Exit()`（[BazaarAgentUiPlumbing.cs:50-83](../../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentUiPlumbing.cs)），会抢在外部脚本 `POST /v1/replay/continue` 前收尾，必须删除或永久关闭。 |

---

## 3. 架构与边界（谁拥有什么）

三程序集三层，分层由 `tests/Architecture.Tests/CoreLayeringTests.cs` 强制（核心不得引 Game/GameInterop/游戏 DLL，[:30](../../../tests/Architecture.Tests/CoreLayeringTests.cs)；GameInterop 不得 import `BazaarPlusPlus.Game.*` 或 `BazaarPlusPlus.BazaarAgent`，[:352,:375,:377](../../../tests/Architecture.Tests/CoreLayeringTests.cs)）。

| 关注点 | 归属程序集/层 | 是否含游戏类型 |
|---|---|---|
| HTTP 路由 + 跨线程队列 + 控制器 drain | `BazaarPlusPlus.BazaarAgent`（纯核心） | 否（`byte[]` 进 / JSON 出）|
| 主线程编组（队列在 host `Update→Tick` 内 drain） | `BazaarPlusPlus.BazaarAgentHost`（宿主桥） | 是（薄适配器，只读 facade 转发；不得在 tick 后自动退出 ReplayState）|
| 暴露的接口 `IBazaarAgentReplayRecorder`（原语签名） | 主 mod `GameInterop/BazaarAgent` | 否（签名只含 `byte[]`/`string`/枚举）|
| msgpack 解码 + `ReplayImportedBattle` + `ReplayState.Exit()` + phase 读取 | 主 mod（`BppComposition` 委托 + `CombatReplayRuntime`） | 是（游戏类型只在此出现）|

> 录制能力本身是 mod 功能、无条件挂载（[BppComposition.cs:98-100](../../../src/BazaarPlusPlus/BppComposition.cs) 挂 `CombatReplayVideoRecorder`、[:108-113](../../../src/BazaarPlusPlus/BppComposition.cs) 挂 HistoryPanel）。装不装 `--with-bazaaragent` 只决定**有没有这条程序化 HTTP 入口**，不决定能不能录。

---

## 4. 回放 / 录制生命周期（反编译实证）

这是本设计的根因证据，也是把「收尾」交给 agent 的原因。

1. `ReplayState.Replay()` 跑完战斗 sim 后，只 `ShowReplayAndRecapButtons(true)` + `BlockInput=false` + `IsReplaying=false` + `Events.ReplayEnded.Trigger()`，**从不调 `Exit()`**（[decompiled ReplayState.cs:246-289](../../../decompiled/TheBazaarRuntime/TheBazaar/ReplayState.cs)）。即回放结束后停在 ReplayState 等人点按钮。
2. 按钮事件映射（[decompiled BoardManager.cs:650-654](../../../decompiled/TheBazaarRuntime/BoardManager.cs)）：
   - **继续 / Exit** → `ReplayState.Exit()`（[BoardManager.cs:3620-3627,3640-3646](../../../decompiled/TheBazaarRuntime/BoardManager.cs)）
   - Replay(重播) → `ReplayState.Replay()`；Recap → `ReplayState.Recap()`
3. mod 已拦截 `ReplayState.Exit()`（[CombatReplayStateExitPatch.cs](../../../src/BazaarPlusPlus/Patches/Combat/CombatReplayStateExitPatch.cs)）→ `CombatReplayRuntime.TryExitBootstrappedSavedReplayToMenu` → `PublishEnded` → recorder finalize（写 moov atom，[CombatReplayRuntime.cs:339-379](../../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs)）。普通状态转移路径也会经 `OnStateChanged` 发 `PublishEnded`（[:302-337](../../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs)）。

**结论：** 无人点继续 ⇒ 录制永不 finalize ⇒ mp4 无 moov atom、不可播放。所以「agent 驱动继续按钮」不是可选项而是收尾的唯一可靠途径——而这正是用户要的。从菜单触发的回放会 bootstrap-from-lobby（`_bootstrappedReplayActive=true`，[CombatReplayRuntime.cs:264-278](../../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs)），故 `Exit()` 两条 finalize 路都覆盖。

**现有代码冲突必须先拆掉：** `BazaarAgentHostPlugin` 当前把 `BazaarAgentUiPlumbing.Tick` 注入 `BazaarAgentRuntimeController`（[BazaarAgentHostPlugin.cs:41-50](../../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentHostPlugin.cs)），controller 在 snapshot 发布后调用该回调（[BazaarAgentRuntimeController.cs:66-72](../../../src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentRuntimeController.cs)）。`BazaarAgentUiPlumbing.TryAdvanceReplay` 会在 `ReplayState && !IsReplaying` 时自动 `replay.Exit()`（[BazaarAgentUiPlumbing.cs:50-83](../../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentUiPlumbing.cs)）。这会让 `finishedAwaitingContinue` 只短暂存在甚至不可观测，外部脚本再调 `POST /v1/replay/continue` 时已经不是可继续阶段。实施本设计时必须删除这条自动退出路径；如果为拆分提交而暂留代码，也必须在生产路径永久关闭。`ReplayState.Exit()` 只能由新的显式 `continue` sink 调用。

---

## 5. 对外接口

### 5.1 三个原语（REST，挂在现有 `127.0.0.1:47900`）

| # | 原语 | 作用 | 落到游戏 |
|---|---|---|---|
| 1 | `POST /v1/replay/record` | 起播 + 开录 | `ReplayImportedBattle(manifest, payload, recordVideo:true)` |
| 2 | `GET /v1/context`（扩展 `replayPhase`/`replayBattleId`） | 让脚本知道「继续」何时可点 | 读 `AppState`/`ReplayState.IsReplaying`/runtime |
| 3 | `POST /v1/replay/continue` | 点「继续」收尾 | `ReplayState.Exit()` |

**`POST /v1/replay/record`**
```
Header  X-Bpp-Battle-Id: <battleId>        # 也可 ?battleId=;与 body 内 BattleId 交叉校验
Content-Type: application/x-bpp-ghostbattle+msgpack+gzip
Body    <原始二进制 = GhostBattlePayload 经 GhostBattlePayloadCodec 序列化>
```
- body **必须是三件套 `GhostBattlePayload`**（含 manifest）；裸 `PvpReplayPayload` 不行——`ReplayImportedBattle` 对 null manifest 抛异常、且 ghost 对手身份/头像只能从 manifest 重建（§7-d）。
- handler **不**做 `Encoding.UTF8.GetString`/JSON、**不**走 64KB 动作路径；按独立 cap 读原始字节。

| 状态码 | 含义 |
|---|---|
| `202` | 已受理、录制已装载（`ReplayImportedBattle` 返回 true）。**不代表 mp4 已生成。** body `{"accepted":true,"battleId":"...","status":"recording-started"}` |
| `400` | body 空 / gzip/msgpack 解码失败 / header 与 payload 的 battleId 不一致 |
| `409` | `CanReplaySavedCombats` 拒绝：游戏内进行中 / 已在 ReplayState / 已有回放启动中（同时即单飞）；或录制不可用（ffmpeg/`supportsAsyncGPUReadback`/视频目录）。**沿用既有码表，不引入 422** |
| `413` | body 超过新的每路由上限 |
| `503` | facade/runtime 未发布、recorder facade 为 null、队列已释放 |
| `500` | 未预期异常 |

**`GET /v1/context` 扩展**（`BazaarAgentContext` 新增字段）：
- `replayPhase`: `none` | `starting`（`IsReplayStartInProgress`，[CombatReplayRuntime.cs:43](../../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs)）| `playing`（`ReplayState && IsReplaying`）| `finishedAwaitingContinue`（`ReplayState && !IsReplaying`，[ReplayState.cs:282-285](../../../decompiled/TheBazaarRuntime/TheBazaar/ReplayState.cs)）
- `replayBattleId`: `CombatReplayRuntime.ActiveBattleId`（[:36](../../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs)）
- 沿用既有 ETag/`If-None-Match`/304 与「内容变化才 bump TickId」语义（[BazaarAgentHttpServer.cs:175-198](../../../src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs)）。

**`POST /v1/replay/continue`**（空 body）：驱动 `ReplayState.Exit()`。`200` 收尾已触发；`409` 当前不在可继续阶段（早到）；`503` runtime/facade 不可用。除该 endpoint 外，BazaarAgentHost 不得在 tick、snapshot 发布、UI plumbing 或后台 timer 中调用 `ReplayState.Exit()`。

### 5.2 主 mod 暴露的接口（在 ADR-0006 facade 旁新增，签名只含原语）

```csharp
// src/BazaarPlusPlus/GameInterop/BazaarAgent/IBazaarAgentReplayRecorder.cs
// GameInterop 不得 import Game.*，故签名禁用 PvpBattleManifest/PvpReplayPayload
public interface IBazaarAgentReplayRecorder
{
    bool TryStartRecord(byte[] ghostBattlePayloadBytes, string? battleId, out string failureReason);
    bool TryContinueReplay(out string failureReason);          // 驱动 ReplayState.Exit()
    BppReplayPhaseSnapshot GetReplayPhase();                   // (phase 枚举, battleId) —— 枚举/struct 定义在 GameInterop
}
```
- 经 `BazaarAgentGameBridge.CurrentRecorder`（public getter / internal setter，仿 `Current`，[BazaarAgentGameBridge.cs:10-15](../../../src/BazaarPlusPlus/GameInterop/BazaarAgent/BazaarAgentGameBridge.cs)）发布；host 只读。
- impl 持一个**原语委托**（仿 `BazaarAgentGameProbe` 的 `Func<bool>` 写法）；真正 decode `GhostBattlePayload`、过守卫、调 `ReplayImportedBattle`/`TryContinueReplay`/读 phase 的逻辑在 `BppComposition` 委托里（组合根可引 `Game.*`），并 **lazy** 读 `_combatReplayModule.Runtime`（发布时 Runtime 尚 null，[BppComposition.cs:128-135](../../../src/BazaarPlusPlus/BppComposition.cs)）。
- `CombatReplayRuntime` 新增 `bool TryContinueReplay(out string reason)`：校验 `AppState.CurrentState is ReplayState && !IsReplaying` 后调 `AppState.GetState<ReplayState>().Exit()` —— 与 dispatcher「调命令方法、让游戏动画/状态链像真点击一样跑」一致（[BazaarAgentGameActionDispatcher.cs:24-25](../../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs)）。如果需要保留现有 LevelUp recap-overlay 清理，迁入这个显式 continue sink 的执行链，而不是保留 tick 自动 `TryAdvanceReplay`。

---

## 6. 线程与编组模型

`ReplayImportedBattle` / `ReplayState.Exit()` / phase 读取都是 Unity 主线程专属；HTTP 在 `HttpListener` 线程池。复用既有「队列 + Tick drain」模型，但用**独立类型队列**承载二进制 + 命令：

```
HttpListener 池线程                         Unity 主线程 (host Update → controller.Tick)
─────────────────                          ────────────────────────────────────────
POST /v1/replay/record (原始字节, battleId)
 → queue.Enqueue({Start, bytes, battleId}) ─┐
   await TCS                                 │  Tick() 顶部(1.5s 快照门控之前):
POST /v1/replay/continue                     │   while queue.TryDequeue(cmd):
 → queue.Enqueue({Continue})  ──────────────┤     sink.Start/Continue(...)  ← 主线程调 facade
   await TCS                                 │     pending.SetResponse(resp)
   TCS 完成 ◄─────────────────────────────── ┘
 → 写 res
GET /v1/context → 直接读已发布快照(无需 drain)
```
- **必须在 Tick 顶部、1.5s 快照发布早退之前 drain**，否则录制请求会被节流到 1.5s 一个甚至超时（现 Tick 在门控处早退，[BazaarAgentRuntimeController.cs:56-79](../../../src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentRuntimeController.cs)）。
- **drain 顺序还必须早于任何 host UI plumbing。** 旧 `BazaarAgentUiPlumbing.TryAdvanceReplay` 不能继续在 snapshot 发布后自动退出 ReplayState；否则 `continue` 命令和 phase 观测会被抢跑。保留的 UI plumbing 只能做非 replay-exit 的一次性 overlay 清理，或把 replay-exit 相关逻辑移动到 `Continue` 命令处理器内。
- 队列复刻 `BazaarAgentActionQueue`：`TaskCompletionSource`(`RunContinuationsAsynchronously`) + `Timer` 超时 + disposed→503（[BazaarAgentActionQueue.cs:21-101](../../../src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentActionQueue.cs)）。`record` 的超时取较宽常量（主线程上 msgpack 解码 + `CombatReplayLoader.Load` 比一次决策重，但仍在一个 Tick 内完成 accept）。
- 队列与 server 在 `ReconcileListener` 内构造并注入（唯一接缝，[BazaarAgentRuntimeController.cs:99-110](../../../src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentRuntimeController.cs)）；server ctor 新增协作者（[BazaarAgentHttpServer.cs:36-47](../../../src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs)）。

---

## 7. 技术难点与对策

| # | 难点 | 对策 | 依据 |
|---|---|---|---|
| a | HTTP 线程 → Unity 主线程编组 | 独立命令队列 + host `Update→Tick` 顶部 drain（§6） | [BazaarAgentHostPlugin.cs:55](../../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentHostPlugin.cs) |
| b | 64KB 上限 + 二进制 vs JSON 解析器 | 新路由专用 raw-bytes handler，独立 `MaxRecordBodyBytes`；**实测 p99 后定上限**，大包倾向**流式落临时文件**而非整包进 `MemoryStream` | [BazaarAgentHttpServer.cs:17,202-232,241-257](../../../src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs)；ghost 下载走 R2 预签名 URL（大 blob）|
| c | 触达 internal `CombatReplayRuntime` 且不破分层 | 三层切：核心端口只 `byte[]` → host 调原语 facade → facade 持委托 → 组合根委托做游戏活 | [CoreLayeringTests.cs:30,352,375,377](../../../tests/Architecture.Tests/CoreLayeringTests.cs) |
| d | recorder 需要 manifest，调用方必须给 | body 强制三件套 `GhostBattlePayload`；校验 `header == payload.BattleId == manifest.BattleId` | `ReplayImportedBattle` 对 null manifest 抛异常 [CombatReplayRuntime.cs:218-221](../../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs) |
| e | 场景前置：不能游戏内开回放 | `CanReplaySavedCombats` 已门控并返回 reason → 映射 409；要求调用方在菜单/大厅，不从 HTTP 自动改运行态 | [CombatReplayRuntime.cs:96-119,223-227](../../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs) |
| f | 完成不可观测 + 回放不自动结束 | 收尾由 agent 点继续驱动（§4）；HTTP 用 202；产物 mp4 落 `<GameRoot>/BazaarPlusPlusV4/CombatReplayVideos/<date>/<battleId>.<stamp>.mp4`，外部按 `battleId` + 日期轮询新文件 | 文件名 [CombatReplayVideoRecorder.cs:821-823](../../../src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs)；目录 [BepInExPathProvider.cs:35](../../../src/BazaarPlusPlus/Core/Paths/BepInExPathProvider.cs)；metadata store 无读 API |
| g | 预检守卫必须完整 | 委托预检**逐字复用** `HistoryPanelReplayService.CanRecordReplay`（replay 可跑 + `supportsAsyncGPUReadback` + ffmpeg 可解析 + 视频目录已设）+ off-thread 预热 ffmpeg；任一不满足返回 409 而非 202 | recorder 在 `OnPlaybackStarting` 任一守卫失败即静默 return（回放照播、什么都不录），[CombatReplayVideoRecorder.cs:138-173](../../../src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs)；canonical 守卫 [HistoryPanelReplayService.cs:55-75](../../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelReplayService.cs) |
| h | 单飞/并发 | 主线程逐条 drain + `CanReplaySavedCombats` 对第二个并发 409；不另加锁。新 `Starting` 事件会 abort 在录会话——靠 409 拦在前面 | [CombatReplayVideoRecorder.cs:129-136](../../../src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs) |
| i | MessagePack public-graph 陷阱 | 复用现有全 public 的 `GhostBattlePayload` 图，不新增序列化 DTO；端口走 `byte[]`/JSON | `GhostBattlePayload`/`PvpBattleManifest`/`PvpReplayPayload` 均 public（[PvpReplayPayload.cs:4](../../../src/BazaarPlusPlus/Game/PvpBattles/PvpReplayPayload.cs)、[PvpBattleManifest.cs:6](../../../src/BazaarPlusPlus/Game/PvpBattles/PvpBattleManifest.cs)）|
| j | 既有 host 自动退出抢跑显式 continue | 删除或永久关闭 `BazaarAgentUiPlumbing.TryAdvanceReplay`；如仍需 `TryExitRecapReplayState` 的 LevelUp 清理，把它并入 `TryContinueReplay`/continue sink。新增测试证明 snapshot tick 不会退出 ReplayState，只有 `POST /v1/replay/continue` 会调用 `Exit()` | 当前自动退出链路：host 注入 `uiPlumbing.Tick`（[BazaarAgentHostPlugin.cs:41-50](../../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentHostPlugin.cs)）→ controller snapshot 后调用（[BazaarAgentRuntimeController.cs:66-72](../../../src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentRuntimeController.cs)）→ `TryAdvanceReplay` 调 `replay.Exit()`（[BazaarAgentUiPlumbing.cs:50-83](../../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentUiPlumbing.cs)）|

---

## 8. 代码组织与文件清单

| 程序集/层 | 文件 | 增量 | game-free |
|---|---|---|---|
| 核心 | `Contract/BazaarAgentPorts.cs` | 端口 `IBazaarAgentReplayControlSink{ Start(byte[],string?); Continue(); }` + 结果结构体 + `ReplayControlKind` + `record` 超时常量 | ✅ |
| 核心 | `Runtime/BazaarAgentReplayControlQueue.cs` | 新建命令队列（`{Kind,Payload?,BattleId?}`+TCS），复刻 ActionQueue | ✅ |
| 核心 | `Transport/BazaarAgentHttpServer.cs` | `POST /v1/replay/record`（原始字节、独立 cap）+ `POST /v1/replay/continue`；注入队列 | ✅ |
| 核心 | `Runtime/BazaarAgentRuntimeController.cs` | 注入 sink+queue；Tick 顶部 drain | ✅ |
| 核心 | context DTO（`Contract/BazaarAgentDecision.cs`） | `BazaarAgentContext` 加 `ReplayPhase`/`ReplayBattleId` | ✅ |
| 主 mod | `GameInterop/BazaarAgent/IBazaarAgentReplayRecorder.cs` + impl + `BppReplayPhaseSnapshot` | 新建（3 方法，原语签名；impl 持委托） | ✅（无 Game.* import）|
| 主 mod | `GameInterop/BazaarAgent/BazaarAgentGameBridge.cs` | 加 `CurrentRecorder { get; internal set; }` | ✅ |
| 主 mod | `Game/CombatReplay/CombatReplayRuntime.cs` | 加 `TryContinueReplay(out reason)` | 否（游戏调用，已在此层）|
| 主 mod | `BppComposition.cs` | 发布 `CurrentRecorder`（委托做 decode+守卫+调 runtime，lazy 读 Runtime）；Dispose 置 null | 否（组合根）|
| 宿主 | `BazaarAgentGameReplayControlSink.cs` | 实现核心端口，转发 facade，映射结果码 | 否（薄适配器）|
| 宿主 | `BazaarAgentHostPlugin.cs` | Awake 读 `CurrentRecorder`、建 sink、注入控制器 | 否 |
| 宿主 | `BazaarAgentGameContextReader.cs` | 调 `GetReplayPhase()` 填进 context | 否 |
| 宿主 | `BazaarAgentUiPlumbing.cs` | 删除/永久关闭 `TryAdvanceReplay` 自动 `ReplayState.Exit()`；只保留非 replay-exit overlay 清理，或把 LevelUp recap 清理迁入 continue sink | 否 |
| 测试 | `tests/Architecture.Tests/CoreLayeringTests.cs` | 守新边界（核心无 game/Unity、GameInterop facade 无 `Game.*`/`BazaarAgent` import） | — |
| 测试 | `tests/BazaarAgent.Tests` / host 可测边界 | 覆盖 replay queue HTTP 路由、snapshot replay 字段 ETag、以及“tick 不自动退出，continue 才退出”的行为 | — |

构建仍 `--with-bazaaragent` 才产出 host dll、默认构建照常清除两 dll —— **构建链不变**。

---

## 9. 实施步骤（有序）

1. 核心：`IBazaarAgentReplayControlSink` + 结果结构体 + `ReplayControlKind` + 超时常量。
2. 核心：`BazaarAgentReplayControlQueue`（命令 + TCS，独立宽超时）。
3. 核心：HTTP 两路由（record 原始字节 / continue 空体）+ 控制器 Tick 顶部 drain。
4. 核心：context DTO 加 `replayPhase`/`replayBattleId`。
5. 主 mod：`CombatReplayRuntime.TryContinueReplay`（校验后调 `ReplayState.Exit()`）。
6. 主 mod：`IBazaarAgentReplayRecorder`（3 方法）+ impl + `CurrentRecorder` + `BppReplayPhaseSnapshot`。
7. 主 mod：`BppComposition` 发布 facade，委托内 decode + 守卫（逐字复用 `CanRecordReplay`）+ 调 runtime；off-thread 预热 ffmpeg。
8. 宿主：sink 适配器 + Awake 接线 + context reader 读 phase。
9. 宿主：删除/永久关闭 `BazaarAgentUiPlumbing.TryAdvanceReplay` 的 tick 自动退出；需要的 LevelUp recap 清理迁入显式 continue sink。
10. 测试：补 replay HTTP 路由、队列 timeout、context replay 字段 clone/equality/ETag、以及“snapshot tick 不会 `Exit()`；`POST /v1/replay/continue` 才会 `Exit()`”。
11. 架构测试 + `./run.sh all --with-bazaaragent` + `./run.sh test`。

**先切最小闭环（建议）：** 步骤 5 + 6 + 7 的 `TryStartRecord` + `TryContinueReplay` + `GetReplayPhase`，先不做 body cap 优化与架构测试，**在线验证一场录制 mp4 完整可播**后再补齐路由/队列/测试（见 §11）。

---

## 10. 外部编排循环（参考实现，归调用方）

```python
for battle in batch:                         # battle = (battleId, ghost_payload_msgpack_bytes)
    r = POST("/v1/replay/record",
             headers={"X-Bpp-Battle-Id": battle.id,
                      "Content-Type": "application/x-bpp-ghostbattle+msgpack+gzip"},
             body=battle.msgpack)
    assert r.status == 202                    # 409=不在菜单/不可录;按需处理
    # 等回放结束、继续可点
    wait_until(lambda: GET("/v1/context").replayPhase == "finishedAwaitingContinue"
                       and GET("/v1/context").replayBattleId == battle.id)
    # （可选）想给视频留尾巴就在此 sleep 一会
    POST("/v1/replay/continue")               # → ReplayState.Exit() → finalize → 回菜单
    wait_for_file(f"CombatReplayVideos/*/{battle.id}.*.mp4")   # 异步 mux,按 battleId 轮询
```
- 快照每 1.5s 发布一次，phase 变化最多 1.5s 后可见；晚点继续只多 1.5s 尾巴，无害。
- 单飞由 `CanReplaySavedCombats` 强制，必须串行（一次一场）。

---

## 11. 开放问题与验证方法

- **【上线前必验】** 程序化触发 `ReplayImportedBattle(recordVideo:true)` → 轮询到 `finishedAwaitingContinue` → `continue` → 确认产物 mp4 **完整且可播放**（有 moov atom、含完整战斗）。验证手段：在主路径加临时探针/直接走最小闭环，游戏内跑一遍读 `BepInEx/LogOutput.log` 的 `[BPP][CombatReplayVideo]` 行 + 打开 mp4。
- **【上线前必验】** 不发 `POST /v1/replay/continue` 时，即使 BazaarAgent snapshot tick 多次运行，ReplayState 也必须停留在 `finishedAwaitingContinue`，不会自动 `Exit()`；发出 `continue` 后才 finalize 并回菜单。
- **body 上限：** 实测真实 `GhostBattlePayload` 的 p99 大小，定 `MaxRecordBodyBytes`；超过单包内存阈值则改流式落盘 + 传 path/stream 给解码。
- **continue facade vs 扩 `IBazaarAgentGameProbe`：** 本设计用独立 `IBazaarAgentReplayRecorder`（probe 文档化为只读/观察，录制是 mutating 二进制操作）；如倾向单一 facade 可合并，但需放宽 probe 的「只读」契约。
- **鉴权：** 现有路由仅 loopback、无鉴权；批量录制是否需要 shared-secret header（loopback-only 是否足够）。
- **continue 时序：** 默认 `finishedAwaitingContinue` 即继续（纯净战斗视频）；要尾巴由外部 sleep，API 不变。严禁用 host tick 自动推进来“省掉”外部 `continue`。

---

## 12. 被否决的备选

- **base64-msgpack 塞进 `/v1/actions` 的 JSON：** 膨胀 33% 仍撞 64KB、走 JSON 动作解析器、继承 1.5s 节流 + 3s 超时。❌
- **新 `BazaarAgentActionKind.RecordBattle`：** ActionKind 是稳定线协议、动作 DTO 无二进制字段、校验会 409 掉不在 `AvailableActions` 的 kind。❌
- **mod 内部 `Events.ReplayEnded` 自动退出收尾：** 会把收尾时机从编排方手里拿走，和显式 continue sink 的 contract 冲突；不作为主路径，也不保留 fallback。❌
- **host snapshot tick 后自动 `ReplayState.Exit()`（当前 `BazaarAgentUiPlumbing.TryAdvanceReplay` 风格）：** 会抢跑外部 `continue`、缩短/消除 `finishedAwaitingContinue` 可观测窗口，并把尾巴时长控制从外部脚本手里拿走。必须删除或永久关闭，不能作为 fallback。❌
- **整批拼成一个 mp4：** 本期不做；recorder 单飞/逐场 finalize,合并交外部 `ffmpeg concat`。❌
- **路径参数 `/v1/replay/{battleId}/record`：** 现路由是精确字符串匹配、无 path-param 解析；header/query 更简单。❌

---

> 相关文档：BazaarAgent 架构 [ADR-0006](../../adr/0006-bazaaragent-as-its-own-plugin.md)、wire 契约 [reference/bazaar-agent-http-api-v1.md](../../reference/bazaar-agent-http-api-v1.md)、回放/录制功能 [features/combat-replay.md](../../features/combat-replay.md)、ghost 数据流 [features/ghost-battle-data-flow.md](../../features/ghost-battle-data-flow.md)。
