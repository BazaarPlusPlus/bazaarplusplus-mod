---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# CollectionPanel 标签 chip「关键词图标」实现 设计

> **Status:** Implemented (2026-06-09) — 代码、格式检查、`Architecture.Tests`、`./run.sh test`、Debug build 均已通过；TMP 图集抽取的游戏内视觉验证仍按 §8.3 待实机确认。
>
> 本文所有 `file:line` 引用经直读对 HEAD（master `b8442fd`）验证；TMP 包 API（`ExtractSprite` 路径）不在 `decompiled/`，已显式标注为唯一「仅能实机验证」项（§8.3）。

**Goal:** 在 CollectionPanel 筛选行的 **TAG（`ECardTag`）/ 关键词（`EHiddenTag`）chip 文字前**渲染游戏官方图标。当前实现中该图标**从未被实现**（非样式/资源 bug，见 §1）——本方案补齐数据→seam→渲染三段链路，让有 `KeywordIconColorConfiguration.IconName` 的标签显示官方图标，无图标的标签保持纯文字。

**Tech Stack:** C# 12 / netstandard2.1、Unity UI Toolkit（UITK chips）、`TheBazaar.UI.Tooltips.TooltipTypography` / `KeywordIconColorConfiguration`（经 `GameInterop/TagTypography` 适配器 + 反射）、TMP（`TMP_SpriteAsset` / `TMP_SpriteGlyph`，`Unity.TextMeshPro` 已在 csproj 引用）、BepInEx `BppLog`。

---

## 0 已确认决策

| # | 决策点 | 选定 |
|---|---|---|
| 1 | 图标来源 | **游戏 keyword 配置的 `IconName`**（TMP sprite 名）；无第二真相源 |
| 2 | 渲染方式 | **抽 `Sprite` 进 UITK `backgroundImage`**（复用 `ApplyHeroChipIcon` 先例）；**不**用 TMP `<sprite>` 富文本（UITK 不解析，§2） |
| 3 | 分层 | 图标解析（TMP / `Resources` 扫描 / 图集抽取）放 `GameInterop/TagTypography` 新 seam；`Game/CollectionPanel` 仅消费 `Sprite`，**不新增** `TheBazaar.*Tooltips` / `TMPro` import |
| 4 | 缓存失效 | 图标 locale 无关 → 图标缓存**不随 locale 清**（与 `NativeTagTypography.Cache` 的 locale 键生命周期不同，刻意为之） |
| 5 | 无图标标签 | 图标位 `display:none`（**承重分支**：多数 `ECardTag` 无 config，§1.4），保持纯文字 + 既有强调色 |
| 6 | 退化语义 | 图集未加载 / 名缺失 / 反射异常 → 返回 `null`，chip 显示无图标纯文字，不抛不崩；按次重试自愈（§5） |

## 0.1 对抗式核查处置（3 组 verifier，2026-06-09）

| # | 发现 | 核实 | 处置 |
|---|---|---|---|
| 1 [high] | root-cause 链（无图标字段 / 不读 IconName / 纯文字 chip / 刷新只设 text+color） | **全部属实，零更正** | 作为 §1 现状依据 |
| 2 [high] | 青龙图标路径是 TMP 富文本（`text.spriteAsset` + `<sprite>`），**对 UITK 不可用**；须独立抽 `Sprite` | 属实 | 采纳：seam 抽 `Sprite`，决策 2 |
| 3 [med] | `ToggleTagRowExpanded`（`Filters.cs:282-293`）**不设 text**，只调 `RefreshChip` 改色 | 属实（早期表述不准） | 采纳：图标只在「创建时 + 每次 Refresh 循环」套用，**不**改 `RefreshChip`；展开/收起靠 chip 重建走创建路径（§4.3） |
| 4 [med] | typography 自愈（`IsNativeTypographyAvailable`）只探 typography 实例，**不覆盖 TMP 图集就绪** | 属实 | 采纳：`BeginResolvePass` 每次 Refresh 限一次扫描重试 + 承认极小残余（§5） |
| 5 [low] | 多数 `ECardTag` 无 keyword config → Item 页 chip 既无色也无图标；图标主要落 `EHiddenTag`（Skill 页） | 属实（运行时数据，静态不可知） | 写入预期校正（§1.4、§7） |

