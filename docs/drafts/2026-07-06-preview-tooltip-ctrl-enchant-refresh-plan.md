# 图鉴 / 十胜面板卡牌 tooltip 的 Ctrl 附魔预览实时刷新 — 实施计划

日期：2026-07-06
状态：待实施（实施完成后需外部 review，再走 wrap-up）

## 背景

游戏内实况卡牌（`CardController`）在 tooltip 已经显示时按下/松开 Ctrl（`HoldEnchantPreview` 热键），tooltip 会立刻重建并更新附魔预览文本。这是 mod 自己实现的行为，驱动者是
`TooltipModifierRefreshController`（src/BazaarPlusPlus/Game/Tooltips/TooltipModifierRefreshController.cs:29-59）：每帧读取 hold 状态，模式变化时调用 `TryRefreshCurrentItemTooltip`（同文件 :88-117），对当前 tooltip 做 Hide + 以新 clone 的 `CardTooltipData` 重新 Show。

图鉴面板（CollectionPanel）和十胜推荐面板（LiveBuildPanel，含 HistoryPanel 的对局 board 预览）里的卡牌不是 `CardController`，是 mod 借游戏原生 `CardPreviewBase` 预制体渲染的预览卡。它们的 tooltip 表现为：

- 先按住 Ctrl 再 hover → 附魔预览正常出现（文本注入发生在渲染期，见下）；
- 已经 hover、tooltip 已显示后再按 Ctrl → **不更新**。这就是本任务要修的缺陷。

## 根因（已从代码确认）

1. **预览卡 tooltip 的显示链路。** `CardPreviewBase.OnHover()` 用 SetUp 时创建的固定 `_tooltipData` 调 `Data.TooltipParentComponent.ShowCardTooltipController(transform, _tooltipWorldSpaceOffset, _tooltipData, parentInScreenSpace)`（decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewBase.cs:215-235；`_tooltipData` 创建于 :91 → :120-126）。mod 侧的 hover 入口有两条：
   - 图鉴：`CollectionCardHoverRelay`（src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardHoverRelay.cs），由 `CollectionGridVirtualizer.PollHover` 驱动（src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:257-342）；
   - 十胜/历史：`NativeCardPreviewHoverRelay`（src/BazaarPlusPlus/GameInterop/CardPreview/NativeCardPreviewHoverRelay.cs），由 `ItemBoardPreviewSurface.PollHover` 驱动（src/BazaarPlusPlus/GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs:225-252），上游是 LiveBuildPanel.cs:98 和 HistoryPanel.cs:421-428。

   两条链路都只在 hover 进入的那一刻转发一次 `OnHover()`，之后不再触碰 tooltip。

2. **附魔预览文本是渲染期注入的，所以"先按住再 hover"能工作。** Harmony postfix 挂在 `CardTooltipData.GetPassiveTooltipBlock` 上（src/BazaarPlusPlus/Patches/Tooltips/ItemEnchantPreviewPatch.cs:57-114），每次 tooltip 渲染文本时按当时的 `TooltipPreviewModePolicy.Resolve` 结果决定是否追加附魔段。`GetPassiveTooltipBlock` 每次调用都重新 Render（decompiled/TheBazaarRuntime/TheBazaar.Tooltips/CardTooltipData.cs:286-313；`_compiledTooltips` 只缓存文案模板编译结果，不缓存最终文本）。因此 tooltip 只要重新 Show 一次，文本就会带上/去掉附魔段。

3. **Ctrl 状态变化时，刷新器找不到预览卡这个目标 → 什么都不做。** `TooltipModifierRefreshController.TryRefreshCurrentItemTooltip` 的目标解析只认识 `CardController`：
   - 主路径 `TooltipPreviewTargetResolver.TryResolveCurrentPrimaryItemTooltip` 拿到当前 tooltip 的 `CardInstance` 后，要在 `Data.CardAndSkillLookup` 里反查 `CardController`（src/BazaarPlusPlus/Game/Tooltips/TooltipPreviewTargetResolver.cs:107-138）。预览卡的 card 是 `CardPreviewBase.SetUp` 里 `DTOUtils.CreateCard` 现造的合成实例（CardPreviewBase.cs:76），从未注册进 `CardAndSkillLookup` → 反查失败。
   - 兜底路径遍历 `CardControllerDictionary` 找 `IsCursorOverCard`/`IsHovering` 的控制器（TooltipModifierRefreshController.cs:139-159）。图鉴/十胜面板上鼠标悬停的是 mod 自己的预览卡，没有任何 `CardController` 处于 hover 态 → 也失败。
   - 两条路径都失败 → `TryResolveRefreshTarget` 返回 false → 直接 return，tooltip 不刷新。**这就是图鉴/十胜界面按 Ctrl 无反应的根因。**

