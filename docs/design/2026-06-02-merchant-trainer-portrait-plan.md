# 商人（Merchant）与训练师（Trainer）头像渲染开发方案

> 范围：在 mod 中新增商人/训练师等 **店主型 / 技能教学型 encounter** 的头像（avatar）渲染能力，复刻现有英雄（Hero）头像系统的「静态 Sprite → UI Toolkit 背景图」通路（Path A）。
> 本方案所有运行时 API 均给出 `decompiled` 路径行号锚点；凡依赖 shipped 卡数据形态、无法从反编译源证明之处，标注 **【需运行时验证】**。
>
> 本文是「反编译源码分析（workflow）」+「bazaardb.gg 实地核对（Chrome）」两路证据合并后的定稿。前身见 [`2026-06-01-collection-panel-hero-portrait-chips-plan.md`](2026-06-01-collection-panel-hero-portrait-chips-plan.md)。

---

## 实现进展（已落地，2026-06-03 校订）— CollectionSources 子系统 + EncounterPortraitSpriteProvider

> **⚠️ 与下文 §3/§4/§5 草案的偏差（重要）**：本特性**已实现并通过 Debug 构建**，但**最终架构与本方案 §3.2 提出的 `Game/CollectionPanel/Encounters/` 设计不同**。实现收敛到一个**数据驱动的 `CollectionSources` 子系统**（嵌入 JSON 目录 + Catalog 加载器），而非 §3.2 设想的"扩展 `CollectionCatalog` 保留 `EncounterCards` + 运行时谓词分类"。下文 §3.2/§4/§5 的具体类名/路径多已过时，仅其**头像加载通路（§2/§3.1 的 `ArtKey → EncounterAssetDataSO → IPortraitAssetData`）与英雄关联语义**仍成立。**以下"实际落地"清单为准。**

### 原计划的 5 个文件**均未创建**（草案作废）
本节早期草案曾声称已新增以下 5 个产物，经核对**全部不存在**，请勿据此理解代码：
- ~~`tools/encounter-portraits/build_catalog.py`~~ —— 不存在（仓库**无 `tools/` 目录**）。
- ~~`Data/Encounters/merchant-trainer-portraits.json`~~ —— 不存在（实际数据在 `Data/CollectionSources/collection-sources.json`）。
- ~~`Game/CollectionPanel/Encounters/MerchantTrainerCatalog.cs`~~ —— 不存在（**无 `Game/CollectionPanel/Encounters/` 目录**）。
- ~~`Game/CollectionPanel/Encounters/MerchantTrainerEntry.cs`~~ / ~~`EncounterPortraitKind.cs`~~ —— 不存在。

### 实际落地的架构
采用 **嵌入 JSON 目录（策展 roster）→ Catalog 加载/校验 → 运行时取 Sprite** 通路：

- `Data/CollectionSources/collection-sources.json` —— 签入的策展目录（**嵌入资源**，`BazaarPlusPlus.csproj:31` `<EmbeddedResource Include="Data\CollectionSources\collection-sources.json" />`）。每条 entry 含 `kind`/`name`/`group`/`availableHeroes`/`description`/`portraitTemplateId`/`sourceTemplateIds`/`offerRule`（DTO 见 `Game/CollectionPanel/Sources/CollectionSourceDtos.cs:19-82`）。
- `Game/CollectionPanel/Sources/CollectionSourceEnums.cs:5-9` —— `CollectionSourceKind { Merchant, Trainer }`（**即原 §3.2 设想的 `EncounterPortraitKind`，但实际命名为 `CollectionSourceKind`**），另含 `CollectionSourceHeroMode`/`CollectionSourceStartingTierMode`/`CollectionSourceOfferPoolStatus`。
- `Game/CollectionPanel/Sources/CollectionSourceEntry.cs` —— 运行时条目模型：`PortraitTemplateId : Guid`（`:17`/`:48` 喂给 Provider 出图）、`SourceTemplateIds`、`AvailableHeroes`，英雄关联即 `AppliesToHero(EHero)`（`:60-61`，**空集 = 全英雄**，复刻 Common-或-空集语义）。
- `Game/CollectionPanel/Sources/CollectionSourceCatalog.cs` —— 加载器：从嵌入资源读 `collection-sources.json`（`:17` `ResourceSuffix`），按 `CollectionSourceKind` 解析（`:128`），校验 `portraitTemplateId`/`sourceTemplateIds`（`:142-159`）去重；由 `Game/CollectionPanel/CollectionPanel.cs` 消费。配套 `CollectionSourceRoster.cs` / `CollectionSourceOfferRule.cs` / `CollectionSourceOfferPoolResolver.cs` / `CollectionSourceOfferPoolResult.cs`。
- `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs` —— **已实现**：按 `Guid`（`sourceTemplateId`）经 `BppStaticDataAccess.GetCardTemplate → template.ArtKey → AssetLoader.LoadAssetAsyncByAddress<EncounterAssetDataSO> → LoadPortraitSpriteAsync` 取 `Sprite`，含 `CachedSprites`/`InFlightLoads` 去重（`:17-18`）与 null/text-fallback 分支（`:51-63`、`:78-85`）。由 `Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs` 消费。**注意：实际用直调泛型方法，非 §3.1 草案的反射形（见 §3.1 校订）。**

**与草案的关键差异**：实现**没有**扩展 `CollectionCatalog` 增设 `EncounterCards`，也**没有** `EncounterPortraitSource.Matches` 运行时谓词分类——"哪些 template ID 是商人/训练师 + 元数据 + 英雄关联"的唯一权威来源是**签入的 `collection-sources.json`** 经 `CollectionSourceCatalog` 加载，英雄关联即 `CollectionSourceEntry.AppliesToHero`。商人/训练师的 NPC↔英雄关系由策展目录直接给出，不再在运行时从 `card.Merchants` 派生。

---

## 0.0 bazaardb 实地核对（先读：它推翻了"训练师无头像"的初判）

