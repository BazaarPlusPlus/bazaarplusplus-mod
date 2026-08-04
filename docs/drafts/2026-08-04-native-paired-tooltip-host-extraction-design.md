# Native Paired Tooltip Host 抽取设计（修订版，待确认）

日期：2026-08-04
状态：**待用户确认，确认前不开始实现**
基线：`master` @ `952d8b23`
来源：用户初版计划 + 独立 red-team review（只读）+ 主 agent 抽验

> 本文是修订版。凡与初版计划冲突之处，以本文为准，冲突原因逐条给出 `file:line` 证据。
> 除特别标注外，所有代码引用均相对仓库根目录；`NativePostCombatImpactTooltipView.cs` 简写为 **View**
> （完整路径 `src/BazaarPlusPlus/Patches/PostCombatImpact/NativePostCombatImpactTooltipView.cs`，2362 行）。

---

## 0. 目标与范围（不变）

只把 Combat Impact 自绘 Tooltip 中与业务无关的**原生双 Tooltip 宿主基建**抽到 `GameInterop/Tooltips/`。

**硬约束**：用户可见行为、视觉、布局、日志语义保持不变。

**明确不做**：不抽 `BuildCaused`/`BuildReceived`、分组/行/图标/文案；不抽 `CombatImpactPerspective` 与 Combat Impact DTO；
不改 hover/锁定/等待 native tooltip/重试/requeue 流程；不合并 `BppTooltipSections` 或 `NativeCardTooltipContentRefresher`；
不移动 View 文件本身；不新增其他 consumer；不接管主 Card Tooltip 的 native 定位权。

---

## 1. 执行位置与既有事实（已核实，修正初版）

| 初版假设 | 实际情况 | 证据 |
|---|---|---|
| 工作区在 `.codex/worktrees/2f14/` | 该 worktree 处于 `952d8b23` detached HEAD 且**完全干净**，与 master 同 commit。**在主 checkout 施工**，无需 installer/decompiled symlink | `git worktree list` |
| `GameInterop/Tooltips/` 为新增目录 | **已存在**，含 `NativeCardTooltipContentRefresher.cs`。实为往已有目录加 3 个文件 | `src/BazaarPlusPlus/GameInterop/Tooltips/` |
| — | `PostCombatImpact.Tests`、`Architecture.Tests` 均存在且带 `Microsoft.NET.Test.Sdk`，`dotnet test` 是正确跑法 | 各自 `.csproj` |
| — | 架构测试惯例是**源码文本扫描**（扫 `using` 行 / forbidden token）+ 文件路径白名单 | `tests/Architecture.Tests/CoreLayeringTests.cs:18-69`、`NativeCardPreviewArchitectureTests.cs:26-40` |

### 1.1 一处被推翻的判断（主 agent 自查有误，review 纠正）

初版分析曾担心 "`ComponentMount` 每次挂载都 new 一个 View，跨场景累积 stale session"。**该判断错误**：

- `MountAll` 只在 `Plugin.Awake` 调用一次（`src/BazaarPlusPlus/Plugin.cs:98`），View 是单实例。
- teardown 顺序是先 `UnmountAll`（`Plugin.cs:160`）后 `_composition.Dispose()`，且 `ComponentMount.Unmount` 用
  `DestroyImmediate`（`src/BazaarPlusPlus/Core/Runtime/ComponentMount.cs:27-32`），`OnDestroy` 同步执行
  `_view?.Dispose()`（`src/BazaarPlusPlus/Game/PostCombatImpact/PostCombatImpactController.cs:1055-1062`）。

结论：**session 一定先于 host 释放，生命周期顺序安全**。原"风险 6"降级为 §4 末尾的一条 host 无状态化约定。

---

## 2. 必须推翻初版文字的 6 处（阻断项）

### B1. "cleanup 必须 once-only" 与现状直接冲突 —— 清理是**两阶段**的

