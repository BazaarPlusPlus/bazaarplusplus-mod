# Goal: Collection Panel 商人 / 训练师选池筛选

> Status: SUPERSEDED by `../2026-06-02-collection-panel-source-catalog-schema.md`
> Superseded reason: This draft planned to reuse game source-card / runtime resolver semantics. The current direction uses a BPP-owned structured source catalog and removes game resolver fallback.
> Date: 2026-06-02
> Scope: Collection Panel 中新增“我遇到这个商人 / 训练师时，预期可能看到哪些物品或技能”的选池筛选能力。
> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this goal task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> **Allowed assistance:** Computer Use may be used for live game validation. Claude Advisor may be used for read-only design / code review. Codex remains the executor and must verify against current code and decompiled code.
> **Goal:** Let Collection Panel users select one merchant or trainer portrait and see the theoretical item / skill candidate pool that source can offer, ANDed with existing filters.
> **Architecture:** Keep source identity, UI state, and filter orchestration in `Game/CollectionPanel/`. Put reusable The Bazaar runtime / static-data adapters in `GameInterop/`. Reuse the game source-card spawning semantics instead of adding manual card matching rules.
> **Tech Stack:** C# 12, BepInEx 5, Unity UI Toolkit, The Bazaar runtime assemblies, decompiled source references, executable/xUnit test projects under `tests/`.

## 结论

不要为商人 / 训练师手写一套卡牌匹配规则，也不要复制 BazaarDB 的 manual query 语法。

商人 / 训练师本身是 source card。游戏已有 source card -> candidate card pool 的筛选语义，Collection Panel 应该复用这套语义生成候选 template id 集合，然后把它当作一个普通筛选维度接入现有过滤引擎。

这不是预测功能。它只回答“这个来源理论上可能给哪些卡”，不回答“当前这次商店一定会 roll 出哪几张”。

## Goal Definition

### User Outcome

用户在 Collection Panel 里可以：

- 在 Item tab 看到当前 hero 适用的商人 portraits。
- 在 Skill tab 看到当前 hero 适用的训练师 portraits。
- 点击一个 source portrait 后，结果只保留该 source 理论上可能提供的卡牌。
- 再叠加 hero / tier / size / search / package 等已有筛选时，所有筛选继续保持 AND。
- 再次点击已选 portrait 可以取消 source filter。

这个目标不承诺预测当前 run 的实际发牌结果，只承诺展示 source-card spawning filter 对应的理论候选池。

### Success Criteria

- [ ] 右侧 rail 不再同时暴露旧 `CollectionMerchantKind` merchant chips 和新 source portraits；第一版用 source portrait selector 替代旧 merchant chip 入口。
- [ ] Item tab 只显示 merchant sources；Skill tab 只显示 trainer sources。
- [ ] 每个 tab 最多选中一个 source；切换 tab / hero 后，不可见的 selected source 会被清空。
- [ ] `EHero.Common` 默认不选中，并且作为独立开关参与卡牌 hero filter / resolver hero filter；它不收窄 portrait 列表。
- [ ] Source identity 使用 `MerchantTrainerCatalog` entry 级别的 stable `SourceKey`，不是单个 template id。
- [ ] 一个 source entry 的所有 `TemplateIds` 都会进入 resolver 并 union 候选池。
- [ ] Source pool cache key 至少包含 `sourceKey + templateIdsFingerprint + heroFilterKey`。
- [ ] `EHero` 是 Collection Panel / catalog / cache key 的 source of truth；旧 `EBazaarHero` 只出现在 `GameInterop` adapter 内。
- [ ] `EHero.Karnok` 明确映射到旧 runtime 的 `EBazaarHero.Hero7`，不使用 enum-name 自动转换。
- [ ] `ItemTierFilters` 按理论并集裁剪：保留 `candidate.StartingTier <= any(source.ItemTierFilters)` 的候选。
- [ ] `CardDealer` / static data 未 ready 时，UI 显示 loading / unavailable，不静默退回未过滤结果。
- [ ] 没有新增 BazaarDB manual query、raw tag filter、文案解析、或手写 merchant-to-card 归属表。

### Agent Tooling Boundaries

- Computer Use：可用于启动 The Bazaar、打开 Collection Panel、点击 source portrait、检查 UI 行为、截图确认 portrait 渲染，以及读取 `BepInEx/LogOutput.log` 里的 `[BPP]` 日志。它是 runtime smoke / visual validation 工具，不是 source of truth。
- Claude Advisor：可用于只读审查方案或实现，重点挑战旧 runtime `cardRepo` 可用性、current static data evaluator fallback、`EHero` / `EBazaarHero` 映射、layer boundary、以及测试覆盖。Codex 必须自行执行最终修改和验证。
- Source of truth：只能是当前 repo 代码、当前 decompiled 代码、以及实际 runtime probe 结果。其他 Markdown 设计文档只能作背景，不能覆盖代码事实。