---

## 1 现状：图标为何不显示（已核查，零更正）

**根因：该功能从未实现。** 它在 [`2026-06-07-collection-tag-native-typography.md:228-231`](2026-06-07-collection-tag-native-typography.md) 被显式划到 Phase 3（「可选视觉增强，仅 Phase 1 游戏内验证后考虑」），后续顶层重构 PR 也未补。代码里没有任何读取/解析/渲染图标的逻辑。

### 1.1 数据层不带图标

- `GameInterop/TagTypography/NativeTagDisplay.cs:8-13` —— struct 仅 `Label` + `AccentColor`，**无图标字段**。
- `GameInterop/TagTypography/NativeTagTypography.cs:96-102` —— `ResolveUncached` 读了 `Text` / `MakeAllUppercase` / `Color`，**唯独不读 `IconName`**（`KeywordIconColorConfiguration.cs:40`，可选字段，默认空串）。

### 1.2 渲染层是纯文字按钮

- TAG / 关键词 chip 由 `Ui/CollectionPanelView.Filters.cs:456-466` `CreateCompactChipButton` 创建 —— 纯文字 `Button`，**无图标子元素**（对比 `CreateHeroChipButton`（`:468-491`）/ `CreateSourceChipButton`（`:493-520`）**有** `chip.Add(icon)`）。
- 每次刷新 `Ui/CollectionPanelView.cs:371-382` 只设 `.text` 与强调色；`RefreshChip`（`Filters.cs:726-743`）只改背景/文字/边框色。

### 1.3 原生机制：游戏也不在 chip 上贴图标——TMP 富文本，UITK 不解析（§2 详述）

- 原生标签行 `TagRenderer.Render`（`decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TagRenderer.cs:18-30`）→ `TooltipTagController.InitTooltipTag` 只渲染带色**文字**（`ColorTag`）。
- 图标只存在于 keyword **富文本**：`TooltipTypography.cs:205-207` 把 `IconName` 拼成 `"<sprite name=" + IconName + ">"`，交 TMP 渲染。

### 1.4 哪个 facet 实际有图标（预期校正）

`KeywordIconColorConfiguration` 主要服务 `EHiddenTag` 关键词（Damage/Heal/Burn/Poison/Shield/Haste/Crit…，`Data/CollectionKeywordWhitelist.cs:16-39`）——这些才有 `IconName`。`ECardTag` 物品品类（Weapon/Tool/Food…，`Data/CollectionTagWhitelist.cs:19-25`）多数无 config，**既无色也无图标**（青龙亦自维护 `ECardTag` 配色，`MerchantQuickReferenceOverlay.cs:3859-3872`）。这是 ScriptableObject 运行时数据，静态不可知。

> 结论：图标主要落在 **Skill 页关键词 facet**；Item 页 `ECardTag` chip 多半图标位隐藏。决策 5 的 `display:none` 分支是承重的，不是兜底。

---

## 2 原生图标链 vs UITK（参考实现核查）

```
KeywordIconColorConfiguration.IconName (string, 可空)            // KeywordIconColorConfiguration.cs:40
  → TooltipTypography 拼 "<sprite name=" + IconName + ">"        // TooltipTypography.cs:205-207, 426-475
  → TMP_Text 渲染（spriteAsset 提供图集）                         // TMP 富文本路径
```

青龙做法（`MerchantQuickReferenceOverlay.cs`）：`ResolveSpriteAssetForIcon`（`:4376-4425`）经 `TMP_SpriteAsset.GetSpriteIndexFromName(name) >= 0` 扫 `Resources.FindObjectsOfTypeAll<TMP_Text/TMP_SpriteAsset>` 找到图集，再 `text.spriteAsset = _gameSpriteAsset`（`:4331-4333`）+ `<sprite>` 标签 —— **这是 uGUI/TMP 路径**。