通过 Chrome 实地核对 [bazaardb.gg](https://bazaardb.gg)（基于游戏 patch 14.1 的卡数据库），确立了 Merchant/Trainer 的**分类法真值**，与反编译枚举完全吻合：

| bazaardb 分类 | 数量 | `ECardType` | `ECardTag` | 卡池 | tooltip | 是否有头像 |
|---|---|---|---|---|---|---|
| **Merchants 商人** | 47 | `EventEncounter` | **含 `Merchant`** | Item Pool | "Sells X" | ✅（详情页 `image`） |
| **Trainers 训练师** | 23 | `EventEncounter` | **不含 `Merchant`** | Skill Pool | "Teaches X skills" | ✅（详情页 `image "Bjorn"`） |

实例佐证：
- `Aimbot`（商人）：`TYPES: Merchant` + 标签 `EventEncounter, Vanessa, Dooley, Mak, Jules, Karnok`，"Sells Crit items"，101 物品池，**有头像**。
- `Bjorn`（训练师）：标签 `EventEncounter`（**无 Merchant**）+ `Dooley, Vanessa, Mak, Jules`，"Teaches Freeze skills"，技能池，**有头像**（`ref image "Bjorn"`）。

**核对结论（对 workflow 初判的修正）：**
> workflow 反编译分析得出"训练师不是运行时类型、无头像、第一期不做"，这是把**训练师 NPC**（`EventEncounter`，有头像）与它**提供的技能奖励瓦片**（`EncounterStep`，纹理图，无人物头像）**混为一谈**了。
> 实地数据证明：**训练师 NPC 与商人一样是 `EventEncounter`，走同一条 `EncounterAssetDataSO → IPortraitAssetData.LoadPortraitSpriteAsync` 头像通路，二者都有可渲染头像**。唯一区别是 `Merchant` 标签的有无（卖物品 vs 教技能）。
> 因此本定稿把**训练师纳入第一期**，作为与商人**同通路、不同筛选谓词**的第二种 encounter 类型——这恰好在**正确的粒度**上满足了用户"每类一个独立类"的意图（2 个游戏意义上的真实类型，而非 15 个 mod 本地桶）。

反编译侧确证（消除歧义）：`ECardType` 无 `Merchant`/`Trainer` 成员，编码为 `EventEncounter`（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Core.Types/ECardType.cs`）；`Merchant` 是 `ECardTag` 成员（`ECardTag.cs:16`），且**无** `Trainer` 标签。故"训练师"= `EventEncounter` 且**未**打 `Merchant` 标签的技能教学 encounter。

**头像数据来源裁决**：bazaardb 的图取自游戏素材，仅作**数据模型/分类法**参考；**mod 在运行时直接取游戏内 `Sprite`**（路线 A，见 §2），不依赖 bazaardb 图片/无公开 API。

---

## 0. 关键结论与对设计意图的修正（先读）

用户的设计意图是：**每种类型一个独立类，各自提供筛选逻辑，且筛选与英雄系统关联**。基于代码事实 + bazaardb 核对，做三处修正，请在动工前确认：

1. **「按 `EHero` 枚举直接取资产」的英雄模式无法照搬。** 英雄之所以简单，是因为 `CollectionManager.GetDefaultHeroSkin(EHero)` 是枚举→资产的直接访问器（`decompiled/TheBazaarRuntime/TheBazaar/CollectionManager.cs:1111`）。商人/训练师**不存在**任何 `GetMerchant`/`GetTrainer` 访问器，也没有可枚举注册表——`MerchantSO`/`MerchantAssetDataSO` 在反编译树中**零运行时消费者**（仅自身定义 + `AotStubs.cs`；对比 `EncounterAssetDataSO` 有 10 个活消费者）。唯一可行通路是：**卡牌模板 `ITCard.ArtKey`（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards/ITCard.cs:30`）→ `AssetLoader.LoadAssetAsyncByAddress<EncounterAssetDataSO>(ArtKey)` → `IPortraitAssetData.LoadPortraitSpriteAsync()`**（与英雄共用最后一段 `IPortraitAssetData` 接缝）。因此 Provider 缓存键是 **`Guid`（卡牌模板 Id）**，而非枚举。

2. **训练师纳入第一期（修正 workflow 的"跳过训练师"）。** 依据 §0.0：训练师 NPC 是 `EventEncounter`，与商人**同头像通路**。第一期渲染**两种** encounter 头像：
   - **商人**：`EventEncounter` + `ECardTag.Merchant`（卖物品）。
   - **训练师**：`EventEncounter` 且**无** `ECardTag.Merchant`、提供技能（教技能）。
   - **【需运行时验证】** 区分二者的客户端判定（见 §4）需在游戏内确认 mod 现有分类器对训练师的归属（`CollectionMerchantKind` 可能已覆盖按技能类型分桶的训练师）。
   - **不渲染**技能奖励瓦片（`ECardType.EncounterStep`，纹理 ArtKey 如 `Reward_Mind_D.png`，走 `RewardController.Setup` 的 `AssetLoader.LoadAssetAsyncByAddress<Texture2D>(artKey)`，`decompiled/.../RewardController.cs:138-154`，**不经 `IPortraitAssetData`**，无人物头像）——这是训练师**池内的技能**，不是 NPC 本身。

3. **「每种 merchant 类型一个类」会造成过度设计，按正确粒度收敛。**
   - **(a) 不为 15 个 `CollectionMerchantKind`（General/Burn/Poison/Freeze/…，`Game/CollectionPanel/Data/CollectionMerchantKind.cs:8-20`，已确认 15 个枚举成员）各建一个类。** 这 15 个桶是按卡牌 `EHiddenTag`/`ECardTag` 派生的 mod 本地分类（`CollectionCardClassifier.ResolveMerchants`，`CollectionCardClassifier.cs:61-103`），与任何 `MerchantSO` 资产/头像**无关联**。
   - **(b) 「类型」的正确粒度是 2 个真实游戏类型（Merchant / Trainer），不是 15 个本地桶，也不是 1 个。** 实现为**一个按 `EncounterPortraitKind { Merchant, Trainer }` 参数化的来源类**（外加可选的 `CollectionMerchantKind?` 子筛选）：1 类 + 有意义的 2 值判别 + 可选子参数。这与评审认可的"1 类 + 参数覆盖 15 桶"模式一致，只是把判别轴扩展到 Merchant/Trainer。
   - **(c) 第一期不引入 `IMerchantPortraitSource` 接口、`MerchantPortraitCatalog` 聚合器。** 既然用参数化单类即可表达 2 种类型，接口/聚合器属投机性泛化。**接口仅在出现机制不同的第三类来源（如未来要渲染 `EncounterStep` 纹理瓦片）时再提取。**

---

## 1. 现有头像获取逻辑的代码位置分析

英雄头像的完整链路（**Path A**，本方案要复刻的模板）：

**① 适配器接缝（GameInterop）** — `GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs`（`internal static`）：
- 缓存字段：`CachedSprites : Dictionary<EHero, Sprite?>`（`:18`，含负缓存）、`InFlightLoads : Dictionary<EHero, Task<Sprite?>>`（`:19`，并发去重）。
- `IsRenderableHero(EHero)`（`:21-22`）：排除 `EHero.Common`、`EHero.Hero8`。
- `TryGetCached(EHero, out Sprite?)`（`:24-28`）：同步缓存命中。
- `LoadDefaultPortraitAsync(EHero)`（`:30-44`）：不可渲染→`Task.FromResult(null)`；缓存命中→直接返回；in-flight 命中→返回既有 task；否则起 `LoadAndMaybeCacheAsync` 并登记。
- `LoadAndMaybeCacheAsync`（`:46-96`）：`Services.TryGet<CollectionManager>`（`:53`）→ `GetDefaultHeroSkin(hero)`（`:63`）→ `await skin.LoadPortraitSpriteAsync()`（`:74`）；`finally` 移除 in-flight 并写缓存（`:90-95`）。空 sprite 记 `Debug`（`:76`），异常记 `Warn`（`:84`）。

**② 游戏侧真值（只读反编译）：**
- `CollectionManager.GetDefaultHeroSkin(EHero)` → `CollectionManager.cs:1111-1122`（线性扫描 `defaultHeroSkins[]`）。
- `SkinAssetDataSO.LoadPortraitSpriteAsync()` → `SkinAssetDataSO.cs:352-383`；其 `portraitTextureReference` 是 **`AssetReferenceSprite`**（`SkinAssetDataSO.cs:65`），需异步 `AssetLoader.LoadAssetAsyncByReference<Sprite>`（`SkinAssetDataSO.cs:372`）。
- 统一接口 `IPortraitAssetData.LoadPortraitSpriteAsync(CancellationToken)` → `IPortraitAssetData.cs:28`，由 `SkinAssetDataSO`（英雄）与 `EncounterAssetDataSO`（`EncounterAssetDataSO.cs:11,105-116`）共同实现；`MerchantAssetDataSO : EncounterAssetDataSO`（`MerchantAssetDataSO.cs:5`）。

**③ 唯一消费者：CollectionPanel 英雄筛选 chip**（`Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs`，`:185-295`）：
- chip 构建：`CreateHeroChipButton`（`:191-214`，尺寸 `Sizes.HeroChipButtonSize`=56f）→ `CreateHeroChipIcon`（`:216-231`）。icon 关键样式：`backgroundSize=Cover`（`:222`）、`backgroundRepeat=NoRepeat`（`:223`）、圆形 `UiStyle.Radius(..., HeroChipIconSize/2f)`（`:225`，`Sizes.HeroChipIconSize`=48f，`Sizes.cs:21`）。不可渲染英雄画 5 点占位 `AddCommonHeroGlyph`（`:227-228,233-258`）。
- **tracked element map + 异步竞态防护（必须照搬的并发模型）：**
  - `_heroChipIcons[hero] = icon`（`:210`）——把每个 chip icon 登记进字典，使后续刷新能拿到**稳定的** VisualElement 句柄来重绑。
  - `LoadHeroChipIcon`（`:260-272`）：先设 `icon.userData = hero` 作 token（`:262`），同步缓存命中即 `ApplyHeroChipIcon`，否则先占位再 fire-and-forget。
  - `ApplyHeroChipIconWhenLoadedAsync`（`:274-283`）：`await` 后 `if (!Equals(icon.userData, hero)) return;`（`:280-281`）防止把迟到 sprite 贴到已被复用的 element 上。
- **Sprite→屏幕绑定核心 4 行**（`ApplyHeroChipIcon`，`:285-295`）：
  ```csharp
  if (sprite == null) { icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null); return; }
  icon.style.backgroundImage = new StyleBackground(sprite);
  icon.MarkDirtyRepaint();
  ```
- 英雄集合来源：`CollectionPanel.HeroOrder[]`（`CollectionPanel.cs:30`，固定 8 英雄），喂入 `AvailableHeroes`（`CollectionPanel.cs:597`）。

**④ 挂载方式**：`CollectionPanelMount : IBppMountable`（`Game/CollectionPanel/CollectionPanelMount.cs`），在 `BppComposition.cs:98` 注册。

**对照（非模板）**：`Game/CombatReplay/PlaybackUi/OpponentPortraitController.cs` 是 **Path B**——在 3D 棋盘 spawn 活体 `EncounterController`（反射 `BoardBuilder.LoadHeroPortraitAsync`），**不是** UI avatar，不复刻。

---

## 2. 商人 / 训练师头像获取的可行性分析

### 三条路线对比与裁决

| 路线 | 产物 | 能否喂给 mod 游戏内 UI | 裁决 |
|---|---|---|---|
| **A. 运行时**（mirror 英雄） | `UnityEngine.Sprite` | 能（进程内直读） | **采用** |
| B. extractor 离线导出 | PNG 文件 | 否（git-ignored 本地产物，不入 `BepInEx/plugins` 拷贝流，受打包规则限制） | 拒绝（仅当站点/安装器也需要时另议） |
| C. bazaardb.gg 外部站 | 远程图片 | 否（无公开 API、第三方 ToS 风险；codebase 内 "bazaardb" 是**出站**截图管线，无 portrait 拉取） | 拒绝（仅作数据模型参考，见 §0.0） |

**裁决：采用运行时路线 A。** 唯一既复刻英雄接缝、又零新资产/零托管/零打包改动、且随游戏版本自动更新的方案。

### 选定的运行时调用链（商人 **与** 训练师，同一条链）

```
ITCard 模板（来自 JsonGameDataManager.GetCardMap / GetCardById）
  → template.ArtKey                                              ITCard.cs:30
  → AssetLoader.LoadAssetAsyncByAddress<EncounterAssetDataSO>(ArtKey)
        （镜像 EncounterController.LoadEncounterData，EncounterController.cs:302-306；
          佐证 EncounterClickController.cs:92-96）
  → ((IPortraitAssetData)so).LoadPortraitSpriteAsync(ct)         EncounterAssetDataSO.cs:105
  → UnityEngine.Sprite
```

**机制已逐行验证；唯一未证之处是 shipped 数据形态。** `EncounterController.cs:302-306` 与 `EncounterClickController.cs:92-96` 都调 `Services.Get<AssetLoader>().LoadAssetAsyncByAddress<EncounterAssetDataSO>(template.ArtKey)`；`AssetLoader.cs` 的 `LoadInternalAsync`（`:710-756`）按 `obj is T` 做类型检查，故 `MerchantAssetDataSO`（子类，`MerchantAssetDataSO.cs:5`）实例也满足 `T=EncounterAssetDataSO`。**训练师同为 `EventEncounter`，走同一调用链**（§0.0 已确立）。

> **【需运行时验证】（动工前必做）** 反编译只能证明**机制**，无法证明**某张带/不带 `ECardTag.Merchant` 的 `EventEncounter` 卡模板的 `ArtKey` 确实解析为 Encounter SO 且出非 null portrait**。`EncounterClickController` 仅对 `EventEncounterCard` 证明（`:87` 过滤该类型）。**取若干样本各跑一次**：
> - 一张 `ECardTag.Merchant` 商人卡（如 Aimbot）；
> - 一张无 `Merchant` 标签的训练师卡（如 Bjorn）；
> 打印其 `ArtKey`，确认 `LoadAssetAsyncByAddress<EncounterAssetDataSO>` 返回非 null SO 且 `LoadPortraitSpriteAsync` 出非 null sprite。若某类卡 ArtKey 指向裸 Sprite/Texture，则该类走占位回退（null 分支已覆盖）。

### `AssetLoader.LoadAssetAsyncByAddress` 签名（已确证，非待验证项）

`AssetLoader.cs:652`：
```csharp
internal async Task<T> LoadAssetAsyncByAddress<T>(string address, bool reportSuccess = false)
```
- **唯一重载，非歧义**；可选参数 `reportSuccess` 默认 `false`。
- §3.1 反射调用 `new object[] { artKey!, false }`（2 实参）与此**精确匹配**。
- 该方法 `internal`，game DLL 已 publicize（`<PublicizeAll>true</PublicizeAll>`）。按 MEMORY「反射优于 Publicizer」约定用反射形；若团队接受直调可塌缩为 `await loader.LoadAssetAsyncByAddress<EncounterAssetDataSO>(artKey)`（纯风格选择）。

### AssetReferenceSprite vs 已加载 Sprite 的关键差异（必须理解）

- **英雄**：`SkinAssetDataSO.portraitTextureReference` 是 **`AssetReferenceSprite`**（`SkinAssetDataSO.cs:65`），`LoadPortraitSpriteAsync` 内部还要再做一次 `AssetLoader.LoadAssetAsyncByReference<Sprite>`（`SkinAssetDataSO.cs:372`）→ **两次** addressable I/O。
- **Encounter（商人/训练师）**：`EncounterAssetDataSO.portraitTextureReference` 是**已序列化的 `Sprite` 字段**（`EncounterAssetDataSO.cs:15`），`LoadPortraitSpriteAsync` **不做第二次异步加载**，直接 `Task.FromResult(portraitTextureReference)`（`EncounterAssetDataSO.cs:115`）。sprite 随 SO 一起进内存——真正 I/O 只有 SO 那一次 `LoadAssetAsyncByAddress`。
- **实现含义**：商人/训练师 Provider 只需 1 次 addressable 加载（SO），sprite 随取随得；但 SO 那次加载经 `LoadInternalAsync`（`AssetLoader.cs:710`）仍是真异步 I/O，故缓存/in-flight 去重骨架照搬英雄仍必要。

### 必须处理的 null 分支

- **动画头像**：若 encounter 配了动画 portrait 预制，`LoadPortraitSpriteAsync` 故意返回 `null`（`EncounterAssetDataSO.cs:111-113`）。按英雄 Provider 的 null 处理（`HeroPortraitSpriteProvider.cs:75-79`）做占位回退。这同时是**头像可渲染性的真正信号**——能否出图由运行时决定，而非 ArtKey 文本预判（见 §4 删除 ArtKey 预检）。
- **加载失败 / ArtKey 非法 / 取消**：`EncounterAssetDataSO.cs:107-110` 取消→null；SO 为 null→null。UI 一律走占位 glyph。

---

## 3. 类结构设计方案

遵循分层规则（`bazaarplusplus-mod/CLAUDE.md`）：**运行时资产加载接缝放 `GameInterop/`，筛选/分类/产品策略放 `Game/`**。

### 3.1 GameInterop 侧：Sprite 加载适配器（不含任何筛选；商人/训练师共用）

> **【已实现，与草案的偏差】** `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs` **已落地**，缓存/in-flight/null-fallback 骨架如本节所述。**唯一偏差**：本节草案下方给出"反射调用 `LoadAssetAsyncByAddress`"的骨架，但**实际代码用直调泛型方法**——`await assetLoader.LoadAssetAsyncByAddress<EncounterAssetDataSO>(template.ArtKey)`（`EncounterPortraitSpriteProvider.cs:75`），随后 `await encounterData.LoadPortraitSpriteAsync()`（`:87`），**不经 `MethodInfo`/`MakeGenericMethod`/`Invoke`**。下方反射骨架仅作历史草案保留，**以实际直调形为准**（game DLL 已 publicize，泛型直调可行；MEMORY「反射优于 Publicizer」此处未采用，属团队接受的风格选择）。

新增 `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs`（命名从 `MerchantPortraits` 泛化为 `EncounterPortraits`，因商人与训练师共用同一加载段），**键为 `Guid`（模板 Id）**：

```csharp
#nullable enable
namespace BazaarPlusPlus.GameInterop.EncounterPortraits;

// 与 HeroPortraitSpriteProvider 对称：缓存 + in-flight 去重；区别仅在键（Guid 而非 EHero）
// 与加载段（ArtKey → EncounterAssetDataSO → IPortraitAssetData，而非 GetDefaultHeroSkin）。
// 商人与训练师都是 EventEncounter，共用本 Provider，无需区分。
internal static class EncounterPortraitSpriteProvider
{
    private const string LogComponent = "EncounterPortrait";
    private static readonly Dictionary<Guid, Sprite?> CachedSprites = new();
    private static readonly Dictionary<Guid, Task<Sprite?>> InFlightLoads = new();

    internal static bool TryGetCached(Guid templateId, out Sprite? sprite);
    internal static Task<Sprite?> LoadPortraitAsync(Guid templateId);   // 镜像 LoadDefaultPortraitAsync
}
```

加载段反射骨架（**历史草案；未采用** —— 实际为 `EncounterPortraitSpriteProvider.cs:75` 的直调 `LoadAssetAsyncByAddress<EncounterAssetDataSO>(template.ArtKey)`，签名已确证 `AssetLoader.cs:652`）：

```csharp
// ⚠️ 草案，未实现：实际代码直调泛型 `assetLoader.LoadAssetAsyncByAddress<EncounterAssetDataSO>(artKey)`。
private static readonly MethodInfo? LoadByAddressOpen = typeof(AssetLoader)
    .GetMethod("LoadAssetAsyncByAddress",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

private static async Task<Sprite?> LoadAndMaybeCacheAsync(Guid templateId)
{
    Sprite? result = null; var shouldCache = false;
    try
    {
        var template = BppStaticDataAccess.GetCardTemplate(BppStaticDataAccess.TryGet(), templateId);
        var artKey = template?.ArtKey;
        // 仅做最廉价的空/Invalid 预筛；不复用 Item/Skill 的 .mat/Placeholder 规则（见 §4）。
        if (string.IsNullOrEmpty(artKey)
            || string.Equals(artKey, "Invalid", StringComparison.OrdinalIgnoreCase))
            return null;                                  // 非法 ArtKey → 占位

        if (!Services.TryGet<AssetLoader>(out var loader) || loader == null || LoadByAddressOpen == null)
            return null;                                  // 运行时未就绪 → 不缓存，下次重试
        shouldCache = true;

        var loadEncounter = LoadByAddressOpen.MakeGenericMethod(typeof(EncounterAssetDataSO));
        var task = (Task)loadEncounter.Invoke(loader, new object[] { artKey!, false })!;  // 2 实参匹配 :652
        await task.ConfigureAwait(false);
        var so = (EncounterAssetDataSO?)task.GetType().GetProperty("Result")!.GetValue(task);
        if (so == null) return null;

        result = await ((IPortraitAssetData)so).LoadPortraitSpriteAsync();  // 动画头像→null，正常回退
        return result;
    }
    catch (Exception ex) { BppLog.Warn(LogComponent, $"load failed id={templateId}: {ex.Message}"); return null; }
    finally { InFlightLoads.Remove(templateId); if (shouldCache) CachedSprites[templateId] = result; }
}
```

### 3.2 Game 侧：单一可参数化来源（满足「每类一个独立类」意图，无接口）

> **【已被 CollectionSources 子系统取代 / 未按本节实现】** 本节整套设计——新增 `Game/CollectionPanel/Encounters/` 目录、`EncounterPortraitSource.Matches` 运行时谓词分类、`EncounterPortraitItem` 条目 struct、以及"扩展 `CollectionCatalog` 保留 `EncounterCards`"——**均未实现**：仓库**无 `Game/CollectionPanel/Encounters/` 目录**，亦无 `EncounterPortraitSource`/`EncounterPortraitItem` 类型。
>
> 实际落地为**数据驱动的 `Game/CollectionPanel/Sources/` 子系统**（见上文「实现进展」）：
> - "哪些 template ID 是商人/训练师"不再由运行时谓词从 `card.Merchants` 分类，而由**签入的 `Data/CollectionSources/collection-sources.json`** 直接给出，经 `CollectionSourceCatalog.cs` 加载/校验。
> - 角色类型枚举实际为 `CollectionSourceKind { Merchant, Trainer }`（`CollectionSourceEnums.cs:5-9`），**非** `EncounterPortraitKind`。
> - 条目模型实际为 `CollectionSourceEntry`（`CollectionSourceEntry.cs`），其 `PortraitTemplateId`（`:17`）喂给 Provider 出图。
> - `CollectionCardClassifier` **未**改动以接纳 encounter 卡：其 `Classify` 仍只接受 `Item`/`Skill`（`CollectionCardClassifier.cs:69` `if (type != ECardType.Item && type != ECardType.Skill) return Rejected(...)`），故 encounter 卡的消费**不**走 `CollectionCatalog`，而走独立的 `CollectionSourceCatalog` 读 JSON。
>
> 下方原 §3.2 草案（`EncounterPortraitSource` / `EncounterPortraitItem` / `CollectionCatalogBuildSession.EncounterCards`）**仅作历史记录保留**，与现有代码不符。

新增 `Game/CollectionPanel/Encounters/`（紧邻现有 `Data/` 分类逻辑）。**第一期不建接口、不建聚合器**——只建一个**按 `EncounterPortraitKind` 参数化**的来源类，直接消费现有 `CollectionCatalog` 缓存。

```csharp
#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Encounters;

internal enum EncounterPortraitKind { Merchant, Trainer }   // 2 个真实游戏类型

// 一种「encounter 头像来源」：定义"哪些卡属于我"的筛选谓词，并产出可渲染条目。
// 复刻英雄系统的"分组+筛选"心智，但来源是已缓存的卡牌目录而非枚举。
// kind 区分商人(卖物品, 带 Merchant 标签) / 训练师(教技能, 不带 Merchant 标签)；
// 可选 merchantKindFilter：用一个类 + 参数覆盖 15 个 CollectionMerchantKind，而非 15 个类。
internal sealed class EncounterPortraitSource
{
    private readonly EncounterPortraitKind _kind;
    private readonly CollectionMerchantKind? _merchantKindFilter;   // 仅 Merchant 下可选细分
    public EncounterPortraitSource(EncounterPortraitKind kind,
                                   CollectionMerchantKind? merchantKindFilter = null)
    { _kind = kind; _merchantKindFilter = merchantKindFilter; }

    // 纯谓词，不依赖任何本地化/UI 类型 → 可在 exe-runner 测试中直接构造、直接断言。
    // 不含 ArtKey 文本预检：可渲染性由 Provider 的 null-sprite 回退在运行时决定（见 §4）。
    public bool Matches(CollectionCardVm card)
    {
        if (!IsEncounterType(card.Type)) return false;             // EventEncounter 等
        return _kind switch
        {
            // 商人：已被 CollectionCardClassifier 派生进 card.Merchants（对应 ECardTag.Merchant 心智）
            EncounterPortraitKind.Merchant =>
                card.Merchants.Count > 0
                && (_merchantKindFilter == null || card.Merchants.Contains(_merchantKindFilter.Value)),
            // 训练师：EventEncounter 且非商人、教技能。判定见 §4.2【需运行时验证】。
            EncounterPortraitKind.Trainer => IsTrainer(card),
            _ => false,
        };
    }

    public bool MatchesHero(CollectionCardVm card, EHero hero) =>
        Matches(card) && HeroAssociation.Includes(card.Heroes, hero);

    private static bool IsEncounterType(ECardType t) =>
        t == ECardType.CombatEncounter || t == ECardType.EventEncounter || t == ECardType.PedestalEncounter;

    // 【需运行时验证】训练师客户端判定：EventEncounter 且非 Merchant、且其奖励池含 Skill。
    // 具体取 card.Merchants 是否已含技能教学桶、或需补一个 IsSkillTrainer 派生，待 §4.2 核对后定稿。
    private static bool IsTrainer(CollectionCardVm card) => /* §4.2 */ false;
}
```

**条目模型**（喂给 UI 与 Provider 的最小数据；不含本地化字段）：

```csharp
internal readonly struct EncounterPortraitItem
{
    public readonly Guid TemplateId;     // → EncounterPortraitSpriteProvider.LoadPortraitAsync
    public readonly string DisplayName;
    public EncounterPortraitItem(Guid id, string name) { TemplateId = id; DisplayName = name; }
}
```

**分组数据来源——复用现有 `CollectionCatalog` 缓存，不再二次扫描。** mod 已有 `CollectionCatalog`（`Game/CollectionPanel/Data/CollectionCatalog.cs:10-140`）缓存 `IReadOnlyList<CollectionCardVm>`（键为静态数据管理器 identity）。**但**其 `IsCatalogCard`（`CollectionCardClassifier.cs:49`）**只接受 `Item`/`Skill`，encounter 卡在入缓存前即被丢弃**——商人/训练师卡不在现有缓存里。

因此**扩展现有缓存接缝**，而非新建并行扫描：
- 在 `CollectionCatalog` 的 build session 里**额外保留 encounter 型 VM**（独立列表 `EncounterCards`，与 `Item`/`Skill` 的 `Cards` 分开），其余筛选 UI 仍只读 `Cards`，行为不变。
- 商人/训练师分组退化为对这份**已缓存** encounter VM 列表的**纯内存 `Where`**：`encounterCards.Where(c => source.MatchesHero(c, hero))`，**零第二次 `GetCardMap()` 遍历、零第二次 `CollectionCardVm.From` 投影**。

> **为何不彻底塞进同一个 `Cards`**：现有过滤/排序 UI 全部下游都假设 `Cards` 仅含 `Item`/`Skill`（`IsCatalogCard` 是这条不变量的守卫）。混入 encounter 会污染既有筛选面板。故用**同一个 build session、一次遍历**产出两份列表（`Cards` 仍 Item/Skill；新增 `EncounterCards` 收 encounter 型），既消除二次扫描，又不破坏不变量。

**演进说明（接口何时才该出现）**：当且仅当出现**机制不同**的第三类来源（如训练师**技能奖励瓦片**的 `EncounterStep → LoadAssetAsyncByAddress<Texture2D>` 纹理通路）需与本 `EncounterPortraitSource` 并存时，再提取接口与轻聚合器。第一期一个参数化具体类足矣。

---

## 4. 不同类型筛选逻辑的实现方式

**只实现一个参数化来源类**（`EncounterPortraitSource`，§3.2），放 `Game/CollectionPanel/Encounters/`。

### 4.1 商人（Merchant kind）

筛选依据：**encounter 卡类型 + 已派生的 `card.Merchants`**（来自 `CollectionCardClassifier.ResolveMerchants`，与游戏 `ECardTag.Merchant` 心智一致）。

**关键修正——谓词中删除 ArtKey 文本预检：**
- 现有 `CollectionCardClassifier.HasValidArtKey`（`CollectionCardClassifier.cs:105-109`）会拒绝 `.mat` 与 `Placeholder`，这些规则是给 **Item/Skill 卡面**写的；商人 encounter 的 `ArtKey` 是解析为 `EncounterAssetDataSO` 的 **GUID**，套用 Item/Skill 排除规则会**误杀**。
- **可渲染性的真正信号**是 `LoadPortraitSpriteAsync` 是否返回非 null。故 `Matches` **不做任何 ArtKey 预检**；非法/空 ArtKey 与不可渲染 portrait 一律由 §3.1 Provider 的 null-sprite 回退 + §7 `ApplyEncounterIcon(icon, null)` 占位处理。
- **不新增** `HasRenderableArtKey` 包装。Provider 内部仅保留「空/`Invalid`」最廉价短路（§3.1）。

**可选 kind 细分**：若产品要按 `CollectionMerchantKind`（Burn/Poison/…）再分组，**不**为每个 kind 建类，而是 `new EncounterPortraitSource(EncounterPortraitKind.Merchant, CollectionMerchantKind.Burn)`——构造参数 + `card.Merchants.Contains(kind)` 一行。15 桶 → 1 类 + 参数。

### 4.2 训练师（Trainer kind）

依据 §0.0：训练师 = `EventEncounter` 且**无** `ECardTag.Merchant`、提供技能。头像通路与商人**完全相同**（§2/§3.1 共用 Provider）。

**【需运行时验证】客户端判定细化（动工前确认其一）：**
1. **优先核对 mod 现有分类是否已覆盖训练师**：`CollectionMerchantKind` 的 Burn/Poison/Freeze 等桶名与"训练师教什么技能"高度对应——`CollectionCardClassifier.ResolveMerchants`（`:61-103`）很可能已把技能教学 encounter 也归进 `card.Merchants`。若如此，训练师无需独立 kind，§4.1 的 Merchant 谓词已涵盖，只是分组标题/英雄关联需区分。
2. **若分类器只认 `ECardTag.Merchant`**（不含训练师），则 `IsTrainer` 派生为：`card.Type == EventEncounter && !card 带 Merchant 标签 && 其奖励池含 ECardType.Skill`。奖励池读取需确认 mod 侧是否已有 encounter→技能池的投影（若无，从 `ITCard` 模板的 encounter step / reward 读取，属新增派生，单列一步）。

> 在运行时核对前，`EncounterPortraitSource.IsTrainer` 暂返回 `false`（§3.2 占位）；核对结果决定它收敛为「复用 `card.Merchants`」还是「新增技能池派生」。**这是训练师落地的唯一未决点，不阻塞商人通路开发。**

### 4.3 组合方式

第一期分组逻辑极简：对**已缓存**的 encounter VM 列表（§3.2）按 kind 各跑一次 `Where(c => source.MatchesHero(c, currentHero))`：
```csharp
var merchants = encounterCards.Where(c => merchantSource.MatchesHero(c, hero)).ToList();
var trainers  = encounterCards.Where(c => trainerSource.MatchesHero(c, hero)).ToList();
```
新增子分组 = 多 `new` 一个带参 `EncounterPortraitSource`，**零新类、零改动**缓存与 UI 骨架。

---

## 5. 与英雄系统的关联处理

### 数据模型真相（grounded + bazaardb 核对一致）

**不存在「英雄 → 商人/训练师列表」的映射。** 关联是**每张 encounter 卡自带一个 `HashSet<EHero> Heroes`**（`TCardBase.Heroes`，`ITCard.cs:24`；mod 已在 `CollectionCardVm.From.cs:19` 复制 `Heroes = template.Heroes`）。bazaardb 实地佐证此为**多对多**：`Aero→{STE}`、`Aimbot→{VAN,DOO,MAK,JUL,KAR}`、`Cymon→{DOO}`、`Bjorn→{VAN,DOO,MAK,JUL}`（缩写 `VAN/PYG/DOO/MAK/STE/JUL/KAR` 对应 `EHero`）。

游戏自身的 hero↔encounter 过滤谓词是 `BazaarCardDealer.FilterDayManager`（**服务端 battle-sim，`BazaarBattleService` 程序集**，`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:4491`，谓词在 `:4494`）：
```csharp
pair.Heroes.Contains(EBazaarHero.Common) || pair.Heroes.Contains(battlePlayer.Hero)
```

**两点精度说明（为何不能直接复用代码）：**
- 该谓词用**服务端域类型 `BazaarTypes.EBazaarHero`**，不是客户端 `EHero`；本方案以它为**语义先例**，用客户端 `EHero` 重写。
- 服务端过滤还按 `BazaarCard.ECardType.Merchant`（`:4520`）这一**服务端独有枚举值**，客户端 `ECardType` **没有** `Merchant` 成员（已确认 `ECardType.cs`）。故 §4 客户端侧用 `EventEncounter/CombatEncounter/PedestalEncounter + card.Merchants`/训练师判定等价替代。

当前运行英雄全局可读：`Data.SelectedHero`（`decompiled/TheBazaar/Data.cs:100`），mod 已有先例 `OpponentPortraitController.cs:153`。注意 mod 的 `IRunContext`/`RunContextStore` **不**携带英雄，**直接用 `Data.SelectedHero`**（或 CollectionPanel 已有的 `SelectedHeroes` 过滤态，`CollectionPanel.cs:590`）。

### 实现：单一关联辅助（与既有 hero-scope 谓词的关系已厘清）

> **【代码校订】** 本节早期草案引用 `CollectionFilterEngine.AnyHeroMatch`（声称在 `CollectionFilterEngine.cs:90-101`），**该类型成员不存在**：`CollectionFilterEngine`（`Game/CollectionPanel/Data/CollectionFilterEngine.cs`）的公开入口是 `Apply(...)`（`:15`），它把 hero 匹配**委托**给 `CollectionHeroScope.MatchesFilter`（`:43`）。真正做集合相交的 `AnyHeroMatch` 是 **`CollectionHeroScope` 的私有方法**（`Game/CollectionPanel/Data/CollectionHeroScope.cs:28-39`），且 `CollectionHeroScope` 还对 `Skill` 卡做单英雄特判（`MatchesSkillHeroScope`，`:23-26`）。下文凡提 `CollectionFilterEngine.AnyHeroMatch(:90-101)` 处，**应读作 `CollectionHeroScope` 内的私有 `AnyHeroMatch(:28-39)`**。
>
> 此外，本节草案新增的 `Game/CollectionPanel/Encounters/HeroAssociation.cs` **未实现**（无此目录/类型）。实际的 encounter↔英雄 Common-OR-空集语义由 `CollectionSourceEntry.AppliesToHero(EHero)`（`CollectionSourceEntry.cs:60-61`：`AvailableHeroes.Count == 0 || AvailableHeroes.Contains(hero)`）承担，英雄集合来自签入目录的 `availableHeroes`，**非**运行时从卡 `Heroes` 派生。下方草案仅作历史记录。

新增 `Game/CollectionPanel/Encounters/HeroAssociation.cs`（纯函数，可单测）。同 feature 文件夹已有 hero-scope 谓词 `CollectionHeroScope.AnyHeroMatch`（私有，`CollectionHeroScope.cs:28-39`），但语义**不同**——`AnyHeroMatch` 只做集合相交，**不** special-case `Common`，也不把空集当 match-all。商人/训练师关联需要 `BazaarCardDealer:4494` 的 **Common-OR-空集回退**语义。故**保留独立 `HeroAssociation`，但在注释交叉引用 `AnyHeroMatch` 说明语义差异，避免两个 matcher 静默分叉**：

```csharp
#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Encounters;

// 复现 BazaarCardDealer.cs:4494 的 hero 谓词（客户端 EHero 版）：Common-OR-空集 → match-all。
// 注意：与 CollectionHeroScope.AnyHeroMatch(私有, :28-39) 语义【不同】——
//   AnyHeroMatch 仅做集合相交，不特判 Common、不把空集当 match-all（那是给 Item/Skill 多选筛选用的）。
//   本谓词专为 encounter↔hero 的 shipped 数据语义而设，故不复用 AnyHeroMatch。
internal static class HeroAssociation
{
    public static bool Includes(IReadOnlyCollection<EHero>? heroes, EHero current) =>
        heroes == null || heroes.Count == 0
        || heroes.Contains(EHero.Common) || heroes.Contains(current);
}
```

### 与英雄 UI 的联动（诚实回退）

UI 用 `Data.SelectedHero`（或 `SelectedHeroes`，`CollectionPanel.cs:590`）作 `currentHero`，对已缓存 encounter 列表跑 `Where(c => source.MatchesHero(c, currentHero))`，选中英雄变化时商人/训练师分组随之过滤。

**诚实回退**：若某卡 `Heroes` 为空，按 `HeroAssociation.Includes` 落入「Common/全英雄」→ 对所有英雄可见。这与游戏 `Common`-OR 语义一致，非 bug。**【需运行时验证】** shipped 数据中是否每张商人/训练师卡都填了 `Heroes`（bazaardb 显示绝大多数有 1+ 英雄标签，但少数如 `Adira`/`Argenta` 无英雄标签 → 落入全英雄回退）。无更细的「英雄→encounter」直链可用。

---

## 6. 完整的项目文件结构调整建议

严格按 `GameInterop/`（运行时适配）vs `Game/`（特性逻辑）切分：

### 新增文件（GameInterop —— 仅 1 个，运行时 Sprite 加载，商人/训练师共用）

```
GameInterop/EncounterPortraits/
  └─ EncounterPortraitSpriteProvider.cs       // §3.1；namespace BazaarPlusPlus.GameInterop.EncounterPortraits
                                              // internal static；键=Guid；复用 BppStaticDataAccess
```

### 新增文件（Game —— 特性逻辑：筛选/UI；无接口、无独立扫描器）

```
Game/CollectionPanel/Encounters/
  ├─ EncounterPortraitSource.cs               // §3.2/§4 参数化来源类（EncounterPortraitKind + 可选 CollectionMerchantKind?）
  ├─ EncounterPortraitItem.cs                 // §3.2 条目 struct（纯数据，无本地化字段）
  └─ HeroAssociation.cs                       // §5 纯函数（可单测；注释交叉引用 AnyHeroMatch）

Game/CollectionPanel/Ui/
  └─ CollectionPanelView.EncounterPortraits.cs // §7 partial UI：分组行 + 圆形 avatar 绑定 + _encounterIcons 跟踪
                                              // 复用 ApplyHeroChipIcon 的 StyleBackground 模式与 userData token
```

> **已删除（相对原始草案）**：`IMerchantPortraitSource.cs`、`MerchantPortraitCatalog.cs`、`Sources/TrainerPortraitSource.cs`——过度设计。商人/训练师由**同一个参数化类**的 `EncounterPortraitKind` 表达。

### 修改文件

```
Game/CollectionPanel/Data/CollectionCatalog.cs          // 扩展 build session 额外保留 encounter 型 VM（§3.2）
Game/CollectionPanel/Data/CollectionCatalogBuildSession.cs / *BuildResult.cs
                                                        // 增补 EncounterCards 列表字段（与 Cards 并列）
Game/CollectionPanel/CollectionPanelText.cs             // 新增 MerchantHeader()/TrainerHeader() 本地化 key（仅 UI 层用，不入测试链）
Infrastructure/UiTokens/Sizes.cs                        // 新增 EncounterPortraitSize/EncounterPortraitButtonSize
Game/CollectionPanel/Ui/CollectionPanelView.cs          // Refresh(model) 中按 SelectedHeroes 变化重建商人/训练师分组（§7）
Game/CollectionPanel/CollectionPanel.cs                 // view model 暴露当前英雄 + 商人/训练师分组条目
```

> **注意**：`CollectionCardClassifier.cs` **不修改**（`HasRenderableArtKey` 包装已作废，§4）——**除非** §4.2 运行时核对判定训练师需要新增技能池派生，届时该派生应落在分类器或一个新的 `EncounterClassification` 辅助里（不在 UI/Provider 内）。

### 测试文件（仅对纯逻辑加单测，遵守「无覆盖率剧场」）

```
tests/CollectionFilterEngine.Tests/Program.cs           // 追加用例：
  //   (a) HeroAssociation.Includes 真值表（空集→true、含 Common→true、含/不含当前英雄）
  //   (b) EncounterPortraitSource.Matches（Merchant: EventEncounter+Merchants 命中、Item 不命中、merchantKindFilter 过滤）
  //   (c) Trainer kind：在 §4.2 判定定稿后补（在此之前 IsTrainer 返回 false，可断言"暂不命中"占位）
tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj  // 追加 <Compile Include> 链接：
  //   ../../Game/CollectionPanel/Encounters/HeroAssociation.cs
  //   ../../Game/CollectionPanel/Encounters/EncounterPortraitItem.cs
  //   ../../Game/CollectionPanel/Encounters/EncounterPortraitSource.cs
  //   （绝不链接 CollectionPanelText —— 见下）
```

> **测试可编译性**：`EncounterPortraitSource.Matches`/`MatchesHero` 与 `HeroAssociation.Includes` 设计为**纯谓词，零本地化/UI 依赖**——`*Header()` 本地化**留在 UI 层（§7）**，不在被测类型表面。故链接这三文件进 exe-runner**不会**拖入 `CollectionPanelText` 的 UI/本地化传递依赖，可正常编译。
>
> **测试可断言性**：现有 `Card(...)` 工厂（`Program.cs:184-204`）**无 `artKey` 参数**，`ArtKey` 恒 `string.Empty`。因 §4 已把 ArtKey 预检**从谓词移除**，`Matches`（Merchant）只看 `Type`+`Merchants`+可选 `merchantKindFilter`，**无需扩展工厂**即可有意义断言。
>
> **不要测**：`EncounterPortraitSpriteProvider`（依赖 Unity addressables，同 `HeroPortraitSpriteProvider` 无测试种子）——项目规则「无有意义测试种子可不加测试」「禁止覆盖率剧场」。
>
> **测试 trap（MEMORY + CLAUDE.md）**：`CollectionFilterEngine.Tests` 是 exe-runner（`.csproj:3` `<OutputType>Exe</OutputType>` + `Program.cs`，`EnableDefaultCompileItems` 关闭，显式跨目录 `<Compile Include>`，`:22-45`），**不自动发现源文件**，必须为每个被测新文件手加 `<Compile Include>` 链接。它已引用 `BazaarGameShared`（含 `EHero`/`ECardType`/`ECardTag`），故纯谓词**可测**。运行：`dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`（**非** `dotnet test`）。
>
> **架构测试**：`tests/Architecture.Tests/CoreLayeringTests.cs` 只守 `Core/` 地板与 CollectionPanel→HistoryPanel.Preview 边界。本方案复用既有 GameInterop-portrait 模式，**不构成新边界**，无需新增架构测试。红线：**任何 `Core/` 下文件不得 `using BazaarPlusPlus.GameInterop.EncounterPortraits`**。

---

## 7. 具体的实现步骤和代码框架（有序）

### Step 0 — 【需运行时验证】（动工前必做，见 §2/§4.2）
游戏内取 1 张商人卡（Aimbot）+ 1 张训练师卡（Bjorn），打印 `ArtKey`，确认 `LoadAssetAsyncByAddress<EncounterAssetDataSO>(ArtKey)` 返回非 null SO 且 `LoadPortraitSpriteAsync` 出非 null sprite；同时确认 mod 分类器对训练师的归属（决定 §4.2 走「复用 `card.Merchants`」还是「新增技能池派生」）。

### Step 1 — GameInterop 适配器（运行时 Sprite 加载）
新建 `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs`，照 §3.1：缓存/in-flight 字段镜像 `HeroPortraitSpriteProvider.cs:18-19`；`LoadPortraitAsync(Guid)` 镜像 `:30-44`；`LoadAndMaybeCacheAsync` 用反射 `LoadAssetAsyncByAddress<EncounterAssetDataSO>`（签名已证 `AssetLoader.cs:652`）+ `IPortraitAssetData.LoadPortraitSpriteAsync`。`LogComponent="EncounterPortrait"`，空 sprite 记 `Debug`、异常记 `Warn`。**纯新增，无现有调用方依赖。**

### Step 2 — 扩展 CollectionCatalog 保留 encounter VM
在 `CollectionCatalogBuildSession` 一次遍历中，每张 `TCardBase` 既按现有 `IsCatalogCard` 收进 `Cards`（Item/Skill），又**额外**把 encounter 型（`CombatEncounter/EventEncounter/PedestalEncounter`）的 `CollectionCardVm.From(t)` 收进新列表 `EncounterCards`；`CollectionCatalogBuildResult`/缓存随之增补。既有筛选 UI 仍只读 `Cards`，**行为不变**。**消除任何第二次 `GetCardMap()` 扫描与二次投影。**

### Step 3 — 来源类与英雄关联（纯逻辑，可单测）
新建 `Game/CollectionPanel/Encounters/` 下 `EncounterPortraitSource.cs`（§3.2/§4，含 `EncounterPortraitKind` 与可选 `CollectionMerchantKind?`；`IsTrainer` 先占位）、`EncounterPortraitItem.cs`、`HeroAssociation.cs`（注释交叉引用 `AnyHeroMatch`）。三者均**不引用** `CollectionPanelText`/UI 类型。

### Step 4 — 本地化 key + 尺寸 token（仅 UI 层）
- `CollectionPanelText.cs`：新增 `MerchantHeader()`/`TrainerHeader()`（中/英按现有本地化模式）。**仅 §7 UI 用，不进测试链。**
- `Sizes.cs`：新增 `public const float EncounterPortraitSize = 48f; public const float EncounterPortraitButtonSize = 56f;`（与 `HeroChipIconSize`/`HeroChipButtonSize` 同值，`:21-22`），**禁止 UI 内写魔数**。

### Step 5 — UI 绑定（partial，复用英雄绑定原语 + tracked element map）
新建 `Game/CollectionPanel/Ui/CollectionPanelView.EncounterPortraits.cs`。圆形 avatar 构建 + Sprite 绑定**直接照搬** `CreateHeroChipIcon`（`:216-231`）与 `ApplyHeroChipIcon`（`:285-295`）。**关键：照搬 tracked element map**——`_heroChipIcons[hero]=icon`（`:210`）对应新增 `_encounterIcons : Dictionary<Guid, VisualElement>`：

```csharp
private readonly Dictionary<Guid, VisualElement> _encounterIcons = new();  // 对应 _heroChipIcons(:210)

private void LoadEncounterIcon(Guid templateId, VisualElement icon)
{
    _encounterIcons[templateId] = icon;                             // tracked map（仿 :210）
    icon.userData = templateId;                                     // 竞态 token（仿 :262）
    if (EncounterPortraitSpriteProvider.TryGetCached(templateId, out var cached))
    { ApplyEncounterIcon(icon, cached); return; }
    ApplyEncounterIcon(icon, null);                                 // 先占位
    _ = ApplyEncounterIconWhenLoadedAsync(templateId, icon);
}

private static async Task ApplyEncounterIconWhenLoadedAsync(Guid id, VisualElement icon)
{
    var sprite = await EncounterPortraitSpriteProvider.LoadPortraitAsync(id);
    if (!Equals(icon.userData, id)) return;                         // 防贴错（仿 :280-281）
    ApplyEncounterIcon(icon, sprite);
}

private static void ApplyEncounterIcon(VisualElement icon, Sprite? sprite)   // 复刻 :285-295
{
    if (sprite == null) { icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null); return; }
    icon.style.backgroundImage = new StyleBackground(sprite);
    icon.MarkDirtyRepaint();
}
```
分组行布局可参考 `Game/Supporters/Ui/BPPSupporterAttributionRow.cs`：`Create()` 在 `:14`，`flexDirection=Row`/`flexWrap=Wrap` 在 `:17-18`；但**图片绑定必须用上面的 `ApplyEncounterIcon`**，不是 Supporter 的文字 pill。

### Step 6 — 接入 CollectionPanelView 刷新路径（明确重建时机）
- `CollectionPanel.cs` view model 增补：当前英雄（`Data.SelectedHero` 或 `SelectedHeroes`，`:590`）+ 商人/训练师分组条目（对缓存 `EncounterCards` 跑 `Where(c => source.MatchesHero(c, hero))`）。
- `CollectionPanelView.cs` 的 `Refresh(model)`：**当 `SelectedHeroes` 变化时**重建分组——清空并重填 `_encounterIcons`（或 per-group 容器），对每个 `EncounterPortraitItem` 调 `LoadEncounterIcon(item.TemplateId, icon)`。句柄登记在 `_encounterIcons` 且每次重建重设 `userData`，§5 异步 token 比对始终有**稳定 element** 可比，跨刷新不贴错、不泄漏。分组标题用 `MerchantHeader()`/`TrainerHeader()`。

### Step 7 — 挂载/组合
**无需新 mountable**——商人/训练师头像是 CollectionPanel 子特性，随 `CollectionPanelMount`（`BppComposition.cs:98`）生命周期管理。在 `CollectionPanel.Initialize` 内 `new EncounterPortraitSource(EncounterPortraitKind.Merchant)` 与 `new EncounterPortraitSource(EncounterPortraitKind.Trainer)`（要 kind 子分组则多 `new` 几个带参实例）。Provider 是 `static`，零注册。

### Step 8 — 测试（仅纯逻辑）
**先在 csproj 加齐 §6 的 3 个 `<Compile Include>` 链接**（绝不含 `CollectionPanelText`），再在 `tests/CollectionFilterEngine.Tests/Program.cs` 追加 §6 (a)(b)(c) 用例。运行 `dotnet run --project ...`（exe-runner）。

### Step 9 — 构建验证（按改动比例）
`./run.sh build`（Debug，自动拷贝 `BepInEx/plugins`）+ 上述单测。无需 `BuildAll`/Release 打包（非打包改动）。

---

## 实现前必须由用户拍板的开放项

1. **训练师判定路径（§4.2）**：运行时核对后决定 `IsTrainer` 收敛为「复用 `card.Merchants`」还是「新增技能池派生」。这是训练师落地唯一未决点，**不阻塞商人通路**。
2. **是否需要 kind 细分 UI**：若要按 Burn/Poison 等 `CollectionMerchantKind` 分组，用参数化 `EncounterPortraitSource`（1 类 + 参数），不建 15 类。
3. **【需运行时验证】清单**（动工前/动工中游戏内确认）：
   - **(必做，§2/Step 0)** 商人卡 + 训练师卡的 `ArtKey` 是否解析为 `EncounterAssetDataSO` 并出非 null portrait——整条链路唯一未由反编译证明的数据形态环节。
   - **(§4.2)** mod 分类器对训练师 encounter 的归属。
   - **(§5)** shipped 数据中商人/训练师卡 `Heroes` 的填充率（影响「按英雄过滤」的视觉差异度；bazaardb 显示多数有英雄标签，少数无 → 全英雄回退）。

---

## 关键文件锚点（绝对路径）

- 模板适配器：`GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs`
- UI 绑定原语 + tracked map：`Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs:210,216-231,260-272,285-295`
- 现有目录缓存（待扩展）：`Game/CollectionPanel/Data/CollectionCatalog.cs:10-140`（`IsCatalogCard` 丢弃 encounter 在 `CollectionCardClassifier.cs:49`）
- 既有 hero 谓词（语义对照）：`Game/CollectionPanel/Data/CollectionHeroScope.cs:28-39`（私有 `AnyHeroMatch`；由 `CollectionFilterEngine.Apply` → `CollectionHeroScope.MatchesFilter` 调用，`CollectionFilterEngine.cs:15,43`）。实际 encounter↔英雄关联落地在 `Game/CollectionPanel/Sources/CollectionSourceEntry.cs:60-61`（`AppliesToHero`）
- 实际落地（取代 §3.2 草案）：`Game/CollectionPanel/Sources/`（`CollectionSourceEnums.cs:5-9`、`CollectionSourceEntry.cs:17,60-61`、`CollectionSourceCatalog.cs:17`、`CollectionSourceDtos.cs`）+ 嵌入目录 `Data/CollectionSources/collection-sources.json`（`BazaarPlusPlus.csproj:31`）；头像加载 `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs:75,87`（直调，非反射）
- 静态数据接缝：`GameInterop/StaticCards/BppStaticDataAccess.cs:25,33`
- 分类器（复用 `ResolveMerchants`）：`Game/CollectionPanel/Data/CollectionCardClassifier.cs:61-103,105-109`
- VM 投影：`Game/CollectionPanel/Data/CollectionCardVm.From.cs:11-28`
- 挂载注册：`BppComposition.cs:98`
- 尺寸 token：`Infrastructure/UiTokens/Sizes.cs:21-22`
- 行布局参考：`Game/Supporters/Ui/BPPSupporterAttributionRow.cs:14,17-18`
- exe-runner 测试 csproj：`tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj:3,22-45`
- 测试卡工厂（无需扩展）：`tests/CollectionFilterEngine.Tests/Program.cs:184-204`
- 运行英雄读取：`decompiled/TheBazaar/Data.cs:100`（先例 `OpponentPortraitController.cs:153`）
- 加载机制锚点：`decompiled/.../EncounterController.cs:302-306`、`EncounterClickController.cs:92-96`、`AssetLoader.cs:652,710-756`、`EncounterAssetDataSO.cs:15,105-116`、`BazaarBattleService/BazaarCardDealer.cs:4491,4494,4520`
- 类型/标签枚举：`decompiled/BazaarGameShared/BazaarGameShared.Domain.Core.Types/ECardType.cs`（无 Merchant 成员）、`ECardTag.cs:16`（Merchant 标签）

---

## 附：bazaardb 分类法速查（数据模型参考，非图片来源）

- 分类：Items / Skills / **Merchants(47)** / **Trainers(23)** / Monsters / Events。
- 商人：`EventEncounter` + `Merchant` 标签 + Item Pool；子类型按"卖什么"（Weapons/Vehicles·Drones/Crit/Small/Ammo/Haste/Slow/中立/英雄专属）。
- 训练师：`EventEncounter`（无 Merchant 标签）+ Skill Pool；子类型按"教什么"（Freeze/Ammo/Shield/Burn/英雄专属技能）。
- 英雄缩写：`VAN`=Vanessa `PYG`=Pygmalien `DOO`=Dooley `MAK`=Mak `STE`=Stelle `JUL`=Jules `KAR`=Karnok。
- 关联多对多：每个 encounter 卡带英雄子集；连物品/技能池本身也可按英雄过滤。