### Execution Tasks

- [ ] Probe runtime availability in a normal Collection Panel scene: log `GameServiceManager.Instance`, `CardDealer`, and `cardRepo` readiness before choosing old-runtime resolver vs current-static-data evaluator path.
- [ ] Add stable source identity to `MerchantTrainerCatalog`: generate / validate `SourceKey`, expose key lookup and hero-filtered visible source helpers, and preserve all `TemplateIds`.
- [ ] Add source selection state to `CollectionFilterState`: `SelectedMerchantSourceKey` and `SelectedTrainerSourceKey`, reset behavior, and `HasActiveFilters` integration.
- [ ] Constrain hero chip interaction: at most one non-Common hero selected, with Common as an independent toggle.
- [ ] Add `GameInterop` source offer resolver: accept primitive source template ids plus `EHero` filters, map to runtime hero filters internally, union all source templates, handle loading / unavailable, and apply `ItemTierFilters`.
- [ ] Add source-pool cache and retry orchestration in `CollectionPanel`: key by source identity plus hero filter, cancel stale retry tokens, and never treat loading as unfiltered results.
- [ ] Extend `CollectionFilterEngine.Apply(...)` to accept an optional resolved offer pool and AND it with the existing card filters.
- [ ] Replace the old merchant chip section in `CollectionPanelView` with tab-aware source portrait selector UI, using 8-per-row layout aligned with hero chips.
- [ ] Add encounter/source portrait provider in `GameInterop`: resolve representative source `ArtKey`, load `EncounterAssetDataSO.LoadPortraitSpriteAsync(...)`, cache sprites / failures, and fallback to text initials.
- [ ] Add focused tests for filter engine, catalog source keys, source visibility, Common behavior, source selection clearing, resolver hero mapping, `ItemTierFilters`, and loading retry.
- [ ] Run targeted automated verification, then use Computer Use for an in-game smoke covering Item merchant source, Skill trainer source, hero switching, reset, hover, and scroll.
- [ ] Optionally ask Claude Advisor for read-only review of the final diff; resolve only issues that are backed by current code, decompiled code, or runtime evidence.

## Source of Truth

本方案只以当前代码和 decompiled 代码为依据，不以其他设计文档作为权威。

当前代码事实：

- `Game/CollectionPanel/Encounters/MerchantTrainerEntry.cs`：一个可见商人 / 训练师 identity 可以对应多个 source card template id；例如 Aila 对应 8 个 `TemplateIds`。`Heroes.Count == 0` 表示全局 source，`AppliesToHero(hero)` 对所有英雄返回 true。
- `Game/CollectionPanel/Encounters/MerchantTrainerCatalog.cs`：`merchant-trainer-portraits.json` 是 embedded resource；当前公开的是 `Entries`、`TryGet(Guid templateId, out entry)`、`ForHero(EHero hero)`。目录按 template id 建索引，但 UI 选择不应把单个 template id 当成 portrait identity。
- `Data/Encounters/merchant-trainer-portraits.json`：当前 source entry 的 `kind/name/tier/heroes` 组合唯一，但 JSON 没有显式 `sourceKey` 字段。
- `BazaarPlusPlus.csproj`：`Data/Encounters/merchant-trainer-portraits.json` 已作为 embedded resource 打包。
- `Game/CollectionPanel/Data/CollectionFilterEngine.cs`：现有过滤是纯函数；类型、包裹、英雄、tier、tag、size、merchant、search 之间是 AND，同组内部是 OR。
- `Game/CollectionPanel/Data/CollectionFilterState.cs` / `CollectionPanelView.cs`：当前还存在 `CollectionMerchantKind` tag-like merchant chips；新功能不能在这个基础上继续扩写手写归属规则。
- Collection Panel / catalog / cache key 的 hero source of truth 是当前代码使用的 `EHero`。旧 runtime 的 `BazaarTypes.EBazaarHero` 只允许出现在 `GameInterop` adapter 内部。

Decompiler 事实：