`restoreNativeContentVisibility` 是二态语义，不是一个可有可无的开关：

- `CleanupCustomContent` 的入口闸门把 `_preparedNativeHost != null` 也算作"有活内容"（**View:620-627**）。
- `RestorePreparedNativeHost` 在 `restoreContentVisibility == false` 时**不清空** `_preparedNativeHost`（**View:939-941**），
  字段清零块（**View:650-667**）里也没有它。
- 因此 `CleanupCustomContent(false)` 之后，下一次调用的闸门仍为真，会**完整再跑一遍**：
  `_renderGeneration++` → `RestorePreparedNativeHost(true)` → `SetLockedFlag(false)`。
- 这正是 `Dispose()` 的设计（**View:2357-2361**）：`Hide()` 可能已走完 `CompleteAnimatedHide → CleanupCustomContent(false)`
  （**View:607**），随后 `Dispose` 的第二次调用才真正恢复 native header/body/divider 可见性。
- **11 条清理调用点**中只有 2 条传 `true`（走默认参数）：`OnNativeAuxiliaryTooltipShowing`（**View:508**）与
  `Dispose`（**View:2360**）；其余 **9 条**显式传 `false`：**View:112, 131, 139, 162, 189, 231, 240, 521, 607**。
  （**View:618** 是方法定义，不是调用点。）

**若按初版字面实现幂等 cleanup**：`Dispose` 的第二遍变 no-op，native 辅助 tooltip 的 header/body/divider 永久停在
`SetActive(false)`（`Show` 在 **View:222-224** 关掉它们）。症状是插件卸载或场景切换后游戏自己的辅助 tooltip 变空框，
**且不产生任何日志**。

**修订**：契约改为
```csharp
session.Release(bool restoreNativeContent);
// false: 释放呈现，保留 native host 快照
// true : 归还快照并解除 session
```
实现时附一张 **11 个调用点**各传什么值的核对表，逐点比对迁移前后。

---

### B2. `_renderGeneration` 是跨 host/view 的**单一**世代计数器，且递增顺序是载荷

唯一计数器 `_renderGeneration`（**View:84**）被四类使用者共享：

| 使用者 | 位置 | 归属 |
|---|---|---|
| `Show` 开新一代 `++_renderGeneration` | View:214 | host |
| fade 协程晚到拒绝 | View:537, 554, 602 | host |
| `CleanupCustomContent` 作废一切 | View:632 | host |
| preview/hero 异步任务身份 + `_pendingPreviewCount` 记账 | View:1605, 1681-1682, 1691, 1715-1716 | **view** |

现状清理顺序（**View:631-648**）：
`StopVisibilityFade()` → `_renderGeneration++` → `DisposeNativePreviews()` → 销毁 UI → 恢复 native。
`_pendingPreviewCount` 只在 `generation == _renderGeneration` 时递减（**View:1681-1682**），清理时直接置 0（**View:663**）。

**若按初版 "View 先 → Host 后"**：`_renderGeneration++` 的位置丢失。在 `scope.AcquireAsync` 的 continuation 与
`Cancel()` 竞争的窗口里，已取消的 preview 仍会通过 generation 检查、把 session 加进 `_previewSessions`（**View:1661**）
并 `owner.Reveal`（**View:1662**），而 `_previewSessions.Clear()`（**View:989**）已经跑过 —— 泄漏一个 native card preview。
更糟的表现：`_pendingPreviewCount` 不归零 → `IsReadyToReveal` 永久为假 → tooltip 直到 120 帧超时才放弃
（`PostCombatImpactController.cs:560-576`），**只留一条 `EntityPreviewCreateTimedOut`，极难归因**。

**修订**：
- session 暴露只读世代 token `session.Generation`；view 的 preview 任务改为与该 token 比较，**不再自持 generation**。
- 清理契约改为 **host → view → host 三段**：
  1. host：`StopVisibilityFade()` + bump generation
  2. view：取消 preview task、释放 preview session/scope、清业务引用
  3. host：销毁 content/background、恢复 native 状态与锁

