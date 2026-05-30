# Collection Panel 设计规格（卡牌图鉴面板）

Status: Draft

> 范围：**仅 Item + Skill 两类卡牌**（原始约 1644 张：Item 1146 + Skill 498；过滤缺图后实际目录约 1571 张，C7）。其余 6 类（EncounterStep / EventEncounter / CombatEncounter / PedestalEncounter / PlayerEffect / SocketEffect）不做。
> 关联：[2026-05-29-historypanel-fullscreen-responsive-design.md](archive/2026-05-29-historypanel-fullscreen-responsive-design.md)（外壳 + overlay 桥接的前身，含 RenderTexture 方案放弃记录）、[adr/0003-history-panel-preview-overlay.md](../adr/0003-history-panel-preview-overlay.md)。

## 动机

把 BazaarDB 式的卡牌数据库搬进游戏：一个全屏黑底面板，网格展示卡牌，顶部筛选栏可按英雄 / 类型 / 稀有度 / 名称筛选（商人维度后续做），悬停出原生 tooltip。从 BPP 设置坞和热键打开。

**核心渲染决策**：不自研扁平 PNG 渲染，而是用卡牌 GUID 实例化**游戏自己的原生卡牌实例**（`CardPreviewBase`，与 HistoryPanel / 商店同一条路径），让卡面、外框、悬停 tooltip 全部由游戏原生提供。datamine 的 `index.json` 只作离线参考；磁盘 PNG 仅作可选兜底。性能靠**虚拟化网格 + 有界原生实例池**解决。

---

## 1. 关键技术结论（全部已逐行核实）

每条都标了出处，`decompiled/` 是游戏 DLL 的只读 ILSpy 反编译（API/行为参考，不可编辑）。

| # | 结论 | 出处 |
|---|---|---|
| C1 | `CardPreviewBase` 是纯 uGUI：卡面 = `RawImage`，外框 = `_frameContainer` 子树。**无 Camera / RenderTexture**，由所在 Canvas 直接绘制 → 可放进屏幕空间网格、可被指针命中。 | `TheBazaar.UI/CardPreviewBase.cs:18-50` |
| C2 | 一个 GUID 贯穿全程：`index.json.id` == `TCardBase.Id` == `GetCardById(Guid)` 入参 == `TCardInstance*.TemplateId`。**直接用 TemplateId，无需任何映射。** | `Cards/TCardBase.cs:11`、`MonsterBoardTooltip.cs:222` |
| C3 | 原生实例化范例：`GetCardById` → 按 `(Size,Type)` 选预制体 → `Instantiate` → `Resize()` → `SetUp(template,false,instance)` → 外层 `Show(true)`。 | `MonsterBoardTooltip.cs:208-314` |
| C4 | 外框免费：`SetUp` 内 `LoadFrame(tier)` 按稀有度从 `CardTierFrameSO`（5 档预制体）取一个塞进 `_frameContainer`。**Item 用方框、Skill 用圆框**，因为 `MonsterBoardTooltip` 持有两套独立引用（`_smallItemReference/_mediumItemReference/_largeItemReference` vs `_skillReference`）。 | `CardPreviewBase.cs:138-164`、`TheBazaar/CardTierFrameSO.cs`、`MonsterBoardTooltip.cs:22-31` |
| C5 | 两条美术管线不同：**Item** 走 `Addressables.LoadAssetAsync<CardAssetDataSO>` 且每次 `new Material`（**绕过 `AssetLoader` 共享缓存**）；**Skill** 走 `AssetLoader.LoadAssetAsyncByAddress<Texture>`（**命中共享缓存**）。`CardPreviewItem.Resize()` 用 `LayoutElement` 定尺寸；`CardPreviewSkill.Resize()` 空操作。 | `CardPreviewItem.cs:31-101`、`CardPreviewSkill.cs:10-28` |
| C6 | 悬停 tooltip 不自动，但接线极廉价：`CardPreviewBase` **无** `IPointerEnterHandler`；`OnHover()/OnHoverOut()` 是 `[UsedImplicitly]` public 方法，需挂中继去调。`OnHover()` 内部已处理屏幕空间判定 + 锁定/次级 tooltip。 | `CardPreviewBase.cs:190-224` |
| C7 | 缺数据 = `ArtKey` 空或 `"Invalid"`（73 张 Item，0 张 Skill），多为 `[DEBUG]`/`[TEMPLATE]`/废弃旧卡。原生 `LoadArt` 对它们 no-op（空白卡面）。**运行时按 `HasValidArtKey()` 过滤即可，无需硬编码 GUID。** | `CardPreviewBase.cs:166-173` |
| C8 | `ECardSize { Small=1, Medium=2, Large=3 }`（棋盘 1/2/3 槽宽，等高）。`ETier { Bronze, Silver, Gold, Diamond, Legendary }` —— **Diamond=3 在 Legendary=4 之前**，稀有度排序须显式定义。 | `Core.Types/ECardSize.cs`、`ETier` |
| C9 | 另一条原生路 `AssetLoader.ConstructAndInstantiateCardVisuals(ITCard,...)` + `ItemVisualsController` 是 **3D 世界空间**（`Renderer`、`dropShadow`、`Update()` 里 billboard 朝向相机），**不适合 2D uGUI 网格**，已排除。 | `AssetLoader.cs:540-585`、`CardFrames/ItemVisualsController.cs:66-78` |
| C10 | 外壳是 UI Toolkit（`PanelSettings.sortingOrder=26`），原生卡在兄弟 `ScreenSpaceOverlay` Canvas（`sortingOrder=27`）+ `RectMask2D`，靠"挖洞 + 像素矩形同步"桥接。RenderTexture 方案 URP 下渲染不出 uGUI，已弃。 | `Ui/HistoryPanelUiToolkitView.cs:96-152`、`Preview/BattleBoardPreview.cs:27-28,238-302` |

---

## 2. 总体架构

### 2.1 复用 vs 新写

| 部件 | 策略 | 参考代码（真实签名） |
|---|---|---|
| 挂载 | 自定义 `CollectionPanelMount`（需订阅 `ChineseLocaleModeChanged` 事件以失效目录缓存，Unmount 时 Dispose；仿 `HistoryPanelMount`） | `BppComposition.cs:92-116`、`Core/Runtime/IBppMountable.cs`、`HistoryPanel/HistoryPanelMount.cs:57-59` |
| 全屏 UITK 外壳（UIDocument/PanelSettings/显隐/Dispose） | 逐字复用 `EnsureCreated` 配方 | `Ui/HistoryPanelUiToolkitView.cs:89-128,162-166,285-297` |
| 「挖洞 + 像素矩形发布」桥接 | 复用 `OnPreviewContainerGeometryChanged`（点→物理像素 + 翻 Y） + `PreviewContainerBoundsChanged` 事件 | `Ui/HistoryPanelUiToolkitView.cs:60,130-152` |
| overlay Canvas（ScreenSpaceOverlay 27 + RectMask2D） | 复用 `BattleBoardPreview` 的 `EnsureInitialized`/`ApplyTransform`/`SetPosition`/`SetClipSize` | `Preview/BattleBoardPreview.cs:212-302` |
| 卡工厂（GUID→原生卡） | 复用 `BattleBoardCardFactory` 反射链，**扩展 Skill 分支** | `Preview/BattleBoardCardFactory.cs` |
| 卡池 | 复用 Take/Return/淘汰形状，**改按 `(type,size)` 分键** + 补 `_skillReference` harvest | `Preview/HistoryPanelPreviewCardPool.cs` |
| 取消保护 | **直接复用**（零 HistoryPanel 耦合，移至 `Core/Runtime/` 或 `Infrastructure/`） | `Preview/HistoryPanelPreviewGenerationGuard.cs` |
| 设计令牌（颜色/尺寸/间距/英雄色/tier 色） | 复用 | `Infrastructure/UiTokens/Colors.cs`、`Sizes.cs`、`Spacing.cs` |
| 设置坞入口 | 克隆 `HistoryPanelSettingsDockEntry`（`ISettingsDockEntry.Build`） | `HistoryPanel/HistoryPanelSettingsDockEntry.cs`、`Game/Settings/BppSettingsDockDefinition.cs` |
| 热键 + Escape + IsInCombat 关闭 | 克隆 `HistoryPanel` 的 static 单例 + `Update` 轮询 | `HistoryPanel/HistoryPanel.cs:19-50` |
| 静态数据访问 | 复用 `BppStaticDataAccess.TryGet()`（返回 `object?`） | `GameInterop/BppStaticDataAccess.cs` |
| 输入硬拦截（可选） | 克隆 `EndOfRunMouseBlocker`（透明 Image + GraphicRaycaster） | `Game/Screenshots/EndOfRunMouseBlocker.cs:54-104` |
| 目录 / 虚拟化器 / 缓存 / 中继 | **新写**（§5–§9） | —— |