- `decompiled/BazaarBattleService/BazaarBattleService.Models/BazaarCard.cs`：`BazaarCard.SpawningFilter` 包含 `CardIdFilters`、`CardTypeFilters`、`MerchantHeroFilters`、`EncounterHeroFilters`、`CardSizeFilters`、`CardTagFilters`、`ItemTierFilters`、`HiddenTagFilters`、`Rerolls`、`NumberCardsToSpawn`、`Enabled` 等字段。
- `decompiled/BazaarBattleService/BazaarBattleService.Repositories.CardRepository/StaticDataCardRepository.cs`：`FilterCards(BazaarCard card, List<EBazaarHero> heroFilters)` 的候选池语义是：如果 `CardIdFilters` 非空，直接按 id 返回；否则对 hero/type/size/card tag/hidden tag/enabled 做 AND，单个 filter list 内部是 OR。
- `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs`：`DealFilteredCards(...)` 在调用 `cardRepo.FilterCards(card, list)` 前，若 source 的 `MerchantHeroFilters` 非空就用它，否则用当前玩家 hero；后续还会排除已拥有技能、处理 `NumberCardsToSpawn`、board space、tier roll、随机 seed 等。
- `decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs`：`GetFilteredCards(...)` 返回的是受当前玩家、已拥有技能、board size、随机数、`NumberCardsToSpawn` 和 tier 限制影响的子集，不是图鉴需要的完整理论候选池。
- `decompiled/BazaarBattleService/BazaarBattleService/BazaarTypes.cs`：旧 runtime hero enum 使用 `Hero7` 表示当前 `EHero.Karnok`，所以不能用 enum name 自动转换。
- `decompiled/TheBazaarRuntime/TheBazaar.Assets.Scripts.ScriptableObjectsScripts/EncounterAssetDataSO.cs`：encounter asset 有 `portraitTextureReference` 和 `LoadPortraitSpriteAsync(...)`，商人 / 训练师 portrait 应走 encounter portrait asset，而不是复用 hero portrait provider。

## 已确认交互决策

- Item tab 只展示商人 portraits；Skill tab 只展示训练师 portraits。
- portrait 列表按当前 hero 选择收敛；选中具体英雄时，展示适用于该英雄以及全局 / Common 的 source。
- 英雄筛选 UI 最多允许一个非 Common 英雄处于选中状态；`EHero.Common` 默认不选中，作为中立开关单独计算，不占用“一个具体英雄”的名额。
- 点击 portrait 是 toggle；每个 tab 同时最多选中一个 source。再次点击已选中的 portrait 会取消该 source filter。
- source filter 与现有 hero / tier / size / search / package 条件继续保持 AND。
- source selector 放在 size / tier / 现有筛选区下面。
- portrait 布局一行 8 个，尺寸和 hero portrait chips 对齐；当前数量可以先不做折叠或分页。
- source identity 使用 `MerchantTrainerCatalog` entry；一个 portrait identity 可以包含多个 `TemplateIds`。UI state 和 cache 不能用单个 template id 代表一个 portrait。
- 切换 hero 或 tab 后，如果当前选中的 source 不再属于可见 source 列表，应自动清空该 tab 的 source selection，避免“看不到选中项但结果仍被过滤”。
- `CardDealer` 或 static data 未 ready 时，source selector 显示 loading / disabled 状态；这应是短暂状态，但不能让用户点击到空数据。

## 非目标

- 不做 BazaarDB 式文本语法，例如 `t:`, `r:`, `|`, `&`, range operators。
- 不暴露 raw `ECardTag` / `EHiddenTag` 标签筛选。
- 不基于文案解析规则，例如从 "Sells Burn items" 手写 Burn 匹配。
- 不维护 `ManualMerchantIdRules` / `ManualMerchantInternalNameRules` 这类卡牌归属表。
- 不做当前 encounter 的精确发牌预测。当前已发出的 selection set 属于另一个运行时观察功能，不属于图鉴筛选。

## 当前 Collection Panel 约束

当前 Collection Panel 的筛选模型已经适合承载这个能力：

- `CollectionFilterState` 持有当前选择状态，`CollectionFilterEngine.Apply(...)` 负责纯过滤。
- 不同筛选组之间是 AND：类型、包裹、英雄、tier、tag、size、merchant、search 逐项不满足就排除。
- 同一筛选组内是 OR：例如多个英雄、多个 merchant kind 任一命中即可。

现有 merchant 维度还不应继续扩大为手写分类。`CollectionCardClassifier.ResolveMerchants(...)` 当前从 `ECardTag.Merchant` 和 `EHiddenTag.*Merchant` 投影到 `CollectionMerchantKind`，但本地 `GameData.db` 中实际物品 / 技能更常见的是 `Burn`、`Crit`、`Freeze`、`Haste`、`Health` 等语义 hidden tag，而不是 `BurnMerchant` 这类归属标签。这个发现说明：如果继续在 mod 内猜“某卡属于哪个商人”，会走向维护成本高且容易错的规则表。

