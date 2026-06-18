# 售卖卡牌商店单卡生成概率分析

状态：draft
最后核对：2026-06-15

> **📚「进商店逻辑」文档系列 · 建议阅读顺序**（你在读 **#4 参考**）
> 1. [总览 · 从进店到离店的完整流程](2026-06-15-shop-entry-1-lifecycle.md)
> 2. [讲解 · 铺货 / 单卡概率怎么算（TL;DR + 流程图 + 名词词典）](2026-06-15-shop-entry-2-stocking-explainer.md)
> 3. [深入 · native/loose + 英雄 + RNG 顺序 + 手牌 + 数据缺口](2026-06-15-shop-entry-3-native-loose-deep-dive.md)
> 4. [参考 · 单卡概率逐行推导 + Collection Panel 接入](2026-06-15-shop-entry-4-single-card-reference.md) ← 本文
> 5. [背景 · 旧 dealer 是什么 / 为何在客户端 / 是否线上权威](2026-06-15-shop-entry-5-client-server-boundary.md)
> 6. [落地设计 · Collection Panel 商店概率浮层](2026-06-15-collection-panel-shop-probability-design.md)

本文专门回答一个问题：在旧
`BazaarBattleService.BazaarCardDealer.DealFilteredCards(...)` 实现里，玩家进入一个
**售卖卡牌的商店**后，某一张目标卡最终出现在货架上的概率应如何拆解、估算或模拟。

本文只把旧 dealer 当作一份可逐行分析的客户端内实现。当前运行时开局会初始化服务器会话并等待
`GameSim` 消息，客户端再按 `GameSimEventCardDealt` / `GameSimEventCardSpawned` 渲染卡牌
（`decompiled/TheBazaarRuntime/TheBazaar/StartRunAppState.cs:134-168`，
`decompiled/TheBazaarRuntime/Networking/NetworkManager.cs:79-113`，
`decompiled/TheBazaarRuntime/Networking/HttpGameClient.cs:244-253`，
`decompiled/TheBazaarRuntime/TheBazaar/GameSimHandler.cs:32-48`，
`decompiled/TheBazaarRuntime/TheBazaar/GameSimHandler.cs:51-76`）。因此，旧 dealer 的概率模型
**不能直接断言为当前线上后端实现**；线上权威结论仍需要服务器源码、抓包样本和当前权重数据验证。

本文排除纯技能商店。技能路径只作为边界说明：旧 dealer 在候选池全是技能时，会进入
`RollSkillTierAndFilter(...)`，绕开物品商店的 native / loose 分支
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3497-3519`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:5114-5172`）。

> 引用约定：本文使用相对仓库路径加行号。例：
> `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3548`。

---

## 1. 先定义要算的事件

目标事件不是“某卡被抽到某个品级”，而是：

> 给定 hero、shop template、day/hour、目标卡 template id 和 player/reroll state，旧
> `DealFilteredCards(...)` 在一次商店发牌中至少一次选择该 template id，并最终尝试把它发到对手区货架。

这一定义来自最终发牌循环：普通物品路径先把选中的卡 id 放进 `list2`，品级放进 `list3`，循环结束后才逐张
`DealCard(..., BazaarBoard.ESections.Opponent, ..., list3[num7])`，并把 id 加入
`DealtCardForReRollExclusion`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3574-3580`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3596-3605`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3626-3629`）。

因此，单卡概率不是“候选池有 100 张就每张 1%”这种单步模型。旧 dealer 至少叠加了：

- 初始候选池资格。
- 固定 `CardIdFilters` 直发路径。
- 玩家已装备技能排除。
- reroll exclusion 排除或清空。
- 技能商店特殊路径排除。
- 每个槽位的 native gate。
- 每个槽位的 tier roll。
- native 分支 `StartingTier == rolled tier` 后的同起始品级 uniform 抽取。
- loose 分支 `StartingTier <= rolled tier` 后的存活池 uniform 抽取。
- `list4 = list10` 的路径依赖收窄。
- 最终 `DealCard` 按记录品级升级或保持卡实例品级。

每一项都有代码入口：`DealFilteredCards` 从清空货架、建池、reroll、技能分支、day/tier 读取，到逐槽位选择和最终发牌，集中在
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3437-3632`。

---

## 2. 输入清单：计算前必须收集什么