---

### B3. `Position(anchor) -> placementResult` 形状错误

现状 `Position`（**View:332-451**）的实际时序：

1. host：双重强制重建 + `ApplyNativeHeight`（View:346-351）
2. host：canvasBounds / primaryBounds / `CanvasUnitsPerLocalUnit`（View:356-363）
3. host：左右侧选择（View:366-381）
4. host + **view**：`ApplyTooltipWidth`（View:385-388，其中 `_metricColumns` 属 view）
5. host：重建 + `ApplyNativeHeight`（View:389-392）
6. **view：`FitActiveContentToCanvas`（View:393）** —— 循环体内回调 host 的
   `RebuildAfterHeightBudgetChange`（View:773, 788, 798）与 `FitsCanvasHeight`（View:782, 791），
   后者读 `auxiliary.backgroundImage.rectTransform` 的 canvas 局部包围盒（View:808-814）
7. host：`impactDelta` + `TranslateRect`（View:395-407）
8. host：竖直修正（View:409-418）
9. host：overflow / 碰撞 / 窄宽判定（View:420-434）
10. view：三条 reason code（View:435-449）

第 6 步夹在 host 定位流程正中间，且是 **view→host→view→host** 的跨界重入循环
（最坏迭代次数 = 所有 group 与 detail row 之和）。初版把 `FitActiveContentToCanvas` 整体划归 view，
同时声称 `Position` 是 host 的 "measure → choose side → shrink → position → return result"，两者不能同时成立。

**修订**：
```csharp
PlacementResult session.Position(Transform anchor, IPairedContentBudget content);

interface IPairedContentBudget
{
    void RestoreAll();        // view: block.Restore() + 隐藏 more row
    bool TryShrinkOneStep();  // view: 隐藏下一行/下一块 + 更新 more 文案；false = 无可裁剪
}
```
host 在第 6 步内部驱动 `while (!FitsCanvasHeight(...) && content.TryShrinkOneStep()) Relayout();`。
host 保有 measure/relayout，view 只回答"下一步裁什么"，消除双向重入。

---

### B4. "host 一个 active session 就串行化了 native 单例" —— 不成立

- `AuxiliaryTooltipController` 确是游戏级单例：`TooltipParentComponent` 只持一个 `_auxiliaryTooltipController`
  （`decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs:61, 142-156`），
  且 `TooltipParentComponent` 自身 `DontDestroyOnLoad` + 单实例守卫（同文件 `:180-183, 187, 210-213`）。
- **但 BPP 内部存在第二个绕过 view 的驱动方**：
  `CurrentReplayRecordingButtonController.ShowTooltip()` 直接调
  `Data.TooltipParentComponent?.ShowAuxiliaryTooltipController(...)`
  （`src/BazaarPlusPlus/Game/CombatReplay/CurrentReplayRecordingButtonController.cs:257-261`），
  `HideTooltip()` 直接 `HideAuxiliaryTooltipController()`（同文件 `:276`），
  并且**每帧** `tooltip.PositionOverUI(_cloneRect)` 重定位同一个单例（同文件 `:297`）。
  它挂在 `FightMenuDialog` 设置按钮旁（`src/BazaarPlusPlus/Patches/Combat/CurrentReplayRecordingButtonPatch.cs:60-70`）。
- 今天真正的仲裁**不在 view，而在 Harmony patch 链**：
  `PostCombatImpactRecapPatch.cs:123-151` → `PostCombatImpactModule` → `PostCombatImpactController.OnNativeAuxiliaryTooltipShowing/Hiding`
  （`PostCombatImpactController.cs:750-821`）→ view。这些 patch 是 PostCombatImpact 私有的；
  plugin-lifetime 的 host 收不到任何 native 事件，除非新增通用 patch 注册 —— 而这被范围明确排除。