因此第一版 source portrait selector 应替换右侧 rail 里现有的 `CollectionMerchantKind` merchant chip 区，而不是在它下面再新增第二套“商人”筛选。保留 `CollectionMerchantKind` 类型本身可以作为兼容的内部投影，但 UI 不再暴露这些 tag-like merchant chips。

## 游戏侧筛选语义

`StaticDataCardRepository.FilterCards(...)` 是本功能的核心 source pool resolver 语义：

- 如果 source 的 `CardIdFilters` 非空，repository 直接按 id 命中。
- 否则在 `CardDictionary` 上依次套 hero、card type、size、card tag、hidden tag、spawn enabled。
- 这些约束之间是 AND。
- 每个 filter list 内部是 OR。

这个 API 目前只在旧 `BazaarBattleService` runtime 里有：`ICardRepository.FilterCards(...)` / `StaticDataCardRepository.FilterCards(...)`。当前 `JsonGameDataManager` 只提供 `GetCardById(...)` / `GetCardMap()` 这类静态模板读取；`BazaarGameShared.Domain.Spawning.SpawnFilters.*` 有新的 spawn-filter DTO / constraint 类型，但当前代码里没有现成的 source-card pool evaluator。

这不等于 Collection Panel 可以无条件依赖旧 runtime。当前游戏 Managed 里仍然带 `BazaarBattleService.dll`，mod 也仍编译引用它，但 `GameServiceManager.OnAwake()` 只是创建 `new BazaarCardDealer()`；`BazaarCardDealer.cardRepo` 只有在 `BazaarCardDealer.InitializeAsync(...)` 之后才会被设置。当前 decompiled `GameServiceManager` 没有显示调用这条旧初始化路径，所以第一版实现前必须做 runtime probe：在 Collection Panel 可打开的正常场景里记录 `Singleton<GameServiceManager>.Instance`、`CardDealer`、`CardDealer.cardRepo` 是否 ready。

实现策略应是：

- 优先尝试旧 runtime `cardRepo.FilterCards(...)`，因为它是现成的 source-card spawning filter evaluator。
- 如果 runtime probe 证明 `cardRepo` 在 Collection Panel 常用场景不可用，则不要硬等它；改为在 `GameInterop` 内实现一个基于当前 `JsonGameDataManager.GetCardMap()` / `ITCard` / `TSpawnFilter*` 的最小 evaluator，仍保持 `EHero` / source key 作为 Collection Panel source of truth。
- 无论 resolver 内部用旧 runtime 还是当前 static data evaluator，`CollectionFilterState`、catalog、UI、cache key 都不改。

注意：`ItemTierFilters` 不在 `FilterCards` 这一层直接裁剪 card id，但它仍然是 source card 的确定性选池语义。实际发牌流程在 source 的 `ItemTierFilters` 非空时，会从这些 tier 中选一个，再保留 `candidate.StartingTier <= selectedTier` 的候选。Collection Panel 不预测随机选中哪一个 tier，但第一版 resolver 应对非空 `ItemTierFilters` 做理论并集：保留 `StartingTier <= any(source.ItemTierFilters)` 的候选。day/hour probability、native tier probability、skill tier roll 仍属于当前 roll 预测，不在第一版复刻。

## 商人 / 训练师识别边界

商人和训练师不是新的 `ECardType`：

- `ECardType` 中没有 `Merchant` 或 `Trainer` 成员；商人 / 训练师 NPC 都属于 encounter。
- `ECardTag.Merchant` 是商人的标签。
- 训练师没有独立 tag，应理解为“非 Merchant 的技能教学 / 技能商店类 `EventEncounter`”。

本地静态数据检查显示：

- 正式商人大多是 `EventEncounter` + `Merchant` tag，并带 `SelectionContext.SpawnContext` 和 reroll 规则。
- 技能训练师大多是 `EventEncounter`，无 `Merchant` tag，描述为 teaches / sells skills / learn skills 等。
- 一些 `EncounterStep` 也会给技能，例如 `Hire a Teacher` 或 `Personal Trainer`，但它们不是 NPC source portrait，也不应直接混进第一版 merchant / trainer source selector。

第一版应优先使用已经落地的 `Data/Encounters/merchant-trainer-portraits.json` / `MerchantTrainerCatalog` 作为“哪些 template id 是商人 / 训练师”的来源目录。这个目录解决了名称重复、同名 tutorial/debug 变体、以及 merchant/trainer 身份识别问题。选池筛选在这个目录基础上补一层 source -> offered card ids。

## 推荐实现

### 1. 给 catalog entry 增加稳定 source key

当前 `MerchantTrainerEntry` 没有显式 key；`TryGet(Guid)` 是 template-id lookup，不是 UI source identity。第一版应在 `MerchantTrainerCatalog.Build(...)` 里给每个 entry 生成稳定 `SourceKey`，并把 UI state 存成 key：

