# Collection 搜索移除、赞助者抽样调整与操作栏布局稳定性

日期：2026-06-03
状态：已批准（待 spec 复审）
本稿经两轮独立评审修订（外部 Codex 对抗式评审 + 内部 red-team subagent），见文末「评审修订记录」。

## 概述

四个部分，全部位于 mod 仓库（`bazaarplusplus-mod`）：

- **A.** 移除 Collection 面板的搜索框（UI + 状态 + 过滤 + 本地化）。
- **B.** 搜索移除后重排操作栏顶部——落地为**稳定的两行布局**（与 Part E / S1 合并）。
- **C.** 三项赞助者（supporter / sponsor）抽样调整，分布在**两条不同的代码路径**上。
- **D.** 更新两个受影响的测试工程。
- **E.** 修复切换页签/按钮时操作栏的布局抖动（根因 + 方案）。

关键事实：sponsor 系统有**两条独立的抽样路径**，Part C 的三项需求并不都落在同一条上：

| 需求 | 路径 | 展示面 |
| --- | --- | --- |
| tier4 权重 6→9 | `BPPSupporterSampler.Sample()`（按 tier 加权） | CardSet 预览旁的单条「Supported by X」面板（字号 17） |
| 同时展示时避免两个 >7 字的 case | `BPPSupporterSampler.SampleMany(4)`（打乱袋，无加权） | Collection/History 的 4 名赞助者署名行（字号 12） |
| 字体大一点 | 赞助者名字 label | 同上：4 名赞助者署名行 |

`Sizes.FontSmall`（12）是全局 token，被 UI 各处复用——**不得全局上调**。

---

## Part A — 移除 Collection 搜索

完整删除搜索框，仅触碰与搜索相关的代码。

| 文件 | 删除内容 |
| --- | --- |
| `Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs` | `CreateSearchShell()`、`StyleSearchInputField`、`CreateSearchIcon`；`BuildOperationRail` 中加入 `_searchShell` 的两行 |
| `Game/CollectionPanel/Ui/CollectionPanelView.cs` | 字段 `_searchShell` / `_searchPlaceholderLabel` / `_searchField`；`_setSearch` 字段、构造函数参数与赋值；ViewModel 的 `Search` 属性；WarmFont 中的搜索占位文案项（`:195`）；`Refresh` 中对搜索框的同步（`:351-356`） |
| `Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs` | `StyleSearchShell`、`RefreshSearchPlaceholder` |
| `Game/CollectionPanel/CollectionPanel.cs` | `SearchDebounceSeconds`；`_pendingSearch` / `_appliedSearch` / `_pendingSearchAt`；`Update()` 中的去抖块；`EnsureView` 里 `setSearch:` 视图回调（`:461-466`）；清除过滤里的搜索重置；模型构建里的 `Search =`；`GetVisibleSearchText()`；`HasActiveFilters()` 中的 pending-search 分支 |
| `Game/CollectionPanel/Data/CollectionFilterState.cs` | `Search` 属性；`HasActiveFilters` 中的 `Search` 分支；`Reset` 中的 `Search = string.Empty` |
| `Game/CollectionPanel/Data/CollectionFilterEngine.cs` | `Apply` 中的 `search` 局部变量与搜索匹配 `continue` 块 |
| `Game/CollectionPanel/CollectionPanelText.cs` | `SearchPlaceholderText`、`SearchPlaceholder()` |

注意：
- `Sizes.SearchResetButtonWidth` 保留——Reset 按钮仍在用它。名字里的「Search」不改名（可选，本次不做）。
- `CollectionPanelView.cs:296` 的 `BPPSupporterAttributionRow.Bind(...)` 与搜索无关，**保留**；Part A 所说「Refresh 中对搜索框的同步」仅指 `:351-356`，不要误删 Bind。
- `EnsureView` 的 `CollectionPanelView` 构造调用是 `setSearch` 形参的唯一调用点；移除形参时同步删该实参 lambda。

---

## Part B — 重排顶部（稳定两行布局）

搜索原本独占第三行「Tools Row」。搜索移除后，删掉该行，将 Reset + Packages 上提。不把全部控件塞进单行（单行换行正是抖动主因，见 Part E），改用**两行固定、不换行**的布局。

### 选定排布与宽度预算（必须满足）

控件均为 `flexShrink = 0`（见 `CollectionPanelView.Filters.cs` 的 `CreateButton`），在 `OperationRailMinWidth = 360` 下若超宽会**裁切**而非压缩。因此每行的「固定宽度之和 + 行内间隙」必须 ≤ ~340（留出弹性间隔余量）。token 实测值（`Sizes.cs`/`Spacing.cs`）：RunsTabWidth=72，PackageToggleWidth=104，SearchResetButtonWidth=68，ChipMinWidth=86，Md=8，Sm=6，Xs=4。