| 输入 | 旧 dealer 如何使用 | 代码证据 | 计算时如何定位 |
| --- | --- | --- | --- |
| 玩家英雄 | 商店卡若没有 `MerchantHeroFilters`，候选池 hero filter 使用 `bazaarPlayer.Hero` | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3442-3449` | 从玩家状态读取 hero；若商店模板有 `SpawningFilters.MerchantHeroFilters`，优先用该列表 |
| 当前商店模板 | `DealFilteredCards` 的第二个参数 `card` 就是被选中的商店/奖励/遭遇模板；商店效果 `LootMerchantCards` 会调用该函数 | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:2916-2928` | 若已知 shop template id，直接查该卡；若要从 day/hour 推导商店，先走 encounter 生成 |
| day/hour encounter 配置 | 旧 dealer 通过 day/hour manager 取本小时 encounter ids，再按 merchant/combat/event/flex 分类筛选 | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4746-4783`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4491-4525`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4889-4939` | 对“已进入某商店”的问题可跳过；对“某天某小时能遇到哪家商店”则需要 `dayByHourManager.json` 和 encounter tier 权重 |
| 商店是否售卖卡牌 | 旧 dealer 没有单独的“card shop”布尔；它先用商店 `SpawningFilters` 建池，若建池后全部是 `Skill` 则走技能路径 | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3497-3519` | 本文只分析建池后不是全技能的商店；纯技能商店另算 |
| 商店 filters | `CardIdFilters`、`NumberCardsToSpawn`、`CardTypeFilters`、`MerchantHeroFilters`、`CardSizeFilters`、`CardTagFilters`、`ItemTierFilters`、`HiddenTagFilters`、`Rerolls.RerollRepeats` 等都在 `SpawningFilter` 上 | `decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarCard.cs:20-66` | 从商店模板的 `SpawningFilters` 读 |
| 目标卡 template id | 固定发牌、reroll exclusion、最终 `list2` 都按 `Guid` id 操作 | `decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarCard.cs:239-247`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3574-3580` | 查静态卡模板，计算目标 id 是否进入各阶段池 |
| 目标卡 hero/type/size/tags/hidden tags/enabled | `FilterCards(...)` 在非 `CardIdFilters` 路径按 hero、type、size、card tag、hidden tag、`SpawningFilters.Enabled` 做 AND 过滤 | `decompiled/BazaarBattleService/BazaarBattleService.Repositories.CardRepository/StaticDataCardRepository.cs:161-171` | 从目标卡模板读取 `Heroes`、`CardType`、`CardSize`、`CardTags`、`HiddenTags`、`SpawningFilters.Enabled` |
| 目标卡 `StartingTier` | native 分支要求 `StartingTier == rolled tier`；loose 分支要求 `StartingTier <= rolled tier` | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3564-3567`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3583-3590` | 从目标卡模板读取；枚举顺序是 Bronze < Silver < Gold < Diamond < Legendary |
| 当前 day/hour | day 被钳到 `gameConfig.NumDays`，hour 用于 `GetByDayAndHour` 取 `NativeItemTierProbability`，day 用于 `GetProbabilitiesByDay` | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3521-3531`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3552-3558` | 从玩家 run state 读取 day/hour；旧 dealer 的 day 上限来自 `BazaarBattleSettings.NumDays` |
| 当天 tier 权重 | 每个槽位调用 `tierRepo.GetProbabilitiesByDay(day)`，再 `SelectRandomTier(...)` | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3552-3558`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4451-4464` | 旧 dealer 读 `tierManager.json`；当前线上权威表需要服务器或抓包验证 |
| `NativeItemTierProbability` | 每槽位在无 `ItemTierFilters` 且 native 未失败时，用 `rng < nativeItemTierProbability` 决定 native 分支 | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3526-3531`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3544-3551` | 旧 dealer 读 day/hour manager；当前线上权威值需要服务器或抓包验证 |
| 玩家已装备技能 | 初始池排除玩家技能槽里的同 id 卡 | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3456-3459`，`decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarBoard.cs:106-108` | 收集 `Board.PlayerSkillCard` 的 template ids |
| 玩家当前手牌 | 只影响重复升级记录品级，不决定目标 id 是否被选中 | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3577`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3599-3601`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4467-4489` | 收集 `Board.PlayerCardHand` 中同 id、`Tier < Diamond` 的卡 |
| 货架/棋盘空位 | `DealFilteredCards` 每槽位调用 `FilterByRemainingBoardSize(..., isPlayerCarpet:false)`，实际看的是对手区货架空位；入口先清空对手区 | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3439`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3559`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4608-4625` | 对普通商店通常从清空货架开始；若先发经验卡或其他卡占位，需要把对手区剩余空位纳入 |
| reroll 状态 | 若 `RerollRepeats` 为 false，则先按 `DealtCardForReRollExclusion` 排除；排除后不够发就清空排除集 | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3485-3496`，`decompiled/BazaarBattleService/BazaarBattleService/GameStateMetadata.cs:29-31` | 收集当前 exclusion set；同时知道本商店 `Rerolls.RerollRepeats` |
| `NumberCardsToSpawn` | 决定要尝试选择多少槽；为 0 直接 return；固定路径会和 `CardIdFilters.Count` 比较 | `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3467-3484`，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3532-3545` | 从商店模板读 |

旧 dealer 的静态数据来源也是输入的一部分。它在 `InitializeAsync(...)` 中从 `StaticFolderPath` 组合
`cards.json`、`dayByHourManager.json`、`tierManager.json` 等文件初始化仓库
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:390-407`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarBattleSettings.cs:15-31`）。当前客户端运行时则下载
`GameData.db.zip` 并用 `JsonGameDataManager` 读 `cards` / `game_modes` 等 SQLite 表
（`decompiled/TheBazaarRuntime/TheBazaar.DataManagement/DataDownloader.cs:65-72`，
`decompiled/TheBazaarRuntime/TheBazaar.DataManagement.Json/JsonGameDataManager.cs:73-90`，
`decompiled/TheBazaarRuntime/TheBazaar.DataManagement.Json/JsonGameDataManager.cs:149-152`）。这说明“可在客户端查到卡模板”
和“可在客户端拿到线上权威发牌权重”是两件事；当前客户端模型里 tier 源字段还带
`[BazaarObfuscate]`，序列化器在 obfuscate 模式会跳过这些属性
（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Game/TGameMode.cs:18-25`，
`decompiled/BazaarGameShared/BazaarGameShared.Domain.Game/TGameMode.cs:62-77`，
`decompiled/BazaarGameShared/BazaarGameShared.Infra.Serialization/BazaarJsonDerivedTypeConverter.cs:75-78`）。

---

## 3. 候选池构造：目标卡先要活过哪些确定性关卡

### 3.1 商店入口会先清空货架

`DealFilteredCards` 的第一步是 `RemoveAllCardsFromOpponent(bazaarPlayer.Board)`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3437-3439`）。
`RemoveAllCardsFromOpponent` 清空的是 `OpponentCardHand` 和 `OpponentSkillCard`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarDeckTools.cs:634-643`）。

这意味着本文计算的是“发到商店货架”概率。玩家购买时自己的棋盘/仓库是否能放下，是另一个选择动作的约束；
`PlayerSelectedCard` 里购买物品/奖励/技能时会检查玩家区或仓库能否容纳，失败会触发 `No_Space`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:1698-1704`）。

### 3.2 hero filter 的来源不是固定为玩家英雄