4. **一个不能绕开的原生守卫（决定修复形态）。** `TooltipParentComponent.ShowCardTooltipController` 在 tooltip 节点仍活跃时走 else-if 分支，要求 `CurrentTooltipData != tooltipData` 才重新 Show（decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs:318）。所以**不能**只是对着预览卡再调一次 `OnHover()`（同一个 `_tooltipData` 实例会被 no-op 掉）；必须像现有游戏卡刷新路径那样，用一个**新的** `CardTooltipData` 实例重 Show。现成工具就是 `CardTooltipDataFactory.Create(card, source)`（src/BazaarPlusPlus/Game/Tooltips/CardTooltipDataFactory.cs:53-94），clone 语义与原生构造函数一致（对比 decompiled CardTooltipData.cs:102-110，二者都用 `new ValueContext(Data.Run, card)`）。

## 修复设计

总体思路：让 mod 自己记录"当前 hover 中的预览卡"，在刷新器里加一条预览卡专用刷新路径 —— clone 一份新 tooltip data 写回预览卡，然后 Hide + 重新触发原生 `OnHover()`，让原生逻辑自己算 offset / screen-space / locked 分支。

### 1. 新增 `NativeCardPreviewHoverTracker`（GameInterop/CardPreview/）

- 静态类，持有当前 hover 的预览卡 `Component`（弱语义即可，用 `Component` 引用 + Unity null 检查）。
- API：`NotifyHover(Component card)`、`NotifyHoverOut(Component card)`（只有当参数与 Current 相同才清空，防两个面板 hover 交叠时次序问题）、`Component? Current { get; }`。
- 放 GameInterop 层：它是对游戏原生 `CardPreviewBase` 的共享运行时状态适配，两个 feature（CollectionPanel、ItemBoardPreview 消费方）都要用，符合分层规则。

### 2. 两条 hover 链路上报 tracker

- `CollectionCardHoverRelay`（Game/CollectionPanel/Grid/CollectionCardHoverRelay.cs）：`OnPointerEnter` 成功转发后 `NotifyHover(_card)`；`OnPointerExit` / `TryInvokeHoverOut` / `Clear` 后 `NotifyHoverOut(_card)`。注意 `Clear` 里要在 `_card = null` 之前上报。
- `NativeCardPreviewHoverRelay`（GameInterop/CardPreview/NativeCardPreviewHoverRelay.cs）：`InvokeHover` 成功（真正调到 OnHover 并置 `_hovered = true`）后 `NotifyHover(_card)`；`InvokeHoverOut` 实际执行后 `NotifyHoverOut(_card)`。

### 3. `NativeCardPreviewReflection` 增加字段访问器

- 新增对 `CardPreviewBase` 的 `_tooltipData`（private）与 `_clientCard`(protected) 的 `FieldInfo` 解析（沿用该类现有的"启动时解析一次 + 可空"风格）。
- 走反射而不是 Publicizer 直接访问，与代码库既有约定一致（NativeCardPreviewReflection / CardTooltipDataFactory 均为此风格）。
- 提供两个小助手：`TryGetTooltipData(Component, out CardTooltipData)`、`TryGetClientCard(Component, out Card)`，以及 `TrySetTooltipData(Component, CardTooltipData)`。注意：这几个助手的返回类型涉及游戏类型 `CardTooltipData`/`Card`，GameInterop 引用游戏 DLL 是允许的。

### 4. `TooltipModifierRefreshController` 增加预览卡刷新路径

在 `TryRefreshCurrentItemTooltip`（Game/Tooltips/TooltipModifierRefreshController.cs:88-117）里，`HasAnyLockedTooltipControllers` 早退之后、现有 `TryResolveRefreshTarget` 之前，插入：

```
if (TryRefreshHoveredPreviewTooltip(tooltipParent))
    return;
```

`TryRefreshHoveredPreviewTooltip` 逻辑：

