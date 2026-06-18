# Collection Panel · 单卡「当前商店生成概率」浮层 — 设计

> **状态：** 设计草案（**design-only，不含实现代码**）· 已经过独立 red-team 复核并据此修订（见 §9）
> **系列：** 「进商店逻辑」5 篇文档（shop-entry-1..5）的 **落地设计 / 接入方案**。
> **模型版本标签：** `old-bazaar-card-dealer`（任何估算数字都必须带此标签 + 数据来源 + 「仅供参考」，且在数据层不可剥离）
> **日期：** 2026-06-15（沿用系列日期前缀；实际撰写 2026-06-18）

> **📚「进商店逻辑」文档系列 · 建议阅读顺序**（你在读 **#6 落地设计**）
> 1. [总览 · 从进店到离店的完整流程](2026-06-15-shop-entry-1-lifecycle.md)
> 2. [讲解 · 铺货 / 单卡概率怎么算](2026-06-15-shop-entry-2-stocking-explainer.md)
> 3. [深入 · native/loose + 英雄 + RNG 顺序 + 手牌 + 数据缺口](2026-06-15-shop-entry-3-native-loose-deep-dive.md)
> 4. [参考 · 单卡概率逐行推导 + Collection Panel 接入路线图](2026-06-15-shop-entry-4-single-card-reference.md)
> 5. [背景 · 旧 dealer 是什么 / 为何在客户端 / 是否线上权威](2026-06-15-shop-entry-5-client-server-boundary.md)
> 6. [落地设计 · Collection Panel 商店概率浮层](2026-06-15-collection-panel-shop-probability-design.md) ← 本文

---

## 0. TL;DR / 一页纸

- **做什么：** 在 Collection Panel（卡牌浏览面板）里，给每张卡一个 **可开关的浮层**，说明它在 **当前选中商人/训练师** 下的生成情况。
- **核心约束（决定成败）：** 权威单卡概率 **客户端算不出来**。`NativeItemTierProbability`（native 闸门概率）客户端运行时拿不到（§3.2），当天 tier 权重客户端 `GameData.db` 被混淆剥成空表（[shop-entry-5 §4.5]），且线上发牌是服务器 `GameSim` 权威、不是这套旧 dealer（[shop-entry-5 §五]）。**所以浮层不能冒充精确线上概率。**
- **怎么做（产品分层）：** 不是「直接显示一个百分比」，而是一个 **三态信息模型**：
  - **`Pool`（来源池归属）** — 这张卡是否属于当前商人的理论来源池。这是 **客户端目录事实**（注意：客户端目录是下载的静态数据，**可能滞后线上一个 patch，且不是线上权威发牌**，[R-honesty-1]）。已有 Level 1 实现（`CollectionSourceOfferPoolResolver`）。
  - **`Explain`（旧模型解释）** — 在 `old-bazaar-card-dealer` 模型下，它走 native 还是 loose、被哪条 day-gate / hero / tier 规则约束。**定性、不带数字，但仍是旧模型推导，须带模型免责标注。**
  - **`Estimate`（旧模型估算，带不可剥离标注）** — 仅当补齐 wiki 参考权重 + 假定 native 概率后，给一个 **明确标注为估算** 的概率区间。默认 **关闭**。
- **首个可交付增量（用户决策 2026-06-18）：** Phase A 解释层 **+** Phase B 估算层 一起交付。解释层默认开、零数字；**估算层默认关、按需单卡计算、强标注**。native 闸门概率做成 **面板可配置项、默认 0.8**（旧模型估算用的假定值，无权威来源、非线上权威——正因如此才暴露成可调）。仍复用现有 catalog / VM / grid 管线，零重建。
- **架构：** 一个 **Unity/BepInEx-free、但经 `BazaarGameShared` 枚举与游戏 DLL 耦合** 的纯解释/概率核心（`Game/CollectionPanel/DealerModel/`，**必须在 `Game/` 层、不能放 `Core/`**，[R-arch-1]）+ 一个 uGUI 浮层组件（镜像现有 `CollectionSourceAttributionBadge`）+ 在 `CollectionPanel.ApplyFilters()` 既有接缝处挂一层（[CollectionPanel.cs:795-849](../../src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs)，`GetOrResolve` 在 :815-819、`SetVisible` 在 :846）。
- **绝对红线：** ① UI 不显示冒充权威的精确概率；② 任何估算在 **数据层** 把数值与 `{Model, Authority, Source, ReferenceOnly}` 绑成不可分的值对象（[R-honesty-2]）；③ 伪造/占位 GUID **绝不**流到 `CollectionCardFactory.TryBind`（[CollectionCardFactory.cs:45](../../src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardFactory.cs)），浮层 payload 一律按真实 `vm.Id` 键控。

---

## 1. 产品范围与「诚实问题」

### 1.1 目标（一句话）

在 Collection Panel 选中（或自动选中）一个商人/训练师来源后，对面板里每张卡给出 **「它在这个商店里怎么出现」** 的可开关说明；默认是 **解释**，不是 **精确概率**。

### 1.2 诚实问题：为什么不能直接显示一个百分比

三条独立证据，全部已核对当前反编译：

1. **`NativeItemTierProbability` 客户端运行时拿不到。** 持有它的 `TDayHourConfig` 类型在 `TheBazaarRuntime` 没有任何 new/load/read 点、未注册多态反序列化，运行时根本没有实例；默认值 `0.05f` 是占位不是真值（[shop-entry-3 §6.1]，`decompiled/.../TDayHourConfig.cs:20`）。
2. **当天 tier 权重表客户端是空的。** 源字段 `ItemSkillSpawnTierPercantagesByDay` 带 `[BazaarObfuscate]`，客户端导出序列化器把它整列剥掉；实测 v5.0.0 客户端 `GameData.db` 的 `ItemSkillTierTablesByDay` / `EncounterTierTable` 全为空（[shop-entry-5 §4.5]）。这正是 mod 的 `DayTierSchedule` 只能硬编码近似的原因（[Data/DayTierSchedule.cs:6-9](../../src/BazaarPlusPlus/Game/CollectionPanel/Data/DayTierSchedule.cs)）。
3. **线上发牌不是这套旧 dealer。** 线上是 `POST /sessions` → 服务器 `NetMessageGameSim` → 客户端只渲染 `GameSimEventCardSpawned/CardDealt`；品级是服务器权威 `SimUpdateCard.Tier`（Key 8），spawn 路径 **零客户端 tier-roll**；`TheBazaarRuntime` 对 `DealFilteredCards` 等旧 dealer 方法 **零调用点**（[shop-entry-5 §4.4、§五]）。

**进一步的诚实点（不止概率数字）：** 连 **`Pool`（来源池归属）本身也只是「客户端目录事实」**——客户端 catalog 来自下载的静态 `GameData.db`，可能比线上落后一个 patch（见 memory「Live card data is in the runtime cache」），而线上是否真会在该商人下提供这张卡由服务器决定。所以「这张卡在池里」= **客户端目录认为理论上可出**，不是「线上一定会出」。这一层免责必须显式化，否则 `Pool` 会用最强的「事实」措辞偷渡同一类过度断言（[R-honesty-1]）。

**结论：** 客户端能拿到的只有「来源池成员（哪些卡能出，且可能滞后）」和「单卡静态字段（StartingTier/Heroes/Size/Tags…）」。**够做解释，不够做权威概率。** 这是产品设计的第一性约束，不是工程难度问题。

### 1.3 三态信息模型（核心产品决策）

浮层在任一时刻处于以下 **互斥** 状态之一，每态有清晰的视觉与文案区分（详见 §5）。**`Pool` 以外的每一态都是旧模型推导，须携带 `old-bazaar-card-dealer · 非线上权威` 免责标注（至少在 hover 抽屉里）**（[R-honesty-3]）：

| 态 | 含义 | 数据可信度 | 显示什么 | 是否带数字 |
| --- | --- | --- | --- | --- |
| `NotInPool` | 不属于当前来源池 | 客户端目录 | 灰显 / 无徽标 | 否 |
| `Pool` | 属于来源池（理论可出） | **客户端目录事实（可能滞后线上一个 patch，非线上权威发牌）** | 「来源池」徽标 + 命中的 segment reason | 否 |
| `Fixed` | 固定直发路径（`CardIdFilters.Count == NumberCardsToSpawn`） | **仅当 dealer hint 已验证** 才显「概率=1」；未验证 hint **降级为 `WeightsMissing`/带问号变体** | 「固定出售」徽标（已验证）/ 待验证变体 | 否（已验证时定性确定） |
| `Explain` | 在旧模型下走 native/loose、被哪条门约束 | **旧模型定性（带模型免责）** | native/loose 资格、day-gate 是否通过 | 否 |
| `WeightsMissing` | 来源池已知，但概率输入不足 | — | 「概率输入不足 · 未知，不代表更低」 | 否 |
| `Estimate` | 旧模型估算（补齐参考权重后） | **旧模型估算，非线上权威** | 概率 **区间** + 不可剥离的 model/source 标注 | 是（带强标注） |