> **共享抽象路线图**：首次实现允许克隆，但以下组件应在 Phase 5 或后续重构中提取为共享模块：(1) `GenerationGuard` → 移至 `Core/Runtime/`（已无 HistoryPanel 耦合）；(2) UITK 外壳 PanelSettings 配方 → 提取 `BppUiToolkitShell.CreatePanelSettings(sortingOrder)` 到 `Infrastructure/`；(3) overlay Canvas EnsureInitialized/ApplyTransform → 提取为参数化的 `NativeCardOverlay` 类；(4) 卡工厂反射链 → 提取共享的 `CardPreviewReflection` 模块。
>
> 注：CollectionPanel 需要自定义 `CollectionPanelMount`（而非 `ComponentMount<T>`），因为需要订阅 `ChineseLocaleModeChanged` 事件以在语言切换时失效目录缓存（`CollectionCatalog._cache`），并在 Unmount 时 Dispose 订阅。仿 `HistoryPanelMount.cs:57-59`。

### 2.2 目录结构（建议）

```
Game/CollectionPanel/
  CollectionPanel.cs                    # MonoBehaviour 宿主：static 单例、Initialize、Update 热键/Escape、IsInCombat 关闭
  CollectionPanelSettingsDockEntry.cs   # ISettingsDockEntry
  CollectionPanelText.cs                # 本地化标签/状态文案（仿 HistoryPanelText）
  Data/
    CollectionCatalog.cs                # 枚举 GetCardMap() → 过滤 Item|Skill + HasValidArt → VM 列表（缓存）
    CollectionCardVm.cs                 # 轻量投影（Guid/Type/Size/Tier/Heroes/Tags/DisplayName/ArtKey）
    CollectionFilterState.cs            # 选中筛选条件（纯状态）
    CollectionFilterEngine.cs           # (VM列表 + 条件) → 有序可见集（纯函数，可单测）
  Ui/
    CollectionPanelView.cs              # 外壳 EnsureCreated/SetVisible/Dispose（克隆 HistoryPanelUiToolkitView）
    CollectionPanelView.FilterBar.cs    # 顶部筛选栏
    CollectionPanelView.Grid.cs         # UITK ScrollView 视口：发布像素矩形 + 滚动偏移
  Grid/
    CollectionGridOverlay.cs            # 兄弟 ScreenSpaceOverlay Canvas(27) + RectMask2D（克隆 BattleBoardPreview）
    CollectionGridVirtualizer.cs        # 回收式虚拟化：可见窗口、Take/Return、re-SetUp、anchoredPosition 定位
    CollectionCardPool.cs               # 按 (type,size) 分键的池 + _skillReference harvest
    CollectionCardFactory.cs            # GUID→原生卡（Item/Skill 双路）
    CollectionCardArtCache.cs           # Item CardAssetDataSO 的 LRU 句柄缓存（§7 L2）
    CollectionCardHoverRelay.cs         # 每张活动卡上的指针中继 → OnHover/OnHoverOut
```

### 2.3 挂载与单例（真实签名）

```csharp
// CollectionPanel.cs —— 仿 HistoryPanel.cs:160-188 的 Update 模式 + :384-398 的 DetectSceneChange
internal sealed class CollectionPanel : MonoBehaviour
{
    private static CollectionPanel? _instance;
    private bool _isVisible;
    private IBppConfig _config = null!;
    private string _lastSceneToken = string.Empty;

    public static bool IsVisible => _instance != null && _instance._isVisible;

    public void Initialize(IBppServices services)   // 由 CollectionPanelMount 调用
    {
        _instance = this;
        _config = services.Config;
        _lastSceneToken = GetSceneToken(SceneManager.GetActiveScene());
    }

    public static void OpenFromDockEntry()
    {
        if (_instance == null)
        {
            BppLog.Warn("CollectionPanel", "Dock entry requested while CollectionPanel is unavailable.");
            return;
        }
        // 面板互斥：打开自己前关闭 HistoryPanel
        if (HistoryPanel.IsVisible)
            HistoryPanel.Instance?.CloseFromExternalRequest();
        _instance._isVisible = true;
        _instance.ApplyVisibility();
        _instance.EnsureCatalogAndRefresh();
    }

    private void Update()
    {
        DetectSceneChange();

        if (_isVisible && TheBazaar.Data.IsInCombat) { Close(); return; }

        if (BppHotkeyService.WasPressedThisFrame(_config.CollectionPanelHotkeyPathConfig.Value))
        { Toggle(); return; }

        if (!_isVisible) return;
        if (Keyboard.current is { } kb && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
    }

    // 仿 HistoryPanel.cs:384-398
    private void DetectSceneChange()
    {
        var currentSceneToken = GetSceneToken(SceneManager.GetActiveScene());
        if (string.Equals(currentSceneToken, _lastSceneToken, StringComparison.Ordinal)) return;
        _lastSceneToken = currentSceneToken;
        if (_isVisible) Close();
        DisposeOverlayAndPool();   // Return 所有卡、释放 L2 handle、dispose overlay
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this)) _instance = null;
        DisposeOverlayAndPool();
    }

    private static string GetSceneToken(Scene scene) =>
        $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";
}
```

```csharp
// BppComposition 构造函数里追加（与 :87/:95 同形）：
_settingsDockRegistry.Register(new CollectionPanelSettingsDockEntry());
_mountables.Register(new CollectionPanelMount());  // 自定义 Mount：订阅 locale 变更、Unmount 时 Dispose
```

---

## 3. 数据层

### 3.1 目录来源：运行时静态数据，不是 index.json

渲染必须用 `GetCardById(guid)` 取 `ITCard`（C3），所以目录干脆**也从同一份静态数据枚举**——单一真相源，目录行与渲染输入是同一个对象，不会漂移。`ITCard`（即 `TCardBase`）自带全部筛选字段：`Type/StartingTier/Size/Heroes/Tags/HiddenTags/Localization/ArtKey/InternalName`（`Cards/TCardBase.cs:11-41`）。

`BppStaticDataAccess.TryGet()` 返回装箱的 `JsonGameDataManager`（`object?`，内部已做 `Data.IsManagerCreated()` 就绪门控）。通过反射调 `GetCardMap()`（返回 `Dictionary<Guid, ITCard>`，`JsonGameDataManager.cs`）。

