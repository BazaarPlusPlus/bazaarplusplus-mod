# 成就卡闪烁 + 关联缺陷 · 完整修复方案

Status: drafted 2026-06-19。针对 commit `18bfda3f`（feat: add custom achievement cards）真机实测出的"成就卡持续闪烁"，
经诊断 workflow（3 路并行 + 对抗验证）+ 本人独立核对 decompiled 着色器与渲染链定位。根因已确认，并连带发现 2 个 major、3 个 minor。

---

## 1. 闪烁根因（已确认，非虚拟化器问题）

**结论：成就卡是全面板唯一一张用"裸着色器材质" `new Material(_cardMaterialShader)` 构建的卡，导致 premium/附魔动画着色器变体被默认启用、每帧自驱动 → 持续 strobe。**

已排除（对抗复核确认，均有 `file:line`）：
- 非每帧重建：`Update` 只调 `Tick/TickFades/PollHover`（`CollectionPanel.cs:405-414`），`ApplyFilters` 只来自用户命令 + 一次 catalog 加载完成。
- 单卡网格永不回收重绑：窗口 `firstIdx==lastIdx==0`（`CollectionGridVirtualizer.cs:150-195`），`BuildItem` 1 格 1 shelf（`CollectionGridLayout.cs:104-137`）。
- 原生 `CardPreviewItem/Base` 无 `Update`/协程；卡只 realize 一次、`ShowWhenReady` 淡入一次。
- `RawImage.texture` 为 null 是红鲱鱼：原生与 package 路径同样不设 `_cardImage.texture`，美术全靠材质 `_BaseMap`（`CardArtInjector.cs:104-117`）。

真正差异点（决定性）：
- 卡面着色器 `_cardMaterialShader` 含 `_IsPremium` / `_ISPREMIUM_ON` 与 16 个 `_ENCHANTMENTSTATUS_*` 变体，均为 `_Time` 驱动的动画（foil/holo/附魔光效），见 `decompiled/.../CardArtShaderVariables.cs:9,11,13-45`。
- 每张**不闪**的卡都**克隆授权材质**（`new Material(assetData.cardMaterial)`，`decompiled/.../CardPreviewItem.cs:59`），该授权材质把 premium 关、附魔关、所有 sampler/float 都配好；且原生 `UpdateCardImageMaterial` 还**显式** `DisableKeyword(_ISPREMIUM_ON)`（非 premium，`CardPreviewItem.cs:63-70`）+ `ApplyEnchantment(null)→ClearEnchantmentKeywords`（`:73`）。
- 成就路径 `CollectionCardMaterialCache.GetOrCreate(artKey, texture, shader)`（`CollectionCardMaterialCache.cs:60-77`）走裸 `new Material(shader)`，只设 `_BaseMap`+`mainTexture`，premium/附魔关键字**一个都没动** → 停在着色器默认变体（premium 默认开）→ 每帧动画 = 持续闪。

---

## 2. 修复清单（按优先级）

### F-1（blocker）成就卡材质：消除动画 premium/附魔状态

分两步走（先验证、再决定是否升级），符合"先复现验证、不臆测"的工作方式：

**F-1a（先做，几乎必定止闪 + 可证伪）—— 把裸材质的关键字状态对齐原生"非 premium / 未附魔"卡。**
在 `CollectionCardMaterialCache.cs:68` 的 `new Material(shader)` 之后、`CardArtInjector.Apply` 前后，加：
```csharp
var material = new Material(shader) { name = $"CollectionPanelMaterial[{artKey}]" };
material.SetFloat(CardArtShaderVariables.PremiumShaderId, 0f);
material.DisableKeyword(CardArtShaderVariables.PremiumShaderKeyword);
CardArtShaderVariables.ClearEnchantmentKeywords(ref material); // 关掉全部 _ENCHANTMENTSTATUS_* 动画
if (!CardArtInjector.Apply(material, texture)) { Object.Destroy(material); return null; }
```
> 这正是原生非 premium、未附魔卡的关键字状态（`UpdateCardImageMaterial` Disable premium + `ApplyEnchantment(null)`）。
> 关键字驱动的动画一旦关闭，strobe 必停。成就卡本就该是"朴素插画"，不需要 foil/附魔光效，所以这也是期望观感。