**关键：三态绝不混淆。** `Pool` 是（可能滞后的）目录事实，`Explain`/`Fixed` 是旧模型定性，`Estimate` 是带标注的旧模型估算。UI 必须让用户一眼分清自己看的是哪一类，绝不把 `Estimate` 的数字伪装成 `Pool` 的事实，也绝不让 `Fixed`/`Explain` 的「确定/能出」措辞冒充线上确定性。

### 1.4 反目标（绝不做）

- ❌ 不显示「87.3%」式的精确单卡概率，哪怕在 `Estimate` 态——只给 **区间 / 档位**（高/中/低 或 5–15% 这样的桶）。
- ❌ 不把任何概率当默认排序键（会让缺权重时的列表「看起来确定其实不可靠」，[shop-entry-4 §11.3 第四阶段]）。
- ❌ **不在同一视图里把带数字的 `Estimate` 卡与 `WeightsMissing` 卡并排成「可比较」的样子**——这会让 `?权重` 被读成「比有数字的更低/≈0」（相对误读，[R-honesty-4]，处置见 §5.4）。
- ❌ 不重建 catalog / grid / VM —— 一律挂在现有管线之后。
- ❌ 不把 `0.05f` 占位、不把 wiki 0.1.9 当线上权威；连「0.1.9」这个精确版本号本身都要带 staleness 提示（[R-honesty-5]）。
- ❌ 不让伪造 GUID 进 `CollectionCardFactory.TryBind`；dealer hint 里的裸 id（`CardIdFilters`/`ItemTierFilters`）只能作模拟 **输入**，绝不当渲染键（[R-guid-1]）。

---

## 2. 概率 / 解释模型

> 本节把 shop-entry-2/3/4 的旧 dealer 算法压缩成 **浮层核心要复刻的那部分**，并写清公式与每槽 RNG 语义。代码行号指 `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs`，已对照该文件逐行复核。

### 2.1 模型边界：复刻什么、删掉什么（显式列全）

旧 dealer 的卖卡入口是 `DealFilteredCards(player, card, cardIsFree)`（`:3437-3632`）。浮层核心 **复刻** 它的 **逐槽选牌状态机**，并 **显式声明** 假设掉的每一处分支（[R-prob-2/3/5]）：

- **删掉「格子放得下」过滤的实际约束，但只在空货架时成立。** `FilterByRemainingBoardSize`（`:3559` → `:4608-4625` `CountEmptySocketsOnCarpet`）读对手货架空位。商店入口已清空对手区（`:3437-3439`），普通商品要等选择循环结束才真正发牌（`:3626-3629`），所以 **当 `card.AdvancedEncounters.ExperiencePointsId == Guid.Empty` 时**，普通商店的 size filter 通常看到空货架、近似 no-op。**若该商人带经验卡**，经验卡在主循环前被发到对手区占一格（`:3463-3465`），槽 1 的 `emptySockets = 10 − size(经验卡)`，size filter 对大卡 **会真生效**——此时空货架假设不成立（[R-prob-2]）。核心把「经验卡占位」做成可选输入，默认空货架。
- **固定 3 张卡。** `NumberCardsToSpawn` 常见 = 3（data-driven，权威值在 `GameData.db`，§3.2）。核心以 3 为默认、以注入参数支持其他值。
- **只处理非 Combat（Item/merchant）入口卡。** `card.CardType == Combat` 时在进逐槽循环前早退（`:3540-3542`）；卖卡场景入口卡是 Item，此分支不触发——但这是「之所以能进循环」的原因，列为显式排除项（[R-prob-5]）。
- **不建模 `AutoSelect` 早退。** 选牌循环填完 `list2` 后、真正 `DealCard` 前，若某张被选中卡带 `AdvancedEncounters.AutoSelect`，dealer 调 `AutoSelectCard` 并 **直接返回**，`list2` 里其余卡 **不会被发到货架**（`:3618-3623`）。这会让「出现在发出的 3 张里」的定义在含 AutoSelect 卡的商店失真。绝大多数卖卡商店无 AutoSelect 物品，故默认 **不建模**，但列入待核对项（§8 开放问题、§3.2 缺口）（[R-prob-3]）。

**不复刻的：** 线上服务器 `GameSim` 的真实发牌（拿不到，§1.2）。核心 **只是旧模型的离线重放**，输出永远标注 `old-bazaar-card-dealer`。

### 2.2 确定性资格闸门（进 RNG 循环前的顺序，已按代码行号校正）

这一段是 **确定性** 的——给定输入，一张卡要么进候选池要么不进，无随机。浮层的 **解释层（Phase A）只需要这一段**。**顺序严格按代码**（[R-prob-1/2] 已校正：固定直发在 reroll/纯技能 **之前**；经验卡预发牌单列）：

1. **清空对手货架**（`:3437-3439`）。
2. **构造候选池：** `cardRepo.FilterCards(card, list)`（`:3457`）内部完成：英雄过滤（`heroFilters = MerchantHeroFilters` 非空时用它否则 `[玩家英雄]`，`Common` 卡满足任意英雄，`StaticDataCardRepository.cs:164-166`）+ `CardIdFilters` 分叉（非空时只保留这些 ID、**跳过其余所有过滤**并短路英雄门，`StaticDataCardRepository.cs:161-171`）+ 为空时的 type/size/tag/hidden/`Enabled` AND 链。
   > ⚠️ **drift 警示：** dealer 的 `MerchantHeroFilters` **会覆盖玩家英雄**。Collection Panel Level 1 的 hero 过滤用「选中英雄」语义（[Sources/CollectionSourceOfferPoolResolver.cs:156-189]），两者 **不是同一个轴**；解释层描述 hero 约束时按 **来源池 hero 规则**（面板事实），不声称复刻了 `MerchantHeroFilters`（缺失输入，§3.2）。
3. **排除已装备技能：** 池里剔除 id 命中玩家技能槽 `PlayerSkillCard` 的卡（`where !playerSkillCardIds.Contains`，`:3456-3459`）；**手牌不在此排除**。
4. **经验卡预发牌：** 若 `ExperiencePointsId != Guid.Empty`，把经验卡发到对手区占一格（`:3463-3465`）——影响后续每槽 size filter（见 §2.1）。
5. **零/空池早退：** `NumberCardsToSpawn == 0`（`:3467`）或池空（`:3472`）直接 return。
6. **固定直发（在 reroll/纯技能之前）：** 若 `NumberCardsToSpawn == CardIdFilters.Count`，逐个 `CardIdFilters` 确定性发牌 **return**（`:3477-3483`）——目标在 filters 里且初始池非空则概率 = 1，否则 0。**此分支短路掉后面的 reroll 与纯技能判定。**
7. **reroll 排除：** 仅当 `RerollRepeats == false`；过滤后池 **size ≥ NumberCardsToSpawn** 才采纳，否则 **整集清空、用原池**（`:3485-3496`）→ **在排除集里 ≠ 概率 0**。
8. **纯技能池边界：** 若过滤后池 **全是 Skill 卡**，跳 `RollSkillTierAndFilter` 返回，**卖卡模型不适用**（`:3497-3519`）。混合池不触发。
9. **取 day/native 概率：** `dayRepo.GetByDayAndHour(day, hour).NativeItemTierProbability`（`:3521-3531`）——这一步取的就是客户端拿不到的关键值（§3.2）。
10. **池不足早退：** `list4.Count < NumberCardsToSpawn` 时只记错误、设局部 `numberCardsToSpawn = list4.Count`，但逐槽循环在 `else` 分支里，故此情况 **不进循环**（`:3533-3537`）。
11. **Combat 早退：** `card.CardType == Combat` 时 return（`:3540-3542`，卖卡不触发）。
12. **进逐槽循环**（`:3544-3545`，`flag2` 在循环外声明）。

### 2.3 每槽 RNG 语义与公式（native / loose）

逐槽循环 `num5 = 0 .. NumberCardsToSpawn-1`。**每槽** 的严格顺序（[shop-entry-4 §5]，已对照 `:3544-3605`）：

```text
(a) native 闸门判定:  flag3 = (ItemTierFilters.Count == 0) AND (flag2 == false) AND (rng < NativeItemTierProbability)   [:3548]
(b) 掷品级:           T = SelectRandomTier(当天权重)   每槽必掷，无论 native 与否   [:3552-3558]
(c) 空间过滤:         FilterByRemainingBoardSize(...)  空货架时通常 no-op；带经验卡时槽1可生效（§2.1）   [:3559]
(d) 按 flag3 走 native 或 loose 分支
```