```csharp
// CollectionCatalog.cs —— 一次性构建并缓存
internal sealed class CollectionCatalog
{
    private IReadOnlyList<CollectionCardVm>? _cache;

    public bool TryBuild(out IReadOnlyList<CollectionCardVm> cards)
    {
        if (_cache != null) { cards = _cache; return true; }
        cards = Array.Empty<CollectionCardVm>();

        var manager = BppStaticDataAccess.TryGet();          // null = 静态数据未就绪（§14 R5）
        if (manager == null) return false;

        // 游戏 DLL 已 publicize，直接强转免反射。GetCardMap() 只返回构造时已建完的 Dictionary（§12），
        // 遍历构建 VM 可安全分帧或后台化（见 §12 性能预算）。
        var cardManager = (JsonGameDataManager)manager;
        var map = cardManager.GetCardMap();   // Dictionary<Guid, ITCard>

        var list = new List<CollectionCardVm>(map.Count);
        foreach (var (_, card) in map)
        {
            if (card is not TCardBase c) continue;
            if (c.Type != ECardType.Item && c.Type != ECardType.Skill) continue;  // §1 范围
            if (!HasValidArt(c)) continue;                                         // C7：丢弃缺图
            list.Add(CollectionCardVm.From(c));
        }
        _cache = list;
        cards = list;
        return true;
    }

    // 语言切换时由 CollectionPanelMount 调用
    public void InvalidateCache() => _cache = null;

    // 复刻 CardPreviewBase.HasValidArtKey()（CardPreviewBase.cs:166-173）
    private static bool HasValidArt(TCardBase c) =>
        !string.IsNullOrEmpty(c.ArtKey) && c.ArtKey != "Invalid";
}
```

> `JsonGameDataManager` 游戏 DLL 已 publicize，直接强转 `(JsonGameDataManager)BppStaticDataAccess.TryGet()` 调 `GetCardMap()` 即可，无需 `AccessTools.TypeByName` 反射。`TCardBase`/`ECardType` 在 `BazaarGameShared`（mod 已引用）。

### 3.2 VM 投影

```csharp
// CollectionCardVm.cs —— 不可变投影，筛选/虚拟化器共享引用
internal sealed class CollectionCardVm
{
    public Guid Id { get; init; }
    public ECardType Type { get; init; }          // Item | Skill
    public ECardSize Size { get; init; }          // Item: Small/Medium/Large；Skill 恒 Medium，不参与
    public ETier StartingTier { get; init; }
    public IReadOnlyCollection<EHero> Heroes { get; init; } = Array.Empty<EHero>();
    public IReadOnlyCollection<ECardTag> Tags { get; init; } = Array.Empty<ECardTag>();
    public string DisplayName { get; init; } = string.Empty;   // 当前语言（§10.2），失败回退 InternalName
    public string ArtKey { get; init; } = string.Empty;

    public static CollectionCardVm From(TCardBase c) => new()
    {
        Id = c.Id, Type = c.Type, Size = c.Size, StartingTier = c.StartingTier,
        Heroes = c.Heroes, Tags = c.Tags,
        DisplayName = LocalizationResolver.Resolve(c.Localization) ?? c.InternalName,
        ArtKey = c.ArtKey,
    };
}
```

### 3.3 缺数据处理（C7）

按指示「有缺失直接不管」：`CollectionCatalog` 构建时用 `HasValidArt` 丢弃，缺图卡（73 张 Item，含 `[DEBUG]`/`[TEMPLATE]`）根本不进目录。无需占位图、无需 PNG 兜底、无需硬编码 GUID。

---

## 4. 筛选层

### 4.1 维度与数据源

| 维度 | 来源字段 | 语义 | 备注 |
|---|---|---|---|
| 类型 | `Type` | Item / Skill 切换 | 决定用哪套网格（§8） |
| 英雄 | `Heroes`（`HashSet<EHero>`） | 集合任一命中 | 多英雄零特判；UI 排除占位 `Hero8` |
| 稀有度 | `StartingTier` | 多选 | 排序显式定义（C8） |
| 名称 | `DisplayName` | 包含匹配 | 当前语言 |
| 玩法标签（可选） | `Tags` | 集合任一命中 | 无需新数据 |
| **商人** | —— | §10.3 | **v1 不做**，UI disabled 占位 |

```csharp
// CollectionFilterEngine.cs —— 纯函数
internal static class CollectionFilterEngine
{
    public static List<CollectionCardVm> Apply(IReadOnlyList<CollectionCardVm> all, CollectionFilterState f)
    {
        IEnumerable<CollectionCardVm> q = all.Where(c => c.Type == f.ActiveType);       // 当前 tab
        if (f.Heroes.Count > 0) q = q.Where(c => c.Heroes.Any(f.Heroes.Contains));
        if (f.Tiers.Count  > 0) q = q.Where(c => f.Tiers.Contains(c.StartingTier));
        if (!string.IsNullOrWhiteSpace(f.Search))
            q = q.Where(c => c.DisplayName.IndexOf(f.Search, StringComparison.OrdinalIgnoreCase) >= 0);
        return q.OrderBy(c => TierRank(c.StartingTier))
                .ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
    }

    private static int TierRank(ETier t) => t switch   // 显式稀有度序（C8：Diamond 在 Legendary 之前）
    {
        ETier.Bronze => 0, ETier.Silver => 1, ETier.Gold => 2, ETier.Diamond => 3, ETier.Legendary => 4, _ => 99,
    };
}
```

筛选变化 → `generation.Bump()`（**单次**，不是每张卡 Bump）取消在飞渲染 → 用新可见集重置虚拟化器、滚回顶部。

> ⚠️ **搜索输入必须 debounce**（200–300ms），否则逐字符触发 Apply + 重置虚拟化器。英雄/稀有度 chip 点击无需 debounce。

---

## 5. 渲染层（核心）

### 5.1 三层结构（C10）

```
UITK 外壳 (PanelSettings.sortingOrder = 26)            ← 黑底 + header + 顶部筛选栏 + ScrollView 网格视口
   └─ 网格视口 = UITK ScrollView (overflow:Hidden)      ← content 放一个高度=contentHeight 的 spacer；发布像素矩形 + scrollOffset.y
兄弟 overlay Canvas (ScreenSpaceOverlay, sortingOrder = 27, overrideSorting)   ← 活的原生卡都在这
   └─ boardRoot (RectMask2D 裁到像素矩形)
        └─ N 张 pooled CardPreviewBase（仅可见窗口 + 过扫）
```

因 **UITK VisualElement 不能承载 uGUI RectTransform 子节点**（C1 决定卡是 uGUI），沿用 HistoryPanel 已验证的「挖洞 + 兄弟 overlay」桥接。常量沿用 `BattleBoardPreview`：`OverlaySortingOrder = 27`、`DefaultLayer = 30`（`BattleBoardPreview.cs:27-28`）。如果启用输入硬拦截（§11.3），卡 overlay 提升到 28，拦截层放 27。

> ⚠️ **面板互斥**：CollectionPanel 和 HistoryPanel 共享 sortingOrder 对（UITK=26, overlay=27），**不可同时可见**。打开一个时必须关闭另一个（§2.3）。

### 5.2 滚动与桥接

- **视口**：网格视口用 UITK `ScrollView`（vertical）。其 `contentContainer` 放一个空 spacer，`style.height = contentHeight`（=`totalRows * rowHeight`），使滚动条范围正确。
- **滚动量**：每帧或 `scrollView` 滚动回调里读 `scrollView.scrollOffset.y` → 喂给虚拟化器。
- **像素矩形**：在视口元素的 `GeometryChangedEvent` 里复用 HistoryPanel 的换算（`HistoryPanelUiToolkitView.cs:130-152`），点→物理像素 + 翻 Y：

```csharp
var ppp = _viewport.scaledPixelsPerPoint;
var wb = _viewport.worldBound;
var pixelRect = new Rect(
    Mathf.Round(wb.x * ppp),
    Mathf.Round(Screen.height - wb.yMax * ppp),
    Mathf.Max(1f, Mathf.Round(wb.width * ppp)),
    Mathf.Max(1f, Mathf.Round(wb.height * ppp)));
PreviewContainerBoundsChanged?.Invoke(pixelRect);   // 仿 HistoryPanelUiToolkitView.cs:60,151
```