商店模板如果有 `SpawningFilters.MerchantHeroFilters`，旧 dealer 直接使用它；否则才把
`bazaarPlayer.Hero` 放入 hero filter
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3442-3449`）。

所以目标卡进入池的 hero 条件是：

- 若商店有 `MerchantHeroFilters`：目标卡 `Heroes` 与该 filter 有交集。
- 若商店没有 `MerchantHeroFilters`：目标卡 `Heroes` 与玩家 hero 有交集。

实际 repository 谓词是 `!heroFilters.Any() || heroFilters.Intersect(p.Value.Heroes).Any()`
（`decompiled/BazaarBattleService/BazaarBattleService.Repositories.CardRepository/StaticDataCardRepository.cs:164-166`）。

### 3.3 `CardIdFilters` 非空时是完全不同的池构造

`StaticDataCardRepository.FilterCards(...)` 有两个分支：

- `CardIdFilters.Any()` 为 true 时，只保留 id 在 `CardIdFilters` 中的卡。
- 否则才按 hero、type、size、card tag、hidden tag、`SpawningFilters.Enabled` 逐项过滤。

代码证据在
`decompiled/BazaarBattleService/BazaarBattleService.Repositories.CardRepository/StaticDataCardRepository.cs:161-171`。

因此，目标卡的初始入池条件是：

```text
if shop.CardIdFilters 非空:
    target.Id in shop.CardIdFilters
else:
    hero_match
    and (shop.CardTypeFilters empty or target.CardType in filter)
    and (shop.CardSizeFilters empty or target.CardSize in filter)
    and (shop.CardTagFilters empty or target.CardTags intersects filter)
    and (shop.HiddenTagFilters empty or target.HiddenTags intersects filter)
    and target.SpawningFilters.Enabled == true
```

这一步没有 `ItemTierFilters`；`ItemTierFilters` 只在逐槽位选择阶段使用
（`decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarCard.cs:60`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3548`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3583-3587`）。

### 3.4 玩家已装备技能会先排除同 id

旧 dealer 读取 `GetBazaarBattlePlayer().Board.PlayerSkillCard` 中的 id，然后把初始池里同 id 的卡排除
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3456-3459`）。
`PlayerSkillCard` 是 4 格技能槽
（`decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarBoard.cs:106-108`）。

这一步会影响目标卡“是否出现”，但只针对技能槽，不针对玩家手牌。玩家手牌只在后面的重复升级逻辑里读取
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4467-4489`）。

固定 `CardIdFilters` 路径有一个边界：旧 dealer 在做固定直发前确实检查 `list4.Any()`，但一旦进入
`NumberCardsToSpawn == CardIdFilters.Count` 分支，就按原始 `CardIdFilters` 逐个 `DealCard`，不再遍历
`list4`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3472-3483`）。所以如果一个固定列表里有多张卡，且至少一张活过了“已装备技能”排除，直发循环仍会尝试发出列表中的每个 id。

### 3.5 reroll exclusion 是条件排除，不是必然排除

若 `Rerolls.RerollRepeats` 为 false，旧 dealer 用
`GameStateMetadata.DealtCardForReRollExclusion` 过滤当前 `list4`；过滤后数量仍不少于
`NumberCardsToSpawn` 才采用过滤结果，否则清空整个 exclusion set 并保留原池
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3485-3496`）。

每次成功发出技能路径或物品路径的卡，id 会被加入该 set
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3514-3518`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3626-3629`）。
该字段定义为 `List<Guid> DealtCardForReRollExclusion`
（`decompiled/BazaarBattleService/BazaarBattleService/GameStateMetadata.cs:29-31`）。
遭遇切换、小时推进、退出、部分 override 等路径会清空它
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:1992-1996`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:2329-2335`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:2438-2444`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:5861-5878`）。

概率上不能只判断“target 在 exclusion set 中”。正确条件是：

```text
if !RerollRepeats:
    L_excluded = L0 without exclusion_set
    if |L_excluded| >= NumberCardsToSpawn:
        L0 = L_excluded
    else:
        exclusion_set is cleared and L0 unchanged
```

### 3.6 纯技能商店在这里停止分析

在 reroll 过滤之后，如果 `list4.All(pair => pair.Value.CardType == Skill)`，旧 dealer 会走技能路径并立即
`return`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3497-3519`）。技能路径每槽调用
`RollSkillTierAndFilter(...)`，按 day tier 权重先找 `StartingTier == tier` 的技能，没货时再反向扫描按概率排序后的
tier 列表
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:5114-5172`）。

本文只分析没有进入该分支的卡牌商店。注意代码条件是“候选池全是 Skill”，不是“商店 filter 明确写了 Skill”。
一个混合池不会进入技能特殊路径。

---

## 4. 固定 `CardIdFilters` 商店：概率可能是确定的

如果 `NumberCardsToSpawn == CardIdFilters.Count`，旧 dealer 直接按 `CardIdFilters` 的顺序逐张
`DealCard(...)`，然后 `return`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3477-3483`）。

在该路径下，目标卡出现概率不是 native/loose 模型，而是：

```text
P(target appears) =
    0, 如果初始池为空导致提前 return
    1, 如果初始池非空且 target.Id in shop.CardIdFilters
    0, 如果初始池非空且 target.Id not in shop.CardIdFilters
```

“初始池为空”仍要先过 `FilterCards` + 已装备技能排除，因为空池会在固定直发前 return
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3456-3475`）。
但固定直发循环本身不使用 `list4`，而是直接使用原始 `CardIdFilters`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3477-3483`）。

如果 `CardIdFilters` 非空但数量不等于 `NumberCardsToSpawn`，则它只是把初始池限制为这些 id，后续仍走
reroll、技能检测、native/loose 随机选择
（`decompiled/BazaarBattleService/BazaarBattleService.Repositories.CardRepository/StaticDataCardRepository.cs:161-171`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3477-3499`）。

---

## 5. 每个槽位的物品商店概率路径

从这里开始假设：

- 初始池 `L0` 非空。
- 没有进入固定直发路径。
- 没有进入纯技能路径。
- 目标卡 `c` 仍在当前存活池中。

旧 dealer 先取 day/hour 的 `NativeItemTierProbability`，再把 `NumberCardsToSpawn` 存为本次循环次数
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3521-3532`）。
如果 `list4.Count < NumberCardsToSpawn`，代码只记录错误并把局部变量设为 `list4.Count`，但实际循环在
`else` 分支里，所以这个分支不会进入逐槽位循环
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3532-3539`）。

### 5.1 每槽位先决定 native gate，再 roll tier，再过滤空间

逐槽位循环从 `num5 = 0` 到 `numberCardsToSpawn - 1`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3544-3545`）。

