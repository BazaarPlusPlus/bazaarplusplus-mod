# Tooltip Upgrade Preview Debugging

## 问题概述

当前问题不是“已经进入升级预览链路，但显示成了错误的 tooltip”，而是更早地卡住了：

- `Shift` / upgrade modifier 输入是生效的
- 升级预览调度也会发生
- 但没有稳定进入原生 `DisplayUpgradeTooltips(...)`
- 因此也没有进入原生 `HandleUpgradePreview(...)`

换句话说，secondary upgrade tooltip 没有真正被创建出来。

## 当前链路

相关文件：

- `Game/Tooltips/TooltipModifierRefreshController.cs`
- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
- `Game/Input/BppHotkeyService.cs`
- `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs`

当前预期流程：

1. `TooltipModifierRefreshController` 检测 modifier 模式变化
2. 找到当前 tooltip 对应的卡并刷新 primary tooltip
3. 调用 `UpgradePreviewTooltipPatch.TryScheduleUpgradeTooltip(...)`
4. 等待 primary tooltip controller 可用
5. 调用原生 `TooltipParentComponent.DisplayUpgradeTooltips(...)`
6. 原生 `HandleUpgradePreview(...)` 执行：
   - `cardController.EnterUpgradePreview()`
   - primary tooltip 切到 `PrimaryUpgradeTooltip`
   - secondary tooltip 切到 `SecondaryUpgradeTooltip`

## 已确认事实

通过日志确认：

- `ModeChanged mode=Upgrade` 会出现，说明输入检测正常
- `UpgradeScheduleQueued card=... source=controller` 会出现，说明升级调度发生了
- 没有出现 `DisplayUpgradeTooltips`
- 没有出现 `HandleUpgradePreview`

因此当前失败点在：

- 调度成功之后
- 原生 secondary upgrade tooltip 真正显示之前

## 为什么以前能工作

早期实现依赖一个隐含前提：

- 当前 hover 的卡
- `IsCursorOverCard` / `IsHovering` 命中的卡
- 当前 primary tooltip 正在显示的卡

这三者大多数时候是同一张卡。

因此旧实现用扫描 `CardAndSkillLookup` 的方式，也能“碰巧稳定工作”。

现在这个前提不再稳定成立，所以旧策略失效了。

## 这次排查中的关键发现

### 1. `EnterUpgradePreview()` 不是主要缺口

原生 `TooltipParentComponent.HandleUpgradePreview(...)` 会负责：

- `cardController.EnterUpgradePreview()`
- 设置 primary / secondary tooltip 的 display mode

所以只要真正进入这条原生路径，就不会漏掉升级预览状态切换。

### 2. 刷新控制器的显式路径没有稳定命中当前卡

`TooltipModifierRefreshController` 中加过以下日志：

- 模式变化
- 是否找到 hovered item
- 是否拿到 primary tooltip controller
- 是否真正调用 `TryScheduleUpgradeTooltip`

实际运行里，`ModeChanged` 能看到，但其余链路日志没有稳定出现，说明这层“通过 hover 状态找当前卡”的策略不可靠。

### 3. `ShowTooltips` postfix 会误调度非目标卡

之前日志里一度能看到一次操作排队多张卡，例如：

- `Chains`
- `Black Mamba`
- `Cinders`
- `Hunting Knife`
- `Waterskin`

这说明 `CardController.ShowTooltips` 的 postfix 过宽，会给不是当前目标的卡也排队。

后来已加保护：

- 只有当前 `IsCursorOverCard` / `IsHovering` 的 controller 才允许从 postfix 隐式进入调度
- 显式刷新传入 `tooltipData` 的路径不受这个限制

但即使过滤后，问题仍然存在。

### 4. 当前更像是“找错卡”，不是“显示错 tooltip”

日志表明：

- 输入正常
- 调度正常
- 原生 secondary upgrade tooltip 不显示

因此最像的根因是：

- 当前刷新逻辑没有准确定位“正在显示 primary tooltip 的那张卡”
- 导致后续 `DisplayUpgradeTooltips(...)` 没有绑定到正确对象

## 当前最可能的根因

`TooltipModifierRefreshController` 仍然依赖：

- `Data.CardAndSkillLookup`
- `controller.IsCursorOverCard`
- `controller.IsHovering`

来推断“当前 tooltip 属于哪张卡”。

但在当前实际运行环境里，这个推断已经不可靠。

更可靠的来源应该是：

- `TooltipParentComponent` 当前 primary tooltip controller
- 或 primary tooltip 当前绑定的 `CurrentTooltipData`
- 或当前 primary tooltip 的 `CurrentCard`

也就是直接从“当前正在显示的 tooltip”反推当前卡，而不是从全局 hover 状态猜当前卡。

## 与主问题无关但会干扰排查的噪音

日志中还有几个高频问题，会淹没 tooltip 线索，也可能影响体感性能：

- `RunLoggingModule`: `Run ... already has terminal status and cannot be recreated`
- `RunUploadController`: 在非 live run 时扫描并上传失败的 run-bundle
- `MonsterPreviewBoardRenderTarget`: board 丢失后反复重建

其中 monster preview 的一部分高频 `Info` 日志已降为 `Debug`。

## 建议的下一步

优先级最高的修正方向：

1. 重写 `TooltipModifierRefreshController` 的目标卡定位方式
2. 不再扫描 hover 标记
3. 改为直接从 `TooltipParentComponent` 当前 primary tooltip controller / current tooltip data 反推当前卡
4. 基于这张卡执行刷新和 upgrade schedule

如果这一步成立，secondary upgrade tooltip 才有机会稳定进入原生 `DisplayUpgradeTooltips(...)`。
