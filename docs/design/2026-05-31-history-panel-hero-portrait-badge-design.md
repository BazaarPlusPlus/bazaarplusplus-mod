# History Panel 英雄头像徽章设计规格（文本 Badge → 真实 Sprite）

Status: Draft

> Supersession note: CollectionPanel is taking the first implementation pass through `GameInterop/HeroPortraits/HeroPortraitSpriteProvider`. When HistoryPanel work starts, reuse that provider and only add HistoryPanel-specific `Label` badge rendering and ListView stale-bind protection here.

> 范围：把 HistoryPanel run 行 / battle 行里那个三字母英雄 **文本徽章**（如 `VAN`/`PYG`，由 `GetHeroBadgeStyle` 生成）换成英雄**真实头像 Sprite**，文本徽章降级为加载失败/无图时的 fallback。**仅 UI 渲染层改动**，不动数据层、不动 SQLite、不动上传。
> 关联：[../features/history-panel.md](../features/history-panel.md)（HistoryPanel 布局 / 行结构）、[archive/2026-05-29-historypanel-fullscreen-responsive-design.md](archive/2026-05-29-historypanel-fullscreen-responsive-design.md)（全屏外壳前身）、[2026-05-31-collection-panel-design.md](2026-05-31-collection-panel-design.md)（同样用「GUID/枚举 → 游戏原生资源」的取图思路）。
> 状态：**Draft，未开工**。本文是「怎么做」的规划；落地或废弃后移入 `archive/` 并加 `Status:` banner。

## 动机

HistoryPanel 现在用一个彩色文本 pill（`VAN`/`PYG`/`DOO`…）表示英雄，信息密度低、辨识靠记忆。游戏自带完整的「英雄 → 头像 Sprite」资源链，且 mod 已在 Combat Replay 用过这条链的前半段。把徽章换成真实头像后，run 列表 / battle 列表一眼可辨英雄，且**不需要任何硬编码 Addressables 路径或 datamine 资源**。

**核心取图决策**：走游戏自己的 `CollectionManager.GetDefaultHeroSkin(EHero)`（同步查表拿 `SkinAssetDataSO`）→ `SkinAssetDataSO.LoadPortraitSpriteAsync()`（异步 Addressables 拿 `Sprite`）。mod 完全不碰具体资源地址（地址藏在序列化的 `AssetReferenceSprite` 字段里，反编译 C# 看不到，但本方案根本不需要它）。

---

## 1. 关键技术结论

`decompiled/` 是游戏 DLL 的只读 ILSpy 反编译（API/行为参考，不可编辑）。下表「核实状态」一栏区分**本设计已逐行核实**与**待本地编译核对**两类。