`SelectRandomTier`：权重升序累加，命中首个累计 ≥ rng 的 tier，未命中回退 `Bronze`（`:4451-4464`）。权重和 < 1 时差额落 Bronze（Bronze 被系统性高估）。

**EItemTier 序：** `Invalid(0) < Bronze(1) < Silver(2) < Gold(3) < Diamond(4) < Legendary(5)`（`BazaarCard.cs:145-154`）。`StartingTier == Invalid` 的卡 **loose 永远合格**（≤ 任意真实 tier，这是结构性的）；**native 不合格是「数据性」而非「算法性」**——因为当天权重表（及 wiki 参考表 §7）里 **没有 Invalid 这一行**，`SelectRandomTier` 永不返回 Invalid（[R-prob-4]）。**因此 `NativeEligible` 分类器须定义为「`StartingTier` 等于 *当前生效权重表里存在的某个 key*」，不要硬编码成固定的 `{Bronze,Silver,Gold,Diamond}`。**
> ⚠️ **`Invalid` 仅存在于 dealer 侧 `BazaarCard.EItemTier`。** mod 侧 `CollectionCardVm.StartingTier` 是 `BazaarGameShared` 的 `ETier`，其变体只有 `{Bronze,Silver,Gold,Diamond,Legendary}`、**无 `Invalid`**。所以落到实现时（见落地计划 Task 4），mod 侧无 Invalid 这一情形，`NativeEligible` 只需额外排除 `Legendary`（参考表无其权重）。计划评审 [F-009] 已据此校正。

**native 严格分支**（`flag3 == true`，`:3564-3580`）：

```text
S_native(T) = { x ∈ L : x.StartingTier == T }
  若空:    本槽不选卡, num5--, flag2 = true, continue   → 此后所有槽强制 loose   [:3567-3572]
  若非空:  在 S_native(T) 内 uniform 选一张, 仅 list4.Remove(选中)（不替换池）   [:3574-3580]

P(c 被本槽选中 | native, T, S_native 非空) = 1 / |S_native(T)|   若 c.StartingTier == T
                                            = 0                  否则
```

**loose / 非原生分支**（`:3583-3605`）：

```text
若 ItemTierFilters 非空:  T = ItemTierFilters[uniform]   （不用当天 roll）   [:3583-3585]
否则:                     沿用本槽 day-roll 的 T
S_loose(T) = { x ∈ L : x.StartingTier <= T }
  若非空:  list4 = list10  （破坏性: 永久丢弃所有 StartingTier > T 的卡）, 再 uniform 选一张   [:3587-3596]
  若空:    不替换 list4, 仍从当前 list4 uniform 选一张   [:3592-3596]

P(c 被本槽选中 | loose, T) = 1 / |S_loose(T)|   若 S_loose 非空 且 c.StartingTier <= T
                           = 1 / |L|           若 S_loose 空 且 c ∈ L
                           = 0                  否则
```

### 2.4 路径依赖：为什么不能「N 槽 × 单槽独立概率」

native 成功只 `Remove` 选中卡（池不收窄）；**loose 成功先 `list4 = list10` 把池替换成 `StartingTier ≤ T` 的下闭子集**（`:3587-3605`）。设第 `i` 槽进 loose 前存活池 `L_i`、roll `T_i`、`S_i` 非空，则：

```text
L_{i+1} = S_i − {selected}
连续 k 个生效 loose 槽后:  max StartingTier in L  ≤  min(T_1, …, T_k)   （running minimum）
```

所以前序低 tier roll 会 **永久删掉** 高 `StartingTier` 的目标卡，使其后续槽概率变 0。**这就是为什么必须按槽串起来模拟整个状态（池 + flag2 + 槽序号），不能把 3 槽当 i.i.d. 相乘。** [shop-entry-4 §6、§9.1] 专门驳斥了「池 100 张所以每张每槽 1%」的错误。

**source-catalog 视角的澄清（纠正一个易混点）：** `collection-sources.json` 的 `startingTier: AtMost` 规则表达的是 **来源池成员**（哪些卡 *能* 出），等价于 loose 池 `S_loose(tier)` 的 **上界**；它 **不是** dealer 的 native/loose 逐槽机制。`startingTier: Exact`（若存在）会表达「只含起始品级 == X 的卡」的池——这与 **没有任何商人** 的行为对应，所以 catalog **零 Exact 规则是预期的**（§3.4）。换言之：**Level 1 catalog 给的是「池成员」，native/loose 是「池内逐槽选牌」，两者正交，catalog 天然无法表达后者。** 这正是要新建概率核心、而非扩 catalog 规则的原因。

### 2.5 「至少出现一次」怎么算

目标事件是「这张卡 **出现在本次发的 3 张里** 的概率」（[shop-entry-4 §1]）。由于 §2.4 的路径依赖，精确解需要在 (每槽 tier roll × native/loose × uniform 选牌) 的树上做加权枚举，分支数随池大小和槽数爆炸。两种实现取向：

- **首选：蒙特卡洛重放。** 把 §2.2–§2.3 状态机包成确定性函数 `SimulateOneDeal(shop, playerState, rng)`，注入一个 **可种子化的 PRNG**；跑 N 次（如 10k），统计目标 id **真正被发到货架**（即跑到 `:3626-3629` 的发牌循环，且未被 `:3618-3623` 的 AutoSelect 早退跳过，§2.1）的频率，得到 **概率桶 + 置信区间**。优点：实现简单、忠实复刻路径依赖、天然产出「区间」而非假精度。
- **可选：小池精确枚举。** 当存活池很小时可做精确状态机展开（[shop-entry-4 §8.2/§8.3]，注意 §8.3「不要重复计入后续路径」）。仅作为对照/测试 oracle，不进默认路径。

**确定性要求：** 核心绝不调用 `Math.random`/`Time` 等不可重放源；PRNG 由调用方注入。这样 §7 的 exe-runner 单测对 **逐槽转移逻辑**（固定输入）可断言精确值，对蒙特卡洛聚合可用固定种子断言稳定区间 + 单调性。

### 2.6 Level 1/2/3 ↔ 三态映射

[shop-entry-4 §11] 的产品分层与 §1.3 三态对应如下：

| 文档分层 | 三态 | 需要的输入 | 落到哪个 Phase |
| --- | --- | --- | --- |
| Level 1：属于哪个来源池 | `Pool` / `NotInPool` | 现成（Level 1 已实现） | 复用 |
| Level 2：旧模型下为何能/不能进候选池 | `Explain` / `Fixed` / `WeightsMissing` | 现成 + 静态字段 | **Phase A** |
| Level 3：旧模型估算概率 | `Estimate` | + wiki 权重 + 假定 native 概率 + dealer hints | Phase B/C |

---

## 3. 数据来源与缺口

### 3.1 运行时可得输入（现成）

| 输入 | 来源 | file:line |
| --- | --- | --- |
| 当前 hero | 打开面板时读 `Data.Run.Player.Hero` / `Data.SelectedHero` | [CollectionPanel.cs:157-230] |
| 当前 day | 打开时读 `TheBazaar.Data.Run?.Day` | [CollectionPanel.cs:185-244] |
| 当前 encounter / choice template ids | `EncounterStateProbe.GetEncounterIds()` | [GameInterop/Encounter/EncounterStateProbe.cs:23-32] |
| 自动选中的 source | `CollectionPanelOpenSelectionResolver` 匹配 `CollectionSourceEntry.SourceTemplateIds` | [CollectionPanelOpenSelectionResolver.cs:12-107] |
| 来源池成员 + 每卡 match reason | `CollectionSourceOfferPoolResolver.Resolve(source, hero, catalogCards)` → `OfferedCardIds` + `OfferMatchesByCardId` | [Sources/CollectionSourceOfferPoolResolver.cs:11-33] |
| 单卡静态字段 | `CollectionCardVm`：`Id/Type/Size/StartingTier/Heroes/Tags/HiddenTags/IsEnchantable/Enchantments` | [Data/CollectionCardVm.cs:34-50] |
| day-gate 近似（哪些起始品级今天能见） | `DayTierSchedule.AllowsStartingTier(tier, day)`（二值天花板，**无逐 tier 权重**） | [Data/DayTierSchedule.cs:28-33] |
| 是否 tier 专卖（恒 loose、绕 day-gate） | `CollectionSourceEntry.SuppressDayGate`（= 首段规则 pin 了 tier） | [Sources/CollectionSourceEntry.cs:38] |

→ **解释层（Phase A）所需输入 100% 现成。**

### 3.2 缺的输入（4 个）+ 缺口处理