**修订**：边界表该项改为
- **host**：`session.OwnsPrimary(controller)` / `session.OwnsAuxiliary(controller)` / `session.ReleasePrepared(controller)`
- **view/feature**：三个 `OnNative*` 的 bool 返回语义与全部后续动作（`NativeAuxiliaryDisplaced` 日志、
  `HideCardTooltipController()`、requeue 决策，`PostCombatImpactController.cs:777-787, 797-821`）**原样保留**

**极易丢失的副作用（必须显式保留）**：`OnNativeAuxiliaryTooltipShowing` 在"**不是**我的 controller"分支上，
仍然会调 `RestorePreparedNativeHost(controller)`（**View:502-506**）—— 即把一个已 prepare 但未激活的 controller
快照归还并清空。漏掉这行的后果是 native 布局的 padding/anchors 被**永久改写**。

同时在设计文档中记录：`CurrentReplayRecordingButtonController` 是同一 native 单例的第二个非受管驱动方，
host **无法也不打算**串行化它；今天靠 patch 链兜底，重构后仍靠它。

---

### B5. fade 协程挂在 native MonoBehaviour 上；generation 防不了"永不到达"

- `StartVisibilityFade` 用 `auxiliary.StartCoroutine(...)`（**View:538**），宿主是游戏的 `AuxiliaryTooltipController`。
- 启动前有保护（`auxiliary == null || !auxiliary.isActiveAndEnabled` 走同步路径，**View:529-535**），
  但**运行中**宿主被失活/销毁没有任何保护 —— 协程静默终止，`_visibilityFade` 悬空，
  `CompleteAnimatedHide(generation)`（**View:581**）永不执行，因而 `HideAuxiliaryTooltipController()`（**View:609**）也不执行。
- `StopVisibilityFade` 在 `_activeAuxiliary == null` 时只丢句柄不 Stop（**View:590-598**），依赖 generation 让协程自杀。
- 现状兜底在 patch 链：`AuxiliaryTooltipController.StartTooltipFadeOut` 的 prefix
  （`PostCombatImpactRecapPatch.cs:142-151`）→ `OnNativeAuxiliaryTooltipHiding` → cleanup。

初版第 4 条只说"generation/identity 拒绝晚到回调"，解决的是"回调来了但过期"；此处失败模式是"回调永远不来"，
恢复信号从 feature 的 patch 进来。host 若拥有 fade 却不暴露强制收尾入口，这条兜底会断。

**修订**：session 暴露 `session.ForceSettle(reason)`（同步跳过 fade、直接执行 `CompleteAnimatedHide` 等价物），
由 view 的 `OnNativeAuxiliaryTooltipHiding` / `OnNativeTooltipChanging` 调用。
并把"协程宿主是 native MonoBehaviour、其生命周期不受 host 控制"作为显式契约写进 `NativePairedTooltipContracts.cs` 注释。

---

### B6. `RelayoutAtomically(anchor, mutation)` 表达不了"失败即回滚并二次定位"

`SetPerspective`（**View:289-330**）的实际结构：
- 遮罩：`visibleAlpha = _preparedAuxiliaryGate?.Alpha ?? 1f` → `SetAlpha(0f)`（View:306-307），`finally` 恢复（View:328）
- 主路径：切换 → `ApplyPerspectiveVisibility` → 重建 → `ApplyNativeHeight` → `Position`（View:310-315）
- **失败路径**：还原 perspective → 再 `ApplyPerspectiveVisibility` → 再重建 → 再 `ApplyNativeHeight` →
  **再跑一次完整 `Position` 且丢弃返回值**（View:317-322）

即一次 `SetPerspective` 最多跑两次 `Position`，两次都会重跑 `FitActiveContentToCanvas`（改内容可见性）
和三条 `LogPlacementDegradationOnce`（View:435-449）。而后者在 `degraded == false` 时会复位 `wasLogged`
（**View:1959-1978**）—— **日志条数是可观测的**。