| # | 结论 | 出处 | 核实状态 |
|---|---|---|---|
| C1 | `CollectionManager.GetDefaultHeroSkin(EHero)` 是**同步**方法，遍历序列化数组 `defaultHeroSkins[]` 按 `hero` 匹配返回 `SkinAssetDataSO`（未配置则 `LogError` 返回 null）。 | `decompiled/TheBazaarRuntime/TheBazaar/CollectionManager.cs:1111` | 已核实 |
| C2 | `SkinAssetDataSO.LoadPortraitSpriteAsync(ct)` 是**异步** `Task<Sprite>`：动画头像(`animatedPortraitPrefabReference` 有效) / `portraitTextureReference` 无效 / `AssetLoader` 缺失 / 加载失败 → **均返回 null**；否则走 `AssetLoader.LoadAssetAsyncByReference<Sprite>` 返回 sprite。 | `decompiled/TheBazaarRuntime/TheBazaar.Assets.Scripts.ScriptableObjectsScripts/SkinAssetDataSO.cs:352-383` | 已核实（见 §2 逐字引用） |
| C3 | 游戏自身就是这样用的：`Services.Get<CollectionManager>().GetDefaultHeroSkin(hero)`。 | `decompiled/TheBazaarRuntime/HeroSelectDisplay.cs:180`、`decompiled/TheBazaarRuntime/TheBazaar.UI.EndOfRun/EndOfRunScreenController.cs:471` | 已核实 |
| C4 | mod 已有「英雄名字符串 → `EHero`」解析范例 `TryParseHeroName`（`Enum.TryParse` + 排除 `EHero.Common`）。HistoryPanel 数据层带的是名字字符串。复用此思路即可，**不新引依赖**。 | `Game/CombatReplay/PlaybackUi/OpponentPortraitController.cs:273`；数据字段 `Game/HistoryPanel/Data/HistoryRunRecord.cs:53`（`Hero`）+ `HistoryBattleRecord.OpponentHero` | 已核实 |
| C5 | 英雄徽章是纯文本 `Label` pill，全 inline style（此 view 无 USS / `AddToClassList`）；`CreateInlinePill` 造 `Label`，固定 20f 高、圆角 `Radii.Md`。 | `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Elements.cs:40` | 已核实 |
| C6 | **两条绑定路径**：run 行在 `BindRunRow` 里**内联**调 `ConfigurePill`（`GetHeroBadgeStyle` 被调 3 次）；battle 行走 helper `BindHeroPill`。两者最终都汇聚到共用的 `ConfigurePill`。 | run：`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Rows.cs:169`；battle：`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.DynamicStyles.cs:58`；共用：`...DynamicStyles.cs:19` | 已核实 |
| C7 | 行是**池化复用**的：两个 `ListView` 只设 `makeItem`+`bindItem`，**无** `unbindItem`/`destroyItem`；`Refresh()` 还会 `Rebuild()`+`RefreshItems()` 强制全量重绑。→ 异步贴图**必须防陈旧**。 | `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs:226` 一带 | 已核实 |
| C8 | 主线程前提：mod 已在主线程设 `_previewImage.image`（`SetPreviewTexture`）。Addressables `await` 的延续在 Unity player loop 主线程恢复，故 `LoadPortraitSpriteAsync` 之后可直接动 `VisualElement.style`。 | `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs:246` | 已核实（机制为 Unity/Mono 通用前提） |
| C9 | sprite 生命周期归游戏 `AssetLoader` 的按-GUID 缓存（`assetCache`），同英雄二次加载基本命中。**mod 不得持有 handle、不得 `Addressables.Release`**，否则会弄花全局共享图。 | `SkinAssetDataSO.cs:372` 内部 `AssetLoader.LoadAssetAsyncByReference<Sprite>` | 结论性推断；`assetCache` 细节待核 |
| C10 | `EHero` 的真实成员名（尤其哪些是「非真实英雄」需排除，如疑似 `Common`/`Hero8`）以枚举定义为准。 | `EHero`（`BazaarGameShared` 域类型，命名空间以 IDE 跳转为准） | **待本地核对** |
| C11 | UI Toolkit 贴图 API（`StyleBackground(Sprite)` 重载 / `IStyle.unityBackgroundScaleMode` / `StyleKeyword.Null` 清图）在编译用的 ref 程序集与运行时是否齐备。 | 编译期决定 | **待本地核对**（备选 `Background.FromSprite`） |

> `OpponentPortraitController.LoadHeroPortraitAsync`（`Game/CombatReplay/PlaybackUi/OpponentPortraitController.cs:218`）走 `BoardBuilder.LoadHeroPortraitAsync`，产出 **3D `EncounterController` prefab** 而非 2D Sprite —— 不要用它做列表徽章；本方案只复用它前半段的 `TryParseHeroName` 思路。

---

## 2. 游戏如何加载英雄头像 Sprite（已核实，逐字引用）

`CollectionManager` 维护序列化的 `EHero → SkinAssetDataSO` 映射并同步查表：

`decompiled/TheBazaarRuntime/TheBazaar/CollectionManager.cs:1111`
```csharp
public SkinAssetDataSO GetDefaultHeroSkin(EHero hero)
{
    for (int i = 0; i < defaultHeroSkins.Length; i++)
        if (defaultHeroSkins[i].hero == hero)
            return defaultHeroSkins[i].skin;
    AppLogger.LogError("DefaultHeroSkins not set in CollectionManager.", ...);
    return null;
}
```

拿到 `SkinAssetDataSO` 后调它自带的异步加载（把 SO 变成 2D `Sprite` 的关键，verbatim）：