native 分支只在三项同时满足时打开：

```text
shop.ItemTierFilters.Count == 0
and flag2 == false
and rng < NativeItemTierProbability
```

代码证据：
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3544-3551`。
`flag2` 初始为 false；第一次 native miss 后会被置为 true
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3564-3572`）。

无论 native gate 是否打开，每个槽位都会从当天 tier 权重抽一个 `ItemTier`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3552-3558`）。
`SelectRandomTier(...)` 用 `seedManager.GetDouble()`，按概率值升序累加，命中首个累计和大于等于随机数的
tier，未命中时回退 `Bronze`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4451-4464`）。
如果权重都是非负且总和为 1，单个 tier 的区间长度就是它的权重；如果总和小于 1，剩余概率会落到
`Bronze` fallback。

然后每槽位都会调用 `FilterByRemainingBoardSize(bazaarPlayer, list4, isPlayerCarpet:false)`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3559`）。该函数在
`isPlayerCarpet:false` 时读取 `OpponentCardHand` 空位数，并保留 `emptySockets >= CardSize` 的卡
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4608-4625`）。
入口已经清空对手区货架
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3437-3439`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarDeckTools.cs:634-643`），普通商品又要到选择循环结束后才真正发牌
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3626-3629`）。所以普通商店里这个
size filter 通常看到的是空货架；若商店先发了经验卡，则经验卡会在主循环前占用对手区位置
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3463-3465`）。

### 5.2 native 严格分支：`StartingTier == rolled tier` 后 uniform

当 `flag3` 为 true，旧 dealer 计算：

```text
S_native(T) = { x in L : x.StartingTier == T }
```

代码是 `list4.Where(p => p.Value.StartingTier == ItemTier).ToList()`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3564-3567`）。

若 `S_native(T)` 为空，本槽位没有选择任何卡，`num5--` 重试同一个槽位，`flag2 = true`，然后
`continue`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3567-3572`）。
因为 `flag2` 是循环外局部变量，后续所有槽位都不会再进 native gate
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3544-3548`）。

若 `S_native(T)` 非空，旧 dealer 在 `S_native(T)` 中 uniform 选一张：
`list9[seedManager.GetNumber(list9.Count)]`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3574`）。
目标卡在该槽位 native 成功路径的条件概率是：

```text
P(c selected | native, rolled T, S_native(T) nonempty, c in L)
  = 1 / |S_native(T)|, if c.StartingTier == T
  = 0, otherwise
```

native 成功后，旧 dealer 只 `list4.Remove(item2)`，不会把 `list4` 替换成同 tier 集合
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3577-3580`）。
这点和 loose 分支不同。

### 5.3 loose / 非原生分支：`StartingTier <= rolled tier` 后 uniform

进入 loose 分支有三种常见原因：

- native gate 没过。
- `ItemTierFilters.Count > 0`，native gate 被条件直接关闭。
- 之前某次 native miss 把 `flag2` 置为 true。

代码证据：
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3544-3551`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3567-3572`。

若商店有 `ItemTierFilters`，loose 分支不会使用 day tier roll，而是从
`ItemTierFilters` 中 uniform 选择一个目标 tier：
`ItemTierFilters[seedManager.GetNumber(ItemTierFilters.Count)]`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3583-3585`）。
若没有 `ItemTierFilters`，则沿用本槽位从 day 权重表抽出的 `ItemTier`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3552-3558`）。

loose 的核心过滤是：

```text
S_loose(T) = { x in L : x.StartingTier <= T }
```

代码是 `pair.Value.StartingTier <= ItemTier`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3587`）。
如果 `S_loose(T)` 非空，旧 dealer 会执行 `list4 = list10`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3587-3590`）。
随后从新的 `list4` 中 uniform 选一张：
`list4[seedManager.GetNumber(list4.Count)]`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3596`）。

目标卡在该槽位 loose 路径的条件概率是：

```text
if S_loose(T) nonempty:
    P(c selected | loose, rolled T, c in L)
      = 1 / |S_loose(T)|, if c.StartingTier <= T
      = 0, otherwise
else:
    P(c selected | loose, rolled T, c in L)
      = 1 / |L|, if c in L
      = 0, otherwise
```

最后一个 `S_loose(T)` 为空的边界来自代码：空时只记录日志，不替换 `list4`，然后仍从当前 `list4`
uniform 选一张
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3587-3596`）。

---

## 6. 为什么 `list4 = list10` 会让后续槽位路径依赖

native 成功分支只移除被选中的那张卡
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3574-3580`）。
loose 成功过滤分支则先把 `list4` 替换为 `StartingTier <= T` 的下闭子集，再移除被选中的那张卡
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3587-3605`）。

设第 `i` 个槽位进入 loose 前的存活池为 `L_i`，本槽 roll 为 `T_i`，且
`S_i = {x in L_i : StartingTier(x) <= T_i}` 非空。代码执行后，下一槽位看到的池是：

```text
L_{i+1} = S_i - {selected_card}
```

所以 `L_{i+1}` 不只是“少了一张卡”，还永久丢失了所有 `StartingTier > T_i` 的卡。连续多个 loose 槽位后，
存活池的起始品级上限等于所有已生效 loose roll 的 running minimum：

```text
max StartingTier in L_after_loose <= min(T_1, T_2, ..., T_k)
```

这直接解释了为什么前序低 tier roll 会降低后续高 `StartingTier` 目标卡概率：一旦某个 loose 槽位 roll 到
Silver，并且池中有 `StartingTier <= Silver` 的卡，高于 Silver 的目标卡就从 `list4` 中被删掉，后续槽位概率
变成 0。这个结论只依赖 `StartingTier <= ItemTier` 和 `list4 = list10`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3587-3590`）。

