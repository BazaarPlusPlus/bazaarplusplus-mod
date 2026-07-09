# Tooltip 遮挡第三次复现 — 诊断文档

> 状态:诊断中。按仓库规则,该问题已两次修复失败,停止继续试探性补丁,先用本文档锁定候选机制与验证方法。

## 背景

BPP 在卡牌 tooltip 里追加 enchant preview 文本块,该区块反复被游戏原生 UI 遮挡:

1. **首版修复**(PR #10 第一轮):判断为跨 Canvas 遮挡(screen 的 fixed / always-on-top UI 盖住原生 tooltip),给 tooltip Canvas 加 sorting override。用户初测"看着可以"。
2. **二版修复**(PR #10 当前,`codex/fix-tooltip-overlay`):用户再报 Lockbox tooltip 被 Morguloth rewards 面板遮挡。重判为**同 prefab 内部遮挡**:`MonsterBoardTooltip` 与 tooltip 内容同属 `CardTooltipController` prefab,锁定时经 `DesktopTooltipLifecyclePolicy.OnLock → _monsterBoardRenderer.HandleShow()` 显示,Canvas sorting 无法解决同 prefab sibling 顺序。补丁改为:`RenderPassiveEffectTextBlock` postfix 里,当 passive 文本含 BPP preview 头时,(a) 对 RootCanvasComponent/PositioningCanvas/LockModeCanvas 设 `overrideSorting=true, sortingOrder=101`;(b) 依次对 `controller.transform → RootCanvas → CanvasContentRectTransform → TooltipRectTransform → LockModeContainer.transform` 调 `SetAsLastSibling()`。
3. **第三次复现**(2026-07-08 22:55 dev 构建,`BPP 4.4.3.t20260708.225516.dev`):怪物遭遇选择屏,hover 怪物板上的物品(Flint?),物品 tooltip(含 BPP enchant 区块)的标题区被 "Castaway Corsair + REWARDS" 面板遮挡;tooltip 下半部(BazaarPlusPlus 区块)可见。

## 当前问题

二版修复在截图场景下无效。已核实的事实:

- `MonsterBoardRenderer.HandleShow()`(decompiled `MonsterBoardRenderer.cs:41`)→ `MonsterBoardTooltip.Show()`(`MonsterBoardTooltip.cs:126`):只做 `gameObject.SetActive(true)` + DOTween 渐变,**不做任何 sibling 重排或 canvas sorting 修改**——"怪物板显示时把自己排到最后"的假设不成立。
- `MonsterBoardTooltip` 自身无 Canvas 组件引用(grep 无 Canvas/sorting)——它按纯层级顺序绘制。
- `MonsterBoardTooltip` 在 prefab 层级中的位置(相对 `TooltipRectTransform` / `LockModeContainer`)**无法从 decompiled C# 判定**(prefab 资产数据),这是静态分析的边界。
- 遮挡面板显示"怪物名 + REWARDS 行",与 MonsterBoardTooltip 的"carpet + 物品/技能板"描述不完全吻合,**它可能根本不是 MonsterBoardTooltip**。

## 候选机制(按可能性排序)

- **M1 · 补丁调用序列自败**:`SetElevated` 里 `SetAsLastSibling` 的最后一步是 `LockModeContainer.transform`。若 MonsterBoard(或 REWARDS 面板)位于 LockModeContainer 内、且 LockModeContainer 与 TooltipRectTransform 同父,则最后这步把含遮挡面板的容器重新压回 tooltip 内容之上——修复自己把自己盖掉。
- **M2 · 时机竞争**:elevation 只在 `RenderPassiveEffectTextBlock` 时机 assert 一次;锁定流程(`DesktopLockModeController`,含 lock 动画、positioning strategy)在其后发生重排/重父化,覆盖顺序,无人再 assert。重父化还会让补丁的还原状态(记录的 Parent/index)失配。
- **M3 · 遮挡物不是同 prefab 的怪物板**:REWARDS 面板可能是另一个 tooltip controller 实例(怪物卡自己的锁定 tooltip,池化的第二实例)或 choice-screen 的原生 HUD 元素。若是前者:两实例 canvas sorting 相同时按层级顺序绘制,后显示者胜;若是后者:sorting=101 的猜测值可能不够。
- **M4 · overrideSorting 被 Unity 重置**:嵌套 Canvas 在 disable/enable 周期会丢 `overrideSorting`;tooltip 是池化对象,Show/Hide 频繁,而 `RenderPassiveEffectTextBlock` 未必每次显示都重跑(BppTooltipSections 有克隆缓存)→ elevation 状态失效。
- **M5 · 触发条件未命中**:截图里的 BazaarPlusPlus 区块若经 `BppTooltipSections` 克隆区块注入而非 passive text 拼接,`ShouldElevateForPassiveText(text)` 恒 false,整个修复在此路径从未生效。

## 验证方法(主路径临时探针,一次复现判别全部机制)

在 PR 分支加两个 Debug 构建探针(Info 级,`BppBuild.IsDebug` 门控,验证完移除):

1. **`TooltipLayerOverride.SetElevated` 入口**:记录 ownerId、elevated、每个 canvas 的 `(name, overrideSorting, sortingOrder)` 读回值。→ 无日志即 M5;读回 override=false 即 M4。
2. **`MonsterBoardRenderer.HandleShow` 临时 postfix**:dump `_monsterBoardTooltip` 的 transform 全路径 + sibling index,`TooltipRectTransform`/`LockModeContainer` 的 sibling index,以及遮挡时刻活跃的所有 `CardTooltipController` 实例数。→ 判别 M1(层级结构)/ M2(顺序被谁最后改)/ M3(第二实例或不在本 controller)。

复现步骤:进入怪物遭遇选择屏 → hover 怪物板物品至 tooltip 锁定 → 出现遮挡 → 读 `LogOutput.log`。

## 结论(2026-07-08 探针实测,一次复现判别完毕)

**M3 确认;二版修复两半全部打空。**

探针实录(关键行):

- 遮挡物("Castaway Corsair + REWARDS" + 怪物板)属于**主 tooltip 克隆** `Tooltip_CardTooltip_LockMode_P(Clone)[0]`(ctrl=-1140);带 BPP 区块的被遮挡 tooltip 是**另一个克隆** `(Clone)[10]` 内的 `SecondaryCardTooltipController`(-87924)。sibling 调序全部发生在自己克隆内部,永远碰不到遮挡物(M1/M2 无关)。
- 两个克隆的根 canvas `Tooltip_P` **同为 order=150**;引擎对平局的裁决偏向锁定克隆。
- 修复的 canvas 半是双重 no-op:`ElevatedSortingOrder=101 < 150`,`if (order < 101)` 永不触发("screens 保留 100"的假设错误);`overrideSorting=true` 写入根 canvas 读回 False(根 canvas 不可设该属性)。M4/M5 排除(elevation 确有触发)。

**修复(已按实证重写)**:删除全部 sibling 机制与 overrideSorting 写入;elevation 改为把带 BPP 区块的克隆的根 canvas `sortingOrder` 提升为**其原值 +1**(150→151),去除绝对阈值 101 与 `BppOverlaySorting.NativeTooltipForeground` token。恢复逻辑保留 owner 计数与原值还原。

**验证**:同场景复现,预期探针读回 `order=151` 且遮挡消失;通过后移除两个探针并提交。
