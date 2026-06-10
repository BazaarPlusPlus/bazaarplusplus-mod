---
status: superseded
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# 包裹卡商人头像（Package → Merchant Portrait）设计

> **Status:** Draft（v2，已过 Codex 对抗评审并修订）— 待人工确认后实施。本轮**只分析、未改代码**。
>
> 本文 `file:line` 引用分两类：**（A）committed 证据** = 仓库内 `decompiled/` 与 `src/` 锚点，长期稳定；**（B）活数据实测** = 对游戏运行期缓存 `GameData.db` 的一次性查询（见 §2），可复现但不入库。凡“数量/覆盖率”结论均标注为 (B)，并给出复现方法。与第一版会话分析（误判为 `offerRule`“商人卖什么”）不一致处，**以本文为准**。
>
> **评审修订记录（2026-06-08，Codex adversarial-review）**：
> - **[high] 池化重挂 bug 已修**：原方案只挂 `ItemController.ShowCard(bool)`，但 `CardDeathComplete()` 走 `Cleanup()→PoolObject()(SetActive(false))→ShowCard(true)`（`decompiled/TheBazaarRuntime/ItemController.cs:808-813`、`Extensions.cs:9-12`），且 `CardController.Cleanup()` **不清** `CardData`/`boardSection`（`CardController.cs:310-315`）——会把头像重挂到已入池的死卡上并污染复用。改为：attach 仅在 active 非池化卡上发生 + 显式 patch `Cleanup` 删除 + 强 token（InstanceId+TemplateId+boardSection）。见 §5.3 / §5.4。
> - **[medium] 边界穿透已修**：`GameInterop` 的 resolver 不得依赖 `Game` 层的 `CollectionSourceCatalog`。名字兜底改为在 `GameInterop` 内**用活静态数据自建** merchant-name→encounter-templateId 索引（`BppStaticDataAccess.LoadCardMap` 枚举 `ECardTag.Merchant`），零 `Game` 依赖。见 §5.1 / §4。

**Goal:** 给玩家**包裹（背包/Stash/手牌）里的「包裹卡」**额外叠加一个**商人头像**——该商人就是“把这张包裹卖给他才会触发特殊效果”的那个唯一商人。参考 `decompiled/bazaar-qinglong` 评级标签的**世界空间挂载技术**，复用 mod 既有的 `EncounterPortraitSpriteProvider` 出图。

**Tech Stack:** C# 12 / netstandard2.1、Unity 世界空间（`SpriteRenderer`，**非 UGUI / 非 UITK**）、Harmony postfix、游戏域类型 `ITCard`/`TCardAbility`/`TPrerequisiteRun`/`TRunConditionalCurrentEncounter`/`TCardConditionalId`（均已 publicize）、既有 `GameInterop/EncounterPortraits`、`GameInterop/StaticCards`。

---

## 0 已确认决策

| # | 决策点 | 选定 |
|---|---|---|
| 1 | “包裹卡”身份 | `ITCard.HiddenTags` 含 `EHiddenTag.Package`（`EHiddenTag.cs:101`） |
| 2 | card→merchant 映射 | **卡能力图里嵌入的商人 encounter GUID**（`TTriggerOnCardSold` 奖励能力的 `TPrerequisiteRun→TRunConditionalCurrentEncounter→TCardConditionalId.Id`），严格 1:1 |
| 3 | 头像来源 | 把该 GUID 直接喂 `EncounterPortraitSpriteProvider.LoadPortraitAsync(Guid)` → `Sprite`（已验证嵌入变体 `ArtKey` 100% 有效） |
| 4 | UI 挂载 | 世界空间子 `SpriteRenderer`，parent 到 `ItemController.transform`，qinglong `TierBadgeRenderer` 同款（mesh/置顶/先删后建） |
| 5 | 钩子 | **attach**：`[HarmonyPostfix]` on `ItemController.ShowCard(bool)`，仅当 `show && gameObject.activeInHierarchy && IsPlayerBoard`；**remove**：`show==false` 以及 `[HarmonyPostfix]` on `ItemController.Cleanup()`（覆盖死亡/入池）。**绝不**在非 active 卡上 attach（避开 `CardDeathComplete` 的 `ShowCard(true)`） |
| 6 | 显示范围 | `IsPlayerBoard`（`boardSection ∈ {Player, Storage}`，排除 `Opponent`）；默认含手牌+Stash |
| 7 | 复用安全 | attach 携带 token=(InstanceId, TemplateId, boardSection)；异步出图回调前校验“卡仍 active + CardData 同 InstanceId/TemplateId + token 未变”，否则丢弃 |
| 8 | fallback | 非 Package / 取不到 GUID / portrait 为 null（含动画头像）→ **静默不显示** |
| 9 | 边界 | resolver→`GameInterop/`（**仅能力图 + 活静态数据自建索引，零 `Game` 依赖**）；renderer→`Game/`；patch→`Patches/`；portrait 复用 `GameInterop/EncounterPortraits/` |