因此，不能用：

```text
N slots * P(target in one independent slot)
```

来估算整店概率。槽位之间不是独立同分布，因为 `list4` 被每次选择和 loose 收窄持续改变
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3574-3580`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3587-3605`）。

---

## 7. 玩家手牌的重复升级不等于目标卡出现概率

native 成功路径在记录选中 id 后调用
`TierDuplicateCardHandling(bazaarPlayer, ItemTier, value.Id)`；loose 路径只有在
`ItemTierFilters.Count == 0` 时才调用同一函数
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3574-3579`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3599-3604`）。

`TierDuplicateCardHandling(...)` 只扫描 `battlePlayer.Board.PlayerCardHand`，收集同 id 且
`Tier < Diamond` 的卡，若存在则返回这些重复卡的最大 tier，否则返回原本 roll 到的 tier
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4467-4489`）。

最终 `DealCard(...)` 先从模板创建实例，再只有在传入 tier 高于实例当前 tier 且卡是 Item 时才调用
`UpgradeCard`
（`decompiled/BazaarBattleService/BazaarBattleService.Repositories.CardRepository/StaticDataCardRepository.cs:190-219`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarDeckTools.cs:73-97`）。

所以重复升级逻辑影响的是“目标卡出现时以什么品级被摆出”，不是“目标卡是否被选中”。计算目标 id 出现概率时，
玩家手牌不会改变候选池，也不会改变 uniform 抽样权重；它只改变 `list3` 中记录的品级。

---

## 8. 可执行计算框架

### 8.1 先做确定性预处理

给定：

- 玩家 hero、day、hour。
- 商店模板 `m`。
- 目标卡模板 `c`。
- 玩家 `PlayerSkillCard`、`PlayerCardHand`、`OpponentCardHand` 初始状态。
- reroll exclusion set。
- 旧 dealer 使用的 day/hour config 和 tier weight table。

按代码顺序做：

```text
1. 清空 OpponentCardHand / OpponentSkillCard。
   证据：BazaarCardDealer.cs:3437-3439，BazaarDeckTools.cs:634-643

2. heroFilters =
      m.SpawningFilters.MerchantHeroFilters, if nonempty
      [player.Hero], otherwise
   证据：BazaarCardDealer.cs:3442-3449

3. L = cardRepo.FilterCards(m, heroFilters)。
   如果 m.CardIdFilters 非空，只按 id 取；
   否则按 hero/type/size/tags/hidden tags/enabled 过滤。
   证据：StaticDataCardRepository.cs:161-171

4. L = L without ids currently in PlayerSkillCard。
   证据：BazaarCardDealer.cs:3456-3459

5. 若 m.NumberCardsToSpawn == 0 或 L 为空，P = 0。
   证据：BazaarCardDealer.cs:3467-3475

6. 若 m.NumberCardsToSpawn == m.CardIdFilters.Count，走固定直发概率。
   证据：BazaarCardDealer.cs:3477-3483

7. 若 !m.Rerolls.RerollRepeats，按 exclusion set 条件过滤。
   证据：BazaarCardDealer.cs:3485-3496

8. 若 L 全部是 Skill，停止；本文不计算纯技能商店。
   证据：BazaarCardDealer.cs:3497-3519

9. 读取 day/hour 的 NativeItemTierProbability 与 day tier weights。
   证据：BazaarCardDealer.cs:3521-3531，BazaarCardDealer.cs:3552-3558
```

如果目标卡在步骤 3/4/7 后不在 `L`，则它在随机路径里的概率为 0。固定直发路径的边界见第 4 节。

### 8.2 用状态机模拟每个槽位

可用递归或动态规划枚举路径。状态至少包含：

```text
(slot_index, L, flag2, target_already_selected)
```

其中：

- `slot_index` 是已经成功选择的槽位数。
- `L` 是当前 `list4` 的精确卡 id 集合。
- `flag2` 是 native miss latch。
- `target_already_selected` 为 true 时，该路径对“至少出现一次”已经成功。

伪代码：

```text
Prob(slot, L, flag2):
    if target not in L:
        return 0
    if slot == NumberCardsToSpawn:
        return 0

    L = FilterByRemainingBoardSize(player, L, isPlayerCarpet=false)
    if L is empty:
        return 0

    result = 0

    if ItemTierFilters empty and !flag2:
        # native gate
        result += p_native * NativeCase(slot, L)
        result += (1 - p_native) * LooseCase(slot, L, flag2=false, tier_source=day_weights)
    else:
        tier_source = ItemTierFilters uniform if nonempty else day_weights
        result += LooseCase(slot, L, flag2, tier_source)

    return result
```

native case：

```text
NativeCase(slot, L):
    sum over tier T with probability w_day(T):
        S = {x in L : StartingTier(x) == T}
        if S is empty:
            # num5-- + flag2=true + continue: retry same slot, now forced loose
            contribution = Prob(slot, L, flag2=true)
        else:
            contribution =
                (1 / |S|) if target in S
                + sum over x in S, x != target:
                    (1 / |S|) * Prob(slot + 1, L - {x}, flag2=false)
```

loose case：

```text
LooseCase(slot, L, flag2, tier_source):
    sum over tier T with probability P(T):
        S = {x in L : StartingTier(x) <= T}
        L2 = S if S nonempty else L
        contribution =
            (1 / |L2|) if target in L2
            + sum over x in L2, x != target:
                (1 / |L2|) * Prob(slot + 1, L2 - {x}, flag2)
```

该伪代码直接对应：

- native gate：`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3544-3551`
- day tier roll：`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3552-3558`
- space filter：`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3559-3562`
- native strict set：`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3564-3580`
- `ItemTierFilters` uniform tier：`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3583-3585`
- loose set and destructive assignment：`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3587-3590`
- loose uniform draw and removal：`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3596-3605`

### 8.3 计算“至少出现一次”时不要重复计入后续路径