选定排布：

- **第 1 行：** `[Items 72] [Skills 72]` —弹性间隔— `[Reset 68] [Count（固定 ~96）]`
  预算 ≈ 72+8+72 + 间隔 + 68+6+96 = **330 ≤ 360** ✓
- **第 2 行：** `[Sort 标签 | Quality 72 | Size 72]` —弹性间隔— `[Packages 104]`
  预算 ≈ 30+4+72+4+72 + 间隔 + 104 = **294 ≤ 360** ✓

两行都设 `flexWrap = NoWrap`。Count label 取**固定宽度**（按最宽实际值，如「999 cards」≈96，含内边距）而非随内容增长，使位数变化不再挤动相邻元素。行内左右顺序可微调，但**任何重排都必须满足每行 ≤340 的预算**（否则在 360 栏宽下裁切）。若后续控件增多导致放不下，宁可上调 `OperationRailMinWidth`，不要让按钮 flexShrink。

---

## Part C — 赞助者抽样调整

> C2、C3 都改动**共享**组件（`BPPSupporterSampler`/`BPPSupporterCatalog` 与 `BPPSupporterAttributionRow`），因此 **Collection 与 History 两个面板都会生效**。这是有意为之（两处署名行都受益），并在 Part D 中对两处都做验证。

### C1. tier4 权重 6 → 9
`Game/Supporters/BPPSupporterSampler.cs` 的 `ResolveTierWeight`：`4 => 6` 改为 `4 => 9`。`ResolveTierWeight` 仅被 `Sample()`（加权路径）调用，`Sample()` 仅被 `BPPSupporters.Sample()` → `CardSetPreviewRuntime.cs:285` 调用；`SampleMany` 不经过它。故只影响单条「Supported by」面板。tier4 占比 6/12（50%）→ 9/15（50% 基底 1+2+3+9=15，tier4=9/15=60%）。

### C2. 4 名署名行中最多一个 >7 字的名字（用「打乱袋内分散长名」实现，不改游标契约）

**目标**：同时展示的这一组（每行 4 个）名字里，名字裁剪后长度 > 7 的最多一个；短名字足够时仍展示 4 个。

**实现选择（关键）**：不在 `SampleMany` 抽样过程中跳过长名——那样会令实际扫描位置数 > `count`，与调用方 `BPPSupporters.SampleMany` 的 `_attributionCursor += count` 错位，进而在小袋/回绕时让相邻行重复或饿死被跳名字（两轮评审都指向此处）。改为在**构袋阶段**把长名字分散开：

- 在 `BPPSupporterSampler.BuildShuffledBag` 现有的 stable-hash 排序之后，追加一个**分散排列**步骤：把裁剪后长度 > 7（`const int LongNameCharThreshold = 7`）的条目沿全袋大致等距铺开，使**任意连续 `AttributionRowWindow`（= 4）个条目里长名字 ≤ 1**——当长名字数 L ≤ ⌊N / 4⌋ 时可严格保证；L 更多时尽力均摊、最小化聚簇。
- `SampleMany`、`BPPSupporters.SampleMany`、游标推进（`cursor += count`、`% N` 回绕）**全部保持不变**。轮转契约（“重复前遍历每个赞助者一次”）与现有测试因此**完全不受影响**。
- 已知边角：跨「袋尾→袋头」回绕缝处的窗口可能偶含 2 个长名（尽力而为）；L > ⌊N/4⌋ 时无法严格满足，只均摊。
- `BuildShuffledBag` 每次以固定的 `AttributionShuffleSeed`（进程内不变）调用，故排列在一次会话内确定且稳定。

### C3. 署名行名字字号 12 → 14（并固定行高，防换行抖动）

- 新增 `Sizes.SupporterAttributionNameFont = 14`，在 `BPPSupporterAttributionRow.CreateSupporterName`（`:122`）用它替换 `Sizes.FontSmall`。`FontSmall`（12）保持不变——前缀、分隔点「·」、后缀、sponsor 按钮（`:75/:111/:156`）仍为 12。这会使名字与同行 12pt 文本基线略有差异；在手动验证中确认基线观感，若不可接受再决定是否同步上调分隔点字号（本次默认只改名字）。
- **防换行（评审项）**：该署名行 `row.style.flexWrap = Wrap`（`BPPSupporterAttributionRow.cs:20`），位于操作栏顶部、tabs 与过滤之上（`CollectionPanelView.Tree.cs:64-65`）。4 个名字（各 maxWidth 118）+ 前缀 + 按钮在 360–680 栏宽下会换行；14pt 让换行更易发生，且 History 顶部行下方紧跟密集列表（`HistoryPanelUiToolkitView.Tree.cs:193`）。为避免「行高随名字长度/字号在 1↔2 行间跳变」顶动下方，给该行预留**固定两行高度**：新增 `Sizes.SupporterAttributionReservedHeight`（按 14pt 两行 + 边距估算，约 44–48），在 `BPPSupporterAttributionRow.Create` 设为该行的固定高度（取代当前仅 `minHeight = 24`）。这样无论名字长短/字号，行高恒定，下方不再被顶动。Collection 与 History 都验证。