overlay 侧用 `SetPosition(pixelRect.position)` + `SetClipSize(pixelRect.size)` 摆放 boardRoot（`BattleBoardPreview.cs:64-92`），`RectMask2D` 裁剪溢出。

### 5.3 虚拟化器：回收式（recycler），手动 anchoredPosition 定位

整个方案的心脏。**不使用 `GridLayoutGroup`**——overlay 里自己算坐标：滚动时只改 `anchoredPosition`（极廉价），只有"格子换绑新卡"才 re-`SetUp`（贵，限流）。

约定（boardRoot 左上为原点，y 向下为负）：

```
cols          = clamp(floor((viewportW + gap) / (cellW + gap)), minCols, maxCols)   // §8
rowHeight     = cellH + gap
totalRows     = ceil(visible.Count / cols)
contentHeight = totalRows * rowHeight                  // 写进 ScrollView spacer 高度
scrollY       ∈ [0, max(0, contentHeight - viewportH)] // 来自 ScrollView.scrollOffset.y
firstVisRow   = floor(scrollY / rowHeight)
lastVisRow    = floor((scrollY + viewportH) / rowHeight)
realizedRows  = [firstVisRow - OVERSCAN, lastVisRow + OVERSCAN]   // OVERSCAN = 1~2
```

```csharp
// CollectionGridVirtualizer.cs（精简）
private float _lastScrollY = float.NaN;
private readonly List<int> _recycleKeys = new();   // 预分配，避免每帧 LINQ 分配

public void Tick(float scrollY)
{
    int first = Mathf.Max(0, Mathf.FloorToInt(scrollY / _rowHeight) - Overscan);
    int last  = Mathf.Min(_totalRows - 1, Mathf.FloorToInt((scrollY + _viewportH) / _rowHeight) + Overscan);
    int firstIdx = first * _cols;
    int lastIdx  = Mathf.Min(_visible.Count - 1, (last + 1) * _cols - 1);

    // 1) 回收滚出窗口的格子（预分配 List 收集 key，避免每帧 LINQ 分配）
    _recycleKeys.Clear();
    foreach (var kv in _realized)
        if (kv.Key < firstIdx || kv.Key > lastIdx) _recycleKeys.Add(kv.Key);
    foreach (var idx in _recycleKeys)
    {
        var cell = _realized[idx];
        TryOnHoverOutSafe(cell.Card);                            // §9.3：回收前无条件 OnHoverOut（null-safe）
        if (cell.IsSetUpInFlight)
            cell.MarkPendingReturn();                            // C2 竞态防护：标记 pending-return，Task 完成后真正 Return
        else
            _pool.Return(cell.Card, cell.Vm.Type, cell.Vm.Size);
        _realized.Remove(idx);
    }

    // 2) 为新进窗口的格子取卡
    //    限流：本帧「冷」加载不超过帧时间预算（自适应，默认 3ms；非固定张数）
    float tickStart = Time.realtimeSinceStartup;
    for (int idx = firstIdx; idx <= lastIdx; idx++)
    {
        if (_realized.TryGetValue(idx, out var existing)) continue;  // 已实现，Reposition 见下
        if (IsCold(_visible[idx]) && (Time.realtimeSinceStartup - tickStart) > _coldBudgetMs * 0.001f)
            continue;   // 留到下一帧

        var vm   = _visible[idx];
        var card = _pool.Take(vm.Type, vm.Size, _boardRoot);     // §6.3
        var bindGeneration = ++_perCardGeneration;                // per-card generation（非全局 Bump）
        var task = _factory.Bind(card, vm);                      // §6.2
        AttachHover(card);                                       // §9
        var cell = new RealizedCell(idx, vm, card, task, bindGeneration);
        _realized[idx] = cell;
        ShowWhenReady(cell);                                     // 不阻塞；generation 不匹配则丢弃
    }

    // 3) 仅在 scrollY 变化时重写 anchoredPosition（避免无用 Canvas rebuild）
    // ReSharper disable once CompareOfFloatsByEqualityOperator
    if (scrollY != _lastScrollY)
    {
        _lastScrollY = scrollY;
        foreach (var kv in _realized)
            Reposition(kv.Key, kv.Value);
    }
}

private void Reposition(int idx, RealizedCell cell)
{
    int row = idx / _cols, col = idx % _cols;
    float x =  col * (_cellW + _gap) + _cellW / 2f;
    float y = -((row * _rowHeight) - _lastScrollY) - _cellH / 2f;
    cell.CachedRect.anchoredPosition = new Vector2(x, y);  // RectTransform 在 RealizedCell 创建时缓存
}

// §9.3 null-safe：场景切换/销毁路径中 TooltipParentComponent 可能已拆除
private static void TryOnHoverOutSafe(Component card)
{
    try { card?.OnHoverOut(); } catch { /* swallow NRE from torn-down tooltip system */ }
}
```

**C2 竞态防护：pending-return 机制**

`RealizedCell` 在创建时记录 (bindGeneration, Task)。回收时若 Task 仍 in-flight，不立即 Return 到池，而是标记 `PendingReturn = true`。`ShowWhenReady` 在 Task 完成后检查：
- `bindGeneration` 不匹配当前卡 → 丢弃（旧 SetUp 覆盖新 SetUp 的竞态）
- `PendingReturn == true` → 跳过 Show，直接 Return 到池

这保证旧 SetUp 的 LoadFrame/LoadArt 完成后不会修改已被新 VM 占用的卡。

要点：
- **滚动 = 仅 `Reposition`**（重写 `anchoredPosition`），不碰 SetUp/Addressables；丝滑关键。scrollY 未变时跳过整个循环，避免无用 Canvas rebuild。
- **换绑 = pending-return + `Take` + `Bind` 新**；只在格子 idx 进/出窗口时发生。
- **冷加载限流**：自适应帧时间预算（默认 3ms），而非固定张数。144Hz 下自动收紧，空视口时自动放宽。
- 活实例数恒等于 `realizedRows × cols`（几十张，§12），不膨胀。
- `ShowWhenReady` 使用 **per-card generation**（每次 Bind 递增的计数器），而非全局 `generation.Bump()`。全局 Bump 仅在筛选变化时触发（§4），用于取消整个可见集重算。

### 5.4 尺寸 / 比例（重要：原生比例不可拉伸）

`CardPreviewItem.Resize()`（`CardPreviewItem.cs:31-49`）从**预制体自带 `LayoutElement.preferredWidth/Height`** 设 `sizeDelta`——每种 size 真实比例由游戏写死，三种 size 是三个不同预制体；Skill 的 `Resize()` 空操作（方形）。

> ⚠️ **与需求的事实冲突，需对齐**：需求给的比例（小卡 2:1 / 中卡 1:2 / 大卡 1:3）与原生「等高、宽度按 1/2/3 槽递增」不一致。原生真实比例以 Phase 0 实测为准。处理原则：
> 1. 取卡后调原生 `Resize()` 得原生 `sizeDelta`（保真比例）。
> 2. 网格按"统一行高 `cellH`"布局；每张卡 `localScale = cellH / nativeHeight` 等比缩放塞进格子，**绝不直接改 sizeDelta 去凑需求比例**（会拉伸卡面/材质变形）。
> 3. 格子宽度 = `nativeWidth × localScale`（Large 比 Small 宽）；若要严格等宽网格，小卡居中留白而非拉伸。
> 4. spike 跑完把实测的三组 `(nativeWidth, nativeHeight)` + Skill 方形尺寸回填到本节，并与需求方确认最终视觉契约。

### 5.5 外框（C4，免费）