一旦某路径选中目标卡，该路径对“目标出现至少一次”的贡献就是当前选择概率；后续槽位不用再展开。旧 dealer 每次选中后都会从
`list4` 移除被选项，所以同一 template id 是否可能重复，取决于池中是否存在多个相同 id 项；静态 repository 的键是
`Guid`，初始化时重复 card id 会抛错
（`decompiled/BazaarBattleService/BazaarBattleService.Repositories.CardRepository/StaticDataCardRepository.cs:84-88`）。

如果要算“目标出现且品级为 Gold/Diamond”，则不能在选中目标时停止；需要把 `TierDuplicateCardHandling` 与
`DealCard` 的升级条件接到目标被选中路径后
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4467-4489`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarDeckTools.cs:73-97`）。

---

## 9. 常见误解校正

### 9.1 “候选池 100 张，所以每张每槽 1%”

只有在某个具体分支已经确定了最终 uniform 抽样集合，并且目标卡在该集合里时，才有
`1 / |that set|`。native 的集合是同 `StartingTier` 集合
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3564-3574`），loose 的集合是
`StartingTier <= T` 后的存活池或空过滤 fallback 的当前池
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3587-3596`）。
这两个集合不一定等于初始候选池。

### 9.2 “高 tier roll 一定提高所有卡概率”

在 loose 分支，高 tier roll 会让更多低 `StartingTier` 卡进入集合，集合变大后目标卡的 uniform 权重可能变小；
目标卡只是在 `StartingTier <= T` 时有资格
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3587-3596`）。
在 native 分支，目标卡必须 `StartingTier == T`，高于或低于目标起始品级的 roll 都不给目标卡资格
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3564-3567`）。

### 9.3 “native miss 只影响当前槽”

native miss 会 `num5--` 重试当前槽，并把 `flag2` 设为 true
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3567-3572`）。
因为 `flag2` 是循环外变量，后续所有槽位都不再满足 native gate 的 `!flag2`
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3544-3548`）。

### 9.4 “玩家手里有同名卡，所以这张卡更容易出现”

玩家手牌只被 `TierDuplicateCardHandling` 读取，并且它发生在卡已经被选中之后
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3574-3579`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3596-3604`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4467-4489`）。
它不会把目标卡加入候选池，也不会改变 uniform 抽样集合。

### 9.5 “玩家当前棋盘空位直接决定商店是否刷出大卡”

`DealFilteredCards` 在商店选择循环里调用 `FilterByRemainingBoardSize(..., isPlayerCarpet:false)`，因此读取的是
`OpponentCardHand` 的空位，不是玩家 `PlayerCardHand` 或 storage
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3559`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4608-4625`）。
玩家购买时自己的空间检查在 `PlayerSelectedCard` 购买动作中，属于出现之后的交互约束
（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:1698-1704`）。

---

## 10. 实战估算流程

要回答“某英雄在第 N 天进入某个售卖卡牌商店时，某张具体卡出现概率是多少”，按下面顺序执行：

1. 取目标卡模板：记录 `Id`、`CardType`、`CardSize`、`Heroes`、`CardTags`、`HiddenTags`、
   `StartingTier`、`SpawningFilters.Enabled`。字段定义见
   `decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarCard.cs:239-247`，
   `decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarCard.cs:58-66`。
2. 取商店模板：记录 `SpawningFilters` 中的 `CardIdFilters`、`NumberCardsToSpawn`、
   `MerchantHeroFilters`、`CardTypeFilters`、`CardSizeFilters`、`CardTagFilters`、
   `HiddenTagFilters`、`ItemTierFilters`、`Rerolls.RerollRepeats`。字段定义见
   `decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarCard.cs:20-66`。
3. 判断是否为本文范围：用步骤 2 的 filters 建池，再应用玩家技能槽排除和 reroll exclusion；如果结果全是 Skill，
   停止，改用技能路径。代码见
   `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3456-3499`。
4. 处理固定 `CardIdFilters`：若 `NumberCardsToSpawn == CardIdFilters.Count`，按第 4 节直接给 0/1 概率。
   代码见 `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3477-3483`。
5. 读取 day/hour 输入：旧 dealer 使用 `GetByDayAndHour(day, hour)` 取 `NativeItemTierProbability`，使用
   `GetProbabilitiesByDay(day)` 取 tier weights。代码见
   `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3521-3531`，
   `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3552-3558`。
6. 对每个槽位跑第 8.2 节状态机。native 分支用 `StartingTier == T`，loose 分支用
   `StartingTier <= T`，并保留 `list4 = list10` 的破坏性收窄。代码见
   `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3564-3605`。
7. 如果只问“是否出现”，在目标 id 被选中时停止该路径；如果还问“以什么品级出现”，把
   `TierDuplicateCardHandling` 和 `DealCard` 的升级条件接上。代码见
   `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4467-4489`，
   `decompiled/BazaarBattleService/BazaarBattleService/BazaarDeckTools.cs:73-97`。
8. 报告边界：若输入来自当前客户端 `GameData.db`，只能确认卡模板等客户端静态字段；不能把缺失或陈旧的客户端权重当作线上权威。
   当前客户端开局使用服务器 `/sessions` 和 `GameSim` 消息，见
   `decompiled/TheBazaarRuntime/TheBazaar/StartRunAppState.cs:134-168`，
   `decompiled/TheBazaarRuntime/Networking/HttpGameClient.cs:244-253`，
   `decompiled/TheBazaarRuntime/TheBazaar/GameSimHandler.cs:296-307`。

这个流程的输出可以是精确枚举结果，也可以是 Monte Carlo 模拟结果。若池子很大，精确枚举会因为
`L` 的路径依赖而膨胀；Monte Carlo 需要严格复刻同一状态机，尤其不能把槽位视为独立抽样。

---

## 11. 后续如何落到 Collection Panel

这套分析很适合和 Collection Panel 做到一起，但不能直接把旧 dealer 结果当成当前线上事实展示。更稳的产品分层是：