| 缺失输入 | 状态 | 缺口处理（本设计的策略） |
| --- | --- | --- |
| **`NativeItemTierProbability`** | 🔴 **唯一关键未知，客户端运行时也拿不到**（§1.2 三证；`TDayHourConfig.cs:20` 默认 `0.05f` 是占位） | 解释层 **不需要它**（只说「走 native 还是 loose」是按规则定性，不按概率）。估算层把它做成 **面板可配置假定值**（DTO 字段 `NativeItemTierProbability`，**默认 0.8**，范围 [0,1]，用户决策 2026-06-18），UI 标「估算假定值·非线上权威」，**绝不**把 `0.05f` 或 `0.8` 当线上真值。可选同时给「忽略 native（全 loose 下界）」作对照区间。⚠️ 0.8 是用户选定的默认，无权威依据；正因暴露为可调，UI 必须把结果标成假定估算，不得伪装成事实。 |
| **当天 tier 权重表** | 🟡 仅有 wiki 0.1.9 参考表（[shop-entry-3 §7]） | 作为 **带版本 + staleness 标注的参考常量** 接入（§3.3），仅供 `Estimate` 态；`Pool`/`Explain` 态用 `DayTierSchedule` 的二值天花板即可。 |
| **选中 merchant 完整 SpawningFilters**（`NumberCardsToSpawn`/`CardIdFilters`/`ItemTierFilters`/`RerollRepeats`，以及 `ExperiencePointsId`/`AutoSelect`） | 🟡 部分已知：8 个 tier 专卖（Curio/Silvia/Goldie/Luxe + 训练师 Adira/Argenta/Orlin/Pip）的 `ItemTierFilters` 由 catalog `SuppressDayGate` 间接已知；`NumberCardsToSpawn` 常见 3；其余普通商人 `ItemTierFilters` 视作空但需逐个核对；`ExperiencePointsId`/`AutoSelect` 影响发牌（§2.1）也需核对 | 做成 **schema v5 可选 `dealer` 块**（Phase C，§6），**只填验证过的**；缺省的 source 显示 `WeightsMissing`，不臆造。 |
| **reroll 排除集** | ⬜ 暂不接（[shop-entry-3 §6]）。旧字段 `GameStateMetadata.DealtCardForReRollExclusion` 不能代表线上状态 | 核心 DTO 留 `RerollExclusionIds` 字段但默认空集；不接入 runtime 读取。 |

### 3.3 wiki 参考表如何接入并标注

- **存放：** 作为一个 **显式常量表** 放进概率核心（如 `DealerTierWeightReference`），**不混进** `DayTierSchedule`（后者是二值天花板，语义不同，混入会让两个近似互相污染）。
- **强制标注（数据载体里就带，不靠 UI 临时拼）：** 表对象自带 `Source = "thebazaar.wiki.gg 0.1.9"`、`Authority = Reference`、`Model = "old-bazaar-card-dealer"`、`RecordedAgainstGameVersion`（如 `"0.1.9"`）。运行时客户端版本（如 v5.0.0）**新于** `RecordedAgainstGameVersion` 时，UI 须显式给 staleness 提示（如「wiki 0.1.9，可能早于当前游戏版本，仅供参考」），避免「精确版本号 = 当前权威」的假象（[R-honesty-5]）。
- **数值（[shop-entry-3 §7]，Day 1..10+ 的 Bronze/Silver/Gold/Diamond）** 原样录入，注明「行和约 1.00，差额按 `SelectRandomTier` 落 Bronze」「无 Legendary」「会随 balance patch 漂移」。
- **退化保证：** 没有参考表或 source 未验证时，估算层 **不出数字**，回落 `WeightsMissing`。

### 3.4 漂移纠正表（docs/任务简报 说法 vs 当前代码）

> 规则：以当前代码为准，发现漂移就纠正并标注。

| 说法 | 实际（当前代码） | 处置 |
| --- | --- | --- |
| 任务简报：json 在 `CollectionPanel/Data/CollectionSources/collection-sources.json` | 实际在 **`src/BazaarPlusPlus/Data/CollectionSources/collection-sources.json`**（不在 CollectionPanel/ 下） | 设计按真实路径。 |
| 「全表零 Exact 规则 = 只建模 loose、未建模 native」 | 数据层确实 **8 条全 AtMost、0 条 Exact**；但 **resolver 代码支持 Exact**（[Sources/CollectionSourceOfferPoolResolver.cs:196-197]）。且 catalog 的 Exact/AtMost 是 **池成员轴**，与 dealer **native/loose 逐槽轴正交**（§2.4） | 纠正措辞：「零 Exact」是 **数据事实**，非代码限制；catalog 无法表达 native/loose，故需独立概率核心。 |
| 「day-gate 是硬编码近似」 | 属实，且文件头已自注「真实表来自旧 `tierManager.json`、随 patch 漂移」（[Data/DayTierSchedule.cs:6-9]）；它是 **二值天花板**，无逐 tier 权重 | 解释层用它做「今天能否见到此起始品级」；**不**拿它当逐槽权重。 |
| 「概率层挂在 source offer pool 之后」 | 接缝确认在 `ApplyFilters()`，`_offerPoolCache.GetOrResolve(...)` 之后（[CollectionPanel.cs:795-849]，GetOrResolve 在 :815-819） | 设计据此。 |
| offer pool 缓存 key | `SourceKey | templateIdsFingerprint | OfferRuleFingerprint | heroKey`，**不含 day**（[CollectionSourceOfferPoolCacheKey.cs]） | 概率/解释缓存 **必须把 day 加进 key**（解释/估算依赖 day-gate 与权重），§4.4。 |

---

## 4. 架构

### 4.1 分层落点

遵守仓库分层（`Core/` 纯抽象、`GameInterop/` 游戏 DLL 耦合、`Game/` 特性、`Patches/`、`Infrastructure/`）：

- **概率/解释核心** → `Game/CollectionPanel/DealerModel/`。它 **Unity-free 且 BepInEx-free，但经 `BazaarGameShared` 枚举（`EHero`/`ETier`/`ECardType`/`ECardTag`/`EHiddenTag`/`EEnchantmentType`）与游戏 DLL 耦合**——**不是** game-DLL-free。**正因为它带游戏枚举，必须放 `Game/` 层，绝不能放 `Core/`**（`Core/` 禁游戏 DLL，由架构测试 `CoreLayeringTests` 强制），也别新建「无游戏 DLL」程序集（[R-arch-1]）。它能进 exe-runner 单测是因为测试 csproj 引用了 `BazaarGameShared.dll`（§7），不是因为它无依赖。
- **浮层 UI 组件** → `Game/CollectionPanel/Grid/`（与现有 `CollectionSourceAttributionBadge` 并列，uGUI）。
- **设置开关** → `Game/CollectionPanel/` 下新增 `ISettingsDockEntry` + `IBppConfig`/`BppConfig` 的 `ConfigEntry<bool>`，在 `BppComposition` 注册（§5.1）。
- **运行时输入读取**（若估算层将来要读 live player state）→ 走 `GameInterop/`（如 live cards 已有 adapter），核心只接收显式 DTO，**不直接读 runtime**。

### 4.2 概率/解释核心（显式 DTO）

沿用 [shop-entry-4 §11.3] 的命名骨架，分两层：

**(A) 解释层（Phase A 交付）** —— `Game/CollectionPanel/DealerModel/`：

```text
CollectionDealerSourceContext   // 输入: SourceKey, SourceKind(Merchant/Trainer), Hero?, Day,
                                //       SuppressDayGate, ActiveTierWeightKeys（当前权重表存在的 tier 集合）,
                                //       可选 dealer hints(见 schema v5)
CollectionDealerCandidate       // 输入: Id, Type, Size, StartingTier, Heroes, Tags, HiddenTags,
                                //       IsEnchantable, OfferMatches（来自现有 resolver）
CollectionDealerCardExplain     // 输出(每卡): ProbabilityState 枚举(NotInPool/Pool/Fixed/Explain/
                                //   WeightsMissing/Estimate), InPool(bool), HitSegmentKeys,
                                //   NativeEligible(bool), LooseEligible(bool), DayGatePass(bool),
                                //   SkillBoundaryExcluded, FixedDealVerified(bool?),
                                //   Estimate(可空, 见下方不可剥离值对象), Notes(本地化键列表)
CollectionDealerExplainResolver // 纯函数: (context, IReadOnlyList<candidate>) -> Dictionary<Guid, explain>
```