```csharp
public string? SelectedMerchantSourceKey { get; set; }
public string? SelectedTrainerSourceKey { get; set; }
```

`SourceKey` 生成规则：

- 不用 display name 单独做 key，因为名称不是 template id 的唯一标识。
- 基础 key 使用 `Kind + Name + Tier + sorted(Heroes)`；当前 JSON 数据这个组合唯一。
- catalog build 时校验唯一性；如果未来数据出现碰撞，再追加 sorted `TemplateIds` fingerprint。
- `TemplateIds` 仍然全部保留；source key 只用于 UI selection 和 cache identity。

UI 可以使用一个代表 template id 渲染 portrait，例如 `entry.TemplateIds[0]`；但 offer pool resolver 必须遍历 entry 的所有 `TemplateIds` 并 union，不能只用代表 id。

### 2. 增加 source offer runtime adapter

新增 `GameInterop` 适配器，但不要让 `GameInterop` 依赖 `Game/CollectionPanel/Encounters/MerchantTrainerEntry`。Feature 层负责从 `SourceKey` 找 entry，并把 `TemplateIds` 传给适配器。这个 adapter 的内部 evaluator 可以先走旧 runtime；但它必须把 `cardRepo` 不可用作为正常状态处理，而不是异常：

```csharp
internal static class EncounterOfferPoolResolver
{
    public static EncounterOfferPoolResult ResolveOfferedTemplateIds(
        IReadOnlyList<Guid> sourceTemplateIds,
        IReadOnlyList<EHero> uiHeroFilters
    );
}
```

职责：

- 从 `Singleton<GameServiceManager>.Instance?.CardDealer?.cardRepo` 取得旧 runtime card repository；不可用时返回 loading / unavailable，而不是空集合。
- 对每个 `sourceTemplateId` 调 `cardRepo.GetCardById(...)` 取得 `BazaarCard`；单个 id 失败可以 warning 后跳过，全部失败才返回 unavailable。
- 按 source card 和 UI hero selection 计算传给 `FilterCards` 的 hero list。
- 调 `cardRepo.FilterCards(sourceCard, repoHeroFilters)`。
- source `ItemTierFilters` 非空时，对 `FilterCards` 结果做理论 tier eligibility 并集：保留 `candidate.StartingTier <= any(source.ItemTierFilters)`。
- union 所有 source template id 的候选 card template ids。

注意：这个 resolver 是 The Bazaar / BazaarBattleService runtime adapter，应放在 `GameInterop/`，不是 `Game/CollectionPanel/Data/`。

不要调用 `BazaarCardDealer.GetFilteredCards(...)` 做图鉴来源池。它会进入当前玩家状态、board space、已拥有技能排除、随机 seed、`NumberCardsToSpawn` 等逻辑，产物是“这次可能抽到的若干张”，不是完整候选池。

#### Hero filter 计算

这里不能简单地“只传当前 hero”。Decompiler 里的 `DealFilteredCards(...)` 先看 source card 自己的 `SpawningFilters.MerchantHeroFilters`：

- source `MerchantHeroFilters` 非空时，游戏用这个 list 作为 `FilterCards` 的 hero filter。
- source `MerchantHeroFilters` 为空时，游戏用当前玩家 hero。

Collection Panel 还要维持现有 UI hero filter 的 AND 语义。规则上先统一到 `EHero`，再在调用 `FilterCards` 前显式映射到 `BazaarTypes.EBazaarHero`：

- `uiHeroFilters` 为空，且 source `MerchantHeroFilters` 为空：传空 list，表示图鉴不按英雄限制。
- `uiHeroFilters` 为空，且 source `MerchantHeroFilters` 非空：把 source `MerchantHeroFilters` 反向映射成 `EHero` 后使用它。
- `uiHeroFilters` 非空，且 source `MerchantHeroFilters` 为空：传 `uiHeroFilters`。
- 两者都非空：传二者交集；交集为空时，该 source template id 对当前 UI hero filter 无候选。

这样同时保留 source card 自带约束和 Collection Panel 当前筛选语义。不要按 enum name 自动转换；新增一个显式 mapper，例如：

- `EHero.Common` <-> `EBazaarHero.Common`
- `EHero.Pygmalien` <-> `EBazaarHero.Pygmalien`
- `EHero.Vanessa` <-> `EBazaarHero.Vanessa`
- `EHero.Stelle` <-> `EBazaarHero.Stelle`
- `EHero.Jules` <-> `EBazaarHero.Jules`
- `EHero.Dooley` <-> `EBazaarHero.Dooley`
- `EHero.Mak` <-> `EBazaarHero.Mak`
- `EHero.Karnok` <-> `EBazaarHero.Hero7`
- `EHero.Hero8` 第一版不出现在 hero chips 中；如果进入 resolver，应返回 unsupported 并 warning，不应静默跳过。