```text
Level 1: 这张卡理论上属于哪个 merchant/trainer 来源池。
Level 2: 在旧 dealer 模型下，它为什么能/不能进入该商店候选池。
Level 3: 在有 day tier weights / NativeItemTierProbability / run state 输入时，给出旧 dealer 模型的估算概率。
```

当前 Collection Panel 已经实现了 Level 1。它从嵌入的 `collection-sources.json` 加载 source catalog，schema 版本固定为 4
（`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:13-17`，
`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:96-104`）。
source DTO 只包含 `sourceTemplateIds` 和 `offerSegments` 等面板字段
（`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceDtos.cs:19-46`），offer rule 只表达
`heroMode`、`startingTier`、size、tags、hidden tags、enchantment facets 等
（`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceDtos.cs:64-100`）。
这些字段足够做“理论来源池”，但不足以完整复刻旧 dealer 的 `NumberCardsToSpawn`、`CardIdFilters`、
`ItemTierFilters`、`RerollRepeats` 和 reroll exclusion 状态。

### 11.1 现有接入点

Collection Panel 的卡牌目录已经有 dealer 概率需要的大部分单卡静态字段：
`CollectionCardVm` 暴露 `Id`、`Type`、`Size`、`StartingTier`、`Heroes`、`Tags`、`HiddenTags`、
`IsEnchantable` 等字段
（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.cs:28-50`）。
这些字段来自 `TCardBase` 模板投影
（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs:16-45`），而当前静态模板本身也有
`Id`、`StartingTier`、`Size`、`Type`、`Heroes`、`Tags`、`HiddenTags`
（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards/TCardBase.cs:9-34`）。

source 过滤现在由 `CollectionSourceOfferPoolResolver.Resolve(...)` 完成：遍历 catalog cards，按当前 source 的
offer segments 得到 `OfferedCardIds` 和每张卡的 match reason
（`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:11-33`）。
每张卡的匹配逻辑先限制 source kind 对应的 card type，merchant 对 item、trainer 对 skill
（`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:36-43`，
`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:153-154`），再按 hero、starting tier、size、tag、hidden tag、enchantment 等规则过滤
（`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:91-112`）。

过滤结果已经接到了 UI pipeline：`CollectionPanel.ApplyFilters()` 读取选中的 source，拿到
`offeredCardIds` / `offerMatchesByCardId`，再传给 `CollectionFilterEngine.Apply(...)`
（`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:795-846`）。
`CollectionFilterEngine` 把 `context.OfferedCardIds` 作为第一个 source-pool narrowing 条件
（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:15-27`，
`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:38-57`）。
grid virtualizer 又把 `offerMatchesByCardId` 传到卡片绑定层
（`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:88-103`，
`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:341-362`），现在的
`CollectionSourceAttributionBadge` 已经能基于 match reason 显示来源徽标
（`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionSourceAttributionBadge.cs:17-34`，
`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionSourceAttributionBadge.cs:77-109`）。

因此，后续不需要重建目录或 grid。概率层应挂在 source offer pool 之后，生成“每张卡的 dealer explain/probability
metadata”，再和现有 `offerMatchesByCardId` 一起传入 virtualizer。

### 11.2 当前还缺哪些 dealer 输入

Collection Panel 当前能稳定拿到：

- 当前 run hero：打开面板时从 `Data.Run.Player.Hero` 或 `Data.SelectedHero` 读取
  （`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:157-188`，
  `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:213-230`）。
- 当前 run day：打开时读 `TheBazaar.Data.Run?.Day`
  （`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:185-188`，
  `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:233-244`）。
- 当前 encounter / choice selection template ids：通过 `EncounterStateProbe.GetEncounterIds()`
  （`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:157-168`，
  `src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs:23-32`，
  `src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs:74-128`）。
- 当前 source 自动选择：`CollectionPanelOpenSelectionResolver` 用当前 encounter template 或 choice template 匹配
  `CollectionSourceEntry.SourceTemplateIds`
  （`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelOpenSelectionResolver.cs:12-42`，
  `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelOpenSelectionResolver.cs:60-107`）。

但完整 dealer 概率还缺：

- 当前线上权威 day tier weights。
- 当前线上权威 `NativeItemTierProbability`。
- 选中 merchant 的完整 spawning filter，尤其是旧模型里的 `CardIdFilters`、`NumberCardsToSpawn`、
  `ItemTierFilters`、`Rerolls.RerollRepeats`。
- reroll exclusion set。旧字段是 `GameStateMetadata.DealtCardForReRollExclusion`
  （`decompiled/BazaarBattleService/BazaarBattleService/GameStateMetadata.cs:29-31`），但当前在线流程由 server sim
  驱动，不能假设这个旧字段代表线上状态
  （`decompiled/TheBazaarRuntime/TheBazaar/StartRunAppState.cs:134-168`，
  `decompiled/TheBazaarRuntime/TheBazaar/GameSimHandler.cs:296-307`）。
- 玩家技能槽/手牌/货架空位的 dealer 同构快照。当前可从 runtime 读 live cards，但旧 dealer 的商店选择用的是
  `BazaarBattleService.Models.BazaarBoard` 语义；需要显式 adapter，不应隐式混用模型。

还有一个现有近似需要保留边界：Collection Panel 的 day gate 是硬编码近似表，文件头已经写明真实表来自旧
`tierManager.json` 且会随 balance patch 漂移
（`src/BazaarPlusPlus/Game/CollectionPanel/Data/DayTierSchedule.cs:6-9`）。
当前 filter 用 `DayTierSchedule.AllowsStartingTier(...)` 做 day ceiling
（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:34-57`，
`src/BazaarPlusPlus/Game/CollectionPanel/Data/DayTierSchedule.cs:16-33`）。
这个近似可以继续用于“这天大致能看到哪些起始品级”，但不能直接拿来当每槽位概率权重。

### 11.3 推荐实现路径

**第一阶段：Dealer Explain，不显示精确概率。**

新增一个纯函数层，例如：

```text
Game/CollectionPanel/DealerModel/
  CollectionDealerCandidate.cs
  CollectionDealerSourceContext.cs
  CollectionDealerExplainResult.cs
  CollectionDealerExplainResolver.cs