要点：
- **输入复用** 现有 `CollectionCardVm`（投影自 `CollectionCardVm.From`）和 `OfferMatchesByCardId`，不重新读模板。
- **键控** 一律 `Guid`（= `vm.Id`），见 §4.5。
- `NativeEligible` 仅按 **静态字段 + day-gate** 定性判定：「这张卡的 `StartingTier` 是否等于 *当前权重表存在的某个 tier key*（`ActiveTierWeightKeys`）」——**不**硬编码 `{Bronze..Diamond}`，也 **不需要** `NativeItemTierProbability`（[R-prob-4]）。
- `Fixed` 态须带 `FixedDealVerified`：仅当 dealer hint 已验证才允许显「概率=1」；未验证则降级 `WeightsMissing`/带问号变体（[R-honesty-3]）。

**(B) 估算层（Phase B）** —— 同目录新增，显式 DTO（[shop-entry-4 §11.3] 第二阶段照搬）：

```text
DealerShopDefinition  { SourceKey, SourceTemplateIds, NumberCardsToSpawn, CardIdFilters,
                        ItemTierFilters, RerollRepeats,
                        NativeItemTierProbability(面板可配置, 默认0.8, 非权威),
                        TierWeightsByDay(参考表), Model="old-bazaar-card-dealer" }
DealerPlayerState     { Hero, Day, Hour, PlayerSkillCardIds, PlayerHandCards(id,tier),
                        OpponentShelfEmptySockets, RerollExclusionIds }
DealerCandidate       { Id, Type, Size, StartingTier, Heroes, Tags, HiddenTags, Enabled }
DealerProbabilityCore // SimulateOneDeal(shop, state, IRng) + EstimateAppearance(target, N) -> EstimateBucket
EstimateBucket        // 不可剥离值对象: { Bucket(高/中/低 或区间), Model, Authority=Reference, Source,
                      //   ReferenceOnly=true } —— 无任何构造路径能产出「只有数值、没有这四项」的 bucket
```

要点（[R-honesty-2]）：
- **数值与标注不可分。** `EstimateBucket` 把概率桶与 `{Model, Authority, Source, ReferenceOnly}` 绑成单一值对象；`CollectionDealerCardExplain.Estimate` 持有它。任何下游消费者（排序键、tooltip、日志、导出）拿到数值就必然拿到标注。§7 有一条测试断言「不存在产出缺标注 bucket 的代码路径」。
- 核心复刻 §2.2–§2.5 的状态机，输出 **带元数据的区间**，不是裸 float。

### 4.3 接到现有 grid / virtualizer / source 解析

唯一接缝在 `CollectionPanel.ApplyFilters()`（[CollectionPanel.cs:795-849]）：

```text
现状:
  offerPoolResult = _offerPoolCache.GetOrResolve(sourceEntry, hero, catalogCards)   // :815-819
  ... CollectionFilterEngine.Apply(...)                                              // :829-845
  _virtualizer.SetVisible(ordered, ActiveType, offerMatchesByCardId)                 // :846

新增（开关开启时）:
  explain = CollectionDealerExplainResolver.Resolve(context, candidates)             // 新, 纯函数
  _virtualizer.SetVisible(ordered, ActiveType, offerMatchesByCardId, explainByCardId) // SetVisible 加一个可选参
```

下游：
- `CollectionGridVirtualizer.SetVisible` 增一个 **可选** `IReadOnlyDictionary<Guid, CollectionDealerCardExplain>? explainByCardId = null`（现签名 [Grid/CollectionGridVirtualizer.cs:88-101]），与 `_sourceMatchesByCardId` 同样缓存。
- 每格实化时，紧挨现有 `CollectionSourceAttributionBadge.Bind(card.gameObject, sourceMatches)`（virtualizer **:362**）旁，调 `CollectionShopProbabilityBadge.Bind(card.gameObject, explainByCardId.TryGetValue(vm.Id))`。
- **回收不变式（load-bearing，[R-recycle-1]）：** `CollectionCardPool.Return` 只 `SetActive(false)`，**不移除/重置子 GameObject**（[Grid/CollectionCardPool.cs:120]）。因此新徽标 `Bind` **必须** 在每个实化的格子 **无条件调用**（与既有 `AttributionBadge.Bind` 同处 virtualizer:362），并在 `explainByCardId` 无该 `vm.Id` 时 **显式 `SetActive(false)` 隐藏自身**——否则曾有徽标的卡回收进对象池后会把上一张卡的 `native/loose/fixed` 残留显示到错的卡上。现有 `AttributionBadge` 正是靠「无条件 Bind + 空时显式 hide」（[Grid/CollectionSourceAttributionBadge.cs:24-28]）才安全。§7 加一条回收测试。
- **零 grid 重建**：复用 `SetVisible` 通道、复用 badge 的「lazy-create 子 GameObject + show/hide」模式。

### 4.4 缓存与性能

- 解释结果可缓存，但 **key 必须含 day**（offer pool key 不含 day，§3.4），建议 key = `offerPoolCacheKey | day | overlayMode`。可新建 `CollectionShopProbabilityCache`，复用 `CollectionSourceOfferPoolCache` 的 per-panel + `Clear()`-on-reload 模式（[CollectionSourceOfferPoolCache.cs:12-35]）。
- 解释层是 O(候选数) 纯过滤，和现有 resolver 同量级，开销可忽略。
- 估算层蒙特卡洛较重：**仅在 `Estimate` 态、且按需**（hover 展开或显式「显示估算」）对 **单张卡** 跑，不在 `ApplyFilters` 对全表跑；结果按 `(sourceKey, day, targetId, 参数指纹)` 缓存。

### 4.5 GUID 安全（TryBind 坑，已知地雷）

`CollectionCardFactory.TryBind(vm)` 通过 `BppStaticDataAccess.GetCardTemplate(staticData, vm.Id)` 解析真实模板，**GUID 解析不到就返回 null、记 Warn、该卡不实化**（[Grid/CollectionCardFactory.cs:45]）。因此：

- 浮层 payload **一律按真实 `vm.Id`（来自 catalog 的真卡）键控**，绝不引入合成/占位 GUID。`CollectionSourceOfferPoolResolver` 只对它遍历的 `catalogCards` 产键（[Sources/CollectionSourceOfferPoolResolver.cs:24-32]），解释/估算核心也只对这些真卡输出——从源头杜绝伪造 GUID 流入 `TryBind`。
- 注意区分：`CollectionCardFactory` 里给 **实例** 用的合成串 `bpp-collection-N`（InstanceId）**不是模板 GUID**，不要混用。
- **dealer-hint 裸 id 只能作输入：** `DealerShopDefinition.CardIdFilters` / `ItemTierFilters`（§4.2，可能来自外部/wiki/抓取数据）是 **模拟输入**，**绝不能** 当 grid / overlay 渲染键；唯一允许到达 `TryBind` 或 overlay payload 的 id 是 **已存在于 `catalogCards` 的 `vm.Id`**（[R-guid-1]）。`TryBind` 对未知 GUID 只 Warn 后静默丢弃（不报错），所以这条约束必须在数据流上明文保证，不能靠「应该不会发生」。

---

## 5. UX

### 5.1 开关（「可选」怎么实现）

走既有 settings dock 模式：

1. `IBppConfig` + `BppConfig` 加 `ConfigEntry<bool> EnableCollectionShopProbabilityConfig`，在 `BppConfig.Initialize` 用 `config.Bind(section, key, false, desc)` 绑定（默认 **关**）。
2. 新 `CollectionShopProbabilitySettingsDockEntry : ISettingsDockEntry`，`Build` 返回 `new BppSettingsDockDefinition(key, labelResolver, new SettingsMenuToggleBridge(read, write, onChanged))`。**bridge 蓝本是 [Game/Settings/SettingsMenuToggleBridge.cs:12-31]（带 `onChanged`，在 `ApplyValue` 内同步触发），不是 `CombatStatusBarSettingsDockEntry`**——后者用自定义 `CombatStatusBarSettingsMenuBridge` 两委托 pull 式、无 `onChanged`，只能作「pull 回退」参考（[R-arch-3]）。
3. `BppSettingsDockOrder` 加常量（下一个空位 = 10）。
4. 在 `BppComposition`（注册区 [BppComposition.cs:98-107]）`_settingsDockRegistry.Register(...)`。
5. **状态推送：** 用 `onChanged` 回调即时刷新面板。但 **`CollectionPanelMount` 目前不持 `CollectionPanel` 实例引用**（它只 `host.AddComponent<CollectionPanel>()` 并订阅 `ChineseLocaleModeChanged`，[CollectionPanelMount.cs:22-27]）。所以推送需要 **新增一个静态钩子 `CollectionPanel.NotifyShopProbabilityToggled()`，镜像现有静态 `CollectionPanel.NotifyLocaleChanged`**，由 `onChanged` 调用（[R-arch-3]）。
   - ⚠️ **避坑：** 不要订阅 `ConfigEntry.SettingChanged`（PublicizeAll 下 CS0229 歧义，仓库全局零订阅）；用 `onChanged` 推或 `Update`/`ApplyFilters` 里 pull `.Value`。
   - ⚠️ **未验证项：** `onChanged→panel` 推送路径在生产中无先例（`CombatStatusBar` 走 pull），需实测；若不稳，**回退为 `ApplyFilters` 内 pull `.Value`**。

