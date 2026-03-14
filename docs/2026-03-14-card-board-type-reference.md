# 2026-03-14 Card / Board Type Reference

## 范围

这份文档整理几个容易混淆的概念：

- `EInventorySection`: 卡当前所在的库存区域
- `BazaarBoard.ESections`: 服务器/战斗板上的区域槽位
- `BazaarBoard.EOpponentBoardStatus`: `Opponent` 区当前展示的界面状态
- `ECardType`: 卡本身的类型

目标是回答几个常见问题：

- `Hand` / `Stash` 到底是不是 card type
- 商店牌属于哪个 section
- `Opponent` 区为什么会同时承载遭遇、商店、奖励等不同界面
- 不同 `ECardType` 分别大致对应什么场景

## 1. `EInventorySection`

定义：

- `decompiled/BazaarGameShared/BazaarGameShared.Domain.Core.Types/EInventorySection.cs`

当前只有两个值：

- `Hand`
- `Stash`

含义：

- `Hand`: 玩家主区域，通常就是当前摆放/交互中的主牌区
- `Stash`: 玩家仓库区

这个枚举描述的是“卡当前在哪个库存区域里”，不是“卡是什么类型”。

运行时映射可以在这里看到：

- `BazaarBoard.ESections.Player => EInventorySection.Hand`
- `BazaarBoard.ESections.Storage => EInventorySection.Stash`

参考：

- `decompiled/TheBazaarRuntime/CardController.cs`

## 2. `BazaarBoard.ESections`

定义：

- `decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarBoard.cs`

完整枚举：

- `Opponent`
- `Player`
- `Storage`
- `Count`

实际可用的只有前三个，`Count` 只是计数占位。

服务端板面数组映射：

- `Opponent => OpponentCardHand`
- `Player => PlayerCardHand`
- `Storage => PlayerStorageHand`

参考：

- `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs`

### 2.1 三个 section 的直观理解

- `Player`: 玩家当前主面板
- `Storage`: 玩家仓库面板
- `Opponent`: 上方展示栏

这里最容易误解的是 `Opponent`。

`Opponent` 不只是“对手牌区”，它更像一个统一的上方展示区域。这个区域会根据 `EOpponentBoardStatus` 切换显示不同内容，例如：

- 遭遇
- 商店
- 奖励
- 升级
- PVP 选择

所以商店没有单独的 `ESections.Shop`。

商店牌在 section 维度上属于：

- `BazaarBoard.ESections.Opponent`

## 3. `BazaarBoard.EOpponentBoardStatus`

定义：

- `decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarBoard.cs`

完整枚举：

- `NewHour`
- `Encounters`
- `CombatCards`
- `PVPCombatCards`
- `MerchantCards`
- `RewardCards`
- `LevelUp`
- `PVPSelectCards`
- `Count`

其中 `Count` 同样只是占位，不是实际状态。

### 3.1 各状态的直观含义

- `NewHour`: 新小时/新一轮刷新阶段
- `Encounters`: 普通遭遇选择界面
- `CombatCards`: PvE 战斗相关展示
- `PVPCombatCards`: PVP 战斗相关展示
- `MerchantCards`: 商店界面
- `RewardCards`: 奖励选择界面
- `LevelUp`: 升级选择界面
- `PVPSelectCards`: PVP 选择阶段

### 3.2 为什么商店不在单独 section

这套设计里：

- `ESections` 负责描述“牌放在哪个大区域”
- `EOpponentBoardStatus` 负责描述 `Opponent` 这个大区域“当前显示什么页面”

因此商店的表达方式是：

- section: `Opponent`
- status: `MerchantCards`

代码里发牌到商店时，也是直接发到 `ESections.Opponent`。

## 4. `ECardType`

定义：

- `decompiled/BazaarGameShared/BazaarGameShared.Domain.Core.Types/ECardType.cs`

完整枚举：

- `Item`
- `Skill`
- `CombatEncounter`
- `EncounterStep`
- `EventEncounter`
- `PedestalEncounter`
- `PvpEncounter`
- `SocketEffect`
- `PlayerEffect`

这是“卡本身是什么”的类型定义，和 `Hand` / `Stash` 不是一个维度。

### 4.1 各类型的场景说明

- `Item`: 普通物品牌。商店购买、放置、移动时最常见的类型
- `Skill`: 技能牌。点击时走技能选择逻辑，不走普通 item 购买逻辑
- `CombatEncounter`: 战斗遭遇牌
- `EncounterStep`: 遭遇过程中的步骤牌/阶段牌
- `EventEncounter`: 事件遭遇牌
- `PedestalEncounter`: pedestal 类遭遇牌
- `PvpEncounter`: PVP 遭遇牌
- `SocketEffect`: 插槽效果牌，附着在物品 socket 上的效果，不是普通 item
- `PlayerEffect`: 玩家身上的效果牌，更偏状态/效果数据，而不是普通商店或棋盘物品

### 4.2 运行时点击分支的一个简化理解

从 `CardController` 的点击逻辑看，可以粗略分成三类：

- `Skill`: 选择技能
- `Item`: 购买/放入玩家区域
- 各种 `*Encounter`: 选择遭遇

`SocketEffect` 和 `PlayerEffect` 不属于普通商店购买卡的主分支。

## 5. 四组概念怎么一起看

最实用的记忆方式如下：

- `ECardType`: 卡是什么
- `EInventorySection`: 卡现在放在哪个库存区域
- `ESections`: 板面上的哪个大区域
- `EOpponentBoardStatus`: 上方 `Opponent` 区当前演的是哪种界面

可以用下面这几个例子理解：

### 5.1 玩家主面板上的一个普通道具

- `ECardType = Item`
- `EInventorySection = Hand`
- `ESections = Player`

### 5.2 玩家仓库里的一件道具

- `ECardType = Item`
- `EInventorySection = Stash`
- `ESections = Storage`

### 5.3 商店里展示的一张可买道具

- `ECardType = Item`
- `ESections = Opponent`
- `EOpponentBoardStatus = MerchantCards`

注意：它不是 `Storage`，也不是单独的 `Shop` section。

### 5.4 上方遭遇区的一张战斗遭遇牌

- `ECardType = CombatEncounter`
- `ESections = Opponent`
- `EOpponentBoardStatus = Encounters`

### 5.5 物品上的 socket 效果

- `ECardType = SocketEffect`
- 通常不是普通商店牌
- 更像附着在 item/socket 上的效果牌

## 6. 结论

如果只记一条：

- `Hand` / `Stash` 是“放在哪”
- `Item` / `Skill` / `CombatEncounter` 等是“它是什么”
- 商店不单独占一个 `section`，而是 `Opponent + MerchantCards`

