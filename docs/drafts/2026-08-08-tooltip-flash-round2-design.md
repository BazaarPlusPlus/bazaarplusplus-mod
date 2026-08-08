# Equipment Van / Skill Tooltip 闪烁 — 第二轮:证据仪表 + 单点修复

日期:2026-08-08。前情:`2026-08-08-equipment-van-tooltip-flash-attempt-report.md`(五次尝试 A–E 全部回滚)。本轮遵守该报告 §9 的纪律:不叠加生命周期补丁,先建立可归因的帧级证据;唯一落地的行为修复必须由**已静态坐实**的机制支撑。

## 根因定位(用户实机观察 + 源码闭合)

用户观察:出问题的场景都是 **原生 tooltip 恰好显示在鼠标位置**。源码闭合:

- 原生 `ToggleInteractabilityOnCanvas` 把 `blocksRaycasts` 与 **isLocked** 耦合(BaseTooltipController.cs:333-337),`ToggleCanvasGroup(true)` 在显示/定位完成时调用它(CardTooltipController.cs:1136/1817/1843);
- 原版 recap 悬停从不加锁 → tooltip 从不挡射线 → 指针永远留在物品上;
- **BPP 在 ShowWhenReady 里 `primary.SetLockedFlag(true)`**(防棋盘 CardController 拆台)→ 下一次 Toggle 后 `blocksRaycasts=true` → 光标下的 tooltip 抢走 EventSystem 指针 → 对 recap 物品发出**合成 OnPointerExit** → 双方清理 → tooltip 消失 → 指针落回物品 → 重新进入 → 循环 = 肉眼可见的闪烁;
- 宽体物品(Equipment Van 占 3 格)与边缘技能图标最容易让 tooltip 盖住光标,与复现分布一致;上一轮日志"指针未动却 pointer_exit 后又 show_handoff"即此合成 exit。

修复:**锁保留,射线放行**——BPP 持有 primary/auxiliary 期间强制 `blocksRaycasts=false`(锁点即时压制 + `ToggleInteractabilityOnCanvas` postfix 防原生复写)。已知取舍:配对展示期间 tooltip 不可鼠标交互(嵌套词条悬停本就会触发 foreign aux show 拆台,净收益为正)。

## 新确认的源码事实(相对上一轮报告新增)

- **F1 原生重显引擎**:`RecapItemVisualController.OnMouseOver` 与 `OnPointerMove`(decompiled/TheBazaarRuntime/TheBazaar/RecapItemVisualController.cs:272,293)在指针停留期间**每帧**检查 `IsCardTooltipDisplayed`,不显示就重新 `ShowTooltip()`。任何一次 primary 被隐藏,只要指针还在卡上,下一帧原生就会自动重显。"消失后原生再次出现"不需要迟到 continuation 也会发生——它是设计使然的重试循环。
- **F2 迟到 Show 无复查**:`TooltipParentComponent.ShowCardTooltipController` 为 `async void`(TooltipParentComponent.cs:302),`await` 后不复查指针/隐藏状态;`CardTooltipController` 属性惰性重取(:100-115),可复活已清理的 tooltip。
- **F3 Hide 静默失败且无重试方**:`HideCardTooltipController` 在 card-to-card transition 活跃期(每次 Show 都会 `BeginCardToCardTransition(0.25s)`)直接 `return`(:361-365)。快速进出(<0.25s)时,BPP 与原生双方的 Hide 全部落空,且没有任何一方重试 → tooltip 永久滞留。这是症状 3 的充分机制,**纯静态可证**。
- **F4 重试判据现成**:`GetCardTooltipController(card)`(:483)仅在"该卡仍占据 controller"时非空;hide 成功(ClearCurrentCard)后为 null。它区分"吞掉了"与"关掉了",正是上一轮报告 §9.3 要求拆开的两个事件。

## 本轮改动(两项,均在 PostCombatImpactController 主路径)

### 1. 修复:退出后 Hide 看护(仅针对症状 3)

指针离开(pointer_exit / locked_pointer_exit / recap 结束)完成清理后,启动有界看护协程(≤45 帧):

- 每帧检查 `GetCardTooltipController(exitedCard)`;为 null(已隐藏或被新卡接管)即停;
- 期间出现新 hover(`_hoveredRequest != null`)或 recap 关闭即停,不与新展示竞争;
- controller 被玩家锁定(右键 pin)即停,尊重锁;
- 否则重发 `HideCardTooltipController()`——transition 窗口(≈15 帧)结束后必然成功。

与失败尝试的区别:不延迟 exit、不做 raycast、不碰 EventSystem 状态(≠尝试 C);不隐藏/伪完成原生节点、无 tombstone(≠尝试 B/D);不引入 handoff 语义(≠尝试 E)。它只是把游戏自己"发出但被吞"的 Hide 在窗口后补发,判据用游戏自己的公开查询。

### 2. 仪表:tooltip 生命周期 Info 事件(为症状 1 归因)

上一轮的 trace 全是 Debug 级(BepInEx 磁盘日志默认不收),且没有帧号与"谁触发"。新增 Info 级事件 `post_combat_impact.tooltip.lifecycle`,字段:phase / detail / frame(Time.frameCount)/ card。只在状态转换时发射。埋点:

- hover_enter / hover_exit(含 origin)
- native_show(OnNativeTooltipPreparing:每次原生 primary 接管,含是否匹配当前 hover)——捕捉 F1/F2 的重显
- native_changing(reset / clear / disable 三个来源区分)
- aux_show(matched / unmatched+原因)、aux_hide(displaced / takeover / none)
- hide_primary(每个调用点:stop_pending / active_selection / fail / watch_retry)
- settle_abort(**IsCurrentPresentation 具体哪个子条件失败**:hover_changed / recap_closed / primary_not_shown / controller_mismatch)
- revealed(settle 完成)

症状 1(进入即闪)的候选触发点是:unmatched aux show、native_changing、settle_abort 之一;这些事件带帧号后,一次复现即可指认。**本轮不对症状 1 做行为修复。**

## 验证

- 静态:build 0 警告、PostCombatImpact.Tests、Architecture.Tests、format-check。
- 实机(用户):按上一轮报告 §9 验收序列。症状 3(快速进出后关不掉)预期消失;症状 1 若仍现,取 `LogOutput.log` 中 lifecycle 事件按 frame 排序即可读出闪烁三段(显示→消失→重显)各自的触发者。