**关键约束：UITK 不解析 TMP `<sprite>` 标签。** 故青龙路径对纯 UITK chip 不可用。要在 UITK chip 前放图标，必须把 `IconName` 解析成真正的 `UnityEngine.Sprite`，赋给子元素 `backgroundImage` —— 即 mod 现成的 `ApplyHeroChipIcon`（`Filters.cs:672-681`）/ `ApplySourceChipIcon`（`:684-698`）的 `new StyleBackground(sprite)` 做法。`TMP_SpriteAsset` 的图标是图集内的 glyph（非独立 Sprite），故须 `Sprite.Create` 在图集子矩形上构造（§4.2、§8.3）。

---

## 3 现状链路（不动的部分）

`NativeTagTypography.Resolve(ECardTag/EHiddenTag/string)`（`NativeTagTypography.cs:59-89`）→ 缓存键 `(typography 实例, languageCode)` → `ResolveUncached`。chip 的可见集合、展开/收起、`TagChipsMatch`/`KeywordChipsMatch` 纯序列比较（`Filters.cs:251-273`）、引擎/过滤逻辑 —— **均不动**。`Unity.TextMeshPro` 已在 `src/BazaarPlusPlus/BazaarPlusPlus.csproj:153-155` 引用，**不改 csproj**。

---

## 4 设计决策

### 4.1 三段链路

| 块 | 文件 | 职责 |
|---|---|---|
| A. 数据 | `NativeTagDisplay` / `NativeTagTypography` | 解析结果带 `IconName`（字符串，locale 无关） |
| B. 图标 seam（新增） | `GameInterop/TagTypography/KeywordIconSpriteProvider.cs` | `IconName → Sprite`：扫 TMP 图集 + 图集抽取 + 按名缓存 + fail-closed |
| C. 渲染 | `Ui/CollectionPanelView.Filters.cs` / `.cs` | TAG/关键词 chip 改 `[图标][文字]`；每次 Refresh 重套图标 |

### 4.2 图标抽取

- 名 → 图集：`_spriteAsset.GetSpriteIndexFromName(name) >= 0` 快路径；未命中再扫 `Resources.FindObjectsOfTypeAll`（青龙先例）。
- 图集 → `Sprite`：`spriteCharacterTable[index].glyph as TMP_SpriteGlyph`，优先 `glyph.sprite`，否则 `Sprite.Create(spriteSheet, glyphRect)`。`Sprite.Create` 不读像素，非可读图集纹理亦可（仅 GPU 采样）。
- 缓存：按 `IconName` 缓存成功的 `Sprite`（**绝不**对同名重复 `Sprite.Create`，防泄漏）；缓存 `_spriteAsset`（一次扫描后 latch，防每帧/每 chip 重扫）。

### 4.3 图标套用位置（核查处置 #3）

`ToggleTagRowExpanded` 不设 text、只改色；展开/收起会因可见集合变化触发 `EnsureTagChips`/`EnsureKeywordChips` **重建 chip**（`TagChipsMatch` 序列变化 → 重建 → 创建路径重套图标）。故图标只需在 **① 创建时（`Ensure*Chips`）+ ② 每次 Refresh 循环（`CollectionPanelView.cs:371-382`）** 套用，**不**改 `RefreshChip`（保持其纯色/选中职责）。

### 4.4 分层与架构测试

图标 seam 在 `GameInterop/TagTypography`（`using TMPro`/`UnityEngine` 合法）；`Game/CollectionPanel` 仅消费 `KeywordIconSpriteProvider.Resolve(string) → Sprite`、`NativeTagDisplay.IconName`（纯字符串），**不**新增 `TheBazaar.*Tooltips`/`TMPro` import —— 既有架构测试 `CollectionPanel_does_not_import_native_tooltip_namespaces` 继续过。

### 4.5 两套缓存的不同生命周期（决策 4）

`NativeTagTypography.Cache` 按 `(typography 实例, languageCode)` 失效（`NativeTagTypography.cs:73-81`）；`KeywordIconSpriteProvider.Cache` **locale 无关、不清** —— 图标不随语言变。两者生命周期刻意不同，已在代码注释声明，属正确设计而非隐患。