```

输入使用现有 `CollectionSourceEntry`、`CollectionCardVm`、selected hero、selected day、source offer matches。
输出按 card id 给出解释：

- 是否在 source offer pool 中。
- 命中的 segment key / normal 或 enchanted reason。
- hero/type/size/tag/hidden tag/starting tier/day gate 是否通过。
- 是否因为纯 skill source 而属于本文排除范围。
- 概率状态：`NotComputed`、`MissingWeights`、`ServerAuthoritative`、`OldDealerEstimateAvailable`。

这一步的收益是高：它能马上把“为什么这张卡在这个商人下面显示/不显示”讲清楚，同时不需要线上权重，也不冒充线上概率。
代码上复用 `CollectionSourceOfferPoolResolver` 已经产出的 match reason
（`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:22-33`）和现有 grid badge pipeline
（`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:88-103`，
`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:341-362`）。

**第二阶段：Dealer Probability Core，仍然默认标注为旧模型估算。**

新增一个不依赖 Unity 的概率核心，输入是显式 DTO，而不是直接读 runtime：

```text
DealerShopDefinition:
  SourceKey
  SourceTemplateIds
  NumberCardsToSpawn
  CardIdFilters
  ItemTierFilters
  RerollRepeats
  NativeItemTierProbability
  TierWeightsByDay

DealerPlayerState:
  Hero
  Day
  Hour
  PlayerSkillCardIds
  PlayerHandCards(id, tier)
  OpponentShelfEmptySockets
  RerollExclusionIds

DealerCandidate:
  Id
  Type
  Size
  StartingTier
  Heroes
  Tags
  HiddenTags
  Enabled
```

核心复刻第 8.2 节状态机：固定直发、reroll exclusion、skill path boundary、native strict、loose destructive
`list4 = list10`、重复升级和“出现概率”分离。这个核心应该有 exe-runner 单测覆盖以下用例：

- 固定 `CardIdFilters` 的 0/1 概率。
- `CardIdFilters` 非空但不是固定直发时仍进入随机路径。
- native 分支只在 `StartingTier == rolled tier` 时可选。
- loose 分支 `StartingTier <= rolled tier`，并且 `list4 = list10` 影响后续槽位。
- native miss 后 `flag2` 让后续槽位强制 loose。
- `ItemTierFilters` 关闭 native，并从 filter tiers 中 uniform 选 tier。
- reroll exclusion 只有在过滤后仍够发时才生效。
- 玩家手牌重复升级不改变目标 id 出现概率。

这些用例都能直接对应旧 dealer 行号：
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3477-3499`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:3544-3605`，
`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4467-4489`。
现有 Collection source tests 已经是 exe-runner，且覆盖 catalog counts、source selection、offer pool、cache key、segment attribution 等
（`tests/CollectionSourceFiltering.Tests/Program.cs:289-357`，
`tests/CollectionSourceFiltering.Tests/Program.cs:398-600`，
`tests/CollectionSourceFiltering.Tests/Program.cs:638-735`，
`tests/CollectionSourceFiltering.Tests/Program.cs:1032-1055`）。可以在同项目里加纯模型测试，避免需要 Unity runtime。

**第三阶段：source catalog schema v5，只补 verified fields。**

如果要让面板对每个商人给出更贴近 dealer 的概率，需要把 source catalog 从“理论来源池规则”扩展为“来源池规则 + dealer hint”。
不要在 v4 的 `offerSegments` 中偷塞概率语义；当前 parser 明确要求 schema version，且 v4 不允许一条 source 混用 pinned tier 和
unpinned tier segments
（`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:96-104`，
`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:239-277`）。

建议 v5 新增可选块：

```json
"dealer": {
  "numberCardsToSpawn": 3,
  "cardIdFilters": [],
  "itemTierFilters": ["Silver"],
  "rerollRepeats": false,
  "model": "old-bazaar-card-dealer"
}
```

只有当这些字段来自当前代码/抓包/验证过的静态数据时才填。没有验证的 source 保持 `dealer` 缺省，UI 显示“来源池已知，概率输入不足”。

**第四阶段：UI 先做解释抽屉，再考虑排序/热度。**

不要一开始把概率做成排序依据。当前 `CollectionFilterEngine` 的排序是 deterministic：按 tier/size 和显示名
（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:70-86`）。
如果把不完整概率混入排序，会让列表在权重缺失或 source 输入不完整时显得确定但其实不可靠。

更合适的 UI 顺序：

1. 在卡片 source badge 或 hover 上显示简短状态：`Pool`、`Fixed`、`Native/Loose`、`Weights missing`。
2. 在选中 source 的面板区域加一个“概率模型”状态：`Source pool only` / `Old dealer estimate` / `Server data unavailable`。
3. 等权重和 dealer hints 覆盖足够稳定后，再考虑添加“按估算概率排序”作为显式 sort mode。

现有 badge 已经能按 `sourceMatches` 生成文字
（`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionSourceAttributionBadge.cs:77-109`），所以第一步可以扩展
`CollectionSourceOfferMatch` 或旁路新增 `CollectionDealerCardExplain` 字典，不需要重写 grid。

### 11.4 最终建议

近期最值得做的是“解释层”，不是“精确概率层”：

- 它复用当前 Collection Panel 的 source catalog、VM 和 grid pipeline。
- 它能直接帮助用户理解“这张卡为什么属于/不属于某商人”。
- 它为后续概率核心准备同一套输入 DTO 和测试用例。
- 它不会把旧客户端 dealer 冒充成当前线上后端实现。

当我们拿到可靠的 `NativeItemTierProbability`、day tier weights 和每个 source 的 dealer hints 后，再打开“旧 dealer 模型估算概率”。
最终 UI 应该同时展示边界：`来源池` 是当前面板的 verified catalog 结果，`概率` 是带输入版本和数据来源的模型结果。