无需任何外框代码。`SetUp → LoadFrame(tier)` 自动按稀有度取边框预制体；Item 方框、Skill 圆框（两套预制体引用不同）。我们只要：(a) 池按 kind 取对应预制体；(b) 补 `_skillReference` 的反射 harvest（§6.3）。5 档 × 2 形状 = 10 种边框全部白嫖。

---

## 6. 卡工厂与池

### 6.1 实例化序列（照搬 C3）

参考 `MonsterBoardTooltip.AddCard`（`MonsterBoardTooltip.cs:258-313`）与现有 `BattleBoardCardFactory`。注意原生顺序**先 `Resize()` 再 `SetUp()`**，且 `SetUp` 第 3 参（`TCardInstance`）不能为 null（内部读 `.Attributes`/`.Tier`）。

### 6.2 Bind：GUID → 原生卡（Item + Skill 双路）

现有 `BattleBoardCardFactory` 只建 `TCardInstanceItem`。新工厂按类型分支：

> ⚠️ **必须修改 `InvokeSetUpSafe` 签名**：现有 `BattleBoardCardFactory.InvokeSetUpSafe`（`BattleBoardCardFactory.cs:158-161`）第三参数类型是 `TCardInstanceItem`。游戏的 `CardPreviewBase.SetUp` 接受基类 `TCardInstance`，所以反射调用时传 `TCardInstanceSkill` 运行时安全，但 **C# 编译器不允许将 `TCardInstanceSkill` 传给 `TCardInstanceItem` 形参**（两个并列子类型）。CollectionCardFactory 的 `InvokeSetUpSafe` 必须接受 `TCardInstance`（基类），或新增重载。

```csharp
// CollectionCardFactory.cs（扩展自 BattleBoardCardFactory）
private int _instanceCounter;

public Task Bind(Component card, CollectionCardVm vm)
{
    var staticData = BppStaticDataAccess.TryGet();
    var template = HistoryPanelPreviewTemplateLookup.GetCardTemplate(staticData, vm.Id);
    if (template == null) return Task.CompletedTask;

    // 仿 BuildSyntheticInstance 补齐 InstanceId/TemplateVersion，避免下游 NPE
    TCardInstance instance = vm.Type == ECardType.Skill
        ? new TCardInstanceSkill { TemplateId = vm.Id, Tier = vm.StartingTier,
                                   InstanceId = $"bpp-collection-{_instanceCounter++}",
                                   TemplateVersion = string.Empty,
                                   Attributes = new Dictionary<ECardAttributeType,int>() }
        : new TCardInstanceItem  { TemplateId = vm.Id, Tier = vm.StartingTier,
                                   InstanceId = $"bpp-collection-{_instanceCounter++}",
                                   TemplateVersion = string.Empty,
                                   Attributes = new Dictionary<ECardAttributeType,int>() };

    ResizeViaReflection(card);
    return InvokeSetUpSafe(card, template, instance); // 签名改为接受 TCardInstance 基类
}
```

`SetUp` 返回 `System.Threading.Tasks.Task`（项目无 UniTask；现有 `InvokeSetUpSafe` 的 `raw is Task` 判断已验证正确）。

### 6.3 池：按 (type,size) 分键 + 补 skill 引用

照搬 `HistoryPanelPreviewCardPool` 的 Take/Return/淘汰（30/键），但：
1. 键从 `ECardSize` 改为 `(ECardType, ECardSize)`：Item 三个尺寸键 + Skill 一个键（Skill 尺寸恒定）。
2. 反射 harvest 时**额外取 `MonsterBoardTooltip._skillReference`**（现有只取 `_smallItemReference/_mediumItemReference/_largeItemReference`，见 `HistoryPanelPreviewCardPool.cs`）：

```csharp
private static readonly FieldInfo? SkillReferenceField =
    MonsterBoardTooltipType != null ? AccessTools.Field(MonsterBoardTooltipType, "_skillReference") : null;
// Take(type,size)：Skill→SkillPrefab；Item→按 size 取对应 ItemPrefab；按 (type,size) 入/出队
```

预制体引用通过 `Resources.FindObjectsOfTypeAll(MonsterBoardTooltipType)` 取（含未激活对象，不需场景里有活的 tooltip）。**就绪时机风险见 §14 R6**。

---

## 7. 缓存设计（四层）

| 层 | 谁管 | 内容 | 动作 |
|---|---|---|---|
| L0 | 游戏 | `AssetLoader.assetCache/_handleCache/_inflight`（`AssetLoader.cs:154-158`）：**Skill 贴图** + 边框预制体走这里，自动去重缓存 | **啥都不做**（白嫖）。**绝不调 `ReleaseAllCachedAssets()`**（`AssetLoader.cs:939`，会清空全局共享缓存、拔掉别处美术） |
| L1 | 我们 | **实例池**（§6.3）：按 kind 分键的 `CardPreviewBase`，随窗口 Take/Return + re-SetUp | 主缓存。每键硬上限（30/键），超出 Destroy |
| L2 | 我们 | **Item 美术 LRU**：`CardPreviewItem.LoadArt` 直接 `Addressables.LoadAssetAsync<CardAssetDataSO>`，**绕过 L0**（C5），不缓存会全常驻 | 自建 `Dictionary<artKey,(SO,handle,refCount)>` + LRU（保留最近 ~256 distinct）；淘汰前检查 refCount==0 才 `Addressables.Release(handle)`（见下方引用计数说明） |
| L3 | 我们 | **Item Material 共享池**（**Phase 2 同期实现，非可选**）：`UpdateCardImageMaterial` 每次 `new Material`（`CardPreviewItem.cs:51-78`）。40-64 张独立 Material = 40-64 个 draw call（结构性问题，非分配成本），阻止 uGUI 动态合批 | 按 artKey 缓存 `Material`；Harmony patch `UpdateCardImageMaterial` 命中即复用、跳过 new。同 artKey 的卡共享 Material 实例，预计将 draw call 从 40-64 降至 ~15-25 |

```csharp
// CollectionCardArtCache.cs —— L2（引用计数 + O(1) LRU）
internal sealed class CollectionCardArtCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, CacheEntry> _entries = new();
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, LinkedListNode<string>> _nodeMap = new();  // O(1) Touch

    private sealed class CacheEntry
    {
        public AsyncOperationHandle<CardAssetDataSO> Handle;
        public int RefCount;   // 活跃+池中引用此 artKey 的卡数量
    }

    public async Task<CardAssetDataSO?> Get(string artKey)
    {
        if (_entries.TryGetValue(artKey, out var entry)) { Touch(artKey); return entry.Handle.Result; }
        var handle = Addressables.LoadAssetAsync<CardAssetDataSO>(artKey);
        await handle.Task;
        if (handle.Status != AsyncOperationStatus.Succeeded) return null;
        var node = _lru.AddFirst(artKey);
        _nodeMap[artKey] = node;
        _entries[artKey] = new CacheEntry { Handle = handle, RefCount = 0 };
        Evict();
        return handle.Result;
    }

    // 池 Take 时 +1，Return 且清除 Material 后 -1
    public void AddRef(string artKey) { if (_entries.TryGetValue(artKey, out var e)) e.RefCount++; }
    public void Release(string artKey) { if (_entries.TryGetValue(artKey, out var e)) e.RefCount = Math.Max(0, e.RefCount - 1); }

    private void Evict()
    {
        while (_lru.Count > _capacity)
        {
            var key = _lru.Last!.Value;
            if (_entries.TryGetValue(key, out var entry) && entry.RefCount > 0)
                break;   // 尾部仍被引用，停止驱逐
            _lru.RemoveLast(); _nodeMap.Remove(key);
            if (_entries.Remove(key, out var removed)) Addressables.Release(removed.Handle);
        }
    }

    // O(1)：通过 _nodeMap 直接定位 LinkedListNode，无需线性扫描
    private void Touch(string k)
    {
        if (!_nodeMap.TryGetValue(k, out var node)) return;
        _lru.Remove(node); _lru.AddFirst(node);
    }
}
```