**修订**（已按用户复核收窄边界）：

初稿曾提出 `session.TryRelayout(anchor, apply, revert, out result)`。**该形状越界并已废弃** —— 它把
"失败判定 → 是否回滚 → 回滚成什么" 这条纯 Combat Impact 的决策链交给了通用 host 编排。
host 不该知道"perspective"，更不该知道"回滚"。

正确切法：**host 只提供遮蔽期间的 atomic layout primitive 与 placement result；apply/revert 与二次 Position 全部留 View。**

```csharp
// host 侧只新增两个原语
IDisposable session.BeginMaskedLayout();   // 进入时置 alpha=0；Dispose 时恢复原 alpha
void        session.RebuildLayout();       // ForceRebuildLayout + ApplyNativeHeight
```

View 的 `SetPerspective` 变成（结构与 View:303-329 逐行对应）：

```csharp
using (session.BeginMaskedLayout())          // 取代 View:306-307 + finally View:324-329
{
    _activePerspective = perspective;        // View:310  业务
    ApplyPerspectiveVisibility(perspective); // View:311  业务
    session.RebuildLayout();                 // View:312-313
    var result = session.Position(anchor, budget);   // View:314
    if (result.Positioned)
    {
        LogPlacementDegradations(result);    // View:435-449 的三条 reason code
        return true;
    }

    _activePerspective = previousPerspective;         // View:317  业务
    ApplyPerspectiveVisibility(previousPerspective);  // View:318  业务
    session.RebuildLayout();                          // View:319-320
    var rollback = session.Position(anchor, budget);  // View:321
    LogPlacementDegradations(rollback);       // 关键：回滚那次的 result 也必须过日志
    return false;
}
```

契约必须写明的两点：

1. **回滚那次 `Position` 的 `PlacementResult` 也要交给 View 记日志。** 现状 View:321 丢弃返回值，
   但 `LogPlacementDegradationOnce` 在 `degraded == false` 时会复位 `wasLogged`（**View:1959-1978**），
   所以第二次 `Position` 的结果**已经**通过复位影响了后续日志条数。迁移后必须显式把它喂回同一套 reason code 逻辑，
   否则条数变化 —— 而条数是可观测的。
2. **`BeginMaskedLayout` 的 alpha 恢复必须覆盖 body 抛异常的情形**（即 `Dispose` 走 `finally` 语义）。
   `PostCombatImpactController.cs:113-119` 依赖 `SetPerspective` 抛出后自行 `HideActiveSelection`，
   但 alpha 必须已复原，否则整对 tooltip 残留在 alpha=0。

---

## 3. 边界表修订

### 3.1 归 host（业务无关，已核实零 Combat Impact 耦合）

`CanvasGroupGate`（View:2197-2259，构造签名已是 `object controller`）、`NativeAuxiliaryHostState`（View:2261-2355）、
`TryCreateNativeBackground`/`CopyRectTransform`/`CopyImage`（View:834-924）、`PrepareNativePresentation`（View:679-709）、
`ApplyNativeHeight`（View:744-754）、`ForceRebuildLayout`（View:822-832）、`FindDescendant`（View:1916-1933）、
`GetCanvasLocalBounds`（View:1870-1887）、`GetPrimaryVisibleBounds`/`GetPrimaryFrameBounds`（View:1889-1914）、
`CanvasUnitsPerLocalUnit`（View:1935-1946）、`TranslateRect`（View:1980-1992）、`Union`（View:1994-2000）、
`Inset`（View:2002-2012）、`ResolveVerticalAdjustment`（View:1948-1957）。

以上**没有任何一个**引用 `CombatImpact*` 类型或 `PostCombatImpact*` 日志。

### 3.2 留在 View（不变）

