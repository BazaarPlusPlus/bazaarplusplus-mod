# Choice Timeline：整局决策时间线记录 + run bundle 可选段

状态：设计稿，待确认后实施。
关联：正式 reopen ADR-0001（其 Consequences 明确写了 "If a real timeline consumer ever materializes (e.g. server-side run-path reconstruction), reopen this decision"——本方案就是那个消费者出现了）。

## 1. 目标

记录一局 run 中每个决策点的**提供了什么（offer）+ 玩家做了什么（action）+ 状态如何推进（state）**，粒度足以离线复原整局：商店/三选一提供的卡、买/卖/reroll、技能选择、遭遇选择、升级/附魔、放置与搬动、天数推进。产物：

1. 本地 SQLite `run_events` 里的新事件行（现有表，零 DDL 变更）。
2. run bundle artifact（gzip MessagePack）里的**可选** `timeline` 段——服务端零改动。

非目标：战斗内部过程（replay payload 已完整覆盖）、实时消费、在 mod 内做任何推导/归因/重建。

## 2. 与 ADR-0001 的关系

ADR-0001 否掉旧 EncounterTracker 的两条理由，本方案如何避开：

| 旧方案被否原因 | 本方案 |
| --- | --- |
| 没有真实消费者（export 脚本从未写、上传超范围） | 消费者明确：bundle 内 timeline 段 → analyzers/站点复原、以及 BazaarAgent 的真人决策语料（context→choice 对） |
| 大量脆弱推导代码（PVPCombat 归因、loot 归边、lineage、gap 检测、resync 标记） | **只记原始观测，不做任何推导**。断线重连只落一条 `sync` 标记（来自 GameStateSync 全量快照），重建留给离线端 |

实施时新增 ADR-0009 记录此决定，部分接替 ADR-0001。

## 3. 信号源（已核实）

**唯一采集口：入站 NetMessage seam，服务端权威，不 patch UI、不 patch 出站命令。**

- Seam 已存在且兼容 online/PTR：`Patches/NetMessageDispatchSeam.cs:22-34`（PTR 私有 `Receive(INetMessage, bool)` / online 公有 `ReceiveOrQueue`），并已剔除 spectate 回放（`IsSpectatePlayback`）。
- `CombatReplayCapturePatch.Postfix`（`Patches/Combat/CombatReplayCapturePatch.cs:22-25`）已把 `NetMessageGameSim`/`NetMessageCombatSim` 发布为 `NetMessageObserved` 事件总线消息——timeline 记录器**直接订阅即可**，唯一 patch 改动是把过滤集扩一型：`NetMessageGameStateSync`（重连/开局全量快照，用作 `sync` 标记）。

`NetMessageGameSim.Data`（`decompiled/.../GameSimEvents/GameSim.cs`）单条消息内齐备：

- `Events: List<IGameSimEvent>`——发生了什么：
  - `GameSimEventCardPurchased(InstanceId, BuyPrice, LeftSocketId, Section, CombatantId)`
  - `GameSimEventCardSold(InstanceId, SellPrice)`
  - `GameSimEventCardMoved` / `GameSimEventCardUpgraded(InstanceId, NewTier)` / `GameSimEventCardEnchanted(InstanceId, EnchantmentType, IsReverted)`
  - `GameSimEventPlayerSkillEquipped` / `GameSimEventPedestalActivated`
  - `GameSimEventCardTransformed` / `GameSimEventCardFused` / `GameSimEventCardDealt(InstanceId, TemplateId, Type)`
  - `GameSimEventStateTransitioned(ToState, CurrentEncounterId)` / `GameSimEventRunDayChanged` / `GameSimEventRunHourChanged` / `GameSimEventRunCompleted`
  - `GameSimEventRunRerollCostChanged(NewCost)` / `GameSimEventSocketsUnlocked` / `GameSimEventPlayerIncomeGained` / `GameSimEventPlayerExperienceGained`
