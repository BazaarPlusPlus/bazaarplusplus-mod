# History Panel — Ghost Elimination Row Indicator

## Scope

战绩面板 (Ghost tab) 的 battle row 上目前**没有**任何视觉信号告诉玩家"这一场把对手打出局了"。该信息仅以黄色横幅形式出现在详情/预览面板里 ([HistoryPanelUiToolkitView.Tree.cs:204](../Game/HistoryPanel/HistoryPanelUiToolkitView.Tree.cs)) — 玩家必须点开每一行才能发现。

这把 Ghost 模式最有"赛后回味感"的高光信息埋到了二级页面，玩家在战绩列表里 scan "出彩的几把"时无路可走。

本文设计 row 级别的指示，让 elimination 在不点开的情况下可见。

## Trigger

复用现有判定函数，**不**新增数据字段：

```
HistoryPanelFormatter.IsGhostOpponentEliminated(battle)
  ⇔ battle.Source == Ghost
  ∧ battle.IsBundleFinalBattle
  ∧ Player 视角胜利
```

详情面板的 `_ghostOpponentEliminatedNotice` 横幅继续保留——row 指示和详情横幅读同一个 flag，不会出现"行上说出局，详情里没说"的不一致。

## Visual

满足条件时，row 上叠加两个**冗余**信号：

### 信号 1 — Accent bar 升金

`BattleRow` 左侧那条 accent strip 当前在 `ApplyBattleRowState` 里按 win/loss 三态着色：

| 状态 | accent 颜色 |
|---|---|
| Win (普通) | `(0.23, 0.54, 0.47)` 绿 |
| Loss | `(0.63, 0.36, 0.24)` 红 |
| 未知 | `(0.34, 0.47, 0.64)` 蓝灰 |

新增第四态：

| 状态 | accent 颜色 |
|---|---|
| **Eliminated win** | `(0.94, 0.70, 0.28)` 金 |

金色复用详情面板 `_ghostOpponentEliminatedNotice` 已有的边框色，保证视觉语义全局一致。Eliminated 是 win 的特化——不是新增独立类别——视觉层级上"金 > 普通绿"。

行底色和边框色按相同方式扩展（深金底 + 半透金边），保持原本三态的色相分布逻辑。

### 信号 2 — "Knocked Out" chip

opponent row（第二行）插入一个新的固定 chip，位置在 `OpponentSummaryChip` 之后、`OpponentName` 之前。语义上这是**对手的**属性，所以贴近对手身份信息。

| 属性 | 值 |
|---|---|
| 文案 (EN) | `Knocked Out` |
| 文案 (CN) | `对手出局` |
| 文案 (ZH-TW) | `對手出局` |
| 背景 | `(0.32, 0.24, 0.10)` 深金 |
| 文字 | `(0.99, 0.90, 0.68)` 浅金 |
| 字号 / 字重 | 12pt Bold |
| 圆角 | 11px (全胶囊) |
| padding | 8px 左右、0 上下 |
| 宽度 | auto (~95px EN / ~56px CN) |
| 显示条件 | `IsGhostOpponentEliminated == true`，否则 `display = None` |

文案不复用现有 `GhostOpponentEliminatedNoticeText`（那是 long form "After this battle, the opponent is eliminated."），新增 short 版本 key：

- `HistoryPanelText.GhostOpponentEliminatedShortText`
- `HistoryPanelText.GhostOpponentEliminatedShort()` accessor

### 为什么是双信号而非单信号

| 单 chip | 单 accent | 双信号 |
|---|---|---|
| 扫描时眼睛先到行左侧 (DayBubble + accent)，文字 chip 要二次定位 | 色弱玩家无法解码"金色 vs 绿色" | 左侧金色拉注意 → 视线右移 → chip 文字校验 |

Chip 文字 + accent 颜色互为校验，色弱玩家靠文字识别，扫描场景靠颜色识别。

## Components Touched