Header、`IsReadyToReveal`、`BuildCaused`/`BuildReceived`、Caused/Received 视角状态、`SetPerspective` 业务选择、
native preview 创建与释放、`_metricColumns` 调整、`FitActiveContentToCanvas` 的裁剪策略、`ImpactContentBlock`、
`+N more`、标签/颜色/图标/CJK 文案、PostCombatImpact reason-code 日志。

### 3.3 初版遗漏、本次补入的 4 项

| 项 | 现状证据 | 归属决定 |
|---|---|---|
| `HideAuxiliaryTooltipController()` | View 内两处直接调游戏全局 API：`PrepareNativePrimary` 位移分支（View:114）、`CompleteAnimatedHide`（View:609） | 归 host；但 **View:607-609 的"cleanup 返回 true 才 hide"条件原样保留** |
| `PlacementEpsilon = 0.5f` 被三种语义复用 | 几何（View:366, 370, 411, 424-432, 814, 1950）／CanvasGroup 交互阈值 `_group.alpha >= 1f - PlacementEpsilon`（View:2241）／Hide 是否需淡出 `visibleAlpha <= PlacementEpsilon`（View:482） | 在 `NativePairedTooltipContracts.cs` 定**单一** `const float Epsilon = 0.5f`，两侧共用；注释写明它同时承担几何与 alpha 两种语义（**历史事实，非设计**） |
| `SetLockedFlag` set/clear 不对称且无条件清除 | set 在 feature（`PostCombatImpactController.cs:332`）；clear 在 View:476、View:648 与 feature `:917, 942`。`isLocked` 是游戏自己的锁模式标志，`TooltipParentComponent.HideCardTooltipController` 会因它早退（`TooltipParentComponent.cs:350-353`），`DesktopLockModeController` 也会写它 | **迁移不改行为**，但不得包装成"generic host restores locks"；记为行为债（§6） |
| host 对主 tooltip 的介入范围 | host 还会：给 `Tooltip_Main` 后代装 `CanvasGroupGate`（View:117-119）、fade 中同时淡入主 gate（View:564）、清主 tooltip lock（View:476, 648）、强制重建主 tooltip 定位 rect（View:347, 349） | 范围表述改为"host 拥有主 tooltip 的**可见性门控与布局重建**，但不接管其 native 定位" |

### 3.4 顺序敏感点（必须冻结，不得在迁移中挪动）

`NativeAuxiliaryHostState.Capture` 相对 feature 的 `AssignTooltipFrame` 是顺序敏感的：
Controller 先调 `auxiliary.AssignTooltipFrame(request.Card.Tier)`（`PostCombatImpactController.cs:460`），它会写
`backgroundImage.sprite`；`NativeAuxiliaryHostState` 捕获 `_backgroundSprite`（View:2306）。
走 `PrepareNativeAuxiliary` 路径时捕获在 `AssignTooltipFrame` **之前**（View:142），走 Show 补捕时在**之后**（View:195），
随后 `Show` 又用 primary 的 sprite 覆盖（View:234-235）。

这是既有的不一致（恢复的 sprite 取决于走哪条路）。**迁移中若改变这两个 capture 点的相对顺序，会产生"辅助 tooltip
边框 tier 错乱"的视觉回归**。两个 capture 点原地冻结。

---

## 4. Session API（修订后完整形状）

```csharp
// GameInterop/Tooltips/NativePairedTooltipContracts.cs
internal const float Epsilon = 0.5f;   // 同时承担几何判定与 CanvasGroup alpha 阈值（历史事实）

internal interface IPairedContentBudget
{
    void RestoreAll();
    bool TryShrinkOneStep();
}

internal readonly struct PlacementResult { /* side, overflowed, tooNarrow, topAdjusted, ... */ }
internal readonly struct PairedTooltipOptions { /* width、gap、canvas margin、fade duration */ }
internal enum PairSide { None, Left, Right }
```

