# CollectionPanel「天数 (Day)」筛选 设计

> **Status:** ✅ 已实现（代码 + exe-runner 单测 + Debug 构建通过）。**UI 于 2026-06-05 改版**：天数选择器 → 顶部紧凑「天数」数字 icon（见下「修订」；本文「设计详述 §5–§7」为原选择器方案，作历史保留）。待游戏内手测验证，验证后物理移入 `archive/`。

**Goal:** 在 CollectionPanel 增加一个**仅对局内 (in-run) 生效**的「天数 / Day」筛选维度。它把当前（或用户所选）天数翻译成一个**等级上限**，只保留 `StartingTier ≤ 上限` 的卡，从而回答「这一天我可能被开出哪些卡」。复用面板已有的「打开时读取英雄 / 商人运行态」机制来默认选中当前天。

**Tech Stack:** C# 12 / netstandard2.1、Unity UI Toolkit（UITK chips）、`BazaarGameShared` 的 `ETier`/`EHero`、`TheBazaar.Data.Run`、BepInEx `BppLog`、`Infrastructure/UiTokens`。纯过滤逻辑由 exe-runner 单测 `tests/CollectionFilterEngine.Tests` 覆盖。

---

## 已确认决策

| # | 决策点 | 选定 |
|---|---|---|
| 1 | Day→等级 数据来源 | **硬编码阈值**（非运行时读游戏表）：Day 1 = 青铜 / 2–5 = +白银 / 6–7 = +黄金 / 8+ = +钻石 |
| 2 | 交互形态 | ~~可选天数选择器，默认 = 当前天（仅对局内显示）~~ → **2026-06-05 改版：顶部紧凑「天数」数字 icon**（面显示当前运行天 / 局外默认 20；点击切换是否参与筛选，亮起=参与） |
| 3 | 与现有「品质/等级 Tier」筛选关系 | **独立 AND**，互不改写 |

> **修订（2026-06-05）：天数选择器 → 顶部紧凑「天数」数字 icon。** 落地后反馈无需逐天可选，遂移除 1–15 天数芯片选择器，改为顶部控制行（紧邻「包裹」）的紧凑「天数」**数字 icon**（窄按钮，面上直接显示天数）：
> - **面显示的数字** = 当前运行天；局外（或读不到 `Data.Run.Day`）显示 `DayTierSchedule.OutOfRunDay = 20`（落在钻石档 ⇒ 开启也不收窄）。
> - **点击切换是否参与筛选**：亮起（金色）= 按该天筛选，暗（chip 底）= 不按天。**默认关闭**；on/off **跨开面板保留**（同包裹），天数值每次打开按运行态刷新（`ApplyOpenSelection` 重钉）；始终显示。
> - 数字 icon 比原 label+switch 形态更窄，修复了控制行被挤换行（包裹掉到第二行）的问题。
> - **过滤语义不变**：仍是 `SelectedRunDay → CeilingTier → StartingTier ≤ 上限` 的独立 AND；`CollectionFilterEngine`/`DayTierSchedule.CeilingTier`/`AllowsStartingTier` 与 exe-runner 单测**原样保留**，仅 UI/state 装配改动。
> - **增删**：移除 `EnsureDayChips`/`_dayChips`/Tree 天数区段、`AvailableDays`/`ShowDayFilter`、`BuildDayRange`、`DefaultMaxPickerDay`、`Sizes.DayChipWidth`；新增 `DayTierSchedule.OutOfRunDay`、`Sizes.DayIconWidth`、`CreateDayToggleButton`/`RefreshDayToggle`、VM `DayFilterActive` + `DayFilterValue`。

---

## Current Source Facts（已核对，`file:line`）

过滤管线是 *state → 纯函数 → VM → chips* 的线性结构，新增一个维度是同构扩展：