---

## 5 自愈与刷新路径（核查处置 #4）

- **每次 Refresh 重套**：`ApplyTagChipContent` 在创建时 + 每个 Refresh 循环都跑 → 晚到的本地化文案、晚到的图集都自动补上（同既有「每次 Refresh 无条件重设文案」机制，`CollectionPanelView.cs:353-355`）。
- **扫描成本受控**：`BeginResolvePass()` 把 `Resources.FindObjectsOfTypeAll` 限为**每次 Refresh 最多一次**（未解析时）；解析成功后走 `GetSpriteIndexFromName` 快路径、不再扫。
- **冷启动自愈**：既有 typography 自愈（`CollectionPanel.cs:368`）在 typography 注册时触发一次 `RefreshView`；TMP 图集与 typography 同属一个资源加载阶段（typography 注册要读 keyword config，config 引用这些图集），那次 `RefreshView` 的扫描即可拿到图集。
- **承认的残余**：若图集严格晚于 typography 加载、且此后用户再无任何交互，图标延到下一次交互（切页/改筛选/重开）才出现 —— 与现有 chip 自愈特性一致，可接受。若实测首屏丢图标，可在 typography 自愈分支补一次延迟重扫（本设计不预置该复杂度）。

---

## 6 代码结构调整

```
src/BazaarPlusPlus/
  Infrastructure/UiTokens/Sizes.cs                 # + TagChipIconSize = 14f
  GameInterop/TagTypography/
    NativeTagDisplay.cs                             # + IconName 字段
    NativeTagTypography.cs                          # ResolveUncached 复制 IconName
    KeywordIconSpriteProvider.cs                    # 新增：IconName -> Sprite
  Game/CollectionPanel/Ui/
    CollectionPanelView.cs                          # Refresh 开头 BeginResolvePass；两循环改 ApplyTagChipContent；+ 2 个常量
    CollectionPanelView.Filters.cs                  # + CreateTagFacetChipButton / ApplyTagChipContent；Ensure* 创建处改调
```

---

## 7 实现步骤

1. `Sizes.cs`：加 `public const float TagChipIconSize = 14f;`。
2. `NativeTagDisplay.cs`：加 `IconName`（见 §9 代码）。
3. `NativeTagTypography.cs`：`ResolveUncached` config 命中时复制 `configuration.IconName`。
4. `KeywordIconSpriteProvider.cs`（新增）：`IconName → Sprite`。
5. `Filters.cs`：新增 `CreateTagFacetChipButton` + `ApplyTagChipContent`；`EnsureTagChips`（`:164-167`）、`EnsureKeywordChips`（`:195-198`）创建处改调新 builder 并套用图标；常量 `TagChipIconName`/`TagChipLabelName` 加在 `CollectionPanelView.cs:65`（`SourceChipInitialsName` 旁）。
6. `CollectionPanelView.cs`：`Refresh` 开头（`:348` 前）调 `KeywordIconSpriteProvider.BeginResolvePass()`；两循环（`:371-382`）`pair.Value.text = display.Label` 换成 `ApplyTagChipContent(...)`。

`_tagMoreButton`（`:173/204`）继续用 `CreateCompactChipButton`（纯文字），不动。

---

## 8 测试与验证

### 8.1 自动化
- `./run.sh build`（worktree 下加 `-p:BPPInstallerSourcePath=<abs>`）。
- `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj`（确认 §4.4 边界）。
- 既有 chip 测试零影响（过滤逻辑层不触文案/图标）。

### 8.2 游戏内验证矩阵

| # | 场景 | 预期 |
|---|---|---|
| 1 | Skill 页关键词 chip | 出现官方图标 + 配色，与原生 tooltip 视觉一致 |
| 2 | Item 页 `ECardTag` chip | 多数无图标（图标位隐藏、布局不塌） |
| 3 | 切 locale | 图标不变（locale 无关）；文案跟随 |
| 4 | 展开/收起标签行 | 图标仍在 |
| 5 | 主菜单冷启动开面板 | 首屏可能无图标，交互后补齐（§5） |
| 6 | 选中态 | 图标不随选中变；金色选中底色与图标共存可读 |