**二级控制（用户决策 2026-06-18）：**
- (a) **`Estimate` 显示开关**：默认关，单独的「显示旧模型估算（仅供参考）」子开关，避免默认就给数字。
- (b) **假定 native 闸门概率**：面板可配置项（`ConfigEntry<float>` 或面板内滑杆），**默认 0.8**，范围 [0,1]，标注「估算假定值·非线上权威」。改动时与 `Estimate` 缓存指纹联动（§4.4 缓存 key 含参数指纹），调一下立即重算可见卡。

### 5.2 触发方式

- **常驻徽标（默认）：** 选中 source 后，池内卡右上角一个小徽标（与 `CollectionSourceAttributionBadge` 同位/并列），显示 **态**：`池` / `固定` / `native` / `loose` / `?权重`。
- **hover 详情抽屉：** 悬停时展开「解释抽屉」，逐条列：在不在池、命中 segment、hero/size/tag/day-gate 是否通过、native/loose 资格、（`Estimate` 态才有的）区间 + 标注。**抽屉的具体挂载与时序须明确（[R-hover-1/2]）：**
  - **挂载点：** `CollectionCardHoverRelay` 当前只是把指针事件反射转发到原生卡的 `OnHover/OnHoverOut`（[Grid/CollectionCardHoverRelay.cs:34-53]），**没有抽屉表面**。两个可选实现：(a) 在 `card.gameObject` 下 **新建一个 uGUI 子 GameObject**（镜像 `AttributionBadge.EnsureBadge` 的子物体模式），由新的 `IPointerEnter/Exit` relay 切换显隐；或 (b) **给 `CollectionCardHoverRelay` 加 `onEnter/onExit` 回调**，在 virtualizer 现调 `hover.Bind(card)` 处（virtualizer:360）接线。设计 **选 (a)** 以与原生 tooltip 解耦；须声明抽屉与原生卡 tooltip 是 **共存** 还是 **抑制**（建议共存、错位摆放）。
  - **时序门（必须）：** 抽屉/解释的读取 **必须复用既有 `PollHover` 的完成门**——仅当 `cell.SetUpTask.IsCompletedSuccessfully` 才派发（[Grid/CollectionGridVirtualizer.cs:313-321]，原因见 :230-236 的 NRE 说明）；且解释查询 **按派发时刻当前格子的 `vm.Id`** 取，不在 bind 时捕获，避免回收后的格子显示上一张卡的解释。
  - overlay 一律 `raycastTarget=false`，避免干扰 hover hit-test。
- **不做** 点击弹窗（避免与卡牌已有交互冲突）。

### 5.3 各状态的视觉（复用既有 token，不造新色）

| 态 | 文案（示意，最终走本地化） | 颜色 token | 备注 |
| --- | --- | --- | --- |
| `NotInPool` | 无徽标 / 卡灰显 | — | 沿用现有 offer-pool narrowing |
| `Pool` | 「来源池」（hover 抽屉注明「客户端目录·可能滞后线上」） | `Colors.StatusDefault*`（蓝/中性） | 目录事实，非线上权威（[R-honesty-1]） |
| `Fixed`（已验证 hint） | 「固定出售」 | `Colors.StatusCompleted*`（绿） | 仅已验证 hint 才显；未验证降级 `WeightsMissing`/问号变体（[R-honesty-3]） |
| `Explain: native 可` | 「原生」（hover 带 `old-bazaar-card-dealer·非线上权威`） | `Colors.InfoChipBackground(accent)` | 模型定性，带模型免责（[R-honesty-3]） |
| `Explain: loose 可` | 「升级」（同上免责） | `Colors.InfoChip*` | 同上 |
| `WeightsMissing` | 「概率输入不足 · 未知，不代表更低」 | `Colors.StatusAbandoned*`（琥珀） | 显式「不知道」，防相对误读（[R-honesty-4]） |
| `Estimate` | 「≈ 中（旧模型·参考）」 | 区间色阶 + 标注角标 | **必带** model/source（数据层不可剥离，§4.2） |

尺寸/圆角/间距走 token：`Sizes.InfoChipHeight=22`/`InlinePillHeight=20`、`Radii.InfoChip=7`、`Borders.Thin=1`、`UiSpacing.Sm=6/Md=8`、`Sizes.FontSmall=12`。镜像现有 badge 的 `BppUiFont.Default` + `raycastTarget=false`；**不要** 像现 badge 那样硬编码背景色 `(0.07,0.08,0.1,0.9)`，改用 `Colors` token（如 `Colors.InfoChipBackground(accent)`）。

### 5.4 视觉上如何不暗示假精度

- `Estimate` **只给桶/区间**（高/中/低 或 5–15%），**绝不给两位小数**。
- `Estimate` 徽标 **永远带角标/前缀**（如「参考」或一个 model 图标）+ hover 必显 `old-bazaar-card-dealer · thebazaar.wiki.gg 0.1.9（可能早于当前游戏版本）· 仅供参考 · 非线上权威`（staleness 见 §3.3，[R-honesty-5]）。
- `Pool`/`Explain`/`Estimate` 三态 **配色族不同**，让「事实 vs 模型定性 vs 估算」一眼可分。
- 缺权重时 **显式显示 `?权重`**，不静默退成空白（否则会被读成「概率为 0」）。
- **相对误读防护（[R-honesty-4]）：** 当 **同一视图里有任一卡是 `WeightsMissing`** 时，**不**在其它卡上显示会形成排名暗示的数字桶——要么把该视图全体 `Estimate` **降级为定性**，要么 `WeightsMissing` 文案显式写「不可比较 / 未知，不代表更低」。这是一条 anti-misread 不变式（同列 §1.4 反目标）。

### 5.5 ScrollView/flexGrow 与 uGUI/UITK 边界

- **每格徽标是 uGUI**（镜像 `CollectionSourceAttributionBadge` 的 `RectTransform+Image+Text`），不受 UITK flexGrow 陷阱影响。
- **若** 加一条「当前 source 概率模型状态」横条到 **UITK 过滤面板**（`CollectionPanelView`），则必须遵守 ScrollView/flexGrow 陷阱：用 **外层 plain `VisualElement` viewport（flexGrow=1, overflow=Hidden）包住 ScrollView** 的既有 prior-art（[Ui/CollectionPanelView.Tree.cs:410-436]），否则 `contentContainer` 会塌缩到内容高度。

### 5.6 文案与本地化

所有徽标/抽屉文案走本地化引擎（`L` facade / `CollectionPanelText`），不硬编码；中英双语。标注串（model/source/仅供参考/staleness）作为不可省略的固定后缀。

---

## 6. 分阶段落地

| Phase | 范围 | 交付物 | 依赖 |
| --- | --- | --- | --- |
| **A（首个增量）解释层** | 三态里的 `NotInPool/Pool/Fixed/Explain/WeightsMissing`，**零数字** | `DealerModel/` 解释核心 + `SetVisible` 加 explain 参 + uGUI 徽标 + hover 抽屉 + settings 开关（默认关） | 全部现成输入（§3.1） |
| **B 估算层（随 A 同批交付，用户决策）** | `Estimate` 态：蒙特卡洛 + wiki 参考权重 + **面板可配置 native 概率（默认 0.8）**，强标注（不可剥离值对象），按需单卡计算，显示开关默认关 | `DealerProbabilityCore` + `EstimateBucket` + 参考权重常量 + native 概率配置项 + 区间 UI | wiki 表录入（已确认）+ native 概率配置项 |
| **C schema v5 dealer hints** | 给验证过的 source 补 `dealer` 可选块（`numberCardsToSpawn/cardIdFilters/itemTierFilters/rerollRepeats/model`）；未验证保持缺省 → `WeightsMissing` | catalog parser v5（保持 v4 不混 pinned/unpinned 段的不变式，[Sources/CollectionSourceCatalog.cs:96-104,239-277]） | 逐 source 验证 |
| **D（最后，可选）排序/热度** | 显式「按估算概率排序」sort mode；**绝不** 设为默认排序 | 复用 `CollectionFilterEngine` 排序（现为 tier/size/displayName 确定性，[Data/CollectionFilterEngine.cs:70-86]） | B/C 稳定后 |