如果运行时验证证明旧 runtime 对某些 source 不能通过这个 mapper 保真，再局部把 adapter 内部的 source filter calculation 改为直接保留 `EBazaarHero`；但 `CollectionFilterState`、catalog、UI 和 cache key 仍以 `EHero` 为 source of truth。

### 3. 在 ApplyFilters 前解析当前 source pool

source -> offered cards 是 hero-aware 的：同一个商人 / 训练师在不同 hero selection 下可能得到不同候选池。因此第一版不要在 catalog build 时给每张 `CollectionCardVm` 固定写死 `OfferSourceIds`。

推荐做法是在 `CollectionPanel.ApplyFilters()` 前解析当前 source pool：

```csharp
var selectedSource = ResolveSelectedSourceEntry(_filter);
var heroFilters = ResolveSelectedHeroFilters(_filter);
var offerPoolResult = selectedSource != null
    ? _offerPoolCache.GetOrResolve(selectedSource, heroFilters)
    : EncounterOfferPoolResult.NoneSelected();

if (offerPoolResult.Status == EncounterOfferPoolStatus.Loading)
{
    ShowSourceLoadingState();
    ScheduleSourcePoolRetry();
    return;
}

if (offerPoolResult.Status == EncounterOfferPoolStatus.Unavailable)
{
    ShowSourceUnavailableState(offerPoolResult.Reason);
    return;
}

var offerPool = offerPoolResult.Status == EncounterOfferPoolStatus.Ready
    ? offerPoolResult.TemplateIds
    : null;
var ordered = CollectionFilterEngine.Apply(_catalogCards, _filter, offerPool);
```

`offerPool` 是当前 source 在当前 hero 过滤下的候选 card template id 集合。`CollectionFilterEngine` 只需要额外检查：

```csharp
if (offerPool != null && !offerPool.Contains(card.Id))
    continue;
```

这样保持两个边界：

- `CollectionCardVm` 仍然只是卡牌静态投影，不承载“在某个 UI hero state 下属于哪些 source”的动态事实。
- source pool resolver / cache 仍然是运行时适配层，随 source selection、hero selection、static data readiness 变化而更新。

`Loading` 不能成为死状态。实现时需要一个明确 retry 触发，并且 retry 应该有上限：

- panel 可见且 selected source 未变化时，`Loading` 后以短间隔 poll `CardDealer.cardRepo` / static data readiness；ready 后重新执行 `ApplyFilters()`。
- source / hero / tab / search 等 filter state 变化时，取消旧 retry token，用新 key 重新 resolve。
- Close panel 时取消 retry。
- 如果短时间内多次失败，转为 `Unavailable("old-runtime-card-repo-not-ready")` 并记录一次 warning；下一次 open 或 filter change 仍可重试。若 runtime probe 表明这是稳定现象，切换到当前 static data evaluator 路线。

缓存 key 至少要包含 `sourceKey + templateIdsFingerprint + heroFilterKey`。含义是：同一个 visible source identity 在不同 hero filter 下可能解析出不同候选池；而一个 visible source identity 又可能包含多个 source template ids，不能只按单个 `sourceTemplateId` 缓存。例子：

- `Aila | no-hero-filter`
- `Aila | Vanessa`
- `Aila | Common+Vanessa`

这三种应是三个不同 cache entry。`heroFilterKey` 可以是规范化后的 hero 名称串或 enum 值串，例如空字符串表示无 hero filter，`Common,Vanessa` 表示 Common 和 Vanessa 同时作为 hero filter 传给 game repo。

### 4. 扩展 filter state

在 `CollectionFilterState` 增加 tab-specific source 选择：

```csharp
public string? SelectedMerchantSourceKey { get; set; }
public string? SelectedTrainerSourceKey { get; set; }
```

过滤时按 `ActiveType` 取对应 source：

```csharp
var selectedSourceKey =
    filter.ActiveType == ECardType.Item
        ? filter.SelectedMerchantSourceKey
        : filter.SelectedTrainerSourceKey;
```

- source 组与其他筛选组 AND：继续和 hero、tier、size、search、package 等条件一起生效。
- 点击未选中的 source：替换当前 tab 的 source。
- 点击已选中的 source：清空当前 tab 的 source。
- Reset 清空两个 source 字段。
- tab / hero 切换后，先根据当前 tab + hero 重算可见 source；如果当前 tab 的 selected source key 不在可见 source key 集合中，清空它再 ApplyFilters。
- `HasActiveFilters` 要把两个 source key 纳入判断。