> L2/L3 接入点：`LoadArt`/`UpdateCardImageMaterial` 是 `SetUp` 内部调用，干净做法是 **Harmony patch `CardPreviewItem.LoadArt`（或 `UpdateCardImageMaterial`）**，命中缓存就用缓存的 `CardAssetDataSO`/`Material` 跳过原生 Addressables。Skill 不需要 L2/L3（走 L0）。
>
> **L2 引用计数要点**：池 Take 一张 Item 卡时调 `AddRef(artKey)`，Return 时先 Destroy 卡的 Material + 置空 `_cardImage.texture`，然后调 `Release(artKey)`。这保证 LRU 驱逐时 `RefCount==0` 的条目不再有任何活跃 Material 引用其 SO 子资产，Release handle 安全。

**关闭面板**：只释放我们自己的句柄（L2 全 `Release` + 清空、L1 Destroy 超额）；L0 交还游戏。Skill 美术若要回收用 `AssetLoader.ReleasePreviouslyLoadedAsset(artKey)`（`AssetLoader.cs:838`），通常留给 L0 自然管理。

---

## 8. 布局与展示

### 8.1 两套网格（Item / Skill 分 tab）

一个网格只能有一种 cell 比例，而 Item（横版、3 种宽度）与 Skill（方形）形状不同 → **顶部 Item / Skill 切换 tab，各一套网格参数**。游戏本身在 `MonsterBoardTooltip` 里就 item/skill 分开处理（`_sockets` vs `_skillParent`），符合直觉。

### 8.2 响应式列数

外壳 `PanelSettings.ScaleWithScreenSize + match=1`（按高缩放，`HistoryPanelUiToolkitView.cs:98-103`）让所有 px 令牌随屏高缩放——**响应式免费**。列数按视口实测宽算：

```
cols = clamp(floor((viewportW - 2*padding + gap) / (cellW + gap)), minCols, maxCols)
```

建议：Item 6–8 列、Skill 10–14 列（随屏宽 + minCols/maxCols 夹紧）。cell 尺寸作为 px 令牌加进 `Infrastructure/UiTokens/Sizes.cs`（如 `CollectionItemCellHeight`、`CollectionSkillCellSize`、`CollectionGridGap`），数值待 §5.4 spike 实测回填。

### 8.3 cell chrome 与配色

原生卡已含外框 + 卡面，cell 视觉无需额外底。可选叠加（放进 overlay、作为卡的兄弟 RectTransform，避免与 UITK 跨排序系统打架）：
- 多英雄角标（Skill 常多英雄）——小英雄色点，取 `Colors.HeroVanessaBackground / HeroPygmalienBackground / HeroDooleyBackground / HeroMakBackground / HeroJulesBackground / HeroKarnokBackground / HeroStelleBackground`（`Colors.cs:123-131`）。
- 名称标签（`DisplayName`，本地化）。
- 稀有度由原生外框已表达，通常不再加；如需 chip 用 `Colors.RankBronze/Silver/Gold/Diamond*` + `RankLegendaryBackground`（`Colors.cs:112-121`）。

黑底用 `Colors.HistoryPanelBackground`；筛选栏元素样式 / 间距 / 字号统一取 `Colors`/`Sizes`/`UiSpacing`。

---

## 9. 交互：悬停 Tooltip

### 9.1 一行中继（C6）

`OnHover()` 内部已做完屏幕空间判定 + 锁定/次级 tooltip 分支（`CardPreviewBase.cs:190-211`），只需在每张活动卡上挂中继：

```csharp
// CollectionCardHoverRelay.cs
internal sealed class CollectionCardHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private CardPreviewBase? _card;
    public void Bind(CardPreviewBase card) => _card = card;
    public void OnPointerEnter(PointerEventData _) => _card?.OnHover();
    public void OnPointerExit(PointerEventData _)  => _card?.OnHoverOut();
}
```

> `CardPreviewBase` 编译期可见（publicize），可直接强引用；否则反射调 `OnHover`/`OnHoverOut`。

### 9.2 接线前提（缺一不可）

1. overlay Canvas 上加 **`GraphicRaycaster`**（`BattleBoardPreview` 当前没有，因它不需 hover；新面板要加）。范式见 `EndOfRunMouseBlocker.cs:64`。
2. 场景里有且仅有一个 **`EventSystem`**：复用既有的，缺失才创建（多个会互相告警致输入不可预测）。
3. 卡要有**可命中 Graphic**：在每张活动卡盖一个全卡透明 `Image{raycastTarget=true}`（尺寸跟随 `Resize()` 后实际大小），中继挂其上。不要依赖 `_cardImage.raycastTarget`（材质/贴图切换、空白卡时不稳定）。范式见 `EndOfRunMouseBlocker.cs:100-103`。
4. `Data.TooltipParentComponent` 在目标场景存在且未 block，否则 `ShowCardTooltipController` 早返回（§14 R4）。

### 9.3 回收期 tooltip 状态机（必须）

硬规则：**任何卡 `Return` 前无条件 `OnHoverOut()`**（见 §5.3 `Tick`），否则：(a) 卡被回收但指针因瞬移没触发 `OnPointerExit` → tooltip 悬挂指向已回收 transform；(b) 锁定态下 `OnHover` 走次级控制器，回收后次级状态悬挂。换绑前若该卡正 hover，先 `OnHoverOut()` 再 `Bind`。

### 9.4 interop 注意

tooltip 渲染栈是 uGUI（独立于 UITK 外壳），UITK 面板能正常显示它。风险在**输入**：overlay(27) 的 uGUI 命中要能越过 UITK 面板(26) 的 `pickingMode=Position` 拾取（`HistoryPanelUiToolkitView.cs:121`）。两套排序非简单整数比较，**Phase 0 必须实测**。兜底（Plan B）：禁用命中 Image，改在 `Update` 里手动 hit-test——用发布的像素矩形 + `Mouse.current.position.ReadValue()`（新 Input System，与 mod 其他位置一致；**不用 `Input.mousePosition`**，旧 Input Manager 在游戏设为仅新 Input System 时不更新）算命中格 → 调该卡 `OnHover()/OnHoverOut()`。Plan B 不依赖 GraphicRaycaster 与 UITK 排序协调，更稳但更繁琐，需预先写好。

> ⚠️ Plan B 对 Skill 圆形卡使用矩形近似 hit-test，四角会有误触。显式接受此折中（圆形卡四角面积极小，实际影响可忽略）；如需精确，为 Skill 增加圆形变体（检查鼠标到卡中心距离 vs 半径）。

---

## 10. 筛选 UI 与商人

### 10.1 顶部筛选栏

`CollectionPanelView.FilterBar.cs`：英雄（chips 多选，排除 `Hero8`）、稀有度（chips 多选）、名称（UITK `TextField`，**200ms debounce**——逐字符触发 Apply + 虚拟化器重置开销过大）、Item/Skill tab。元素样式复用 `Colors`/`Sizes`/`UiSpacing`。任一变化 → 更新 `CollectionFilterState` → `CollectionFilterEngine.Apply` → `generation.Bump()`（全局单次）→ 重置虚拟化器。

### 10.2 本地化名

`TCardBase.Localization`（`TCardLocalization`）解析为当前 UI 语言（游戏用 `PlayerPreferences.Data.LanguageCode`，见 `BppHotkeyService.cs:189` 的同款用法）。`LocalizationResolver.Resolve` 的访问器待确认（§14 开放问题）；失败回退 `InternalName`。index.json 的 `name.translations`（含 zh-CN 等 7 语言）作离线兜底。

### 10.3 商人筛选（v1 不做，后续推导）