---

## 1 背景：包裹机制拆解

### 1.1 机制（贴游戏语义）

“包裹卡”是 `cardType==Item` 且 `HiddenTags` 含 `EHiddenTag.Package` 的物品，InternalName 一律为 `"<Merchant>'s Package"`（如 `Tinker's Package`、`Tok's Clocks' Package`、`Chronos' Package`）。其“卖出奖励”能力结构为：

```
TCardAbility {
  Trigger = TTriggerOnCardSold              // 卖出本卡时
  Action  = TActionGameSpawnCards           // 生成奖励
  Prerequisites = [
    TPrerequisiteRun {
      Conditions = TRunConditionalCurrentEncounter {   // 当前 encounter 必须是…
        Conditions = TCardConditionalId { Id = <商人encounter templateId>, IsNot = false }
      }
    }
  ]
}
```

语义：仅当“当前 encounter == 该商人”时奖励才触发——即**卖给这个特定商人才有特殊效果**。
- `TRunConditionalCurrentEncounter.IsSatisfiedBy` 取 `Run.GetCurrentState()?.GetCurrentEncounter()`（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Prerequisites.Conditionals/TRunConditionalCurrentEncounter.cs:14-22`）。
- `TCardConditionalId.IsSatisfiedBy` 比对 `target.TemplateId == Id`（同目录 `TCardConditionalId.cs:23-31`）。

### 1.2 域类型坐标（resolver 导航路径，全部 committed 证据 A）

| 节点 | 成员 | 锚点 |
|---|---|---|
| `ITCard.Abilities` | `Dictionary<string,TCardAbility>` | `BazaarGameShared.Domain.Cards/ITCard.cs:41`、`TCardBase.cs:43` |
| `TCardAbility` | `Prerequisites : List<ITPrerequisite>?`、`Trigger : TTriggerBase` | `BazaarGameShared.Domain.Effect/TCardAbility.cs:20,14` |
| `TPrerequisiteRun` | `Conditions : ITRunConditional` | `BazaarGameShared.Domain.Prerequisites/TPrerequisiteRun.cs` |
| `TRunConditionalCurrentEncounter` | `Conditions : ITCardConditional?` | `…Prerequisites.Conditionals/TRunConditionalCurrentEncounter.cs` |
| `TCardConditionalId` | `Id : Guid`、`IsNot : bool` | `…Prerequisites.Conditionals/TCardConditionalId.cs:11-14` |
| `ITCard` 其它 | `Id/InternalName/Tags/HiddenTags/ArtKey` | `ITCard.cs:10,14,26,28,30` |
| `EHiddenTag.Package` | 枚举值 | `BazaarGameShared.Domain.Core.Types/EHiddenTag.cs:101` |

### 1.3 实测结论（活数据，证据 B —— 见 §2 复现）

- 当前活数据共 **120 张包裹卡 / 40 个商人**。
- **120/120** 张都能经 §1.2 类型路径取到商人 GUID；**0 张**含多个商人 GUID → **严格 1:1**。
- 取到的 GUID 即商人 encounter 卡（`Tags` 含 `ECardTag.Merchant`、`$type` 为 `TCardEncounterEvent`）的 templateId。
- **120/120** 张所指向的商人 encounter 变体都有有效 `ArtKey`（非空、非 `"Invalid"`）。
- 嵌入变体的 `ArtKey` 与 `collection-sources.json` 规范变体的 `ArtKey` **逐字节相同**（例：Aila `dc12f4bd…` 与 `0cb20108…` 都 → `562ac0b78d6ccfa47bb72e5d7aaa8ad8`）。→ **直接用嵌入 GUID 即可拿到正确头像**，无需名字解析、无需 `collection-sources.json`。

---

## 2 数据漂移说明（务必知晓）

发现并记录一处会误导的漂移：

- 仓库随包的 `…/StreamingAssets/GameData.db`（及 `bazaarplusplus-agent/data/card-oracle.json`）是**陈旧 v5.0.0：2709 张卡，0 张 Package**——`EHiddenTag.Package` 与 14 个 `*Merchant` 隐藏标签在枚举里存在但**无任何卡使用**。
- **当前真数据在游戏运行期缓存**：`~/Library/Application Support/com.TempoStorm.TheBazaar/prod/cache/GameData.db`（**2889 张卡，120 张 Package**）。Mod 运行期经 `BppStaticDataAccess`(`Data.GetStatic()` → `JsonGameDataManager`) 读的就是这份，**所以 mod 在游戏里看得到包裹卡**。
- 仓库 `decompiled/` 也是 v5.0.0 期，但**本方案用到的类型都已在 `decompiled/`**（见 §1.2），故**类型层面无需重新反编译**；漂移只在“卡数据量”。
- 中文“包裹” = 英文 `Package`：游戏译文走 hash 索引的 per-locale SQLite（`…/prod/cache/translations/zh-CN.bytes`，表 `translation(hash,text)`），`GameData.db` 本身全英文。包裹卡 zh 标题形如 `<商人名>的包裹`（如 `叮当的包裹`=Tinker's Package）。

**证据 B 复现方法**（只读，勿入库）：

```bash
cp "$HOME/Library/Application Support/com.TempoStorm.TheBazaar/prod/cache/GameData.db" /tmp/prod.db
python3 - <<'PY'
import sqlite3,json
c=sqlite3.connect('/tmp/prod.db'); rows=c.execute('select Id,Data from cards').fetchall()
cards={i:json.loads(d) for i,d in rows}
merch={i for i,x in cards.items() if 'Merchant' in (x.get('Tags') or []) and str(x.get('$type','')).startswith('TCardEncounter')}
pkg=[x for x in cards.values() if 'Package' in (x.get('HiddenTags') or [])]
def target(card):
    out=[]
    def rec(o):
        if isinstance(o,dict):
            if o.get('$type')=='TCardConditionalId' and o.get('IsNot') in (False,None) and o.get('Id') in merch: out.append(o['Id'])
            for v in o.values(): rec(v)
        elif isinstance(o,list):
            for v in o: rec(v)
    rec(card); return set(out)