**F-1a 真机验证（ComputerUse 看图）：** `./run.sh build` → Steam 启动 `open steam://run/1617400` → 开成就 tab 截图。
- 通过 = 不再闪 **且** 卡面美术显示正常（插画落在卡窗内、无溢出/错色）。

**F-1b（仅当 F-1a 后仍有残留静态渲染问题，如缺遮罩/错色才升级）—— 克隆授权材质。**
裸材质仍缺授权材质里的其它 sampler/float（mask/noise 等）。若 F-1a 后画面"不闪但仍不对"，改走克隆路径：
- 复用已有克隆重载 `CollectionCardMaterialCache.GetOrCreate(artKey, assetData, shader)`（`:44-58`，真实物品卡走的就是它）。
- donor `CardAssetDataSO` 来源（二选一，避免硬编码脆弱键）：(a) 复用 art-cache 里已加载的任一真实物品授权材质做 base；
  (b) 给合成模板 `BppCustomCardTemplateFactory.Build` 设一个**稳定 donor `ArtKey`**，让原生 `LoadArt` 自己加载授权 assetData、
  建好 `_cardMaterial`，成就 postfix 再克隆它 + 换 `_BaseMap`（等价于 package 换贴图路）。donor 的美术永不显示（被我们覆盖），
  且 swap 发生在 `Show(true)` 之前（`ShowWhenReady` await 完 SetUp 才显示），无 donor 美术闪现。
- 决策点：donor 来源 (a)/(b) 在 F-1b 真正需要时再定；F-1a 大概率已足够，不预先引入耦合。

无论 a/b，都应**删除/不再使用**裸 `new Material(shader)` 这条会留隐患的路径（或仅在已硬化后保留）。

### F-2（major）成就 tab 进入时清空 `Tiers`，避免遗留 tier chip 把唯一的卡筛没

`SelectTab` 对 Achievements 清了 `Heroes/Sizes/Tags/Keywords/SelectedSourceKey`，**漏了 `Tiers`**（`CollectionFilterState.cs:87-94`）。
而 tier 筛选在引擎里**无条件**生效（`tierFilterCount = filter.Tiers.Count`，`CollectionFilterEngine.cs:30,54`）。cosmic_ray 是 `Legendary`，
从物品/技能 tab 带入一个非 Legendary 的 tier chip 就会把成就网格筛空（卡能渲染但"消失"）。这正是方案 §11 陷阱清单里点名的坑。
```csharp
// CollectionFilterState.SelectTab, Achievements 分支内补一行：
Tiers.Clear();
```
> 注意：是进入 tab 时**重置为不筛**（保留 tier 行让用户之后主动筛，符合"成就有 ETier、保留 tier 筛选"的决策），不是永久禁用 tier。
> 可选一致性：把 `CollectionFilterEngine.cs:30` 的 `tierFilterCount` 也 gate 到 `profile.ShowTierFilter`，与 tag/keyword/size（`:31-33`）对齐。

### F-3（major）`RegisterAchievementCards` 加 try/catch，避免坏 catalog 砸掉整个插件