游戏里**没有**静态「商人→卡」表（已核实）：`MerchantSO`/`TMonster` 不含售卖卡；`CardSetPreviewSponsorCatalog` 的 Sponsor 是金主鸣谢（红鲱鱼）；商店出货是运行时 spawner 按 `SpawningFilters`（`CardIdFilters/ItemTierFilters`）+ Hero/Day/Hour 动态生成（mod 的 `ShopForecastLogPatch.cs`）。

v1 在 UI 放 disabled 的「商人（即将到来）」占位。后续做**推导逻辑**（已确认单独做）：枚举每个商人的 spawner `SpawningFilters` 展开 `CardIdFilters` + tier/hero 约束得近似售卖集；或离线挖一份「商人→[guid]」目录随 mod 发布（先例：编译进 DLL 的字典字面量，或 `Data/BuildRecommendations/*.json` 嵌入 JSON）。单列后续设计，不阻塞本面板。

---

## 11. 打开方式

### 11.1 设置坞入口（真实签名，克隆 `HistoryPanelSettingsDockEntry`）

```csharp
// CollectionPanelSettingsDockEntry.cs
internal sealed class CollectionPanelSettingsDockEntry : ISettingsDockEntry
{
    public int Order => 1;   // HistoryPanel 是 0；紧随其后

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "CardCollection",                       // key
            CollectionPanelText.ResolveLabel,       // Func<string,string> resolveLabel
            ResolveStatus,                          // Func<string,string> resolveStatus
            IsActionable,                           // Func<bool> isActive
            CollectionPanel.OpenFromDockEntry,      // Action activate
            collapseAfterActivate: true
        );

    private static string ResolveStatus(string lang) =>
        TheBazaar.Data.IsInCombat ? CollectionPanelText.ResolveInRunStatus(lang)
        : CollectionPanel.IsVisible ? CollectionPanelText.ResolveOpenStatus(lang)
        : CollectionPanelText.ResolveViewStatus(lang);

    private static bool IsActionable() => !TheBazaar.Data.IsInCombat;
}
```

在 `BppComposition` 构造函数 `_settingsDockRegistry.Register(new CollectionPanelSettingsDockEntry());`（仿 `:84-90`）。

### 11.2 热键（复用 `BppHotkeyService`）

`BppHotkeyService` 是 static，`WasPressedThisFrame(string bindingPath)` 接受 InputSystem 绑定路径并内部缓存 `InputAction`（`Game/Input/BppHotkeyService.cs:79-105`）。最干净的接入：在 `BppConfig` 加一个可重绑的 `ConfigEntry<string> CollectionPanelHotkeyPathConfig`（默认 `"<Keyboard>/f9"`，避开 HistoryPanel 用键），`CollectionPanel.Update` 里：

```csharp
if (BppHotkeyService.WasPressedThisFrame(_config.CollectionPanelHotkeyPathConfig.Value))
    Toggle();
```

> 现有 `BppHotkeyActionId` 只有两个 hold 修饰键（`HoldEnchantPreview/HoldUpgradePreview`，`BppHotkeyActionId.cs`），它们用 `IsHeld(actionId)`。toggle 类用 `WasPressedThisFrame(path)` 更合适，无需扩 enum/switch。热键默认值须登记到 `docs/reference/hotkeys-reference.md`。Escape 关闭 + `TheBazaar.Data.IsInCombat` 强关见 §2.3 `Update`（仿 `HistoryPanel.cs:39-50`）。门控复用 `!IsInCombat`。

### 11.3 输入硬拦截（可选）

若要阻止点击/热键漏到游戏，加一个 `EndOfRunMouseBlocker` 式拦截层（`EndOfRunMouseBlocker.cs:54-104`）。**排序层方案**：因 26 和 27 之间无整数间隙，将卡 overlay 提升到 `sortingOrder=28`，拦截层放 `sortingOrder=27`（介于 UITK 面板(26) 和卡(28) 之间），这样拦截层挡背景区但不吞卡的 hover。

---

## 12. 性能预算

| 项 | 目标 / 数字 | 依据 |
|---|---|---|
| 同时存活原生卡 | Item 峰值 ~40–64（6–8 列 × 4–6 行 + 过扫）；Skill 收紧到 ~70 量级 | §5.3 窗口 × 列数 |
| 每键池上限 | 30/键（照搬现有池） | `HistoryPanelPreviewCardPool` |
| 每帧冷加载限流 | 自适应帧时间预算（默认 3ms/帧），而非固定张数。144Hz 下自动收紧，空视口时自动放宽 | 防 Addressables 集中抖动，适应不同刷新率 |
| Item 美术 LRU | 保留最近 ~256 distinct `CardAssetDataSO` | §7 L2 |
| 滚动开销 | 仅 `anchoredPosition` 重写，O(可见格) | §5.3 |

**冷 / 热区分**：限流单位是「本帧冷加载已消耗的墙钟时间」（`Time.realtimeSinceStartup` 差值），而非固定张数。Skill 贴图走 L0 共享缓存，重访同 GUID 几乎瞬时（热），可一次铺满可见窗；Item 首次 Addressables（冷）才严格按时间预算限流。

**首次打开构建目录**：`GetCardMap()` 只是 `return _cards`（`decompiled/JsonGameDataManager.cs:64-66`），返回 `Create()` 时已构建完成的不可变 `Dictionary`。`AsParallel` 反序列化只在 `Create()` 内执行（`ReadAllInParallel`），mod 调用 `GetCardMap()` 时字典已经是安全的只读状态。因此遍历字典 + 构建 VM 列表**可以安全地后台化**（`Task.Run` 或分帧 coroutine），消除首次打开的主线程卡顿。Phase 0 spike 实测耗时以决定是否需要分帧。

---

## 13. 分阶段实施

### Phase 0 — 可行性 spike（闸门，先做，1–2 天）
在游戏里证明五件事全绿，否则各自落兜底。每项有**定量 pass/fail 标准**：

1. **overlay 里原生卡能被指针命中、触发 `OnHover` 出 tooltip**（Item + Skill 各一张）→ pass = tooltip 弹出且内容正确；fail → §9.4 手动 hit-test。
2. **三种 Item size 预制体真实 `(W,H)` 比例** + Skill 方形尺寸 → 回填 §5.4 / §8.2。记录实测 `(nativeWidth, nativeHeight)` 三组 + Skill 尺寸。
3. **`GetCardMap()` 遍历 + VM 构建耗时** → pass = ≤200ms（可分帧）；≤500ms 可接受但需 loading；>500ms 需后台化（§12 确认线程安全）。
4. **帧率**：加到 30/50/80 张读 `BepInEx/LogOutput.log` 帧时间 → pass = ≥45fps@80 卡。
5. **Item Material churn**（profiler）→ 量化 draw call 数。L3 已提升为 Phase 2 必做项，spike 确认 draw call 基线。
6. **`TLocalizableText` → 当前语言字符串的访问器**（Phase 1 阻塞依赖，不可推迟）→ 确认访问器模式（反射 or 游戏工具方法）并产出可工作的 `LocalizationResolver`。

附带确认：`_skillReference` harvest 通、`TCardInstanceSkill` SetUp 通（含 `InstanceId`/`TemplateVersion` 填充）。交付：能 hover 出 tooltip 的原生卡小网格 + 一份实测数字表。**这是 go/no-go。**

### Phase 1 — MVP：单类型静态网格
目录（`CollectionCatalog` 枚举 + 过滤 + VM 缓存）、外壳（克隆 HistoryPanel，黑底 + header + Close + 热键/坞入口）、网格只渲染前 N 张（≈一个过扫窗口）不滚动。交付：能打开、看到一屏 Item、hover 出 tooltip。

### Phase 2 — 虚拟化滚动（核心性能）
`CollectionGridVirtualizer`（§5.3，含 pending-return 竞态防护 + 自适应冷加载预算）+ 池按 kind 分键 + L2 美术 LRU（引用计数）+ **L3 Material 共享池**（draw call 结构性问题，不可推迟）+ per-card generation 取消。加 Skill tab。交付：~1571 张平滑滚动（实测 60fps），Item/Skill 切换。