print('packages',len(pkg),'merchants',len(merch))
print('all-resolve', all(len(target(x))==1 for x in pkg))
PY
```

---

## 3 可复用基建（均 committed 证据 A）

| 关注点 | 复用件 | 锚点 |
|---|---|---|
| 头像加载 | `EncounterPortraitSpriteProvider.LoadPortraitAsync(Guid)` / `TryGetCached(Guid,out Sprite?)`，key=商人 templateId，自带缓存/in-flight/负缓存 | `src/BazaarPlusPlus/GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs:13,20,27,49-87,17-18,100-102` |
| 静态卡数据 | `BppStaticDataAccess.GetCardTemplate(staticData, guid) : ITCard`、`TryGet()`、`LoadCardMap()` | `GameInterop/StaticCards/BppStaticDataAccess.cs:29-35,48,63-64` |
| 全表枚举 prior art | `CollectionCatalog.BeginCardMapLoad`（后台线程 `LoadCardMap`） | `Game/CollectionPanel/Data/CollectionCatalog.cs:59-70` |
| `EHiddenTag` 可直接引用 | `CollectionCardClassifier` 已用 `EHiddenTag.*` | `Game/CollectionPanel/Data/CollectionCardClassifier.cs:27-` |
| 商人名册（**仅供参考，不被 GameInterop resolver 依赖**） | `collection-sources.json` 47 个 Merchant（`Name+PortraitTemplateId`），`CollectionSourceCatalog` 是 **`Game` 层**（`namespace …Game.CollectionPanel.Sources`），故 resolver 不得引用它（见 §4 边界修订） | `Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:11-13,30,51`；schema 见 [`2026-06-02-collection-panel-source-catalog-schema.md`](2026-06-02-collection-panel-source-catalog-schema.md) |
| 世界空间挂载技术 | qinglong `TierBadgeRenderer`：`SetParent(card.transform,false)` + `MeshRenderer` + `PromoteRenderer`（`sortingOrder`/`renderQueue=5000`/`_ZTest=8`/`_ZWrite=0`）+ `RemoveBadge`（`Find+Destroy`） | `decompiled/bazaar-qinglong/BazaarRecommend/TierBadgeRenderer.cs:20-28,134-157,232-256` |
| live 卡挂点 | `ItemController.ShowCard(bool)`(`815`)、`Setup(Card)`(`852`)、`Cleanup()`(`466`，池化 `810-812`)；`CardController:MonoBehaviour`(`32`)、`boardSection`(`77`)、`IsPlayerBoard`(`174-181`) | `decompiled/TheBazaarRuntime/ItemController.cs`、`CardController.cs` |

> **与 qinglong `MerchantQuickReference` 的关系**：该子系统是 merchant→多卡谓词、且**完全不加载商人头像**（标题仅文字 `"商人速查 - "+MerchantName`，`MerchantQuickReferenceOverlay.cs:1749`），对本方案的映射/出图**不可复用**；可复用的只有 `TierBadgeRenderer` 的世界空间挂载技术。

---

## 4 架构与边界

- **in-run 卡是世界空间 `MonoBehaviour`**（`CardController.cs:32`，无 `Canvas`/`RectTransform`）。故 portrait 必须是**世界空间对象**（`SpriteRenderer`/quad），**不能**挂 UGUI `Image` 子节点。这是本方案与“UITK chip 贴 `backgroundImage`”路径的根本区别。
- 分层：
  - `GameInterop/PackageMerchant/` — `PackageMerchantResolver`（读 `ITCard` 能力图 → 商人 GUID，游戏类型适配）。**依赖方向严格 `GameInterop → 游戏 DLL`，不引用任何 `BazaarPlusPlus.Game.*`**（含 `CollectionSourceCatalog`）。名字兜底所需的 merchant 索引由 resolver 自己用 `BppStaticDataAccess.LoadCardMap()` 枚举 `ECardTag.Merchant` 模板构建（仍在 `GameInterop`）。
  - `Game/PackageMerchantPortrait/` — 渲染器 + 特性装配（产品策略 + Unity 渲染）。如需复用 `collection-sources.json`，在此 `Game` 层做（可作为 delegate 注入 resolver），**不要反向让 `GameInterop` 依赖 `Game`**。
  - `Patches/PackageMerchantPortrait/` — `ItemController.ShowCard` + `ItemController.Cleanup` postfix（Harmony 钩子）。
  - 复用 `GameInterop/EncounterPortraits/`（出图）、`GameInterop/StaticCards/`（取模板）。
- **架构测试**：新增/扩展 Architecture.Tests 断言 `GameInterop.PackageMerchant` 命名空间**不引用** `BazaarPlusPlus.Game.*`（编译器不强制此边界，须显式测试守住，见 repo 规则）。

---

## 5 详细设计

### 5.1 `PackageMerchantResolver`（`GameInterop/PackageMerchant/`）

输入 `ITCard template`，输出 `Guid?`（商人 encounter templateId）：

1. `if (!template.HiddenTags.Contains(EHiddenTag.Package)) return null;`
2. 遍历 `template.Abilities.Values`；对每个 `ability.Prerequisites`（可空）里的项：
   - `prereq is TPrerequisiteRun run && run.Conditions is TRunConditionalCurrentEncounter enc && enc.Conditions is TCardConditionalId id && !id.IsNot` → 候选 `id.Id`。
3. 用 `BppStaticDataAccess.GetCardTemplate(staticData, candidate)` 校验候选解析到 `Tags.Contains(ECardTag.Merchant)` 的 encounter；命中即返回。
4. **enchantment 兜底**：基础 `Abilities` 取不到时，扫描 enchantment 能力（活数据中奖励能力在 `Enchantments.<x>.Abilities` 也有同一 GUID，见 §9.2）。
5. **名字兜底（仅当能力图异常，且零 `Game` 依赖）**：`InternalName` 去尾缀 `"'s Package"`/`"' Package"` → 在 resolver **自建的 merchant 索引**里按 `Name` 命中 → 取该 encounter templateId。该索引由 `BppStaticDataAccess.LoadCardMap()` 枚举 `Tags.Contains(ECardTag.Merchant)` 的模板、按 `InternalName` 建（懒加载一次、缓存），**不引用 `CollectionSourceCatalog`**（边界修订，见 §4）。
6. 结果按 `template.Id` 缓存（解析纯静态）：`ConcurrentDictionary<Guid, Guid?>`。

> 注：A 主路径（嵌入 GUID）120/120 可靠且自给自足，名字兜底只是“能力图结构异常”时的安全网；若实现时确认不需要，可整段省略以进一步收敛依赖。

### 5.2 `PackageMerchantPortraitRenderer`（`Game/PackageMerchantPortrait/`）

- `Attach(CardController card, Sprite sprite, PortraitToken token)`：`new GameObject("BppPackageMerchantPortrait")` → `transform.SetParent(card.transform, false)` → `AddComponent<SpriteRenderer>().sprite = sprite`；按卡 `BoxCollider`/renderer bounds 取一角定位（参 `TierBadgeRenderer.cs:103-132`），仿 `PromoteRenderer` 置顶。把 `token` 存到子物体（一个轻量 `MonoBehaviour` 或字典）以便复用校验。
- `Remove(Transform card)`：`card.Find("BppPackageMerchantPortrait")?.gameObject` → `Destroy`（参 `TierBadgeRenderer.cs:20-28`）。
- `PortraitToken` = `(string InstanceId, Guid TemplateId, BazaarBoard.ESections BoardSection)`，取自挂载时的 `card.CardData.InstanceId` / `.TemplateId` / `card.boardSection`。

### 5.3 `BackpackMerchantPortraitPatch`（`Patches/PackageMerchantPortrait/`）

两个 postfix，均经 `BppPatchHost` 取特性服务：

1. `[HarmonyPatch(typeof(ItemController), nameof(ItemController.ShowCard))] [HarmonyPostfix]`
   - `show==false` → `Remove`，返回。
   - **attach 守卫（关键，修 [high]）**：仅当
     `show == true && __instance.gameObject.activeInHierarchy && __instance.IsPlayerBoard && __instance.CardData != null` 才继续。
     `activeInHierarchy` 守卫专门挡掉 `CardDeathComplete()` 的 `PoolObject()(SetActive(false)) → ShowCard(true)` 序列（`ItemController.cs:808-813`）——死/入池卡此时已 inactive，不会重挂。
   - 继续：`Remove` 后 → `resolver(CardData.Template ?? GetCardTemplate(CardData.TemplateId))` → 命中商人 GUID 则构造 `token`，`TryGetCached` 快路同步 `Attach`；否则 `LoadPortraitAsync` 异步，**回调里再校验**：`card != null && card.gameObject.activeInHierarchy && card.CardData?.InstanceId == token.InstanceId && card.CardData.TemplateId == token.TemplateId && card.boardSection == token.BoardSection`，且当前子物体不存在/或其 token 等于本 token，方可 `Attach`；否则丢弃 sprite。
2. `[HarmonyPatch(typeof(ItemController), nameof(ItemController.Cleanup))] [HarmonyPostfix]`（修 [high]）
   - 无条件 `Remove(__instance.transform)`。`Cleanup()` 在 `CardDeathComplete` 里先于 `PoolObject()` 调用（`ItemController.cs:808-810`），所以入池前头像必被清掉，杜绝陈旧子物体随对象进池再被复用。

> 备选/加固：也可在 `CardController.UpdateData` postfix 里，当 `CardData.TemplateId` 变化时 `Remove`（卡被复用为另一张卡的场景）。`Cleanup` 删除 + active 守卫 + token 三者已足够；`UpdateData` 删除作为 belt-and-suspenders，可按游戏内观察决定是否加。

### 5.4 生命周期 / 缓存 / fallback

- **生命周期（修订后）**：
  - attach 只发生在 **active、非池化、`IsPlayerBoard`** 的 live 卡上（`ShowCard` 守卫）。
  - remove 发生在 `show==false`、以及 `ItemController.Cleanup`（死亡/入池前）。
  - 池化复用：入池前 `Cleanup` 已删子物体；复用时新一轮 `Setup`/`ShowCard` 重新解析重挂；token 防止上一轮的异步出图回调贴到已复用的卡。
  - 幂等：attach 前先 `Remove` 同名子物体，避免重复。
- **缓存**：① resolver `templateId→GUID?` 永久缓存（`ConcurrentDictionary`）；② merchant-name 索引懒建一次缓存；③ sprite 复用 `EncounterPortraitSpriteProvider` 内部缓存。
- **fallback**：非 Package / 无 GUID / sprite==null（含动画头像）→ 静默；resolver 异常 → 记 Debug 日志并跳过。

---

## 6 方案对比与推荐

| 路线 | 映射 | 出图 | 优点 | 缺点 |
|---|---|---|---|---|
| **A（推荐）** | 嵌入 GUID（类型导航） | 嵌入 GUID 直喂 provider | 1:1 精确、贴语义、零名字解析、零外部数据；qinglong 已验证挂载稳 | 依赖能力图类型导航（需进游戏验一次实际填充） |
| B | 名字名册去尾缀 | catalog `PortraitTemplateId` | 仅依赖 `EHiddenTag.Package`+InternalName+既有 catalog，最稳态 | 占有格 `'s`/`'` 解析需小心；依赖 `collection-sources.json` 跟版 |
| C | — | 屏幕空间 overlay canvas 跟踪世界坐标 | UGUI 全控、易描边 | 逐帧 world→screen、遮挡/缩放重，重造 qinglong 一招解决之事 |

**推荐 A 为主、B 为兜底**：二者落到同一张头像（§1.3 ArtKey 相同），A 更精确且零外部依赖，B 仅在能力图异常时兜底。UI 一律走世界空间 `SpriteRenderer`（淘汰 C）。

---

## 7 实施步骤（按文件/模块）

1. `GameInterop/PackageMerchant/PackageMerchantResolver.cs` — §5.1，含 GUID 解析、自建 merchant-name 索引兜底、缓存。**零 `Game` 依赖**。
2. `Game/PackageMerchantPortrait/PackageMerchantPortraitRenderer.cs` + `PortraitToken` — §5.2。
3. `Patches/PackageMerchantPortrait/BackpackMerchantPortraitPatch.cs` — §5.3，**两个 postfix**：`ShowCard`（带 active/IsPlayerBoard 守卫 + token）与 `Cleanup`（无条件 remove）。
4. `Game/PackageMerchantPortrait/PackageMerchantPortraitFeature.cs`（`IBppFeature`）+ 在 `BppComposition.cs` 注册；如需开关加 `ISettingsDockEntry`。
5. `tests/PackageMerchantPortrait.Tests/`（xunit）— 纯逻辑覆盖 resolver：Package 命中 / 非 Package / 多 tier 同商人 / 取 GUID / `IsNot=true` 不取 / 名字兜底 / enchantment 兜底；并对 token 比较逻辑（同/异 InstanceId·TemplateId·boardSection）做单测。
6. `tests/Architecture.Tests/`（扩展）— 断言 `GameInterop.PackageMerchant` 不引用 `BazaarPlusPlus.Game.*`（守住 §4 边界）。

---

## 8 验证方案

- **Build**：`./run.sh build`（Debug 自动拷 `BepInEx/plugins/`）；worktree 下加 `-p:BPPInstallerSourcePath=<abs>`。
- **Test**：`dotnet test tests/PackageMerchantPortrait.Tests/…csproj` 或 `./run.sh test`（注意 grep `Failed test projects:`，勿只看尾部）。
- **进游戏**（经 Steam：`open "steam://run/1617400"`）：
  - run 内背包/Stash 持有包裹卡（如 `Tinker's Package`）→ 卡面角出现 Tinker 头像。
  - 进/出商人、滚动、拖拽 Stash↔手牌 → 不重复 / 不串卡 / `show=false` 消失。
  - **池化/销毁/复用专项（修 [high] 的回归点）**：把包裹卡**卖掉/销毁**（走 `CardDeathComplete→Cleanup→PoolObject→ShowCard(true)`）→ 确认**不会**在死卡上残留头像；随后该 `ItemController` 被**复用为另一张卡**（尤其换成另一个商人的包裹或非包裹卡）→ 确认无陈旧头像、无串卡。
  - 对手卡（Opponent）不显示；多商人各自正确；动画头像商人静默兜底。