### 8.3 唯一 to-verify（仅实机）
TMP 不在 `decompiled/`，`ExtractSprite` 的精确 API（`spriteCharacterTable[index].glyph as TMP_SpriteGlyph` → `glyph.sprite` / `Sprite.Create(spriteSheet, glyphRect)`）无法静态确认，须实机验证。代码对每步做空值/越界/异常 fail-closed，最坏退化为「无图标纯文字」，不崩。`GetSpriteIndexFromName` 返回 `spriteCharacterTable` 索引（青龙 `:4435` 已用该方法）。

---

## 9 完整实现代码

### A. `NativeTagDisplay.cs`

```csharp
#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.TagTypography;

/// <summary>Resolved native display data for one tag: the locale-correct label, the game's
/// official keyword accent color (null when no keyword config), and the keyword's TMP sprite
/// icon name (empty when the tag has no icon — most ECardTag values).</summary>
internal readonly struct NativeTagDisplay(string label, Color? accentColor, string iconName)
{
    public string Label { get; } = label;

    public Color? AccentColor { get; } = accentColor;

    /// <summary>The keyword config's TMP sprite name, or "" when the tag has no icon.</summary>
    public string IconName { get; } = iconName ?? string.Empty;
}
```

### B. `NativeTagTypography.cs` — 仅改 `ResolveUncached`（91-112）

```csharp
private static NativeTagDisplay ResolveUncached(TooltipTypography? typography, string key)
{
    string label;
    Color? accentColor = null;
    var iconName = string.Empty;

    var configuration = GetConfigurationOrNull(typography, key);
    if (configuration != null)
    {
        label = LocalizeConfiguredText(configuration, key);
        if (configuration.MakeAllUppercase)
            label = label.ToUpperInvariant();
        accentColor = configuration.Color;
        iconName = configuration.IconName ?? string.Empty;
    }
    else
    {
        // No keyword configuration (legal state) — fall back to the game string table.
        label = LocalizeThroughStringTable(key);
    }

    return new NativeTagDisplay(label, accentColor, iconName);
}
```

### B2. `GameInterop/TagTypography/KeywordIconSpriteProvider.cs`（新增）

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.TagTypography;

