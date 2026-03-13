# 2026-03-13 NetMessage Data Reference

## 范围

本文整理 decompiled 里两个最值得直接利用的消息体：

- `NetMessageCombatSim.Data`
- `NetMessageGameSim.Data`

目标不是完整抄写所有类型定义，而是沉降对 BazaarPlusPlus 更有价值的字段：

- 字段本身是什么
- 原生运行时如何消费
- 哪些字段已经被同步到 `TheBazaar.Data`
- 哪些字段只存在于原始消息里，若要用需要直接读 message

## 一、`NetMessageCombatSim.Data`

定义入口：

- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages/NetMessageCombatSim.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.CombatSimEvents/CombatSim.cs`

### 顶层字段

#### `Frames`

- 类型：`List<CombatSimFrame>`
- 作用：完整战斗时间线，`CombatSimHandler.Simulate()` 逐帧消费。
- 当前已知用途：
  - 每帧处理 `CardUpdates`
  - 每帧处理 `Events`
  - 每帧处理 `PlayerUpdates` / `OpponentUpdates`
  - 可以直接拿来估算战斗总帧数

对 BazaarPlusPlus 的价值：

- 战斗进度条
- 战斗日志 / 时间轴
- 每帧状态变化追踪

#### `Winner` / `Loser`

- 类型：`ECombatantId`
- 作用：战斗胜负结果。
- 当前 BazaarPlusPlus 已经用 `Winner` 推导 `LastVictoryCondition`。

对 BazaarPlusPlus 的价值：

- 标记战斗结果
- 区分胜/负后续上报和展示

#### `OpponentHealthThresholdsForGold`

- 类型：`List<float>`
- 作用：对手血量掉到哪些阈值时会给金币奖励。
- 原生运行时用途：
  - `BoardUIController.OnCombatSimReceived()` 遍历该列表，往敌方血条上加金币奖励节点。

对 BazaarPlusPlus 的价值：

- 直接显示怪物奖励断点
- 做怪物战收益预测

#### `OpponentHealthThresholdsForXp`

- 类型：`List<float>`
- 作用：对手血量掉到哪些阈值时会给经验奖励。
- 原生运行时用途：
  - `BoardUIController.OnCombatSimReceived()` 遍历该列表，往敌方血条上加经验奖励节点。

对 BazaarPlusPlus 的价值：

- 和金币阈值一起做战斗收益展示

#### `CardStats`

- 类型：`Dictionary<string, Dictionary<ECardStats, int>>`
- key：卡实例 `InstanceId`
- value：该卡在整场战斗中的统计累计

原生运行时用途：

- `BoardManager` 在战后 recap 卡片构造时直接读取。
- 如果没有对应统计，会默认塞一个 `UseCount = 0`。

已确认至少包含：

- `UseCount`
- `DamageDone`
- `JoyAdded`
- `ShieldAdded`
- `HealAdded`
- `PoisonAdded`
- `BurnAdded`
- `HastedCardsCount`
- `SlowedCardsCount`
- `FrozenCardsCount`
- `RegenAdded`
- `RageAdded`

对 BazaarPlusPlus 的价值：

- 战后 recap
- 单卡贡献排行
- “哪张卡打了多少伤害 / 触发了多少次”这类统计

#### `VfxKeys`

- 类型：`List<string>`
- 作用：战斗事件里的 `VfxIndex` 索引表。
- 原生运行时用途：
  - `CombatSimHandler.ExtractFrameEffects(...)` 和事件处理会结合它解析特效 key。

对 BazaarPlusPlus 的价值：

- 如果要做更细的技能/特效日志，不能只看 `VfxIndex`，还要反查这个表。

#### `PortraitKeys`

- 类型：`List<string>`
- 作用：战斗中头像变化索引表。
- 原生运行时用途：
  - `BoardManager.OnCombatSimReceived()` 缓存到 `_combatPortraitKeys`
  - 后续配合每帧 `Portrait.Index` 切头像 art key

对 BazaarPlusPlus 的价值：

- 战斗中头像状态切换跟踪
- 区分普通 / 狂暴 / 特殊立绘状态

### `CombatSimFrame`

定义：

- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.CombatSimEvents/CombatSimFrame.cs`

#### `Events`