- **日志**：`<GameDir>/BepInEx/LogOutput.log`，过滤 `[BPP][PackageMerchantPortrait]`（Debug 行仅 Debug build）。
- **落地前必做（探针）**：在 `ShowCard` 主路径加一段**临时 Debug 日志** dump 一张真实包裹卡的能力图与解析出的 GUID，进游戏确认 §1.2 路径与活数据实际填充一致，验证后移除（符合 repo「探针放主路径，不另建脚手架」规则）。

---

## 9 风险与开放问题

0. **[已在 v2 修订]** 池化重挂（Codex [high]）：经 active 守卫 + `Cleanup` patch + token 闭合；保留为头号回归点（见 §8 池化专项）。
1. **[已在 v2 修订]** 边界穿透（Codex [medium]）：resolver 零 `Game` 依赖，名字兜底自建索引；用架构测试守住。
2. **类型导航需活 DLL 验一次**：类型在 `decompiled/` 存在，但卡数据更新（2889>2709）；实现后必须进游戏确认 `Abilities/Prerequisites` 实际填充（探针见 §8）。
3. **奖励能力位置**：若某包裹卡奖励能力只在 enchantment 分支或 tier 专属、根 `Abilities` 取不到 → 触发 §5.1 step 4/5 兜底；需覆盖 `Enchantments.<x>.Abilities` 扫描。
4. **占有格解析（名字兜底）**：`Kev's Armory's` / `Tok's Clocks'` / `Chronos'` 用“遍历名册前缀匹配”，勿手写裁剪。
5. **世界空间 sorting/缩放**：Stash 网格 vs 手牌 vs 拖拽动画下的置顶/缩放需实测，复用 `PromoteRenderer` 手法。
6. **性能**：`ShowCard` 高频；解析与 sprite 必须命中缓存；异步贴做 token 校验。
7. **与 qinglong 共存 / 视觉拥挤**：子物体命名唯一、考虑放不同角，避免与第三方 tier 徽标重叠。
8. **范围配置**：默认 `IsPlayerBoard`（手牌+Stash，排除对手）；若只想 Stash，加 `boardSection==Storage`。
9. **数据跟版**：包裹/商人随版本增删；A 自适应活数据，名字兜底自建索引同样自适应活数据。
10. **是否还有不经 `Cleanup` 的入池/销毁路径**？已确认 `CardDeathComplete` 走 `Cleanup`；实现时 grep 其它 `PoolObject(`/`gameObject` 失活路径确认无遗漏（否则 active 守卫仍能兜底，但应记录）。