`decompiled/TheBazaarRuntime/TheBazaar.Assets.Scripts.ScriptableObjectsScripts/SkinAssetDataSO.cs:352`
```csharp
public async Task<Sprite> LoadPortraitSpriteAsync(CancellationToken cancellationToken = default(CancellationToken))
{
    if (cancellationToken.IsCancellationRequested)
        return null;
    if (animatedPortraitPrefabReference != null && animatedPortraitPrefabReference.RuntimeKeyIsValid())
        return null;                                  // 动画(Spine)头像 → 无静态 sprite
    if (portraitTextureReference == null || !portraitTextureReference.RuntimeKeyIsValid())
    {
        Debug.LogError("Skin asset " + base.name + " has no valid game portrait reference.");
        return null;
    }
    Services.TryGet<AssetLoader>(out var service);
    if (service == null)
        return null;
    Sprite sprite = await service.LoadAssetAsyncByReference<Sprite>(portraitTextureReference);
    if (cancellationToken.IsCancellationRequested)
        return null;
    if (sprite == null)
    {
        Debug.LogError("Skin asset " + base.name + " failed to load game portrait sprite.");
        return null;
    }
    return sprite;
}
```

**同步 vs 异步**：取 `SkinAssetDataSO` 同步（`GetDefaultHeroSkin`），取 `Sprite` 异步（`LoadPortraitSpriteAsync` → Addressables）。必须按异步处理。

> 备选链（本方案**不用**）：profile/career 界面走 `HeroSO.PortraitThumbnail`（`AssetReferenceSprite`）→ `AssetLoader.LoadSpriteAsyncByReference`，但反编译里**不存在**全局 `EHero → HeroSO` 注册表（各界面自己序列化 `HeroSO[]`），mod 拿不到现成 `HeroSO`，故弃用。

---

## 3. 现状：HistoryPanel 徽章如何渲染

**创建**（每个池化行只创建一次）—— `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Elements.cs:40`
```csharp
private static Label CreateInlinePill(VisualElement row, float minWidth)
{
    var pill = CreateLabel(Sizes.FontTiny, FontStyle.Bold, Colors.White);
    pill.style.minWidth = minWidth;
    pill.style.height = Sizes.InlinePillHeight;          // 20f
    UiStyle.HorizontalPadding(pill.style, UiSpacing.Md);
    pill.style.marginRight = UiSpacing.Sm;
    pill.style.unityTextAlign = TextAnchor.MiddleCenter;
    UiStyle.Radius(pill.style, Radii.Md);
    row.Add(pill);
    return pill;
}
```
- run 行：`heroPill` 宽 60f（`SetFixedPillWidth(heroPill, Sizes.RunHeroPillWidth)`），存 `RunRowRefs.HeroPill`（`Label`）。`Rows.cs:34`、`Rows.cs:393`。
- battle 行：`opponentHeroPill` 宽 80f，存 `BattleRowRefs.OpponentHeroPill`。`Rows.cs:256`。

**绑定（两条路径都要改）**：

run 行内联 —— `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Rows.cs:169`
```csharp
ConfigurePill(
    refs.HeroPill,
    GetHeroBadgeStyle(run.Hero).ShortCode,
    GetHeroBadgeStyle(run.Hero).Background,
    GetHeroBadgeStyle(run.Hero).Text,
    true
);
```

battle 行 helper —— `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.DynamicStyles.cs:58`
```csharp
private static void BindHeroPill(Label pill, string? rawHero)
{
    var hero = HistoryPanelFormatter.FormatOpponentHero(rawHero);
    if (string.IsNullOrWhiteSpace(hero))
    {
        ConfigurePill(pill, string.Empty, Colors.Clear, Colors.Clear, false);
        return;
    }
    var heroStyle = GetHeroBadgeStyle(hero);
    ConfigurePill(pill, heroStyle.ShortCode, heroStyle.Background, heroStyle.Text, true);
}
```

两者汇聚到 `ConfigurePill`（`...DynamicStyles.cs:19`），但它**还被 rank/status/progress pill 共用，不能全局改它去贴图**。

---

## 4. 改造方案（step-by-step，带代码）

### 4.1 新增：英雄名 → Sprite 解析器 + 进程级缓存

英雄数量极少（`EHero` 枚举），用永久 `Dictionary` 缓存即可，不做淘汰。null 也缓存（代表「确定无图」），避免重复失败加载。