**首个可交付增量 = Phase A + Phase B（用户决策 2026-06-18）。** Phase A（解释层、默认开、零数字）是基础与同一套输入 DTO；Phase B（估算层、显示默认关、按需单卡、强标注、native 概率面板可配置默认 0.8）随同交付。Phase C/D 仍后置。即便 B 同批上，**估算显示默认关、按需计算**，确保默认体验仍不冒充线上概率。

---

## 7. 测试计划

### 7.1 exe-runner 测试项目前置（Compile-Include 清单，[T-compile-include-trap]）

核心是纯 C# 但 **不是无依赖**：DTO 字段类型 `ETier/EHero/ECardType/ECardSize/ECardTag/EHiddenTag/EEnchantmentType` 都在 `BazaarGameShared.dll`。现有 exe-runner `tests/CollectionSourceFiltering.Tests` 之所以能编译 `CollectionCardVm`/resolver，是因为它的 csproj **显式引用了 `BazaarGameShared.dll`（经 `ManagedPath` HintPath）并逐文件 `<Compile Include Link=...>` pin 了每个 mod 源**。因此：

- 新建 `tests/CollectionShopProbability.Tests`（**exe-runner**：`<OutputType>Exe</OutputType>`、`Program.cs`、无 `Microsoft.NET.Test.Sdk`）须：(a) 引用 `BazaarGameShared.dll`（经 `ManagedPath`；`BazaarBattleService.dll` 仅在确有 DTO 需要时——本设计 DTO 不需要）；(b) **逐个 pin** `DealerModel/*.cs` 与其 **传递依赖** 的 `<Compile Include>`。
- **已知会被漏的传递依赖：** `DayTierSchedule.cs`（解释层调 `AllowsStartingTier`）**当前不在** `CollectionSourceFiltering.Tests` 的 Compile-Include 列表里；任何调它的核心一旦编入测试项目就会断编译，除非把它加进列表（`CollectionCardFacetRanks.cs` 已在列、是其传递依赖）。
- **每新增一个 `DealerModel/*.cs` 都要在测试 csproj 里镜像一条 `<Compile Include>`**——这正是 memory「Test harness Compile-Include trap」记录的坑。把 `From()`-式游戏卡投影拆成单独 partial（如 `CollectionCardVm.From.cs` 拆出 `TCardBase`）以保持可测半边干净，是既有范式。

### 7.2 Phase A 测试（`CollectionDealerExplainResolver`，确定性、无随机）

这些只依赖静态字段 + day-gate，**Phase A 即可独立验证**：

- **A1 day-gate：** `DayTierSchedule.AllowsStartingTier` 在 day 边界（1/5/7/8）对各起始品级的通过/拒绝。
- **A2 hero 资格：** 来源池 hero 规则（AllHeroes/FixedHero/SelectedHero/NeutralOnly）下 `InPool` 正确。
- **A3 native/loose 静态资格：** `NativeEligible` = `StartingTier ∈ ActiveTierWeightKeys` 且等于某可掷 tier；`LooseEligible` = `StartingTier ≤` 某可掷 tier；`Invalid` 起始品级 → `LooseEligible=true, NativeEligible=false`（且断言这是「权重表无 Invalid key」推出的，[R-prob-4]）。
- **A4 三态分类（原 9a）：** 无 dealer hint 时池内卡落 `WeightsMissing`；`Fixed` hint 未验证时不显「概率=1」而降级（[R-honesty-3]）。
- **A5 GUID 安全（守护）：** 解释输出的 key 全部来自输入 candidate 的真实 `Guid`，无合成 id（防回归到 TryBind 坑）。
- **A6 回收不变式（[R-recycle-1]）：** 模拟「曾有 explain 的格子回收为无 explain」，断言 `Bind` 被无条件调用且在无 entry 时 `SetActive(false)`（可在 UI 适配层做轻量验证，不引 Unity 运行时则以契约测试覆盖 Bind 的显隐分支）。

### 7.3 Phase B 测试（`DealerProbabilityCore` 状态机，[shop-entry-4 §11.3] 8 用例 + 估算）

依赖 Phase B 的 `DealerShopDefinition`/`DealerProbabilityCore`（含 `CardIdFilters/NumberCardsToSpawn/ItemTierFilters/RerollRepeats`，[T-phase-mismatch]）。每用例对应旧 dealer 行号（`BazaarCardDealer.cs:3477-3499, 3544-3605, 4467-4489`）：

1. **固定 `CardIdFilters` 0/1：** `NumberCardsToSpawn == CardIdFilters.Count` → 目标在 filters 且池非空 = 1，否则 0（`:3477-3483`，**注意此路径在 reroll/纯技能之前**，[R-prob-1]）。
2. **`CardIdFilters` 非空但 ≠ 固定直发：** 仍进随机路径（只窄化初始池）。
3. **native 仅 `StartingTier == T`：** 目标 `StartingTier != T` → native 路径 0（`:3564-3567`）。
4. **loose `<= T` 且 `list4 = list10` 影响后续槽：** loose 槽 roll Silver 后，`StartingTier > Silver` 的目标在 **后续槽** 概率变 0（断言池被破坏性收窄，`:3587-3605`）。
5. **native miss → `flag2` 强制后续 loose：** `S_native(T)` 空 → `num5--`、`flag2=true`，断言后续所有槽不再进 native 门（`:3567-3572`）。
6. **`ItemTierFilters` 关 native：** `ItemTierFilters` 非空 → native 门恒关，且 T 从 `ItemTierFilters` uniform 选、不用 day roll（`:3548, 3583-3585`）。
7. **reroll exclusion 条件生效：** `RerollRepeats==false` 且过滤后池 `< NumberCardsToSpawn` → 排除集清空、用原池（断言「在排除集 ≠ 概率 0」，`:3485-3496`）。
8. **手牌重复升级不改出现概率：** `TierDuplicateCardHandling` 只改展示品级、读 `PlayerCardHand`，断言目标 **出现概率** 不随手牌副本变化（`:4467-4489`）。
9. **确定性 + 蒙特卡洛区间（原 10，具体化，[T-test10]）：** 固定 fixture——池 = {一张 Bronze、一张 Silver}，day=3（天花板 Silver），`NativeItemTierProbability=0`（全 loose 下界），`N=10000`，`seed=42`。断言：(a) 两次 `seed=42` 跑出 **完全相同** 的发牌序列；(b) Silver 卡的出现频率 **严格高于** Bronze 卡（单调性断言，不断言精确浮点）。
10. **估算标注不可剥离（原 9b + [R-honesty-2]）：** 全 dealer hint + 参考权重下，`DealerProbabilityCore` 产出的 `EstimateBucket` **必有** 非空 `Model/Authority/Source/ReferenceOnly`；断言不存在产出「只有数值、缺标注」bucket 的构造路径。

### 7.4 禁止 coverage-theater

不写「断言 mock 调用序列」「匹配源码文本快照」「纯为覆盖率」的测试。每个用例 **对应一条 dealer 行为断言**（§7.3 第 3 列即行号锚点）。蒙特卡洛用例断言 **区间/单调性**，不断言脆弱的精确浮点。

> 参考：`CollectionSourceFiltering.Tests` 是 exe-runner（csproj `<OutputType>Exe</OutputType>` + 无 `Microsoft.NET.Test.Sdk`；`Program.cs` 用内联 `AssertEqual/AssertTrue` 顶层语句），可在其中加纯模型测试或另起 `CollectionShopProbability.Tests`。

---

## 8. 开放问题 / 需用户拍板

1. ✅ **已定（2026-06-18）：** `Estimate` 与解释层 **同批交付**（Phase A + B），但估算显示默认关、按需单卡计算、强标注。
2. ✅ **已定（2026-06-18）：** `NativeItemTierProbability` 做成 **面板可配置项、默认 0.8**（非权威假定，UI 标注）。可另给「全 loose 下界」作对照（实现时可选）。
3. ✅ **已定（2026-06-18）：** 接受 wiki 0.1.9 参考表入库，**带版本 + staleness 标注**（§3.3）。仍开放：谁/何时重新校验这个参考版本对当前 v5.0.0 的有效性（[R-honesty-5]）。
4. **schema v5 `dealer` 块的验证来源？** 逐 source 的 `ItemTierFilters/NumberCardsToSpawn/ExperiencePointsId/AutoSelect` 从哪取信（当前 `GameData.db` 抓取 / 抓包 / 人工核对）？这决定 Phase C 能覆盖多少 source。
5. **`AutoSelect` / 经验卡商店要不要建模？** 默认不建模、列为「出现概率定义在这些商店失真」（§2.1，[R-prob-2/3]）。是否接受这个 scope，还是要求识别并对这些商店显式标「不适用」？
6. **徽标默认显隐粒度？** 常驻徽标只在 **选中 source** 时出现，还是未选 source 时也对「全英雄池」给提示？倾向仅选中时出现（与 Level 1 一致）。
7. **`onChanged → panel` 推送 vs `ApplyFilters` pull？** 推送需新增静态 `CollectionPanel.NotifyShopProbabilityToggled()`，生产无先例（§5.1，[R-arch-3]）；是否接受首版用 pull、确认稳后再切推送？