/// <summary>
/// Resolves a keyword icon name (KeywordIconColorConfiguration.IconName) to a UITK-usable
/// UnityEngine.Sprite. The native tooltip renders these icons as TMP "&lt;sprite name=X&gt;" markup,
/// which UI Toolkit cannot parse — so we locate the TMP_SpriteAsset that owns the glyph and build a
/// Sprite over its atlas sub-rect (prior-art: bazaar-qinglong ResolveSpriteAssetForIcon).
/// Main thread only. Sprites are locale-invariant, so the cache never clears on locale change.
/// Fail-closed: any miss/exception returns null and the chip simply shows no icon.
/// </summary>
internal static class KeywordIconSpriteProvider
{
    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.Ordinal);
    private static TMP_SpriteAsset? _spriteAsset;

    // Bounds the Resources scan to ONCE per Refresh pass while the atlas is still unresolved
    // (without this, every chip with an icon would trigger a full FindObjectsOfTypeAll scan).
    private static bool _scannedThisPass;

    /// <summary>True once a usable TMP sprite atlas has been located.</summary>
    public static bool IsAtlasResolved => _spriteAsset != null;

    /// <summary>Call once at the top of each view refresh. While the atlas is unresolved this
    /// re-arms a single scan attempt for the pass, so a late-loaded atlas is picked up on any
    /// refresh; once resolved it is a no-op (the per-name fast path never rescans).</summary>
    public static void BeginResolvePass() => _scannedThisPass = false;

    public static Sprite? Resolve(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
            return null;
        if (Cache.TryGetValue(iconName, out var cached))
            return cached;

        try
        {
            var asset = ResolveSpriteAsset(iconName);
            if (asset == null)
                return null;

            var sprite = ExtractSprite(asset, iconName);
            if (sprite != null)
                Cache[iconName] = sprite;
            return sprite;
        }
        catch (Exception ex)
        {
            BppLog.Warn("KeywordIconSpriteProvider", $"Icon '{iconName}' failed to resolve: {ex.Message}");
            return null;
        }
    }

    private static TMP_SpriteAsset? ResolveSpriteAsset(string iconName)
    {
        // Fast path: the cached atlas already owns this glyph (no scan).
        if (_spriteAsset != null && _spriteAsset.GetSpriteIndexFromName(iconName) >= 0)
            return _spriteAsset;

        // One scan per refresh pass while unresolved.
        if (_scannedThisPass)
            return null;
        _scannedThisPass = true;

        foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            var asset = text != null ? text.spriteAsset : null;
            if (asset != null && asset.GetSpriteIndexFromName(iconName) >= 0)
                return _spriteAsset = asset;
        }
        foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
        {
            if (asset != null && asset.GetSpriteIndexFromName(iconName) >= 0)
                return _spriteAsset = asset;
        }
        return null;
    }

    // Build a Sprite over the glyph's pixel rect in the atlas texture. A baked Sprite reference is
    // unreliable for atlas-based assets, so we construct one from the glyph rect. Sprite.Create
    // does not read pixels, so a non-readable atlas texture is fine.
    private static Sprite? ExtractSprite(TMP_SpriteAsset asset, string iconName)
    {
        var index = asset.GetSpriteIndexFromName(iconName);
        var table = asset.spriteCharacterTable;
        if (index < 0 || table == null || index >= table.Count)
            return null;

        if (table[index]?.glyph is not TMP_SpriteGlyph glyph)
            return null;

        if (glyph.sprite != null)
            return glyph.sprite; // some assets bake a standalone Sprite

        if (asset.spriteSheet is not Texture2D atlas)
            return null;

        var r = glyph.glyphRect;
        if (r.width <= 0 || r.height <= 0)
            return null;

        return Sprite.Create(
            atlas,
            new Rect(r.x, r.y, r.width, r.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect
        );
    }
}
```

### C. `CollectionPanelView.Filters.cs`

常量（加在 `CollectionPanelView.cs:65` `SourceChipInitialsName` 旁）：

```csharp
private const string TagChipIconName = "bpp-tag-chip-icon";
private const string TagChipLabelName = "bpp-tag-chip-label";
```

builder + 内容套用助手：

```csharp
// Tag/keyword chip = [icon][label] row. Mirrors CreateHeroChipButton/CreateSourceChipButton
// (empty Button.text + child elements). The icon is hidden until a sprite resolves; most ECardTag
// chips have no keyword icon and stay text-only.
private static Button CreateTagFacetChipButton(Action onClick)
{
    var chip = CreateButton(string.Empty, onClick, 0f, Sizes.InfoChipHeight, fixedWidth: false);
    chip.style.minWidth = Sizes.InfoChipMinWidth;
    chip.style.flexDirection = FlexDirection.Row;
    chip.style.alignItems = Align.Center;
    chip.style.justifyContent = Justify.Center;
    UiStyle.HorizontalPadding(chip.style, UiSpacing.Md);
    chip.style.marginRight = UiSpacing.Sm;
    chip.style.marginBottom = UiSpacing.Xs;

    // Neutralize the implicit Button text element so it does not consume row space (queried before
    // our own Label is added, so it cannot match our Label).
    var implicitText = chip.Q<TextElement>();
    if (implicitText != null)
    {
        implicitText.style.flexGrow = 0f;
        implicitText.style.display = DisplayStyle.None;
    }

    var icon = new VisualElement { name = TagChipIconName, pickingMode = PickingMode.Ignore };
    UiStyle.FixedSize(icon.style, Sizes.TagChipIconSize, Sizes.TagChipIconSize);
    icon.style.flexShrink = 0f;
    icon.style.marginRight = UiSpacing.Xs;
    icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
    icon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
    icon.style.display = DisplayStyle.None; // shown only when a sprite resolves
    chip.Add(icon);

    var label = new Label { name = TagChipLabelName, pickingMode = PickingMode.Ignore };
    label.style.fontSize = Sizes.FontSmall;
    label.style.unityFont = BppUiFont.Default;
    label.style.unityFontStyleAndWeight = FontStyle.Normal;
    label.style.unityTextAlign = TextAnchor.MiddleCenter;
    label.style.flexShrink = 0f;
    chip.Add(label);

    StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);
    return chip;
}