**新文件 `Game/HistoryPanel/Ui/HistoryHeroPortraitProvider.cs`**
```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;                 // CollectionManager / Services（命名空间以编译为准）
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;   // SkinAssetDataSO
using UnityEngine;
// using <EHero 命名空间>;       // 待 C10 核对

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

/// <summary>
/// 名字字符串 → 英雄头像 Sprite，按 EHero 永久缓存。
/// 走 CollectionManager.GetDefaultHeroSkin -> SkinAssetDataSO.LoadPortraitSpriteAsync,
/// 不依赖任何硬编码 Addressables key。
/// </summary>
internal static class HistoryHeroPortraitProvider
{
    // null = 已尝试且确定无图(动画头像/失败/无此英雄)，避免反复重试。
    private static readonly Dictionary<EHero, Sprite?> _cache = new();
    private static readonly HashSet<EHero> _inFlight = new();

    internal static bool TryGetCached(string? heroName, out Sprite? sprite)
    {
        sprite = null;
        return TryParseHero(heroName, out var hero) && _cache.TryGetValue(hero, out sprite);
    }

    /// <summary>未命中则异步加载；成功/确定失败后回到主线程调 onResolved(可能 null)。仅解析出有效英雄才返回 true。</summary>
    internal static bool TryLoadAsync(string? heroName, Action<Sprite?> onResolved)
    {
        if (!TryParseHero(heroName, out var hero))
            return false;
        if (_cache.TryGetValue(hero, out var cached))
        {
            onResolved(cached);
            return true;
        }
        if (_inFlight.Contains(hero))   // 同英雄加载在途：让它先完成，本次走文本回退
            return false;
        _inFlight.Add(hero);
        _ = LoadAsync(hero, onResolved);
        return true;
    }

    private static async Task LoadAsync(EHero hero, Action<Sprite?> onResolved)
    {
        Sprite? result = null;
        try
        {
            var collectionManager = Services.Get<CollectionManager>();
            if (collectionManager == null)
                BppLog.Warn("HistoryHeroPortrait", "CollectionManager unavailable; using text fallback.");
            else
            {
                SkinAssetDataSO? skin = collectionManager.GetDefaultHeroSkin(hero);
                if (skin == null)
                    BppLog.Warn("HistoryHeroPortrait", $"No default skin for hero={hero}.");
                else
                {
                    result = await skin.LoadPortraitSpriteAsync();   // 内部 await Addressables，延续回主线程
                    if (result == null)
                        BppLog.Debug("HistoryHeroPortrait", $"hero={hero} has no static portrait (animated or load failed).");
                }
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn("HistoryHeroPortrait", $"Portrait load failed for hero={hero}: {ex.Message}");
            result = null;
        }
        finally
        {
            _cache[hero] = result;     // 即使 null 也缓存
            _inFlight.Remove(hero);
        }
        onResolved(result);            // 已在 Unity 主线程
    }

    private static bool TryParseHero(string? heroName, out EHero hero)
    {
        if (!string.IsNullOrWhiteSpace(heroName)
            && Enum.TryParse(heroName.Trim(), ignoreCase: true, out hero)
            && hero != EHero.Common      // 待 C10 核对：排除所有「非真实英雄」枚举值
            && hero != EHero.Hero8)
            return true;
        hero = default;
        return false;
    }
}
```
- `GetDefaultHeroSkin`、`SkinAssetDataSO` 靠 `<PublicizeAll>` 可见 —— 无需碰 `AssetLoader` 的 internal 成员。
- **不** `Addressables.Release`（见 C9）。
- `BppLog.Debug` 仅 Debug build 输出（见 CLAUDE.md「Logs & Debugging」）；若无该重载改 `Info` 或删行。

### 4.2 让 HeroPill 能贴图（保留 `Label` 类型，最小改动）

不要把 `RunRowRefs.HeroPill` / `BattleRowRefs.OpponentHeroPill` 从 `Label` 改成 `Image`（会牵动 refs 构造签名与所有调用点，且文本回退更麻烦）。直接在现有 `Label` 上设 `style.backgroundImage`。