`BppComposition` ctor **无保护**地调 `RegisterAchievementCards`（`BppComposition.cs:96`），其内部 `LoadEmbedded()`/`Register()`
对任何校验失败（schema/UUID/重复/撞真实卡/缺语言）都抛异常，异常冒出构造函数 → **所有功能**（美术替换、run 记录、战斗回放、
状态栏、历史面板、上传、BazaarAgent…）全部启动失败，而不只是成就。
```csharp
private static void RegisterAchievementCards(BppCustomCardRegistry registry)
{
    try
    {
        var catalog = AchievementCardCatalog.LoadEmbedded();
        var mapper = new AchievementCardDescriptorMapper();
        foreach (var card in catalog.Cards)
            registry.Register(mapper.Map(card));
    }
    catch (Exception ex)
    {
        BppLog.Warn("Achievements", $"Achievement catalog load failed; achievements disabled: {ex.Message}");
        // registry 保持空：只丢成就 tab 的卡，不连累整个插件。
    }
}
```

### F-4（minor）成就 tab 的 tier 区头标签显示成了"排序-品质"文案
`CollectionPanelView` 在 `showSizeChips==false` 时把标签翻成 `SortQuality()`；成就 `ShowSizeFilter=false` 但 `ShowTierFilter=true`，
于是可见的 tier chip 行配了错误标题。改：标签按 `ShowTierFilter`/`ShowSizeFilter` 独立判断，tier-only 行给 tier 标题。（仅装饰，未独立复核，落地时确认。）

### F-5（minor）`TryRealize` 给自定义模板 `Build` 包 try/catch
`CollectionCardFactory.TryBind` 直接调 `BppCustomCardTemplateFactory.Build(descriptor)`，而 `CollectionGridVirtualizer.TryRealize`
在每帧 Tick 路径上无 try/catch。合成 record 字段一旦抛，就是每帧未捕获异常。改：自定义分支包 try/catch、失败返回 `HardMiss()`（从而被 memoize 跳过）。

### F-6（minor）`BppCustomCard.Tests` 改为 leaf-include
`tests/BppCustomCard.Tests` 直接 ProjectReference 整个 game/Unity 耦合的主插件程序集，与同目录 Collection 测试项目"只 Compile-Include 叶子源 + 仅引 BazaarGameShared"的约定不一致。改：只 include 需要的源（ids/catalog/mapper/template factory），按兄弟 csproj 形态引 BazaarGameShared。

---

## 3. 提交切分与时序

1. **Commit 1（修闪 + 两个 major，一起，因为都属"成就卡不可用"）**：F-1a + F-2 + F-3。
   - 先 F-1a，真机看图确认止闪 + 美术正确（这步是 F-1a/F-1b 的判定闸）。
   - 同提交带 F-2（tier 清空）、F-3（catalog 守护）。
   - 验证：`./run.sh build` + `tests/BppCustomCard.Tests` + 三个 Collection exe-runner 测试（`dotnet run --project`，grep "Failed test projects:"）。
     真机：成就 tab 不闪；从物品 tab 选非 Legendary tier 再切成就，cosmic_ray 仍在；hover 出原生 tooltip（名字+描述）正常。
2. **Commit 2（仅当 F-1a 不够）**：F-1b 克隆授权材质（含 donor 来源决策），重测看图。
3. **Commit 3（minor 收尾）**：F-4 + F-5 + F-6。

收尾按仓库规则：每个提交先自查 diff，验证通过后再 commit；完成后合并工作分支到 `master`、push、删除已合并分支。

---

## 4. 验证矩阵

| 项 | 手段 | 通过标准 |
|---|---|---|
| F-1a 止闪 | 真机 ComputerUse 看图 | 成就卡不再 strobe |
| F-1a 美术正确 | 真机看图 | 插画落在卡窗内、无溢出/错色（若不对 → F-1b） |
| F-2 stale tier | 物品 tab 选非 Legendary tier → 切成就 | cosmic_ray 仍显示 |
| F-3 守护 | 单测（喂一个坏 catalog 断言不抛 + registry 空）或代码评审 | 坏 catalog 只丢成就、其余功能正常 |
| 回归 | 真机切 物品/包裹/技能 | 行为与改前一致；package 换贴图 toggle 路不变 |
| 构建/单测 | `./run.sh build` + 全部受影响测试项目 | 绿 |