英雄筛选可以先保留现有 `HashSet<EHero>` 状态形状，但 UI toggle 逻辑要收敛：

- 点击非 Common 英雄时，清掉其他非 Common 英雄，再 toggle 当前英雄。
- 点击 Common 时，只 toggle Common，不影响当前具体英雄。
- 传给 source resolver 的 hero filters 应包含当前具体英雄和 Common（如果 Common 被选中）；如果没有任何 hero filter，则保持“不按英雄限制”的默认行为。
- Common 默认不选中。

### 5. UI 呈现为 source selector，不是 tag selector

右侧 rail 中在 size / tier / 现有筛选区下面新增来源选择区：

- 使用商人 / 训练师显示名和头像。
- Item tab 显示 merchants；Skill tab 显示 trainers。
- 按当前具体英雄过滤 source entries：没有具体英雄选中时显示当前 tab 的全部 source；有具体英雄选中时显示 `entry.AppliesToHero(selectedHero)` 的 source。`Heroes.Count == 0` 的全局 source 会自然保留。
- Common toggle 不单独收窄 portrait 列表；它只参与卡牌 hero filter / resolver hero filter。原因是当前 catalog 没有 `EHero.Common` entry，只有“空 heroes 表示全局 source”的语义。
- 选中 source 后更新当前 tab 的 `SelectedMerchantSourceKey` 或 `SelectedTrainerSourceKey`。
- portrait 一行 8 个，尺寸与 hero chips 对齐。
- 不显示 `Burn`、`Poison`、`Weapon` 这类 raw tag chip，除非它们只是 source 的说明文案而非可点筛选项。
- 当前 `CollectionMerchantKind` merchant chip section 应被 source portrait section 替代，避免右侧 rail 同时出现“语义 merchant tag”和“真实 merchant source”两套入口。

source portrait asset 路径也要明确：

- 不复用 `HeroPortraitSpriteProvider`，它只处理英雄默认皮肤。
- 新增 encounter/source portrait provider，输入 representative source template id。
- 通过 `BppStaticDataAccess.GetCardTemplate(staticData, entry.TemplateIds[0])` 取 `ArtKey`；再按 encounter asset 路径加载 `EncounterAssetDataSO`，调用 `LoadPortraitSpriteAsync(...)` 得到 `Sprite`。
- 如果 representative id 或 portrait asset 加载失败，fallback 为文本 / initials chip，不阻断筛选。
- provider 应 cache sprite / failed key，并用 token 防止异步加载完成后写回已复用的按钮。

用户心智是：“我在游戏里遇到 Aila / Hef / Bjorn / Nufu，点同名来源，看可能出现的东西。”

### 6. Loading / unavailable 状态

区分四个状态，避免把“数据没 ready”误当成“没有 source filter”：

```csharp
internal enum EncounterOfferPoolStatus
{
    NoneSelected,
    Loading,
    Ready,
    Unavailable,
}
```

- `NoneSelected`：没有选中 source，`CollectionFilterEngine` 不接收 offer pool，行为保持现状。
- `Loading`：已选中 source，但 `CardDealer` / `cardRepo` / static data 暂不可用；UI 显示 loading / disabled，不要以 `offerPool = null` 继续显示未过滤结果，并按上面的 retry 规则重新 resolve。
- `Ready`：传 `TemplateIds` 给 filter engine。
- `Unavailable`：catalog entry 或 runtime source card 全部解析失败；UI 显示状态，不要静默退回未过滤结果。

## 候选池边界

`FilterCards` 产出的是理论候选池，不是某次商店当前实际发出的卡。

游戏在实际发牌时还会考虑：

- `NumberCardsToSpawn`
- reroll 排除
- board / storage 空位
- day/hour tier probability
- native tier probability
- already owned skill exclusion
- skill tier roll
- seed 随机选择

这些逻辑决定“这次 roll 出哪几张”。Collection Panel 的 source filter 不应复刻这些随机和局部状态，它只需要展示可被该 source 选中的候选集合。

## 验证计划

优先做小范围验证：

1. 单元 / executable test 覆盖 filter engine：
   - 无 source 选择时行为不变。
   - 传入 resolved offer pool 时，只显示 `card.Id` 属于该 pool 的 VM。
   - 第二个 source 选择会替换同 tab 的第一个 source。
   - 再次点击已选 source 会取消 source filter。
   - source 与 hero / tier / size / search 组间 AND。
   - Item tab 只使用 merchant source；Skill tab 只使用 trainer source。
   - reset 清掉 merchant / trainer source selection。
   - 英雄 UI 最多保留一个非 Common 英雄，Common 可与一个具体英雄共存。