**编辑 `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.DynamicStyles.cs`**（紧跟 `ConfigurePill` 之后）
```csharp
    // 英雄徽章 Label → 方/圆头像。sprite 非空贴图; null 清图并恢复彩色文本 pill。
    private static void ConfigureHeroBadge(Label badge, Sprite? sprite, HeroBadgeStyle fallback)
    {
        UiStyle.FixedSize(badge.style, Sizes.InlinePillHeight, Sizes.InlinePillHeight);   // 正方形
        UiStyle.Radius(badge.style, Sizes.InlinePillHeight / 2f);                          // 圆形
        badge.style.display = DisplayStyle.Flex;

        if (sprite == null)
        {
            badge.style.backgroundImage = new StyleBackground(StyleKeyword.Null);   // 清图
            badge.text = fallback.ShortCode;
            badge.style.backgroundColor = fallback.Background;
            badge.style.color = fallback.Text;
            badge.MarkDirtyRepaint();
            return;
        }

        badge.text = string.Empty;
        badge.style.backgroundColor = Colors.Clear;          // 让 sprite 透出
        badge.style.backgroundImage = new StyleBackground(sprite);
        badge.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;   // 非正方头像铺满圆，溢出被圆角裁掉
        badge.MarkDirtyRepaint();
    }

    private static void HideHeroBadge(Label badge)        // battle 行无对手英雄
    {
        badge.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
        ConfigurePill(badge, string.Empty, Colors.Clear, Colors.Clear, false);
    }
```
- 清图用 `new StyleBackground(StyleKeyword.Null)`，**勿**用 `default(StyleBackground)`（keyword 为 `Undefined`，不可靠）。
- 贴图时 `backgroundColor = Colors.Clear`，否则原 pill 底色压在图边缘。
- `Sizes.InlinePillHeight`(20f) 作边长；嫌小可在 `Infrastructure/UiTokens/Sizes.cs` 加 `HeroAvatarSize` token，**勿改** `InlinePillHeight`（被所有 pill 共用）。
- `UiStyle.FixedSize` 存在性以 `Infrastructure/UiTokens/UiStyle.cs` 为准；无则直接四连赋值 `width/height/minWidth/maxWidth`。

### 4.3 改造 run 行绑定

`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Rows.cs:169-175` 整段替换为：
```csharp
        BindHeroBadge(refs.HeroPill, run.Hero);
```

### 4.4 改造 battle 行绑定

`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.DynamicStyles.cs:58` 的 `BindHeroPill` 整体替换为：
```csharp
    private static void BindHeroPill(Label pill, string? rawHero)
    {
        var hero = HistoryPanelFormatter.FormatOpponentHero(rawHero);
        if (string.IsNullOrWhiteSpace(hero))
        {
            HideHeroBadge(pill);
            return;
        }
        BindHeroBadge(pill, hero);
    }
```

### 4.5 新增统一入口 `BindHeroBadge`（主线程回调 + 陈旧防护）

token 写到 `pill.userData`；先同步画文本/缓存头像（不闪空）；异步完成回调里比对 token 才贴图。

**编辑 `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.DynamicStyles.cs`**（放在 `ConfigureHeroBadge` 附近）
```csharp
    // run + battle 共用。heroName 已是规整显示名(如 "Vanessa")。
    private static void BindHeroBadge(Label badge, string? heroName)
    {
        var fallback = GetHeroBadgeStyle(heroName);   // 文本短码 + 配色, 一次解析(修掉原 run 路径调 3 次)
        var token = heroName ?? string.Empty;
        badge.userData = token;                       // 防止异步头像画到被回收成别英雄的行

        if (HistoryHeroPortraitProvider.TryGetCached(heroName, out var cached))   // 命中(含 null) → 立即定型
        {
            ConfigureHeroBadge(badge, cached, fallback);
            return;
        }

        ConfigureHeroBadge(badge, sprite: null, fallback);   // 先画文本回退, 不闪空

        HistoryHeroPortraitProvider.TryLoadAsync(heroName, sprite =>
        {
            if (!Equals(badge.userData, token))   // 陈旧防护: 行仍是同一英雄才贴
                return;
            ConfigureHeroBadge(badge, sprite, fallback);
        });
    }
```
`BindHeroBadge` 为 `static`，`HistoryHeroPortraitProvider` 静态无状态 → 无需往 `HistoryPanelUiToolkitView` 构造注入任何东西。

### 4.6 线程：为何不需要额外 dispatcher