### Phase 3 — 筛选系统
顶部筛选栏（英雄多选 / 稀有度 / 名称搜索 /（可选）玩法标签），变化触发 Bump + 重算可见集。交付：hero/tier/type/搜索可用。

### Phase 4 — 商人筛选（后续，需新数据）
按 §10.3 做 spawner 推导或离线目录。单列设计。

### Phase 5 — 打磨
可重绑热键、输入硬拦截、art 淡入、滚动惯性、键盘导航、面板开关过渡动画（alpha 淡入）、共享抽象提取（§2.1 路线图）。

---

## 14. 风险与开放问题

| # | 风险 | 缓解 |
|---|---|---|
| R1 | overlay(27) 的 uGUI 指针能否越过 UITK 面板(26) 命中卡（「tooltip 免费」唯一未验证前提） | Phase 0 实测；兜底 §9.4 手动 hit-test（预先写好） |
| R2 | 同时存活卡数 / 帧率上限是设计估计 | Phase 0 用 `LogOutput.log` 定数；撑不住则收紧窗口/列数 |
| R3 | `GetCardMap()` 遍历 + VM 构建耗时 | `GetCardMap()` 只返回已建完的不可变字典（§12），遍历可安全后台化/分帧；Phase 0 实测耗时决定是否需要 loading |
| R4 | `Data.TooltipParentComponent` 在目标场景（主菜单/非战斗）是否就绪且未 block | Phase 0 验；缺失则定位/等待 |
| R5 | 静态数据未就绪（`BppStaticDataAccess.TryGet()==null`） | 打开时重试/禁用，不假设启动即有 |
| R6 | `MonsterBoardTooltip` 预制体就绪时机（`FindObjectsOfTypeAll` 只返回已加载对象） | 延后就绪/兜底来源；Phase 0 确认目标场景能 harvest |
| R7 | Item 比例需求与原生事实冲突（§5.4） | spike 实测原生比例，localScale 适配，回填并与需求方确认契约 |
| R8 | 游戏更新重命名/移除反射目标（`_skillReference`、`GetCardById`、`SetUp` 等） | 所有反射 harvest 加 null 检查 + `BppLog.Warn`；启动时 smoke test 验证关键目标存在；缺失时禁用面板并在设置坞显示「当前游戏版本不兼容」 |
| R9 | 场景切换时面板状态未清理（MonoBehaviour 销毁但 UITK/overlay 清理顺序不确定） | `DetectSceneChange`（仿 `HistoryPanel.cs:384-398`）：场景变化时关闭面板、Return 所有卡、释放 L2 handle、dispose overlay（§2.3） |
| R10 | CollectionPanel 与 HistoryPanel 同时打开导致 overlay z-fight（共享 sortingOrder 27） | 面板互斥：打开一个时关闭另一个（§2.3 `OpenFromDockEntry`）；HistoryPanel 同理需检查 CollectionPanel.IsVisible |

**开放问题**
- 回收换绑到不同卡时是否有旧材质/贴图残留一帧（虽已丢弃缺图卡，仍建议 rebind 时 Destroy Material + 清 `_cardImage.texture`——与 L2 引用计数的 Release 配合）。
- Item 三尺寸在图鉴里保真原生宽度（推荐）还是强行统一——§5.4 待确认。
- 稀有度渲染数值：默认按 `StartingTier`（与别处一致）；tier selector 为后续增强。
- `Hero8` 排除是否硬编码：当前 §10.1 排除 `Hero8`（占位/测试英雄），如果游戏引入第 8 个正式英雄需改为检查是否有关联卡牌。

---

## 15. 源码锚点索引

**游戏侧（`decompiled/`，只读参考）**
- `TheBazaarRuntime/TheBazaar.UI/CardPreviewBase.cs` — `SetUp`/`Show`/`Resize`/`OnHover`/`OnHoverOut`/`LoadFrame`/`HasValidArtKey`/`Size`/`OnDestroy`。
- `TheBazaarRuntime/TheBazaar.UI/CardPreviewItem.cs` — Item 美术（Addressables + per-card Material + Resize 用 LayoutElement）。
- `TheBazaarRuntime/TheBazaar.UI/CardPreviewSkill.cs` — Skill 美术（共享缓存贴图 + Resize 空操作）。
- `TheBazaarRuntime/TheBazaar.UI.Tooltips/MonsterBoardTooltip.cs` — **原生实例化范例 `AddCard`** + 4 个预制体引用 + sockets/skillParent。
- `TheBazaarRuntime/AssetLoader.cs` — `assetCache/_handleCache/_inflight`、`LoadAssetAsyncByAddress`、`ReleasePreviouslyLoadedAsset`、`ReleaseAllCachedAssets`、`ConstructAndInstantiateCardVisuals`（3D，已排除）。
- `TheBazaarRuntime/TheBazaar/CardTierFrameSO.cs` — 5 档边框 `GetAssetReferenceByRarity`。
- `TheBazaarRuntime/TheBazaar.Game.CardFrames/ItemVisualsController.cs` — 3D 收藏卡视觉（已排除，C9）。
- `BazaarGameShared/.../Cards/TCardBase.cs`、`Cards/ITCard.cs` — 全筛选字段。
- `BazaarGameShared/.../Core.Types/ECardSize.cs`、`ETier`、`EHero`、`ECardType`、`ECardTag` — 枚举。

**mod 侧（可复用 / 克隆）**
- `Game/HistoryPanel/Preview/BattleBoardCardFactory.cs` — GUID→SetUp 反射链（扩展 Skill）。
- `Game/HistoryPanel/Preview/HistoryPanelPreviewCardPool.cs` — 池（改 (type,size) 分键 + 补 `_skillReference`）。
- `Game/HistoryPanel/Preview/BattleBoardPreview.cs` — overlay Canvas(27) + RectMask2D + `SetPosition`/`SetClipSize`/`ApplyTransform`。
- `Game/HistoryPanel/Preview/HistoryPanelPreviewGenerationGuard.cs` — 取消保护。
- `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs` — 外壳 `EnsureCreated`(PanelSettings 26) + `OnPreviewContainerGeometryChanged` 点→像素换算 + `PreviewContainerBoundsChanged` 事件。
- `Game/HistoryPanel/HistoryPanel.cs` — static 单例 + `Update` Escape/IsInCombat + `OpenFromDockEntry`/`IsVisible`。
- `Game/HistoryPanel/HistoryPanelSettingsDockEntry.cs` — `ISettingsDockEntry.Build` 模式。
- `BppComposition.cs` — 注册位置（`:84-116`）。
- `Core/Runtime/IBppMountable.cs`、`BppMountableRegistry.cs`、`ComponentMount<T>` — 挂载。
- `Core/Runtime/IBppServices.cs` — 服务聚合（EventBus/Config/Paths/RunContext/GameStateProbe/EncounterState/Logger）。
- `Game/Input/BppHotkeyService.cs`、`BppHotkeyActionId.cs` — 热键。
- `GameInterop/BppStaticDataAccess.cs` — `TryGet()` 静态数据（`object?`）。
- `Infrastructure/UiTokens/Colors.cs`（含 `Hero*Background`/`Rank*`/`HistoryPanelBackground`）、`Sizes.cs`、`Spacing.cs`。
- `Game/Settings/ISettingsDockEntry.cs`、`BppSettingsDockDefinition.cs`。
- `Game/Screenshots/EndOfRunMouseBlocker.cs` — 透明命中 Image + GraphicRaycaster 范式。
- `Patches/`（如 `ShopForecastLogPatch.cs`）— 商人 spawner 出货证据（§10.3 后续）。