| 文件 | 改动 |
|---|---|
| [HistoryPanelText.cs](../Game/HistoryPanel/HistoryPanelText.cs) | 新增 `GhostOpponentEliminatedShortText` `LocalizedTextSet` 和 `GhostOpponentEliminatedShort()` accessor |
| [HistoryPanelUiToolkitView.Rows.cs](../Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs) | `BattleRowRefs` 加一个 `EliminatedChip Label` 字段；`MakeBattleRow` 构造并挂入 opponent row；`BindBattleRow` 设文案 + 切 display；`ApplyBattleRowState` 增加 elimination 着色分支 |

不动：
- `HistoryBattleRecord` — 已有 `Source` 和 `IsBundleFinalBattle` 字段，无需扩展
- `HistoryPanelFormatter.IsGhostOpponentEliminated()` — 判定逻辑复用
- `HistoryPanelUiToolkitView.Tree.cs` — 详情面板横幅保留不变
- `HistoryPanel.UiToolkit.cs` — view-model 装配路径无变化

## Data Flow

```text
HistoryBattleRecord (Source, IsBundleFinalBattle, Result, WinnerCombatantId)
  ↓
HistoryPanelFormatter.IsGhostOpponentEliminated()         [unchanged]
  ↓
BindBattleRow            → EliminatedChip.text + display  [new]
ApplyBattleRowState      → accent / row bg / border       [extended branch]
```

Row 指示和详情横幅都从 `IsGhostOpponentEliminated()` 单一来源派生，不可能漂移。

## Layout Details

opponent row 当前布局：

```text
[OpponentHeroPill 80px] [OpponentSummaryChip 100px] [OpponentName flex-grow right-aligned]
```

插入后：

```text
[OpponentHeroPill 80px] [OpponentSummaryChip 100px] [EliminatedChip auto] [OpponentName flex-grow right-aligned]
```

`EliminatedChip` 在 `display = None` 时不占布局空间，`OpponentName` 仍然贴行尾右对齐。可见时 name 区域被压缩，name 文本走原生 ellipsis 截断（Unity UI Toolkit `Label` 默认行为），无需额外处理。

Chip 自身在 EN/CN 下宽度差 ~40px，因玩家只看一个 locale，跨语言宽度差不构成可见问题。

## Error Handling

- `IsGhostOpponentEliminated(null) == false`，已 null-safe
- `BindBattleRow` 现有的 index 越界守卫覆盖 chip 路径
- `MakeBattleRow` 构造期不读 battle 数据，无 null 风险
- 没有新增 I/O 或异步路径

## Out of Scope

- **Ghost vs Local 来源指示** — Ghost battles 已在独立 tab，无来源混淆
- **Win/Loss 显式文字 chip** — 当前仅靠颜色暗示是另一个 UX 议题，本次不处理
- **Chip hover tooltip** — 详情面板横幅已说明，不需要二次悬浮信息
- **Local tab 行为** — 完全不动；`IsGhostOpponentEliminated` 对 `Source != Ghost` 直接返回 false

## Testing

按 `.rules` 项目惯例，UI Toolkit 面板在本仓库**没有**有意义的自动化测试 seam，本次不引入 source-text 断言式测试。

人工验证清单（每条对应一个 acceptance case）：

1. Ghost tab，bundle 最后一场 + 我赢 → row 显示金色 accent 和 chip；详情面板横幅同步显示
2. Ghost tab，bundle 非最后一场 + 我赢 → row 无金色无 chip
3. Ghost tab，bundle 最后一场 + 我输 → row 无金色无 chip（保持普通 loss 着色）
4. Local tab (Runs)，任何战斗 → row 无金色无 chip
5. 切语言 EN ↔ CN ↔ ZH-TW → chip 文字更新，宽度变化不破坏 row 布局
6. 选中和未选中状态在 elimination 行下，accent / 行底色都比普通 win 行更亮
7. 把面板拉窄到极限 → chip 不变形，`OpponentName` 走 ellipsis 截断