---

## Part D — 测试

- `tests/CollectionFilterEngine.Tests/Program.cs` — 删除搜索断言：`Search = "wand"` 的可重置检查（`:10-11`）、`Search = "Vanessa"` 的 AND 语义用例（`:320`）；这两处是仅有的 `Search` 用法。
- `tests/Supporters.Tests/Program.cs`：
  - `TestTierWeightsSelect...` 的边界从 /12 改为 /15（tier4 边界 6/12 → 6/15；总权重 1+2+3+9=15）。
  - **不要改动** `TestSampleManyRotatesThroughEverySupporterBeforeRepeating`：其 5 条目袋全是短名（Alice/Bob/Cara/Dora/Evan），不触发 C2 路径，断言依旧成立——它正好记录了「无长名」情形。C2 改的是构袋排列，不破坏轮转契约，故该测试无需放宽。
  - 新增（**固定 seed + 手搭含 ≥2 个长名的袋**，确定性触发分散逻辑）：
    a. **窗口约束**：对构袋结果，断言任意连续 4 元素窗口（非回绕）内长名 ≤ 1（前提 L ≤ ⌊N/4⌋）；并断言短名充足时 `SampleMany(4)` 返回 4 个、其中长名 ≤ 1。
    b. **轮转仍成立**：连续多次 `SampleMany(4)`（游标推进不变）走完一圈不重复——复用现有轮转语义，证明 C2 未破坏它。
- 共享署名行验证：在手动验证中覆盖 **History** 路径——以含一个允许长名的 4 个样本绑定 `BPPSupporterAttributionRow`，确认 14pt + 固定行高下 History 顶部行不挤动其密集列表。