2. catalog / controller 层测试：
   - 一个 `MerchantTrainerEntry` 多个 `TemplateIds` 时，resolver 输入包含所有 ids。
   - `SourceKey` 在当前 JSON 中唯一；碰撞时能追加 template fingerprint 或 fail fast。
   - 有具体 hero 时 source 列表只保留 `AppliesToHero(hero)`；没有具体 hero 时显示当前 tab 全量 source。
   - Common toggle 不影响 portrait 列表，但会进入 resolver 的 `uiHeroFilters`。
   - tab / hero 切换后不可见 selected source 会被清空。
   - source portrait provider 对有效 encounter portrait 返回 sprite，对失败加载返回 fallback，不影响筛选。

3. runtime adapter 验证：
   - 先做旧 runtime availability probe：Collection Panel 可打开的主场景中，记录 `GameServiceManager.Instance`、`CardDealer`、`cardRepo` readiness。
   - 对 Aila / Hef / Bjorn / Nufu 各解析一次 offered ids。
   - 记录 source 名称、source key、source template id count、候选数量、前几个候选名。
   - 确认没有走手写规则或文案解析。
   - 确认有 `MerchantHeroFilters` 的 source 会按 source hero filters 和 UI hero filters 的交集解析。
   - 明确覆盖 `EHero.Karnok` -> `EBazaarHero.Hero7` 映射。
   - 对非空 `ItemTierFilters` source 验证 `StartingTier <= any(source.ItemTierFilters)` 的理论并集裁剪。
   - 模拟 `cardRepo` 暂不可用后 ready，确认 loading retry 会重新 apply source pool。

4. 游戏内 smoke：
   - 打开 Collection Panel，选择一个商人来源，结果数量变化合理。
   - 切换英雄后 source 列表和结果按英雄收敛。
   - Item tab 上商人来源有效；Skill tab 上训练师来源有效。
   - hover / scroll / reset 不回退。

## 代码锚点

- `Game/CollectionPanel/Data/CollectionFilterState.cs`：新增 source selection state。
- `Game/CollectionPanel/Data/CollectionFilterEngine.cs`：接入 resolved offer pool membership，保持组间 AND。
- `Game/CollectionPanel/Data/CollectionCardVm.cs`：第一版不新增 source fields，保持静态卡牌投影。
- `Game/CollectionPanel/Data/CollectionCatalogBuildSession.cs`：第一版不注入 source 反向索引。
- `Game/CollectionPanel/Encounters/MerchantTrainerEntry.cs`：新增 / 暴露 `SourceKey`，保留 `TemplateIds` 作为 resolver 输入。
- `Game/CollectionPanel/Encounters/MerchantTrainerCatalog.cs`：生成并校验 source key，提供 key lookup / visible source helpers。
- `Game/CollectionPanel/CollectionPanel.cs`：按 active tab + hero selection 计算 visible sources；在 `ApplyFilters()` 前解析 source pool；处理 loading / unavailable。
- `Game/CollectionPanel/Ui/CollectionPanelView*.cs`：用 source portrait selector 替代当前 merchant chip section；异步加载 source portrait 时使用 token 防陈旧。
- `GameInterop/StaticCards/BppStaticDataAccess.cs`：静态模板读取先例。
- `GameInterop/`：新增 source offer resolver；优先调用 `Singleton<GameServiceManager>.Instance.CardDealer.cardRepo.FilterCards(...)`，但必须容忍旧 runtime `cardRepo` 不 ready，并保留切换到当前 static data evaluator 的内部边界。该 resolver 只接收 primitive ids / `EHero` filters，不依赖 CollectionPanel entry 类型。
- `GameInterop/`：新增显式 `EHero` <-> `BazaarTypes.EBazaarHero` mapper，至少覆盖 `Karnok` / `Hero7`。
- `GameInterop/`：新增 encounter portrait provider，基于 representative source template `ArtKey` 加载 `EncounterAssetDataSO.LoadPortraitSpriteAsync(...)`。
- `tests/CollectionFilterEngine.Tests/Program.cs`：最小 executable verification target。
- `tests/Architecture.Tests/CoreLayeringTests.cs`：增加 `GameInterop` 不依赖 `Game/` feature namespace 的 ratchet，保护 resolver 边界。

## Open Questions

- 是否后续要 mirror day/hour probability、native tier probability 或 skill tier roll；第一版先不做，因为这些会进入当前 run prediction。
- 对 `EncounterStep` 技能来源是否做第二阶段支持。第一版建议不做，避免把 NPC source 和奖励步骤瓦片混在一起。