---

## 10 附录

### 10.1 活数据 40 个包裹商人（证据 B，按 InternalName）

Aero、Aila、Aimbot、Ande、Barkun、Chronos、Cobweb、Colt、Curio、Eli、Flex、Freiya、Gaseo、Gastro、Goldie、Hef、Herma、Jay Jay、Kev's Armory、Kina、Knightshade、Luxe、Midsworth、Mittel、Mr. Morland、Nautica、Orion、Pinfeather、Pol、Prospero、Quixel、Serafina、Shelter Shelby、Silvia、Tatiana、The Antiquarian、The Tester、Tinker、Tok's Clocks、Valpak。

> 全部 40 个名字均被 `collection-sources.json`（47 Merchant）按 `Name` 覆盖；A 方案不依赖此表，B 兜底依赖。

### 10.2 相关文档

- [`2026-06-02-merchant-trainer-portrait-plan.md`](2026-06-02-merchant-trainer-portrait-plan.md) — 商人/训练师头像（经 `CollectionSources/` 落地）。
- [`2026-06-02-collection-panel-source-catalog-schema.md`](2026-06-02-collection-panel-source-catalog-schema.md) — `collection-sources.json` v3 schema。
- [`2026-06-01-collection-panel-hero-portrait-chips-plan.md`](2026-06-01-collection-panel-hero-portrait-chips-plan.md) — 头像经共享 provider 的 UITK 用法（**注意：那是 UITK，本方案是世界空间**）。
- [`../CONTEXT.md`](../../../CONTEXT.md) — Encounter / Pedestal 术语。