两个均为 exe-runner 测试工程（`Program.cs` 入口）：

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj -c Debug
dotnet run --project tests/Supporters.Tests/Supporters.Tests.csproj -c Debug
```

外加 Debug 构建：`./run.sh build`。

---

## Part E — 操作栏布局稳定性

切换 Items↔Skills 或过滤项数量变化时，操作栏会明显重排。根因（基于现有代码）：

1. **`tabsRow` 是 `flexWrap = Wrap`**（`CollectionPanelView.Tree.cs:71`）。子元素总宽超栏宽时换行，行高翻倍并顶动下方。触发点：Count label 随位数变宽（`flexShrink = 0`）；折入 Packages + Reset 会加剧。
2. **Size 区在 Skill/Item 间切换 `display:None`/`Flex`**（`CollectionPanelView.cs:342-344`）。整行插入/移除顶动 Source、状态行与网格。
3. **Source 区高度随 chip 数量变化**。Merchants（Item）与 trainers（Skill）chip 数不同；Source 行换行（`:185`），重建时还先清空再填充（`Filters.cs:155`）→ 闪一下。
4. **Source chip 尺寸在布局后才结算**。绑定时 `ApplySourceChipSizing(_sourceChipRow.resolvedStyle.width)`（`Filters.cs:128`）宽度为 0/NaN（`:184` 的 NaN/≤0 守卫令其回退最小值），待 `OnSourceChipRowGeometryChanged`（`:179`）再 resize → 可见跳变。

（Hero/Tier/Size 的 chip **列表**是常量——`HeroOrder`/`TierOrder`/`SizeOrder`——不会重建；观察到的位移是竖直方向的，来自 2–4，加上 1 的换行。）

### 方案 — 让操作栏成为固定高度、不换行的区域

- **S1（必做；并入 Part B）。** 控件行 `flexWrap = NoWrap`；Count label 固定宽度；两行排布按 Part B 的宽度预算。消除换行（根因 1）。
- **S2。** Skill 页签下，**保留整个 `_sizeFilterSection` 可见**（不再 `display:None`），但只清空/隐藏其 chip，并给该 section 设**显式固定 `minHeight` = Item 页签下的实测高度**（标题 label + `ChipHeight = 32` + label 的 `marginBottom = Sm(6)` + section 的 `marginTop`）。这样切页签不再插拔整行高度。解决根因 2。注意：要保留高度的是 section，不是被隐藏的 chip 行。
- **S3（独立子任务，体量较大）。** 不用「典型最大值」截图尺寸。Source 区每行固定 6 个 chip（`box = rowWidth/6 - gap`，`Filters.cs:188` 硬编码 6），且 group-break 占位（`CreateSourceChipBreak`，`:169`）每个**强占一整行**。据此按当前栏宽**遍历所有受支持英雄（`HeroOrder`，8 个）× 两种 source 类型**，用 `CollectionSourceCatalog.For(kind, hero)` 算出各自的行数 = Σ⌈每组数/6⌉ + group-break 行数，取全局最大行数对应高度作为 Source 区**固定高度**；栏宽（geometry）变化时重算。若某英雄仍超出该上界，则把 Source 区**钳制为固定高度并内部滚动**（需新增一个 `ScrollView` 容器——这是一处非平凡的新增，应单独评估/实现）。验证须遍历所有英雄，而非仅一次页签切换。
- **S4（修订）。** 「绑定时按已知栏宽 seed」不可行——绑定时唯一可得的宽度就是那个未解析的 `resolvedStyle.width`，而栏宽是 `flexBasis = 32%` 钳制到 [360,680]、布局前未知。故改为：**移除绑定时的 `ApplySourceChipSizing` 调用，仅在首个 `GeometryChanged` 回调里结算一次尺寸**。代价是首帧 chip 以 `SourceChipMinSize = 40` 占位渲染一帧、随后结算一次——单次结算优于当前的「回退→resize」双跳。

范围分层：**S1** 必做（Part B 依赖它）。**S2、S4** 体量小，建议随本次一起做。**S3** 体量较大（跨英雄计算 + 可能新增 ScrollView），作为独立子任务，可在 S1/S2/S4 落地后单独推进。

---

## 不在本次范围内

- 重命名 `Sizes.SearchResetButtonWidth`。
- History 面板除经共享组件自然承接 C2/C3（且需验证）外，不额外改动。
- 单条 sponsor 面板除 tier4 权重外的其它抽样改动。

## 验证

- 单元：上述两条测试命令通过（含 C2 的窗口约束 + 轮转不变两类断言；Supporters 的 tier4 边界改 /15）。
- 构建：`./run.sh build`（Debug，自动拷入 BepInEx/plugins）。
- 手动（经 Steam 进游戏）：
  - 打开 Collection——无搜索框；两行控件在最窄栏宽下不裁切、不换行；切换 Items↔Skills 操作栏不跳动（逐个英雄检查 Source 区高度恒定，对应 S3）。
  - Collection 与 **History** 两处署名行：最多一个长名字、14pt + 固定行高下不挤动下方。
  - 单条 sponsor 面板更偏向 tier4 名字。

---

## 评审修订记录

**第一轮（外部 Codex 对抗式评审，needs-attention）**——三点已采纳：
1. [high] C2 跳过逻辑会破坏游标轮转 → 初版改为 `scannedCount` 契约。
2. [medium] S3 「典型最大值」非不变量 → 改为跨英雄确定性最大行数 + 钳制滚动。
3. [medium] C2/C3 改的是共享行、History 也绑定 → 改为有意作用于两处并验证 History。

**第二轮（内部 red-team subagent，ship-with-fixes）**——进一步采纳：
1. [high] `scannedCount` 契约在「第二轮填充 + 回绕」处仍可在小袋上产生相邻行重复（已用 2-call 反例验证）→ **改方案**：放弃抽样期跳过/游标改动，改为**构袋阶段分散长名**，保持 `SampleMany`/游标/轮转测试不变（见 C2）。
2. [medium] Part B 第 2 行在 `OperationRailMinWidth = 360` 下超宽且按钮 `flexShrink = 0` 会裁切 → 已给出实测宽度预算与可放下的选定排布（见 Part B）。
3. [medium] C3 的 14pt + `Wrap` 仍可让署名行换到两行、顶动下方 → 增加**固定两行行高**预留（见 C3 防换行）。
4. [medium] S2 应保留 section 可见、仅隐藏 chip 并设显式 `minHeight`；S4「按已知栏宽 seed」不可行，改为仅在首个 GeometryChanged 结算一次；S3 钳制滚动是非平凡新增，单列子任务（见 Part E）。
5. [low] Part A：`CollectionPanelView.cs:296` 的 `Bind` 保留，不属删除项（已在 Part A 注明）。
6. [medium] Part D：不放宽 `TestSampleManyRotatesThroughEverySupporterBeforeRepeating`（其袋无长名、不受影响），改为另加含长名的新测试（已在 Part D 修正）。