// Sets the chip's label text + leading icon. Called at create-time and on every Refresh, so a
// late-localized label or a late-loaded icon atlas both self-correct (same model as the existing
// per-Refresh text reset; see CollectionPanelView.cs:353-355).
private static void ApplyTagChipContent(Button chip, NativeTagDisplay display)
{
    var label = chip.Q<Label>(TagChipLabelName);
    if (label != null)
        label.text = display.Label;

    var icon = chip.Q<VisualElement>(TagChipIconName);
    if (icon == null)
        return;

    var sprite = KeywordIconSpriteProvider.Resolve(display.IconName);
    if (sprite != null)
    {
        icon.style.backgroundImage = new StyleBackground(sprite);
        icon.style.display = DisplayStyle.Flex;
    }
    else
    {
        icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
        icon.style.display = DisplayStyle.None;
    }
}
```

`EnsureTagChips` 创建处（`:164-167`）：

```csharp
var captured = tag;
var chip = CreateTagFacetChipButton(() => _toggleTag(captured));
ApplyTagChipContent(chip, NativeTagTypography.Resolve(captured));
_tagChips[captured] = chip;
_tagChipOrder.Add(captured);
_tagChipRow.Add(chip);
```

`EnsureKeywordChips` 创建处（`:195-198`）同理（`_toggleKeyword(captured)` + `_keywordChips`）。

### C2. `CollectionPanelView.cs` — `Refresh`（324-382）

开头（`:348` `EnsureHeroChips` 前）加：

```csharp
KeywordIconSpriteProvider.BeginResolvePass();
```

两循环（`:371-382`）改为：

```csharp
foreach (var pair in _tagChips)
{
    var display = NativeTagTypography.Resolve(pair.Key);
    ApplyTagChipContent(pair.Value, display);
    RefreshChip(pair.Value, model.SelectedTags.Contains(pair.Key), display.AccentColor);
}
foreach (var pair in _keywordChips)
{
    var display = NativeTagTypography.Resolve(pair.Key);
    ApplyTagChipContent(pair.Value, display);
    RefreshChip(pair.Value, model.SelectedKeywords.Contains(pair.Key), display.AccentColor);
}
```

---

## 10 范围边界（本方案明确不做）

- 不动 `decompiled/`、不改 csproj（TMP 已引用）、不改 `BazaarPlusPlus.csproj` Publicizer。
- 不动 `CollectionFilterEngine` / 过滤逻辑 / `CollectionTagWhitelist` / `CollectionKeywordWhitelist` / 虚拟化全链。
- 不动 More/Less、tier/size/sort chip（继续纯文字 `CreateCompactChipButton`）。
- 不做 `TagFrame` 品质横幅底图（须活体捕获，语义是品质非标签身份）。
- **可选分阶段**：若降风险，先落 chip 结构改造（A + C，图标 provider 暂返回 null 的空壳），实机验证布局无回归后再接 B2 图集抽取。

---

## 11 验证来源

3 组多 agent 对抗式核查（root-cause / native-mechanism / design red-team，§0.1）+ 人工直读核心文件（`NativeTagDisplay`/`NativeTagTypography`/`Filters.cs`/`CollectionPanelView.cs`/`CollectionPanel.cs` 自愈、`decompiled/` 的 `KeywordIconColorConfiguration`/`TagRenderer`/`TooltipTagController`/`TooltipTypography`/青龙 `ResolveSpriteAssetForIcon`）。唯一未能静态确认项：TMP 包 `ExtractSprite` API（§8.3，仅实机可验）。
