# History Panel

游戏内 `HistoryPanel` 是 run logging 的读侧 UI：浏览本地 runs、本地 PVP battles、ghost battles 与保存的棋盘快照，按条件启动本地 / ghost replay，并可删除 run 及其关联 battle。大厅内通过 Bazaar++ 设置坞的 **Game History** 入口打开，或 `F8` 切换。

数据来源与上传语义见 [run-logging-and-upload.md](run-logging-and-upload.md)；SQLite 列定义见 [sqlite-schema-reference.md](../reference/sqlite-schema-reference.md)。本文记录面板的**当前布局、预览渲染与行级视觉**状态。

## 布局：全屏响应式

面板是「边到边全屏、按相对比例自适应」，不再是屏幕居中的固定 1280×1020 盒子（2026-05-29 改造，归档设计见 [docs/design/archive/2026-05-29-historypanel-fullscreen-responsive-design.md](../design/archive/2026-05-29-historypanel-fullscreen-responsive-design.md)）：

- **按高匹配**：`PanelSettings.match = 1f`（`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs`）——token 随屏幕高度缩放，竖向密度恒定、超宽屏不竖向溢出。
- **flex 填满 + 百分比**（`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs`）：panel 铺满 root（`flexGrow:1`）、直角、背景不透明；runs 栏 `width: Percent(52)`、battles `flexGrow:1`、preview 容器 `height: Percent(33)`。结构常量改为比例常量 `RunsColumnWidthPercent=52`、`PreviewHeightPercent=33`。
- **预览口径对齐**：`ApplyPreviewContainerBounds` 是纯 `worldBound→clip` 映射；叠加层走 `scaledPixelsPerPoint`，`match` 一变自动跟随。`ComputePreviewPanelScale` 与 F9 调参器已删除。

> 取舍：`match=1`（纯按高）而非折中的 `0.5`。对竖向列表为主的全屏面板，按高更能保证各分辨率下可见行数比例一致；代价是 32:9 等超宽屏两侧留白，已确认接受。

## 预览渲染：overlay 而非 RenderTexture

战斗板预览维持独立 `ScreenSpaceOverlay` Canvas（`GameInterop/ItemBoardPreview/BppItemBoardPreview.cs` + `ItemBoardPreviewSurface.cs`）+ 读容器 `worldBound` 同步坐标，**不**用「离屏 Camera 渲染卡片到 RenderTexture 再贴进 UI Toolkit Image」。曾实现过 RT 方案，但在 URP 下离屏 Camera→RT 无法渲染 uGUI（`CardPreviewBase`），已回退到 overlay 并删除 RT 相关代码。该决策见 [ADR-0003](../adr/0003-history-panel-preview-overlay.md)；勿再提议改回 RT。

`HistoryPanel` 的预览栈与怪物预览 / LiveBuildPanel overlay 完全解耦（见 [monster-preview.md](monster-preview.md)）。

## Ghost 出局行级指示（Knocked Out）

Ghost tab 的 battle row 在「这一场把对手打出局」时叠加两个**冗余**信号，让 elimination 不点开即可见。触发复用现有判定、不新增数据字段：

```
HistoryPanelFormatter.IsGhostOpponentEliminated(battle)
  ⇔ battle.Source == Ghost ∧ battle.IsFinalBattle ∧ Player 视角胜利
```

- **信号 1 — accent 升金**：`BattleRow` 左侧 accent strip 在原 win/loss/未知三态外新增「Eliminated win = 金 `(0.94,0.70,0.28)`」第四态；行底色 / 边框同步深金。金色复用详情面板 `_ghostOpponentEliminatedNotice` 的边框色，语义全局一致。
- **信号 2 — “Knocked Out” chip**：opponent row 插入固定 chip（`OpponentSummaryChip` 之后、`OpponentName` 之前），文案 `Knocked Out` / `对手出局` / `對手出局`；`display=None` 时不占布局，name 仍贴行尾右对齐。

双信号互为校验：扫描场景靠颜色、色弱玩家靠文字。row 指示与详情面板横幅都从 `IsGhostOpponentEliminated()` 单一来源派生，不会漂移。

涉及文件：`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Rows.cs`（chip 构造 / 绑定 / 着色分支）、`Game/HistoryPanel/HistoryPanelText.cs`（`GhostOpponentEliminatedShortText`）。`HistoryBattleRecord` 与 `HistoryPanelFormatter.IsGhostOpponentEliminated()` 不改。

## Known Issues / 待现场确认

- **z 序穿透**（非阻塞）：面板 `sortingOrder = 26`、预览叠加层 `27`。全屏后若游戏自有 UI `sortingOrder > 26`，会盖在面板之上；需在商店 / 地图 / 主菜单现场确认是否够高。

## 关键文件

- `Game/HistoryPanel/HistoryPanel.cs`
- `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs`、`HistoryPanelUiToolkitView.Tree.cs`、`HistoryPanelUiToolkitView.Rows.cs`
- `GameInterop/ItemBoardPreview/BppItemBoardPreview.cs`
- `GameInterop/ItemBoardPreview/BppItemBoard.cs`
- `Game/HistoryPanel/Storage/HistoryPanelRepository.cs`
- `Game/HistoryPanel/HistoryPanelReplayService.cs`、`HistoryPanelFormatter.cs`、`HistoryPanelText.cs`