```csharp
// GameInterop/Tooltips/NativePairedTooltipHost.cs
NativePairedTooltipSession Acquire(object owner);

// session
int  Generation { get; }                       // B2：view 的 preview 任务与之比较
void PreparePrimary(CardTooltipController primary);
void PrepareAuxiliary(AuxiliaryTooltipController auxiliary);
bool TryOpen(primary, auxiliary, PairedTooltipOptions options);   // R7：失败返回 false，不抛异常
bool AttachContent(GameObject contentRoot, Action<float> onContentWidthChanged);
PlacementResult Position(Transform anchor, IPairedContentBudget content);         // B3
IDisposable     BeginMaskedLayout();       // B6：遮蔽作用域，Dispose/异常均恢复原 alpha
void            RebuildLayout();           // B6：ForceRebuildLayout + ApplyNativeHeight
void Reveal();
void Hide();
void ForceSettle(string reason);                                                  // B5
bool OwnsPrimary(CardTooltipController c);                                        // B4
bool OwnsAuxiliary(AuxiliaryTooltipController c);                                 // B4
void ReleasePrepared(AuxiliaryTooltipController c);                               // B4（含 View:502-506 副作用）
void Release(bool restoreNativeContent);                                          // B1
```

**R7（失败语义必须保留）**：Controller 对两类失败的日志不同 —— `Show` 返回 false → `AuxiliaryTooltipContentUnavailable`
（`PostCombatImpactController.cs:482-490`）；`Show` 抛异常 → `TooltipRenderException`（同文件 `:471-480`）。
现状 `Show` 有三个 false 出口（View:180-186 前置校验、View:225-233 背景图缺失、View:238-242 背景克隆失败）。
host 的所有 open/attach 失败**一律以 `false` + 已完成的自清理返回**，不得改用异常。

**host 不实现 `IDisposable`**（修订初版的"最终 Dispose 共享 Host"）：
`BppComposition.Dispose()`（`src/BazaarPlusPlus/BppComposition.cs:307-318`）不 dispose 任何 GameInterop host，
`_nativeCardPreviewHost` 即先例，`NativeCardPreviewHost` 也不是 `IDisposable`。可释放单元是 session。

**host 必须无状态化到只持 `_activeSession`**（由 §1.1 推导）：session 释放后 host 不得残留任何 Unity 引用，
否则会跨场景持有已销毁的 `AuxiliaryTooltipController`。生命周期顺序本身是安全的（`Plugin.cs:98/160`、
`ComponentMount.cs:27-32`、`PostCombatImpactController.cs:1055-1062`），这条约束防的是引用滞留而非顺序错乱。

---

## 5. 执行清单与验收标准 → GitHub issue #204