`LoadPortraitSpriteAsync` 内部 `await AssetLoader.LoadAssetAsyncByReference<Sprite>`（底层 Addressables `AsyncOperationHandle.Task`），在 Unity/Mono 里延续在主线程 player loop 恢复，故 `onResolved` 已在主线程，可直接动 `style`（同 `SetPreviewTexture` 前提，C8）。若想更保守可改用 `HistoryPanel`(MonoBehaviour) 的 `StartCoroutine` + 生成计数模式（`Game/HistoryPanel/HistoryPanel.cs:303`），但 `userData` token 已足够防陈旧。

---

## 5. 注意事项 / 坑

1. **两条绑定路径都要改**（C6）：只改一处会漏掉另一个徽章。
2. **别改 `ConfigurePill` 本体**：它被 rank/status/progress 共用；头像逻辑走独立的 `ConfigureHeroBadge`。
3. **池化无 `unbindItem` → 必须 token 防陈旧**（C7）：否则慢加载完成时把头像画到错误的行。
4. **`LoadPortraitSpriteAsync` 会返回 null**（C2）：动画头像/无效引用/加载失败。必须当「回退到文本徽章」处理。
5. **null 也要缓存**：否则没图的英雄每次绑定都重发一次失败加载。
6. **不要 `Addressables.Release`**（C9）：sprite 由游戏 `AssetLoader` 持有，mod 释放会弄花全局共享图。
7. **贴图时 `backgroundColor = Colors.Clear`**，回退时恢复 `fallback.Background`。
8. **清图用 `StyleKeyword.Null`**，非 `default(StyleBackground)`。
9. **版本特定 API**（C11）：`StyleBackground(Sprite)` 重载报错则退用 `Background.FromSprite(sprite)`。
10. **publicizer 依赖**：靠 `<PublicizeAll>`，无需改 csproj。
11. **`EHero` 成员名以枚举为准**（C10）：核对并排除所有「非真实英雄」值。
12. **形状取舍**：当前用正方形+圆角=圆形头像，覆盖原 60f/80f 宽矩形；想保持矩形则用 `ScaleMode.ScaleToFit` 且不设圆角。

---

## 6. 验证

**构建**（targeted code change，按项目规则跑最小相关构建即可，无需 `BuildAll`）：
```powershell
dotnet build BazaarPlusPlus.csproj
```
Debug 构建自动拷 dll 到 `BepInEx/plugins/`（探测不到游戏路径则显式传 `-p:ManagedPath="...\TheBazaar_Data\Managed"`）。

无现成自动化测试 seam 覆盖此 UI 改动，按项目规则可不新增「仅证明覆盖率」的测试。

**运行期日志**（`<GameDir>\BepInEx\LogOutput.log`，找 `[BPP][HistoryHeroPortrait]`）：
- 正常：通常无警告 —— 头像静默贴上。
- `CollectionManager unavailable; using text fallback.` → 打开时游戏态未就绪，回退文本（不崩）。
- `No default skin for hero=X.` → `defaultHeroSkins[]` 未填或无默认皮肤，回退文本。
- `hero=X has no static portrait ...`（Debug only）→ 动画头像/加载失败，按设计回退文本。
- `Portrait load failed for hero=X: <msg>` → 加载异常，回退文本，不应导致面板崩溃。

**目视**：HistoryPanel run 列表行显示圆形英雄头像（不再是 `VAN`/`PYG`）；快速滚动**不应**出现头像错位（串行）；battle 视图对手英雄徽章也是头像；无对手英雄时徽章隐藏。

---

## 7. 文件清单

| 操作 | 文件 | 内容 |
|---|---|---|
| 新增 | `Game/HistoryPanel/Ui/HistoryHeroPortraitProvider.cs` | §4.1 名字→Sprite 解析 + 缓存 |
| 编辑 | `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.DynamicStyles.cs` | §4.2 `ConfigureHeroBadge`/`HideHeroBadge`、§4.4 改 `BindHeroPill`、§4.5 新增 `BindHeroBadge` |
| 编辑 | `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Rows.cs:169` | §4.3 替换为 `BindHeroBadge(refs.HeroPill, run.Hero)` |
| 可选 | `Infrastructure/UiTokens/Sizes.cs` | 若加专门的 `HeroAvatarSize` token |

**开工前先解决的两处待核对**：C10（`EHero` 真实成员名）、C11（`StyleBackground(Sprite)` 重载可用性）。