- `CurrentState: SimUpdateRunState`——提供了什么：`StateName`、`CurrentEncounterId`、`SelectionSet`（可选卡 instance ids）、`SelectionContextRules`（`SelectionIsFree`/`CanSelectMultiple`/`CanExit`/`WillAutoSellOnExit`/`NextEncounterOnExit`/reroll 规则）、`RerollCost`/`RerollsRemaining`。
- `Cards: Dictionary<string, SimUpdateCard>`——卡增量（补 SelectionSet 中卡的 template/tier/enchant/attributes）。
- `Player: SimUpdatePlayer.Attributes`（`EPlayerAttributeType → int`）——金币/血量等，用于每个事件后的 `gold` 快照。

噪声排除：`GameSimEventEffectExecuted` / `EffectAuraExecuted` / VfxKeys 一律不进 timeline（战斗与展示噪声）；采用**显式 allowlist**，未知新事件类型静默跳过并 Debug 计数。

## 4. 架构落位

沿用现有 RunLogging 链路，分层遵守 GameInterop=游戏typed适配 / Game=特性策略：

```
CombatReplayCapturePatch (过滤集 +GameStateSync)
  → NetMessageObserved (事件总线，已有)
    → GameInterop/Timeline/RunTimelineProjector      // 纯映射：GameSim → List<RunTimelineEntry>（POCO，无策略）
    → Game/RunLogging/Timeline/RunTimelineRecorder    // 策略：仅 live run 时启用、去重 offer、封顶、组 RunLogEvent
      → RunLoggingController.AppendEvent (已有)
        → run_events (payload_json，已有表)
          → RunBundleUploadStore.TryBuildRunBundleSnapshot (已有) 读回 → RunArtifact.timeline
```

- `RunTimelineProjector`：无状态纯函数，game DTO → 中性 POCO。可用 exe-runner 测试直接喂手工构造的 `GameSim` 验证（MessagePack DTO 全 public，构造无碍）。
- `RunTimelineRecorder`：
  - 仅 `IsInGameRun` 时记录（回放态天然排除，回放不置 IsInGameRun——见 MEMORY「IsInGameRun cache is false during replay」；CombatSim/ReplayState 不在 allowlist 内双保险）。
  - **offer 去重**：`SelectionSet` 与上一条 offer 相同（同 state、同集合、同 reroll 计数）则不重复落 offer；SelectionSet 在同一 Choice 态内被整体替换 → 落一条 `reroll` action + 新 offer。
  - **封顶**：每 run timeline 事件数上限 5000，超限丢弃并在最后落一条 `truncated` 标记（不 silent）。
  - 写入走现有 `RunLoggingController` → `QueuedRunLogStore` 后台队列，主线程只做投影（微秒级）。

## 5. 数据模型

`run_events` 新 kind（全部带 `timeline_` 前缀，payload 放进 `RunLogEvent` 新增的一个可空 `Payload`(JSON string) 字段；RowSchemaVersion 11→12）：

| kind | 触发 | payload 要点 |
| --- | --- | --- |
| `timeline_offer` | SelectionSet 出现/变化 | state、encounterId、rules(free/multi/canExit/autoSell)、rerollCost/Remaining、`offers[]`（复用 `CardSetItemArtifact` 形状：instance/template/tier/enchant/attributes/section/socket） |
| `timeline_action` | allowlist 内的玩家动作事件 | action(purchase/sell/move/upgrade/enchant/skill_equip/pedestal/transform/fuse/reroll)、instanceId、templateId、price、socket/section、goldAfter |
| `timeline_state` | StateTransitioned / Day / Hour changed | toState、encounterId、day、hour |
| `timeline_sync` | NetMessageGameStateSync 到达（开局/重连） | state、day、hour、selectionSet 摘要——即"从这里起是全量重置"，替代 gap 检测 |

每条沿用 `RunLogEvent` 既有 `Seq`（run 内单调）+ `Ts` + `Day`/`Hour` 列，payload 内不重复。

bundle 侧（`ModApi/Models/RunBundleUploadRequest.cs` 的 `RunArtifact`）：

```csharp
public sealed class RunArtifact
{
    // 既有 run_id / battles 不动
    [JsonProperty("timeline_schema_version")] public int? TimelineSchemaVersion { get; set; }   // = 1
    [JsonProperty("timeline")] public List<RunTimelineEventArtifact>? Timeline { get; set; }     // 可空=旧包
}
```