执行清单（Step 1–8）与全部验收标准已迁至
**[#204 refactor(tooltips): extract native paired tooltip host](https://github.com/BazaarPlusPlus/bazaarplusplus-mod/issues/204)**，
本文档不再保留副本，避免两处漂移。

- **本文档负责**：目标与范围（§0）、既有事实核实（§1）、六条阻断项的根因与证据（§2）、边界表（§3）、Session API 契约（§4）、已知债务（§6）、评审覆盖缺口（§7）。
- **issue 负责**：Step 1–8 执行清单、几何/场景/日志三类验收标准、必测场景清单。

> 设计复核（原 Step 0）**已完成**：独立 red-team review（只读、`file:line` 证据）已执行；
> 主 agent 抽验 B1 / B2 顺序 / B4 / §1.1 共 4 处，全部核实。本文档即修订结果。

## 6. 已知债务（本次不修，登记备查）

1. **`SetLockedFlag(false)` 无条件清除**（View:648）：玩家自锁的主 tooltip 会被 BPP 清理解锁。既有行为，迁移原样保留。
   风险在于把它描述成"generic host restores locks"会给未来 consumer 埋雷 —— 文档层面已澄清。
2. **View 文件位置违反分层**：`NativePostCombatImpactTooltipView.cs` 在 `Patches/PostCombatImpact/`
   （namespace `BazaarPlusPlus.Patches.PostCombatImpact`，View:21），但它不是 Harmony patch，而是实现
   `Game/PostCombatImpact/Ui/IPostCombatImpactTooltipView` 的 feature UI 适配器，按 `CLAUDE.md` 分层规则应在 `Game/`。
   本次为控制 diff 规模不移动，登记为遗留债。
3. **通用性未验证**：host 只有一个 consumer，架构测试证明不了 API 对第二个 consumer 可用。#204 Step 4 的纸面推演是部分缓解，
   不是证明。文档需明确写"通用性在引入第二个 consumer 前是未验证的假设"。
4. **未验证项**：`Tooltip_Main` 后代节点名是否在所有 tier / 平台（Desktop vs Mobile tooltip prefab，
   `TooltipParentComponent.cs:191-193`）下都存在。`FindDescendant` 找不到时 `_preparedPrimaryGate` 静默为 null
   （View:117-119），主 tooltip 不被遮罩。

---

## 7A. 二方适配纸面推演结果（#204 Step 4，已完成）

候选二方：`CurrentReplayRecordingButtonController` 的辅助 tooltip 定位
（`src/BazaarPlusPlus/Game/CombatReplay/CurrentReplayRecordingButtonController.cs:250-319`）。
**只做纸面检查，未写代码、未新增 consumer。**

**结论：当前 API 形状不适配该 consumer，且这是设计边界问题而非签名细节问题。**

证据与原因：

| 维度 | paired host 的假设 | 该 consumer 的实际情况 |
|---|---|---|
| 是否成对 | 必须有 primary `CardTooltipController` + auxiliary 面板两个宿主 | **没有 primary**。它只驱动 auxiliary 单体（`:257-261`），锚点是一个普通按钮 `RectTransform`（`_cloneRect`） |
| 内容来源 | feature 自建 content root 注入 `auxParent`，host 关掉 native header/body | 直接用 native 文本通道 `ShowAuxiliaryTooltipController(rect, offset, text)`，**不注入自绘内容** |
| 定位模型 | 一次性放置在 primary 左/右侧，`ResolvePairOffset` 全部相对 `primaryBounds` | **每帧**重定位到按钮正上方居中（`:297-315`），并调 native 的 `KeepTooltipWithinBounds()` |
| 背景 | 克隆 primary 的渐变遮罩层叠 | 用 native 原始外观，不克隆 |

也就是说，本次抽出来的 host 是"**对 feature 通用**"，不是"**对锚点类型通用**"：它的整个模型是
"auxiliary 贴在一个 primary card tooltip 旁边"。要覆盖上面这个 consumer，至少需要
(a) 锚点从 `CardTooltipController` 放宽到 `RectTransform`，
(b) primary gate / lock 清除 / 背景克隆全部变成可选，
(c) 增加"跟随锚点持续重定位"这一模式。

这三项都不是本次范围，**不做**。价值在于把假设证伪并写下来：

> `NativePairedTooltipHost` 的通用性目前是**"同一种成对形态下的多 feature 通用"**，
> 不是"任意 native tooltip 宿主通用"。在出现第二个**成对形态**的 consumer 之前，
> 通用性仍是未验证假设（§6 债务 3）。

---

## 7. 本次评审未覆盖的部分

给 red-team reviewer 的简报**遗漏了执行清单**，因此 #204 的 **Step 1（行为基线）与 Step 7（游戏内回归）未经独立评审**。
reviewer 仅就"三条不变量能否被测量"给出了意见（已并入 #204 的验收标准）。
如需补评，可将 #204 全文发回同一 reviewer 做一次针对性复核。
如需补评，可将 §5 全文发回同一 reviewer 做一次针对性复核。