- 类型：`List<ICombatSimEvent>`
- 作用：这一帧发生的战斗事件。

高价值事件：

- `CombatSimEventEffectTriggered`
  - 含 `ExecutionContextId`、`EffectId`、`Source`、`TriggerSource`、`Targets`
- `CombatSimEventEffectExecuted`
  - 含 `ExecutionContextId`、`EffectId`、`ActionType`、`VfxIndex`、`Source`、`TriggerSource`、`Target`
- `CombatSimEventEffectAuraExecuted`
  - 含 `AppliedTo` / `RemovedFrom`
- `CombatSimEventCardTransformed` / `CombatSimEventCardTransformReverted`
- `CombatSimEventCardEnchanted`
- `CombatSimEventCardQuestUpdated` / `CombatSimEventCardQuestCompleted`
- `CombatSimEventMonsterGoldReceived` / `CombatSimEventMonsterXpReceived`
- `CombatSimEventCombatantDied`

对 BazaarPlusPlus 的价值：

- 逐帧技能日志
- 触发链路归因
- 战斗动画 / 事件说明文本
- 怪物奖励触发时机跟踪

#### `PlayerUpdates` / `OpponentUpdates`

- 类型：`CombatSimPlayerUpdate`
- 作用：这一帧玩家/敌方玩家的状态更新。

字段：

- `IsPlayerDead`
- `HealthAdjustments`
- `Attributes`
- `Portrait`

其中：

- `HealthAdjustments` 的单项结构 `CombatSimPlayerHealthAdjustment` 还带：
  - `DamageType`
  - `AttributeChanged`
  - `Amount`
  - `IsCrit`
  - `IsDamageReduced`
- `Attributes` 的单项结构 `CombatSimPlayerAttributeUpdate` 带：
  - `AttributeType`
  - `PreviousValue`
  - `CurrentValue`
- `Portrait.Index` 用于索引 `PortraitKeys`

对 BazaarPlusPlus 的价值：

- 暴击/减伤/受伤来源展示
- 玩家属性变化流
- 狂暴等状态切换捕捉

#### `CardUpdates`

- 类型：`Dictionary<InstanceId, CombatSimCardUpdate>`
- 作用：这一帧卡片状态变化。

字段：

- `CardInstanceId`
- `Attributes`
- `Enchantment`
- `Heroes`
- `HiddenTags`
- `Placement`
- `Size`
- `Tags`
- `Tier`
- `State`

其中：

- `Attributes` 的单项结构 `CombatSimCardAttributeUpdate` 带前后值
- `State` 的单项结构 `CombatSimCardStateUpdate` 带前后状态

对 BazaarPlusPlus 的价值：

- 每帧卡片属性变化
- 位置/容器变化追踪
- 战斗中附魔、升阶、死亡、冻结等状态变化

### `NetMessageCombatSim.Data` 的总结

最值得直接利用的字段：

1. `Frames`
2. `Winner` / `Loser`
3. `CardStats`
4. `OpponentHealthThresholdsForGold`
5. `OpponentHealthThresholdsForXp`
6. `VfxKeys`
7. `PortraitKeys`

如果只做轻量功能，优先级建议：

1. `Winner`
2. `Frames.Count`
3. `CardStats`
4. `Frames[*].Events`
5. `Frames[*].PlayerUpdates` / `CardUpdates`

## 二、`NetMessageGameSim.Data`

定义入口：

- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages/NetMessageGameSim.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/GameSim.cs`

### 顶层字段

#### `Events`

- 类型：`List<IGameSimEvent>`
- 作用：本次 game sim 消息附带的事件流。
- 原生运行时：
  - `GameSimHandler.HandleMessage()` 先调用 `Data.UpdateFromGameSimAsync(message)`
  - 再处理 `message.Data.Events`

高价值事件：

- `GameSimEventStateTransitioned`
- `GameSimEventStateSuspended`
- `GameSimEventStateResumed`
- `GameSimEventRunDayChanged`
- `GameSimEventRunHourChanged`
- `GameSimEventRunRerollCostChanged`
- `GameSimEventRunCompleted`
- `GameSimEventCardSpawned`
- `GameSimEventCardDisposed`
- `GameSimEventCardPurchased`
- `GameSimEventCardSold`
- `GameSimEventCardMoved`
- `GameSimEventCardUpgraded`
- `GameSimEventCardFused`
- `GameSimEventCardTransformed`
- `GameSimEventPlayerExperienceGained`
- `GameSimEventPlayerIncomeGained`
- `GameSimEventPlayerPrestigeChanged`
- `GameSimEventSocketsUnlocked`

对 BazaarPlusPlus 的价值：

- 全局 run 状态机跟踪
- 经济 / 商店 / 升级 / encounter 生命周期跟踪
- 局内埋点和调试面板

#### `Player` / `Opponent`

- 类型：`SimUpdatePlayer`
- 字段：
  - `CombatantId`
  - `Attributes`

作用：

- 这是 run 内玩家和对手的属性增量更新。
- 原生会同步进 `Data.Run.Player` / `Data.Run.Opponent`。

对 BazaarPlusPlus 的价值：

- 不必自己重建整套玩家属性状态
- 可以直接截取局内属性变化

#### `Cards`

- 类型：`Dictionary<string, SimUpdateCard>`
- 作用：卡实例的增量更新表。

字段：

- `InstanceId`
- `Attributes`
- `Enchantment`
- `Heroes`
- `HiddenTags`
- `Placement`
- `Size`
- `Tags`
- `Tier`
- `State`

原生运行时：

- `Data.UpdateCardAttributes(message.Data.Cards)` 会把这些更新应用到 `Data.Entities`

对 BazaarPlusPlus 的价值：

- 当前所有局内卡状态的同步来源之一
- 可用于追踪购买、移动、升级、附魔之后的真实状态

#### `Run`

- 类型：`SimUpdateRun`
- 字段：
  - `Day`
  - `Hour`
  - `Victories`
  - `Defeats`
  - `HasVisitedFates`
  - `CurrentHourXP`
  - `DataVersion`

其中原生运行时已经同步到 `Data.Run` 的字段：

- `Day`
- `Hour`
- `Victories`
- `Defeats`
- `HasVisitedFates`

其中当前没有被 `DataExtensions.Update(this Run, SimUpdateRun)` 沉降的字段：

- `CurrentHourXP`
- `DataVersion`

这意味着：

- 如果 BazaarPlusPlus 需要这两个值，不能只看 `Data.Run`
- 必须直接读取原始 `message.Data.Run`

对 BazaarPlusPlus 的价值：

- `CurrentHourXP` 适合做当前小时经验进度展示
- `DataVersion` 适合做调试、兼容性、版本漂移判断

#### `CurrentState`

- 类型：`SimUpdateRunState`
- 字段：
  - `StateName`
  - `CurrentEncounterId`
  - `RerollCost`
  - `RerollsRemaining`
  - `SelectionSet`
  - `PvpOpponent`
  - `SelectionContextRules`
  - `PreviousRunStates`

原生已经同步到 `Data.CurrentState` 的字段：

- `StateName`
- `CurrentEncounterId`
- `RerollCost`
- `RerollsRemaining`
- `SelectionSet`
- `SelectionContextRules`

原生额外特殊处理：

- 如果 `PvpOpponent != null`，会同步到 `Data.SimPvpOpponent`
- 并进一步清洗 `PlayerLoadout`、更新对手收藏品信息

当前没有沉降到 `Data.CurrentState` 的字段：

- `PvpOpponent`
  - 沉降到了 `Data.SimPvpOpponent`
- `PreviousRunStates`
  - 目前只存在于原始消息

### `SimUpdateRunState` 里值得重点关注的字段

#### `CurrentEncounterId`

- 当前 encounter / 事件卡模板 id
- 原生状态切换时会解析并同步到 `Data.CurrentEncounterId`

对 BazaarPlusPlus 的价值：

- 怪物预览
- encounter 识别
- 商店/事件页面上下文判断

#### `RerollCost` / `RerollsRemaining`

- 商店/选择页直接有用
- 原生 `RerollButton` 就依赖这两个值

对 BazaarPlusPlus 的价值：

- 自定义 UI
- 商店状态面板

#### `SelectionSet`

- 当前可选择对象的实例 id 列表

对 BazaarPlusPlus 的价值：

- 识别当前候选项
- 做 encounter / loot / pedestal 的备选项跟踪

#### `PvpOpponent`

类型：`SimPvpOpponent`

字段：

- `Name`
- `TitlePrefix`
- `TitleSuffix`
- `Rank`
- `Rating`
- `Division`
- `Victories`
- `Prestige`
- `Level`
- `Hero`
- `PlayerLoadout`
- `PlayerCollection`

对 BazaarPlusPlus 的价值：

- PVP 对手信息展示
- 对手英雄和外观信息获取
- 对手负载、收藏品、棋盘资源推断

#### `SelectionContextRules`

类型：`TSelectionContextRules`

字段：

- `CanSelectMultiple`
- `SelectionIsFree`
- `CanExit`
- `RerollRules`
- `WillAutoSellOnExit`
- `NextEncounterOnExit`

对 BazaarPlusPlus 的价值：

- 判断当前是否免费选择
- 判断当前页面能否退出
- 判断是否允许多选
- 判断退出时是否自动卖出/跳转下一个 encounter

#### `PreviousRunStates`

- 类型：`List<SimUpdatePreviousRunState>`
- 单项字段：
  - `State`
  - `SequenceNumber`
  - `Day`
  - `Hour`

当前判断：

- 这是 run 状态栈/历史痕迹
- 原生当前没有看到它被同步进公共 `Data`

对 BazaarPlusPlus 的价值：

- 调试 run 状态机
- 排查中断状态、嵌套状态切换

#### `VfxKeys`

- 类型：`List<string>`
- 作用：`GameSim` 事件里 `VfxIndex` 的索引表

对 BazaarPlusPlus 的价值：

- 和 combat 的 `VfxKeys` 一样，用于把 effect 事件里的 index 还原成实际资源 key

## 三、对 BazaarPlusPlus 最有价值的字段清单

### 战斗相关

- `NetMessageCombatSim.Data.Winner`
- `NetMessageCombatSim.Data.Frames`
- `NetMessageCombatSim.Data.CardStats`
- `NetMessageCombatSim.Data.OpponentHealthThresholdsForGold`
- `NetMessageCombatSim.Data.OpponentHealthThresholdsForXp`
- `NetMessageCombatSim.Data.VfxKeys`
- `NetMessageCombatSim.Data.PortraitKeys`

### run / encounter 相关

- `NetMessageGameSim.Data.Run.Day`
- `NetMessageGameSim.Data.Run.Hour`
- `NetMessageGameSim.Data.Run.Victories`
- `NetMessageGameSim.Data.Run.Defeats`
- `NetMessageGameSim.Data.Run.HasVisitedFates`
- `NetMessageGameSim.Data.CurrentState.StateName`
- `NetMessageGameSim.Data.CurrentState.CurrentEncounterId`
- `NetMessageGameSim.Data.CurrentState.RerollCost`
- `NetMessageGameSim.Data.CurrentState.RerollsRemaining`
- `NetMessageGameSim.Data.CurrentState.SelectionSet`
- `NetMessageGameSim.Data.CurrentState.PvpOpponent`
- `NetMessageGameSim.Data.CurrentState.SelectionContextRules`

### 只在原始消息里、值得额外留意的字段

- `NetMessageGameSim.Data.Run.CurrentHourXP`
- `NetMessageGameSim.Data.Run.DataVersion`
- `NetMessageGameSim.Data.CurrentState.PreviousRunStates`

## 四、结论

如果只是做轻量功能，建议优先使用已经被原生沉降到全局 `Data` 的字段，因为稳定性更高：

- `Data.Run`
- `Data.CurrentState`
- `Data.SimPvpOpponent`
- `Data.Entities`

如果要做更强的调试、日志或高级 overlay，就要直接看原始消息，尤其是：

- `NetMessageCombatSim.Data.CardStats`
- `NetMessageCombatSim.Data.Frames[*].Events`
- `NetMessageGameSim.Data.Run.CurrentHourXP`
- `NetMessageGameSim.Data.Run.DataVersion`
- `NetMessageGameSim.Data.CurrentState.PreviousRunStates`

## 五、`TheBazaar.Data.Run` 本体

`Data.Run` 不是消息 DTO，而是客户端运行时模型：

- 定义：`decompiled/BazaarGameClient/BazaarGameClient.Domain.Models/Run.cs`
- 初始化：`decompiled/TheBazaarRuntime/TheBazaar/Data.cs`

### `Data.Run` 的字段

`Run` 类本体字段如下：

- `GameModeId`
- `DataVersion`
- `HasVisitedFates`
- `Hour`
- `Day`
- `Victories`
- `Losses`
- `Player`
- `Opponent`

其中：

- `Player` / `Opponent` 是完整 `Player` 运行时对象，不是 DTO
- `Data.Initialize()` 时会创建 `Run = new Run()`，并初始化 `Run.Player` 与 `Run.Opponent`

### 哪些字段会被原生消息更新

#### 通过 `RunSnapshotDTO` / `StateSync` 更新

`DataExtensions.Update(this Run, RunSnapshotDTO)` 当前会写入：

- `GameModeId`
- `Day`
- `Hour`
- `Losses`
- `Victories`
- `HasVisitedFates`

注意：

- `RunSnapshotDTO` 里虽然有 `DataVersion`
- 但原生 `DataExtensions.Update(this Run, RunSnapshotDTO)` 没有把它写进 `Run.DataVersion`

#### 通过 `SimUpdateRun` / `GameSim` 更新

`DataExtensions.Update(this Run, SimUpdateRun)` 当前会写入：

- `Day`
- `Hour`
- `Losses`
- `Victories`
- `HasVisitedFates`

注意：

- `SimUpdateRun` 里有 `CurrentHourXP`
- `SimUpdateRun` 里也有字符串版 `DataVersion`
- 但原生同样没有把这两个值写进 `Data.Run`

### `Data.Run` 字段的当前使用价值

#### 稳定可直接读的字段

- `GameModeId`
- `Day`
- `Hour`
- `Victories`
- `Losses`
- `HasVisitedFates`
- `Player`
- `Opponent`

这些字段已经被原生运行时大量消费，可以视为相对稳定的公共状态。

#### 不能指望 `Data.Run` 持有最新值的字段

- `DataVersion`

虽然 `Run` 类上有这个字段，但从当前 decompiled 链路看：

- `RunSnapshotDTO.DataVersion` 没有写进去
- `SimUpdateRun.DataVersion` 也没有写进去

当前判断：

- `Data.Run.DataVersion` 这个字段在原生链路里大概率是“定义存在，但没有被当前消息更新逻辑真正维护”

如果 BazaarPlusPlus 需要版本信息，应优先直接读：

- `RunSnapshotDTO.DataVersion`
- `NetMessageGameSim.Data.Run.DataVersion`

而不是依赖 `Data.Run.DataVersion`。

### 对 BazaarPlusPlus 的建议

如果你的目标是“拿当前局信息”，优先级建议如下：

1. 日常逻辑直接读 `Data.Run.Day` / `Hour` / `Victories` / `Losses` / `HasVisitedFates`
2. 玩家/对手实体直接读 `Data.Run.Player` / `Data.Run.Opponent`
3. 需要 `CurrentHourXP`、`DataVersion` 时，不要从 `Data.Run` 取，直接读原始消息

## 参考文件

- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages/NetMessageCombatSim.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.CombatSimEvents/CombatSim.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.CombatSimEvents/CombatSimFrame.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.CombatSimEvents/CombatSimCardUpdate.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.CombatSimEvents/CombatSimPlayerUpdate.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages/NetMessageGameSim.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/GameSim.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/SimUpdateRun.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/SimUpdateRunState.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/SimPvpOpponent.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Domain.Runs/TSelectionContextRules.cs`
- `decompiled/BazaarGameClient/BazaarGameClient.Domain.Models/Run.cs`
- `decompiled/BazaarGameClient/BazaarGameClient.Domain.Models/RunState.cs`
- `decompiled/TheBazaarRuntime/TheBazaar/DataExtensions.cs`
- `decompiled/TheBazaarRuntime/TheBazaar/Data.cs`
- `decompiled/TheBazaarRuntime/TheBazaar/GameSimHandler.cs`
- `decompiled/TheBazaarRuntime/TheBazaar/CombatSimHandler.cs`
- `decompiled/TheBazaarRuntime/TheBazaar.UI.Components/BoardUIController.cs`
- `decompiled/TheBazaarRuntime/BoardManager.cs`