`RunTimelineEventArtifact`：`seq`、`ts_utc`、`kind`、`day`、`hour`、`state`、`payload`（上表 kind 对应的强类型子对象，offer 复用现有 `CardSetItemArtifact`）。整图保持 public（MessagePack Unity/Mono 规则）。

## 6. 版本与兼容

- artifact 走 `ContractlessStandardResolver`（`ModApi/MessagePackGzipCodec.cs:19`）：旧读端遇到新字段静默跳过；服务端本就不解析 artifact（只存 R2）→ **服务端零改动，metadata `schema_version` 维持 5 不动**。
- artifact 内部自带 `timeline_schema_version` 独立演进；缺失 = 旧 mod 上传。
- 上传门槛不变：仍只有 Ranked + 完赛 + 非 PTR 的 run 进 bundle（`RunBundleUploadStore.GetPendingCompletedRunIds` 现有过滤），timeline 随 artifact 自然生效，无需新开关。本地 SQLite 则**所有 run 都记**（供本地 HistoryPanel 未来消费）。
- 局部 SQLite：零 DDL（payload_json 已是 TEXT）；`RunLogSchema.RowSchemaVersion` 11→12（RunLogEvent 增 `Payload` 字段）。

## 7. 体积/性能预算

- 事件量级：一局约 15-30 个 offer、100-300 个 action/state → 数百条；单条 payload ~150-400B JSON。
- artifact 增量：raw ~100-200KB，gzip 后 ~15-30KB——对比 replay payload（每场战斗数百 KB 的 msgpack bytes）可忽略。
- 主线程成本：GameSim 到达频率低（Player.log 实测一次会话 18 条），投影为一次 List 遍历；落库在既有后台队列。

## 8. 待运行时验证项（主路径临时 Debug 探针，不建独立脚手架）

1. 免费三选一（choice 卡)：确认走 `CardPurchased(BuyPrice=0)` 还是仅 `CardDealt`/SelectionSet 收缩——决定 `timeline_action` 的 purchase 语义标注。
2. Reroll：确认新 SelectionSet 与 `RunRerollCostChanged` 的到达顺序/同包性。
3. `SelectEncounterCommand` 对应的入站回声：Encounter 选择在 Events 里是 `StateTransitioned` 还是有独立事件。
4. SelectionSet 中卡的属性齐备度：`GameSim.Cards` 增量是否总含 offer 卡全量字段，不足时经由 ClientCache 兜底读（现有 `BppClientCacheBridge`）。
5. 升级/附魔 pedestal 流：`PedestalActivated` + `CardUpgraded/Enchanted` 的组合形状。

## 9. 明确不做

- 不 patch `Cmd.Send*`（出站命令可能被服务端拒绝，非权威）。
- 不记战斗内过程、不记 spectate/replay（seam 已滤 + IsInGameRun 门）。
- 不在 mod 内做 lineage/归因/gap 修复/重建——离线端职责。
- 不动 metadata `schema_version`、不改服务端、不改上传门槛。
- 不新建 SQLite 表。

## 10. 实施切分（单 PR 可容纳，按序落）

1. `RunLogEvent.Payload` + RowSchemaVersion 12 + 存取层透传（Storage）。
2. `GameInterop/Timeline/RunTimelineProjector` + POCO + exe-runner 测试（手构 GameSim 断言投影）。
3. `Game/RunLogging/Timeline/RunTimelineRecorder` + 订阅 NetMessageObserved + patch 过滤集加 `NetMessageGameStateSync`。
4. `RunArtifact.Timeline` DTO + `RunBundleUploadStore` 组装 + `TryDeserialize` 回读断言测试。
5. 真机验证（§8 五项）→ 摘除探针 → ADR-0009。

## 11. 开放决策点（需要用户拍板）

1. **本地是否默认全量记录**：建议是（无用户开关，数据量小；bundle 侧本就只随 Ranked 完赛上传）。若要开关，挂 `[RunLogging]` 配置节默认 ON。
2. **offer 卡快照的字段深度**：建议复用 `CardSetItemArtifact` 全形状（含 attributes dict），与 battle snapshots 一致，便于下游统一解析。
3. **HistoryPanel 本地消费**：本期不做 UI，仅落数据；未来"整局回顾"页作为独立 PR。