---

## 9. Red-team 自检与修订

> 初稿完成后由 6 个独立评审（按 distinct lens，仅挑弱点/风险/坏假设，带 file:line，不改代码）复核，共 **27 条**（3 blocker / 10 major / 14 minor）。两条改动算法的 model-correctness 发现（R-prob-1 固定直发顺序、R-prob-2 经验卡预发牌）已亲自对照 `BazaarCardDealer.cs:3455-3545` 复核确认。**全部采纳**；修订点在正文以 `[R#]` 回链。

| # | 严重度 | lens | 弱点 | 处置（修订位置） |
| --- | --- | --- | --- | --- |
| R-honesty-1 | blocker | 诚实 | `Pool` 标成「verified 事实」，未承认客户端目录可能滞后线上一个 patch、非线上权威 | 降级 credibility 措辞；§0/§1.2/§1.3/§5.3 加目录滞后免责 |
| R-honesty-2 | blocker | 诚实 | 标注只绑在权重常量与 UI，未绑到每卡估算 DTO，数值与标注可分 | §4.2 引入 `EstimateBucket` 不可剥离值对象；§7.3 加测试 10 |
| T-compile-include-trap | blocker | 测试 | 未警告新增 `DealerModel/*.cs` 要扩 exe-runner csproj 的 Compile-Include；`DayTierSchedule.cs` 不在现列表 | 新增 §7.1 Compile-Include 清单 |
| R-honesty-3 | major | 诚实 | `Explain`/`Fixed` 泄露确定性；未验证 hint 也能显绿「概率=1」 | §1.3/§4.2/§5.3：`Fixed` 须 `FixedDealVerified`；`Explain` 带模型免责 |
| R-honesty-4 | major | 诚实 | `WeightsMissing` 防绝对空白但不防与 `Estimate` 并排的相对 0% 误读 | §1.4/§5.4 加 anti-misread 不变式 |
| R-arch-1 | major | 架构 | 「零 Unity/游戏 DLL」对游戏 DLL 半边为假；DTO 用 `BazaarGameShared` 枚举 | §0/§4.1/§4.2 改为「Unity/BepInEx-free 但 game-DLL-coupled，必须在 `Game/` 不能 `Core/`」 |
| R-prob-1 | major | 模型 | §2.2 把固定直发列为第 7 步；实际在 reroll/纯技能 **之前**（`:3477` vs `:3485`/`:3497`） | §2.2 重排步骤（已对照源码） |
| R-prob-2 | major | 模型 | §2.2 漏经验卡预发牌（`:3463-3465`）；空货架假设未限定 `ExperiencePointsId==Empty` | §2.1/§2.2/§2.3(c) 补经验卡步骤与限定 |
| T-phase-mismatch-tests-1-7 | major | 测试 | 测试 1-7 需 Phase B DTO，却列在一处暗示 Phase A 即可写 | §7 拆成 §7.2 Phase A / §7.3 Phase B，逐条标 phase |
| T-test10-vague-interval | major | 测试 | 测试 10 蒙特卡洛断言无 N/无期望/无容差 | §7.3 第 9 条给定 fixture + 单调性断言 |
| R-hover-1 | major | 集成 | hover 抽屉说「复用 `CollectionCardHoverRelay`」但该 relay 无抽屉表面/挂载点 | §5.2 给出具体挂载（新 uGUI 子物体 or 扩 relay 回调） |
| R-hover-2 | major | 集成 | 未要求抽屉读取受 `SetUp` 完成门约束，有 NRE/陈旧风险 | §5.2 加 `PollHover` 完成门 + 按派发时 `vm.Id` 取 |
| R-recycle-1 | major | 集成 | 未点明回收不变式：`CollectionCardPool.Return` 只 `SetActive(false)` 不重置子物体 | §4.3 加「无条件 Bind + 空时显式 hide」不变式 + §7.2 A6 |
| R-honesty-5 | minor | 诚实 | 固定「0.1.9」版本号本身是 false precision（早于 v5.0.0） | §3.3/§5.4 加 staleness 信号 + `RecordedAgainstGameVersion`；§8 开放问题 3 |
| R-arch-2 | minor | 架构 | 「exe-runner 可测」混淆了 Unity-free 与无依赖 | §7.1 明确须引 `BazaarGameShared.dll` + pin Compile-Include |
| R-arch-3 | minor | 架构 | 蓝本引错 bridge；`CombatStatusBar` 不用 `SettingsMenuToggleBridge`；Mount 不持面板引用 | §5.1 改引 `SettingsMenuToggleBridge.cs:12-31`，指明新增静态 `NotifyShopProbabilityToggled()` |
| R-prob-3 | minor | 模型 | 忽略 `AutoSelect` 早退（`:3618-3623`）会让「出现在 3 张里」失真 | §2.1/§2.5 注明并列入 §8 开放问题 5 |
| R-prob-4 | minor | 模型 | `Invalid` native-never 说成算法性，实为数据性（权重表无 Invalid key） | §2.3 限定为数据性；`NativeEligible` 绑 `ActiveTierWeightKeys` |
| R-prob-5 | minor | 模型 | 模型边界漏列 Combat 早退（`:3540-3542`） | §2.1 补为显式排除项 |
| T-test9-phase-b-dependency | minor | 测试 | 测试 9 把 `WeightsMissing`(Phase A) 与 `Estimate`(Phase B) 混为一条 | 拆成 A4（Phase A）与 §7.3 第 10 条（Phase B） |
| T-stale-program-cs-line-ref | minor | 测试 | `Program.cs:289-1055` 陈旧（文件已 1479 行） | §7.4 改为结构性描述，去掉脆弱行号 |
| R-path-1 | minor | 集成 | §5.5 路径漏 `Ui/` 子目录 | 修正为 `Ui/CollectionPanelView.Tree.cs:410-436` |
| R-guid-1 | minor | 集成 | 未声明 `CardIdFilters/ItemTierFilters` 是输入、绝不当渲染键 | §4.5 加该约束 |
| R-cite-1 | minor | 引用 | `AllowsStartingTier` 在 `:28-33` 不是 `:16-33` | §3.1 修正 |
| R-cite-2 | minor | 引用 | `ApplyFilters` 方法体 `795-849`；`:815-846` 起止不洁 | §0/§3.4/§4.3 用 `795-849` + `:815-819` + `:846` |
| R-cite-3 | minor | 引用 | `CollectionSourceEntry.cs` 在 `Sources/`、`CollectionPanelView.Tree.cs` 在 `Ui/` | 全文补子目录 |
| R-cite-4 | minor | 引用 | `EncounterStateProbe.GetEncounterIds` 在 `:23-32`、`GameInterop/Encounter/` | §3.1 修正 |

**复核结论：** 核心 per-slot RNG 数学（§2.3–§2.4）经评审确认与反编译逐行一致；缺陷集中在 ① 诚实边界从「概率数字」外扩到 `Pool`/`Explain`/`Fixed` 与估算 DTO 的标注完整性，② §2.2 状态机外围步骤顺序与遗漏，③ 集成层的回收/hover 时序与测试项目前置。均已在上文修订。

### 9.1 用户决策（2026-06-18）

复核修订后交回用户确认，定下三项，已并入正文：

1. **首版 = Phase A + Phase B**（估算层随解释层同批交付；估算显示默认关、按需单卡、强标注）。→ §0、§6。
2. **`NativeItemTierProbability` = 面板可配置、默认 0.8**（非权威假定，UI 标注；可另给全 loose 下界对照）。→ §3.2、§4.2、§5.1。
3. **wiki 0.1.9 参考表入库**，带版本 + staleness 标注。→ §3.3。

> **设计阶段到此结束（design-only）。** 实现按 §6 Phase A+B 推进时，先复核本文 §2.2/§2.3 的 file:line 与当前代码、§7 的 Compile-Include 清单，再动手。

---

## 附：与「进商店逻辑」系列的关系

- 本文是系列 #1–#5 的 **落地设计**：把 [shop-entry-4 §11] 的四阶段路线图细化为可执行的 Phase A–D、三态信息模型、DTO/接缝/测试。
- 本文 **不重新论证** 旧 dealer 行为；所有 dealer 机制结论沿用系列已复核的 file:line（并已亲自抽查 `BazaarCardDealer.cs` 关键段）。
- 本文 **以当前代码为准**：凡与系列文档或任务简报有出入，见 §3.4 漂移纠正表。