- **过滤状态**：`CollectionFilterState`（[Game/CollectionPanel/Data/CollectionFilterState.cs:16](../../Game/CollectionPanel/Data/CollectionFilterState.cs#L16)）持有各筛选集合（`Heroes`/`Tiers`/`Sizes`/…）。
- **纯过滤**：`CollectionFilterEngine.Apply`（[CollectionFilterEngine.cs:15](../../Game/CollectionPanel/Data/CollectionFilterEngine.cs#L15)），循环内每个维度一条 `continue` 谓词，第 35–53 行；**现有 Tier 谓词在第 45 行** `if (tierFilterCount > 0 && !filter.Tiers.Contains(card.StartingTier)) continue;`。
- **卡牌已暴露 `StartingTier`**：`CollectionCardVm.StartingTier`（[CollectionCardVm.cs:17](../../Game/CollectionPanel/Data/CollectionCardVm.cs#L17)），由 `From()` 从 `template.StartingTier` 填充（[CollectionCardVm.From.cs:25](../../Game/CollectionPanel/Data/CollectionCardVm.From.cs#L25)）。⇒ **天数维度无需任何 catalog / 卡牌数据改动。**
- **等级 enum**：`ETier { Bronze, Silver, Gold, Diamond, Legendary }`（`decompiled/BazaarGameShared/…/ETier.cs`，值 0–4）。`TierRank` 抽象出排序序，过滤/排序都不依赖 enum 整数值（`CollectionCardFacetRanks`）。
- **运行态读取**已在面板打开路径里：`ResolveOpenSelection()`（[CollectionPanel.cs:122](../../Game/CollectionPanel/CollectionPanel.cs#L122)）→ `IsInGameRunForOpen()`（[CollectionPanel.cs:157](../../Game/CollectionPanel/CollectionPanel.cs#L157)）判是否对局内；英雄读 `TheBazaar.Data.Run?.Player?.Hero`（`TryReadCurrentHero()`，[CollectionPanel.cs:175](../../Game/CollectionPanel/CollectionPanel.cs#L175)）。**天数是同根兄弟字段** `TheBazaar.Data.Run?.Day`（`Run.Day : uint`，默认 1，`decompiled/BazaarGameClient/…/Run.cs:18`）。
- **toggle 装配**：`EnsureView()`（[CollectionPanel.cs:407](../../Game/CollectionPanel/CollectionPanel.cs#L407)）把各 toggle 回调注入 `CollectionPanelView` 构造器；`toggleTier` 在第 433–440 行：改 `_filter` → `_scrollY=0` → `ApplyFilters()` → `RefreshView()`。
- **VM 装配**：`RefreshView()`（[CollectionPanel.cs:689](../../Game/CollectionPanel/CollectionPanel.cs#L689)）构造 `CollectionPanelViewModel`（[Ui/CollectionPanelView.cs:17](../../Game/CollectionPanel/Ui/CollectionPanelView.cs#L17)）。
- **条件显隐先例**：`Refresh()` 里 Size 行仅在 Item tab 可见、Source 行按数量显隐（[Ui/CollectionPanelView.cs:326](../../Game/CollectionPanel/Ui/CollectionPanelView.cs#L326)–337）。**Day 行的「仅对局内显示」复用此 `style.display` 模式。**
- **chips 模式**：`CreateFilterSection`（默认 `chipRow.flexWrap = Wrap`，[Ui/CollectionPanelView.Tree.cs:229](../../Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs#L229)）；Tier section 在 [Tree.cs:126](../../Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs#L126)；`EnsureTierChips`/`TierChipsMatch`（[Ui/CollectionPanelView.Filters.cs:32](../../Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs#L32)）；`CreateChipButton`（[Filters.cs:216](../../Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs#L216)）；`RefreshChip`（[Filters.cs:494](../../Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs#L494)）。
- **本地化**：`CollectionPanelText`（[CollectionPanelText.cs:11](../../Game/CollectionPanel/CollectionPanelText.cs#L11)）；header 用 `LocalizedTextSet`（如 `TierHeader`，第 32–37 行）；数字无需逐项本地化。
- **单测**：`tests/CollectionFilterEngine.Tests` 是 **exe-runner**（`OutputType=Exe`，`dotnet run` 执行），用 `Card(name, tier, …)` helper 直接造 `CollectionCardVm`（[Program.cs:573](../../tests/CollectionFilterEngine.Tests/Program.cs#L573)）。它**逐文件 `<Compile Include>`** 引擎依赖（[csproj:22](../../tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj#L22)–66）。

---

## 核心语义

把「天数」映射为一个**等级上限 (ceiling)**，保留 `StartingTier ≤ ceiling` 的卡：

| Day | Ceiling | 保留的 StartingTier |
|---|---|---|
| 1 | Bronze | 青铜 |
| 2–5 | Silver | 青铜、白银 |
| 6–7 | Gold | 青铜、白银、黄金 |
| 8+ | Diamond | 青铜、白银、黄金、钻石、传说\* |

**为什么用 `StartingTier` 而非「卡的全部可用 tier 集」**：在 The Bazaar，一张卡从其 `StartingTier` 起才会进入商人出价池。`StartingTier=Gold` 的卡在黄金未解锁前（< Day 6）不出现；`StartingTier=Bronze` 的卡从 Day 1 起即可出现（之后随天数升阶，但「是否出现」只取决于起始档）。因此 `StartingTier ≤ ceiling(day)` 精确等价于「该天可被开出」，且 `StartingTier` 已在 VM 上、零数据改动。

**\* Legendary 边界**：`ETier` 中 `Legendary` 序高于 `Diamond`；游戏在 tier 查表里将 `Legendary→Diamond` 别名化（`TCardItem.TryGetTierTemplate`）。本设计在 Day 8+（Diamond 上限）把 `StartingTier=Legendary` 的卡**视作 Diamond 一并显示**，避免「最高天反而隐藏某些卡」的反直觉行为。

**独立 AND**：天数谓词与 Hero/Tier/Size/Tag/商人 offer-pool 谓词并列做 AND（同 `Apply` 循环里其它 `continue`）。它与手动 Tier 行在等级维度可能重叠，这是有意接受的代价，换取实现干净、行为可预测、且不改写用户手动的 Tier 选择。

---

## 设计详述

### 1. 数据来源 — 新文件 `Game/CollectionPanel/Data/DayTierSchedule.cs`

单一事实来源；阈值集中在一处，便于平衡补丁后维护。

```csharp
#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// 硬编码近似游戏的「每天各等级概率表」(tierManager.json，由 StaticDataTierRepository 加载、
// BazaarCardDealer.GetProbabilitiesByDay 消费)。游戏真实表是数据驱动且随版本变动的；
// 本表是 mod 侧的稳定近似，平衡补丁后需回看。
internal static class DayTierSchedule
{
    // 天数选择器展示的默认上限（与游戏 NumDays 默认一致）。currentDay 超过时按 currentDay 扩展。
    public const int DefaultMaxPickerDay = 10;

    public static ETier CeilingTier(int day) => day switch
    {
        <= 1 => ETier.Bronze,   // Day 1
        <= 5 => ETier.Silver,   // Day 2–5
        <= 7 => ETier.Gold,     // Day 6–7
        _    => ETier.Diamond,  // Day 8+
    };

    // 该天可出现 ⇔ StartingTier ≤ 当天上限。Legendary 视作 Diamond（与游戏别名一致）。
    public static bool AllowsStartingTier(ETier startingTier, int day)
    {
        var effective = startingTier == ETier.Legendary ? ETier.Diamond : startingTier;
        return CollectionCardFacetRanks.TierRank(effective)
            <= CollectionCardFacetRanks.TierRank(CeilingTier(day));
    }
}
```

> **构建陷阱（必做）**：`DayTierSchedule.cs` 被 `CollectionFilterEngine` 引用 ⇒ 必须在 `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj` 加一条 `<Compile Include="..\..\Game\CollectionPanel\Data\DayTierSchedule.cs" Link="DayTierSchedule.cs" />`，否则 exe-runner 单测编译失败（同 `CollectionCardFacetRanks.cs` 等既有 include，[csproj:39](../../tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj#L39)）。

### 2. 过滤状态 — `CollectionFilterState.cs`

新增一个字段，`null` = 不过滤天数（非对局 / 用户取消）：

```csharp
// 用户当前所选的「天数」过滤；null 表示不按天数过滤。仅对局内有意义，由面板打开时按
// Data.Run.Day 默认填入。不参与 CollectionPanelSelectionState 的跨会话 round-trip。
public int? SelectedRunDay { get; set; }
```

### 3. 过滤引擎 — `CollectionFilterEngine.Apply`

循环前取一次，循环内加一条 AND 谓词（紧邻第 45 行的 Tier 谓词）：

```csharp
// 循环前，与 tierFilterCount 等并列：
var dayFilter = filter.SelectedRunDay;   // int?

// 循环内，card.Type 已确认后：
if (dayFilter is int day
    && !DayTierSchedule.AllowsStartingTier(card.StartingTier, day))
    continue;
```

（`AllowsStartingTier` 内部是常数级 switch + 两次 `TierRank`，与现有 `Contains` 谓词同量级；不另做提前 hoist。）

### 4. 运行态 + 当前天读取（复用「读英雄/商人」方案）

面板打开时（天数在面板开着时不会变，捕获一次即可）：

- `CollectionPanel` 新增字段：`private bool _isInGameRun;`、`private int? _currentRunDay;`
- 新增私有方法，镜像 `TryReadCurrentHero()`（[CollectionPanel.cs:171](../../Game/CollectionPanel/CollectionPanel.cs#L171)）：

```csharp
private static int? TryReadCurrentDay()
{
    try { return (int?)TheBazaar.Data.Run?.Day; }
    catch (Exception ex)
    {
        BppLog.Warn("CollectionPanel", $"Open selection day read failed: {ex.Message}");
        return null;
    }
}
```

- 在 `ResolveOpenSelection()`（已读 `isInGameRun`，[L122](../../Game/CollectionPanel/CollectionPanel.cs#L122)）末尾记录：
  ```csharp
  _isInGameRun = isInGameRun;
  _currentRunDay = isInGameRun ? TryReadCurrentDay() : null;
  ```
- **默认 = 当前天**：在 `Open(selection)`（应用 selection 到 `_filter` 处，[L204](../../Game/CollectionPanel/CollectionPanel.cs#L204)）设 `_filter.SelectedRunDay = _currentRunDay;`（非对局即 `null`）。

> 不入 `CollectionPanelSelectionState`（该 DTO 仅 round-trip 英雄+来源用于重开，[CollectionPanel.cs:117](../../Game/CollectionPanel/CollectionPanel.cs#L117)）；天数每次开面板按运行态重算、不持久化。

### 5. ViewModel — `CollectionPanelViewModel`

新增 3 个字段：

```csharp
public bool ShowDayFilter { get; set; }                 // = _isInGameRun
public IReadOnlyList<int> AvailableDays { get; set; } = Array.Empty<int>();
public int? SelectedRunDay { get; set; }
```

`RefreshView()` 装配：

```csharp
ShowDayFilter = _isInGameRun,
AvailableDays = BuildDayRange(_currentRunDay),   // 1..Max(DefaultMaxPickerDay, currentDay)
SelectedRunDay = _filter.SelectedRunDay,
```

`BuildDayRange` 为 `CollectionPanel` 私有静态：`var max = Math.Max(DayTierSchedule.DefaultMaxPickerDay, _currentRunDay ?? 0); return [1..max]`。

### 6. UI — Tree / Filters / View

- **`CollectionPanelView.Tree.cs`**：在 **Tier section 之上**（聚拢「运行上下文」并让「天数→收窄等级」的关系就近可读）新建 Day section：
  ```csharp
  _dayFilterSection = CreateFilterSection(rail, CollectionPanelText.DayHeader(), UiSpacing.Lg, out _dayChipRow);
  // 不覆写 flexWrap ⇒ 默认 Wrap，数字芯片自动换行（Tier/Size 行才覆写为 NoWrap）。
  ```
- **`CollectionPanelView.Filters.cs`**：加 `EnsureDayChips(IReadOnlyList<int>)` + `_dayChips : Dictionary<int,Button>` + `DayChipsMatch`/清理，镜像 `EnsureTierChips`/`TierChipsMatch`（[Filters.cs:32](../../Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs#L32)）。芯片文本即天数数字，单选，窄固定宽（新增 `Sizes.DayChipWidth ≈ 40f`，或复用既有小宽 token）。
- **`CollectionPanelView.cs`**：
  - 字段：`_dayChipRow`、`_dayFilterSection`、`_dayChips`。
  - 构造器加 `Action<int> toggleDay`（→ `_toggleDay`），与现有 toggle 同位置注入（[ctor L123](../../Game/CollectionPanel/Ui/CollectionPanelView.cs#L123)）。
  - `Refresh()` 末尾：
    ```csharp
    EnsureDayChips(model.AvailableDays);
    foreach (var p in _dayChips)
        RefreshChip(p.Value, model.SelectedRunDay == p.Key);
    if (_dayFilterSection != null)
        _dayFilterSection.style.display = model.ShowDayFilter ? DisplayStyle.Flex : DisplayStyle.None;
    ```
  - `EnsureCreated()` 字体预载串（[L172](../../Game/CollectionPanel/Ui/CollectionPanelView.cs#L172)）追加 `+ CollectionPanelText.DayHeader()`（数字 0–9 已在既有预载串内）。
- **`CollectionPanelText.cs`**：加 `DayHeaderText = new("Day", "天数", "天數", "天數")` + `internal static string DayHeader() => Resolve(DayHeaderText);`。

### 7. 装配 + 交互 — `EnsureView()`

加 `toggleDay` 回调，与 `toggleTier`（[L433](../../Game/CollectionPanel/CollectionPanel.cs#L433)）同构；再次点击已选天 ⇒ 取消（回到「不过滤天数」、显示全部等级）：

```csharp
toggleDay: day =>
{
    _filter.SelectedRunDay = _filter.SelectedRunDay == day ? (int?)null : day;
    _scrollY = 0f;
    ApplyFilters();
    RefreshView();
},
```

---

## File Structure（改动清单）

- **新建** `Game/CollectionPanel/Data/DayTierSchedule.cs` — 天→上限表 + `AllowsStartingTier`（单一事实来源）。
- **改** `Game/CollectionPanel/Data/CollectionFilterState.cs` — `int? SelectedRunDay`。
- **改** `Game/CollectionPanel/Data/CollectionFilterEngine.cs` — 加 AND 谓词。
- **改** `Game/CollectionPanel/CollectionPanel.cs` — `_isInGameRun`/`_currentRunDay`/`TryReadCurrentDay()`；打开时捕获 + 默认选天；`toggleDay`；`RefreshView` VM 装配；`BuildDayRange`。
- **改** `Game/CollectionPanel/Ui/CollectionPanelView.cs` — VM 3 字段；ctor `toggleDay`；`_dayChipRow`/`_dayFilterSection`/`_dayChips`；`Refresh` 显隐+高亮；字体预载。
- **改** `Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs` — 新建 Day section（Tier 之上）。
- **改** `Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs` — `EnsureDayChips` + 辅助。
- **改** `Game/CollectionPanel/CollectionPanelText.cs` — `DayHeader()`。
- **改** `Infrastructure/UiTokens/Sizes.cs` —（可选）`DayChipWidth`。
- **改** `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj` — 加 `DayTierSchedule.cs` 的 `<Compile Include>`。
- **改** `tests/CollectionFilterEngine.Tests/Program.cs` — 天数过滤用例。
- **改** `docs/design/README.md` — 把本 spec 列入「Active proposals」。

---

## 边界情况

1. **非对局**：`ShowDayFilter=false`、`SelectedRunDay=null` ⇒ Day 行隐藏、无过滤影响。
2. **对局内默认**：开面板自动选中当前天、即时按上限过滤。
3. **切天**：点其它天 ⇒ 预览该天卡池；点当前选中天 ⇒ 取消（显示全部等级）。
4. **Legendary-start 卡**：Day 8+ 随 Diamond 显示（见核心语义 \*）。
5. **天数超出表**（如 Day 12）：落入 `_ => Diamond` 上限，行为同 Day 8。
6. **Items + Skills 都生效**：技能同样有 `StartingTier`，与现有 Tier 行覆盖两 tab 一致（不同于 Size 仅 Item）。
7. **与选中商人/训练师 offer pool 叠加**：天数谓词与 offer-pool 谓词 AND，进一步收窄，无特例。
8. **`Data.Run` 为 null / 读取异常**：`TryReadCurrentDay` 吞异常返回 `null` ⇒ 退化为「不过滤天数」，不崩面板。

---

## 测试与验证

**单测**（`tests/CollectionFilterEngine.Tests/Program.cs`，复用 `Card(name, tier)` helper）：
- Day 1 ⇒ 仅 `StartingTier=Bronze`；Day 2 ⇒ Bronze+Silver、排除 Gold/Diamond；Day 6 ⇒ 至 Gold；Day 8 ⇒ 含 Diamond，且 `StartingTier=Legendary` 随 Diamond 显示。
- `SelectedRunDay=null` ⇒ 不过滤。
- 天数谓词与 hero / tier / offer-pool **AND**（沿用既有 AND 用例风格，[Program.cs:280](../../tests/CollectionFilterEngine.Tests/Program.cs#L280)）。
- `DayTierSchedule.CeilingTier` 阈值点测：1/2/5/6/7/8。
- 运行：`dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj -c Debug`

**集成 / 手测**：`./run.sh build`（Debug 自动拷入 `BepInEx/plugins/`）→ 经 Steam 启动 The Bazaar（`open "steam://run/1617400"`）。
- 对局内开「卡牌图鉴」：Day 行可见、默认选中当前天；Day 1 vs Day 6 卡集收窄；切天即时刷新；点选中天可取消。
- 主菜单开图鉴：Day 行不显示，其它筛选不受影响。
- 看 `BepInEx/LogOutput.log`：无 `CollectionPanel` 报错；`Data.Run` 为空时无崩溃。

> ⚠️ `./run.sh test` **退出码会撒谎**（即便有失败也退 0）：核对输出里的 `Failed test projects:`，勿只看尾部截断。

---

## Scope Boundaries（YAGNI / 明确不做）

- **不**运行时反射读 `BazaarCardDealer.tierRepo` / `tierManager.json`（决策①已选硬编码）。
- **不**跨会话持久化天数（不入 `CollectionPanelSelectionState`）。
- **不**改 catalog / 卡牌数据 / 虚拟化网格 / 卡面渲染。
- **不**改 Size 仅-Item 逻辑或现有 Tier 行行为。
- **不**让 Day 驱动或隐藏 Tier 行（决策③选独立 AND）。
- **不**加只断言源码文本或 mock 调用序列的覆盖率测试。

---

## 风险与权衡

- **硬编码阈值会与游戏真实表漂移**：游戏「天→等级」是每天概率分布、随版本变动（`StaticDataTierRepository.GetProbabilitiesByDay` → `BazaarCardDealer`）。落地时**未能在游戏安装目录定位 `tierManager.json` 松散文件**（已打入 bundle）离线校验 1/2/6/8 阈值，故按用户给定 spec 实现。**缓解**：全部阈值集中于 `DayTierSchedule` 单表，文件头注明来源与「补丁后回看」；未来若需权威化，可在不改 UI/state 的前提下把 `CeilingTier` 换成运行时读游戏表。
- **与手动 Tier 行语义重叠**：两者都作用于等级，并存时用户可能困惑「为何选了 Diamond 仍看不到卡」（因当天上限更低）。接受此代价以换取实现干净；header 文案「天数 / Day」+ 仅对局内出现，已有上下文暗示其语义。

---

## Self-Review Checklist

- **Spec 覆盖**：语义（day→ceiling、StartingTier、Legendary、AND）、数据源表、state/engine、运行态读取（复用英雄读法）、VM/UI/chips/显隐/本地化、装配交互、边界、测试、范围、风险——均含。
- **Placeholder 扫描**：无 TBD / 空泛验证；每处改动有具体片段或 `file:line` 锚点；UI 微调值（`DayChipWidth`、芯片摆位）已标注为可调。
- **内部一致**：`SelectedRunDay` 在 state/engine/VM/toggle 命名一致；`DayTierSchedule` 既被引擎引用又登记进测试 csproj include。
- **类型一致**：`int? SelectedRunDay`、`Run.Day:uint→(int?)`、`ETier`/`TierRank`；UI 维持 `Button` chips。
- **歧义检查**：「再次点选中天 = 取消」「Day 8+ 含 Legendary」「Items+Skills 均生效」均已显式化。
