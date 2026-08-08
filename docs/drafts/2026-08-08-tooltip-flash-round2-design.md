# Equipment Van / Skill Tooltip 闪烁：根因与最小修复

日期：2026-08-08

前情见 `2026-08-08-equipment-van-tooltip-flash-attempt-report.md`。此前针对 Tooltip teardown、PointerExit、late Show 和 handoff 的方案均未通过游戏内验收，已全部回滚。

## 根因

问题只容易出现在原生 Tooltip 恰好覆盖鼠标的位置，例如三格宽的 Equipment Van 和靠近屏幕边缘的技能：

1. Combat Impact 为了避免棋盘 `CardController` 拆掉配对展示，会在原生 Card Tooltip 完成定位后调用 `SetLockedFlag(true)`。
2. 原生 `BaseTooltipController.ToggleInteractabilityOnCanvas` 会把锁定状态同步到 `CanvasGroup.blocksRaycasts`。
3. 锁定后的 Tooltip 因而成为鼠标射线目标；当它覆盖当前卡牌或技能时，EventSystem 会向原 hover owner 发出合成 `PointerExit`。
4. 配对 Tooltip 随之关闭，鼠标重新落回卡牌，原生 hover 下一帧再次显示 Tooltip，形成“出现 → 消失 → 再出现”的闪烁。

这解释了三个已观察事实：鼠标没有主动移出、问题与 Tooltip 位置相关、Equipment Van 和边缘技能最容易复现。

## 最小修复

保留原生 Tooltip lock，只移除它的鼠标遮挡能力：

- `SetLockedFlag(true)` 后立即把当前 controller 的 `blocksRaycasts` 和 `interactable` 设为 `false`。
- Harmony postfix 监听原生 `ToggleInteractabilityOnCanvas`；只有 controller 仍由 Combat Impact pending/active presentation 持有时，才重新设为 `false`。
- controller 交还原生流程后不再干预，普通锁定 Tooltip 的交互语义保持不变。

没有保留退出 watcher、raycast 查询、延迟清理、tombstone、deferred replay 或逐帧 Info 日志。它们不属于已验证根因，继续存在只会扩大共享 Tooltip 生命周期的风险。

## 验证

- 源码契约测试固定“lock 后立即禁用 raycast”以及原生重新切换 interactability 后的 postfix 保护。
- Architecture Tests 与 PostCombatImpact Tests 全量通过。
- 用户在 Windows 游戏内验证：空白区域移入 Equipment Van、空白区域移入技能均不再闪烁，移回空白可以正常关闭 Tooltip。
