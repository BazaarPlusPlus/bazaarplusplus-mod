# 设置 dock 面板锚点 & CollectionPanel 滚轮手感 — 两处 playtest 微调

> 范围：两处**互相独立**的小型 UI 调整，均在 `bazaarplusplus-mod`。来源是一次 playtest 的三条手记，经澄清对话收敛为下述两项（第三条「加竖向滚动条」已撤销）。
> 改动量为**常量 / 锚点级别**；无新增类型、无新增资产、无新增测试。

## 背景

playtest 原始三条手记：

1. 「BPP 这个弹出的位置，应该是基于下面弹出的」
2. 「滑轮的滚动速度肯定可以控制一下」
3. 「可以加一个竖向的滚动条」

**澄清结论**：这三条落在**两个不同的 UI** 上——

- 点 1 → **设置 dock 面板**（挂在 dock 按钮上、向上弹出的那个）。
- 点 2、3 → **CollectionPanel 卡牌网格**（全 mod 唯一有滚轮速度 / 滚动条代码的地方）。
- 点 3（竖向滚动条）**撤销**：决定不加滚动条，只把滚轮手感调顺即可。

→ 两项独立改动，互不依赖，可分别提交。

---

## 改动 A — 设置 dock 面板：生成锚点从按钮「上边界」改到「下边界」

**文件**：`Game/Settings/BppSettingsDockController.Presentation.cs:13-37`（`ConfigurePanelRect`）

**现状**：面板挂在 dock 按钮（`_dockButtonRect`）下，锚点在按钮**上**边界（`anchor.y=1`），pivot 在面板底边（`pivot.y=0`）→ 面板底边贴按钮上边界、**向上生长**。

```csharp
rectTransform.anchorMin = new Vector2(0f, 1f);            // 按钮【上】边界
rectTransform.anchorMax = new Vector2(0f, 1f);
rectTransform.pivot = UpRight ? (0f, 0f) : (1f, 0f);      // 面板底边为支点 → 向上长
rectTransform.anchoredPosition = UpRight ? (8f, 0f) : (-8f, 0f);
```

**改动**：仅把 `anchorMin` / `anchorMax` 的 **Y 从 `1f` 改到 `0f`**（生成原点：按钮上边界 → 下边界）。`pivot`、`anchoredPosition`、X 全部不动。

```csharp
rectTransform.anchorMin = new Vector2(0f, 0f);            // 按钮【下】边界  ← 唯一改动
rectTransform.anchorMax = new Vector2(0f, 0f);
```

**结果**：面板整体下移约一个按钮高度，底边与按钮**下**边界对齐；**仍然向上弹出、向上生长**（pivot 不变）。纯竖向 / 锚点改动，X 不变。

**不做**：设置面板不引入滚动。⚠️ 已知隐患备忘——7+ 条设置项继续向上长仍可能触顶；本次明确不处理，记录待后续。

---

## 改动 B — CollectionPanel 卡牌网格：滚轮每格跳跃量调小

**文件**：`Game/CollectionPanel/Grid/CollectionGridConstants.cs:76-80`（`MouseWheelScrollPoints`）
**相关**：`Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:419-425`（UITK `ScrollView` 配置）

**现状**：网格用 UITK `ScrollView`，每个滚轮 notch **即时跳 `300` 点**，偏跳偏快。代码注释（`CollectionGridConstants.cs:76-79`）已记录：这版 UITK 上的「平滑滚轮」尝试会把滚轮滚动整个搞坏，故 shipped 的是即时跳转、靠每格够大的位移来「显得有分量」。

```csharp
public const float MouseWheelScrollPoints = 300f;
```

**改动**：把 `300f` **调小**（起步试 `~120f`，进游戏凭手感微调到舒服）。**保留即时跳转**机制，**不**改成动画平滑（注释所述风险，本次不碰）。

```csharp
public const float MouseWheelScrollPoints = 120f;        // 起步值，凭手感微调
```

**不做（点 3 撤销）**：不加竖向滚动条；`_gridScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto`（`CollectionPanelView.Tree.cs:424`）保持原样不动。

---

## 范围边界（明确不做）

- 设置面板**不加**滚动 / max-height（点 1 仅锚点改动）。
- CollectionPanel **不加**竖向滚动条（点 3 撤销）。
- **不**做动画式平滑滚动（既往在该 UITK 版本上有把滚轮搞坏的前科）。

## 验证

按改动比例：`./run.sh build`（Debug，自动拷贝 `BepInEx/plugins`）→ 经 Steam 启动游戏（`open "steam://run/1617400"`）→

1. 打开设置 dock 面板，确认其改为从按钮**下**边界生成、且**仍向上弹出**；
2. 打开 CollectionPanel，滚卡牌网格看手感，`MouseWheelScrollPoints` 数值再微调。

非打包改动，无需 `BuildAll` / Release。

## 开放 / 待调项

- **B 的最终数值**：`MouseWheelScrollPoints` 起步 `~120f`，最终值由游戏内手感定。
- **A 的向下溢出**（后续）：设置项继续增多、面板向上触顶时，再考虑给设置面板加 max-height + 滚动（本次明确不做）。