1. `NativeCardPreviewHoverTracker.Current` 为 null（或 Unity 已销毁）→ false。
2. 反射读预览卡 `_tooltipData` 与 `_clientCard`，任一缺失 → false。
3. 校验当前主 tooltip 确实属于这张预览卡：`Traverse` 拿 `tooltipParent.CardTooltipController.CurrentTooltipData`，与预览卡 `_tooltipData` **引用相等**才继续（防止 tooltip 已切换到别的目标时误刷）。注意刷新一次之后 `_tooltipData` 已被替换为 clone，后续 Ctrl 松开再刷时引用相等依然成立。
4. `var refreshed = CardTooltipDataFactory.Create(clientCard, currentTooltipData);` clone 失败时该工厂会退回 source —— 若返回的还是同一实例，直接 false（避免撞上第 4 节的 `CurrentTooltipData != tooltipData` no-op 守卫，白白 Hide 一次）。
5. `TrySetTooltipData(preview, refreshed)` 写回预览卡。
6. `tooltipParent.HideCardTooltipController();` 然后反射调用预览卡的 `OnHover()`（`NativeCardPreviewReflection` 已有 OnHover MethodInfo；不要走 `NativeCardPreviewHoverRelay.InvokeHover`，它有 `_hovered` 幂等守卫会吞掉这次调用）。
7. 返回 true。加一条 `BppLog.Debug("TooltipPreview", ...)` 便于诊断，风格对齐现有 ResolverSkipped/ResolverMatched 日志。

预览卡路径放在 CardController 路径**之前**：tracker 是 mod 自有状态，命中即说明鼠标在我们的面板预览卡上，判定是确定性的；反之若让 CardController 兜底扫描先跑，在十胜面板开着、下层棋盘卡仍自认 hover 的边角场景可能误刷成游戏卡 tooltip。

不调用 `UpgradeTooltipScheduler.TryScheduleUpgradePreview`（预览卡的原生 hover 本来就不排升级预览，保持与"重新 hover 一次"完全同构）。

## 明确不做 / 已知可接受行为

- `HoldUpgradePreview`（升级预览热键）状态变化同样会触发这条刷新（模式统一解析，无法区分来源）。对预览卡而言只是无害的一次重 Show，接受，不专门过滤。
- 战斗中 `Data.IsInCombat` 时 postfix 本来就不注入附魔段（ItemEnchantPreviewPatch.cs:68-69），刷新后文本与重新 hover 一致 —— 保持 parity，不特判。
- 锁定 tooltip（`HasAnyLockedTooltipControllers`）时现有早退保持不变，预览卡也不刷新。
- 不改 `decompiled/`、不改任何 wire contract、不动 `BazaarPlusPlus.csproj`。

## 触点清单

| 文件 | 改动 |
| --- | --- |
| `src/BazaarPlusPlus/GameInterop/CardPreview/NativeCardPreviewHoverTracker.cs` | 新增 |
| `src/BazaarPlusPlus/GameInterop/CardPreview/NativeCardPreviewReflection.cs` | 加 `_tooltipData` / `_clientCard` 字段访问器 |
| `src/BazaarPlusPlus/GameInterop/CardPreview/NativeCardPreviewHoverRelay.cs` | 上报 tracker |
| `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardHoverRelay.cs` | 上报 tracker |
| `src/BazaarPlusPlus/Game/Tooltips/TooltipModifierRefreshController.cs` | 新增 `TryRefreshHoveredPreviewTooltip` 路径 |

预计净增 ~150 行以内。不新增测试项目；这段逻辑是反射 + Unity 运行时行为，单测只能断言 mock 调用序列，属于 coverage theater。跑 `./run.sh test` 确认无回归即可（注意 exe-runner 的失败要 grep 全量输出，不能只看退出码）。

## 验证

1. `./run.sh build` 通过。
2. 游戏内手工矩阵（通过 Steam 启动，macOS: `open "steam://run/1617400"`）：
   - 图鉴面板：hover 一张可附魔卡 → tooltip 出现后按下 Ctrl → 附魔段出现；松开 Ctrl → 附魔段消失；先按住 Ctrl 再 hover → 仍正常。
   - 十胜推荐面板（跑一局进商店阶段打开）：同上矩阵；并确认下层棋盘的游戏卡 tooltip 未被误刷。
   - 历史面板 board 预览：同上矩阵。
   - 回归：实况游戏卡牌的 Ctrl 刷新行为不变；附魔台（pedestal）AutoOnPedestalChoice 模式不变。
   - `LogOutput.log` 无 `[BPP]` Error。
3. 实施完成后：自查 diff、**保持在 feature 分支上不合并**，交外部 review 后再走 wrap-up（review → commit → merge master → push → 删已并分支）。
