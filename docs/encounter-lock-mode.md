# Encounter 右键 Lock Mode 完整链路分析

## 目标

本文整理原生反编译代码里：

- 右键野怪后为什么会进入“放大查看”界面
- 为什么这个状态下看起来“屏蔽了其他 hover”
- 这条链路里各个类分别负责什么

结论先说：

- 这不是 `EncounterController` 独有机制，而是通用的 `tooltip lock mode`
- 野怪/Encounter 只是复用了卡牌 tooltip 锁定链路
- “屏蔽其他 hover”不是单点开关，而是由“锁定态 + tooltip 吃射线 + 当前卡不失焦”共同造成的

---

## 高层链路

```text
鼠标移到 Encounter
  -> EncounterController.Select(...)
  -> CardController.Select()
  -> ShowTooltips()
  -> TooltipParentComponent.ShowCardTooltipController(...)
  -> CardTooltipController.ShowTooltipController(...)

此时右键释放
  -> InputManager 把 RightClick 映射为 Actions.Lock
  -> CardTooltipController.LockTooltipToggle()
  -> CardTooltipController.Lock()
  -> DesktopLockModeController.Lock(...)
  -> 锁定当前卡 + 移动卡和 tooltip + 打开 lockModeCanvas

锁定后继续移动鼠标
  -> tooltip Canvas blocksRaycasts = true
  -> EventSystem 优先命中锁定 tooltip
  -> 原卡 Deselect() 因 HoveredTooltipIsLocked() 被短路
  -> 视觉上表现为“其他 hover 被屏蔽”

再次右键
  -> CardTooltipController.LockTooltipToggle()
  -> DesktopLockModeController.Unlock(...)
  -> 卡片归位 + tooltip 隐藏 + lock mode 结束
```

---

## 1. Hover 阶段

### 1.1 Encounter 的 hover 入口

`EncounterController` 自己维护一个 `wantsHover`。鼠标移动到卡上后，`Select` 会把它设成 `true`，下一帧 `Update()` 再真正执行 hover 展示。

关键点：

- 只有鼠标真的移动了才会触发 `wantsHover`
- hover 时会调用 `ShowTooltips()`
- 同时还会触发野怪本体的 pulse / VFX

参考：

- [EncounterController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/EncounterController.cs#L140)
- [EncounterController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/EncounterController.cs#L645)

### 1.2 通用卡牌 hover 入口

真正的通用逻辑在 `CardController.Select()`：

- 标记 `_cardState = 1`
- 启动 `CheckForDeselect()` 协程
- 设置 `IsCursorOverCard = true`
- 调 `ShowTooltips()`
- 调 `ShowCardHint(...)`

参考：

- [CardController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/CardController.cs#L900)

### 1.3 Tooltip 的显示

`CardController.ShowTooltips()` 会检查一堆前置条件：

- 应用是否聚焦
- 是否处于过场
- `TooltipParentComponent.CardTooltipsBlocked` 是否为 `false`
- 当前 controller 是否已经 locked
- 当前是否有卡在拖拽

都满足后，调用 `Data.TooltipParentComponent.ShowCardTooltipController(...)`。

参考：

- [CardController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/CardController.cs#L1046)
- [TooltipParentComponent.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs#L296)

---

## 2. 右键锁定阶段

### 2.1 右键不是走 Encounter 的点击逻辑

`CardController.CanBeClicked()` 对 Encounter 返回的是“只接受左键”，所以右键不会走 `SelectEncounterCommand(...)`。

这意味着：

- 左键是“选中/进入 encounter”
- 右键是 tooltip lock 的独立输入链路

参考：

- [CardController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/CardController.cs#L1168)

### 2.2 右键输入来自 InputManager

`InputManager.MapInput()` 把 `UI.RightClick` 映射到 `Actions.Lock`，触发时机是 `OnRelease`。

参考：

- [InputManager.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.Inputs/InputManager.cs#L215)

### 2.3 谁在监听这个 Lock 输入

`CardTooltipController.OnSystemsInitialized()` 里注册了：

```csharp
_inputManager.AddAction(InputManager.Actions.Lock, LockTooltipToggle);
```

所以右键释放时，实际收到事件的是 tooltip controller，不是 card controller。

参考：

- [CardTooltipController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipController.cs#L833)

### 2.4 LockTooltipToggle 的行为

`CardTooltipController.LockTooltipToggle()`：

- 已锁定则 `Unlock()`
- 未锁定且 `CurrentCard != null` 则 `Lock()`

参考：

- [CardTooltipController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipController.cs#L1532)

---

## 3. 放大界面是怎么形成的

实际桌面端锁定逻辑在 `DesktopLockModeController.Lock()`。

它做了几件关键事：

1. 找到当前 tooltip 对应的 `CardController`
2. 把卡提升到 `aboveBoardLayer`
3. 打开 `LockModeContainer`
4. 计算卡片 bounds 和锁定位置
5. 用 DOTween 同时移动：
   - 卡片到锁定展示位置
   - tooltip 到锁定 tooltip 位置
6. 设置 `controller.SetLockedFlag(true)`
7. 让 tooltip canvas 可交互
8. 触发 `Events.TooltipLock`

参考：

- [DesktopLockModeController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/DesktopLockModeController.cs#L12)

这就是用户看到的“右键后卡片放大、tooltip 固定、进入 inspect 状态”的来源。

---

## 4. 为什么会“屏蔽其他 hover”

这部分是最关键的。原生不是简单设一个“禁用所有其他 hover”的全局位，而是以下几层共同形成效果。

### 4.1 锁定 tooltip 会拦截 UI 射线

锁定时执行：

```csharp
controller.SetCanvasInteractable(enabled: true);
```

最终会走到：

```csharp
tooltipCanvasGroup.interactable = isInteractable;
tooltipCanvasGroup.blocksRaycasts = isInteractable;
```

也就是说，锁定态 tooltip 会变成一个真正参与 UI 射线命中的前景层。

结果：

- 鼠标移动时，EventSystem 更容易先命中这个锁定 tooltip
- 下方其他卡片不容易再收到新的 pointer enter / hover

参考：

- [DesktopLockModeController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/DesktopLockModeController.cs#L117)
- [BaseTooltipController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/BaseTooltipController.cs#L327)

### 4.2 当前卡在锁定态不会执行正常 Deselect

`CardController.Deselect()` 里有这个判断：

```csharp
if (!HoveredTooltipIsLocked())
{
    IsCursorOverCard = false;
    HideTooltips();
    ...
}
```

也就是：

- 如果当前 hover 的这张卡就是 locked tooltip 对应的卡
- 那么鼠标离开卡本体时也不会走正常的取消 hover / 隐藏 tooltip 流程

参考：

- [CardController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/CardController.cs#L923)
- [CardController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/CardController.cs#L1154)

对于 Encounter，还额外在自己的 `Deselect()` 里再次短路：

```csharp
if (HoveredTooltipIsLocked() || EncounterIsHiddenOnStashOpen() || AppState.CurrentState == null)
{
    return;
}
```

所以野怪放大后，不会执行还原 scale / pulse 退出等逻辑。

参考：

- [EncounterController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/EncounterController.cs#L679)

### 4.3 鼠标在 tooltip 上，也算“还在当前卡上”

`CardController.IsPointerOverThis()` 有一个特殊判定：

如果当前 tooltip 的 `CardInstance.InstanceId` 就是本卡，并且 `CardTooltipController.IsPointerOverTooltip()` 为真，那么直接返回 `true`。

这意味着：

- 鼠标从卡本体移到锁定 tooltip 上
- 系统仍然认为“你还在这张卡对应的可交互区域里”
- `CheckForDeselect()` 不会把这张卡取消掉

参考：

- [CardController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/CardController.cs#L964)
- [CardTooltipController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipController.cs#L1760)

### 4.4 所以“屏蔽其他 hover”本质上是表现结果

更准确地说，原生做的是：

- 把当前 tooltip 切成 locked / interactable 前景层
- 保持当前卡的 hover/selected 生命周期不结束
- 让鼠标停在 tooltip 上时仍被视作属于当前卡

于是最终表现出来就像：

- 其他卡 hover 没了
- 其他 tooltip 不再弹
- 当前右键这张卡独占交互焦点

---

## 5. 再次右键是如何退出的

再次右键后仍然走 `LockTooltipToggle()`，因为此时 `isLocked == true`，会直接调用 `Unlock()`。

桌面端 `DesktopLockModeController.Unlock()` 会：

1. `SetLockedFlag(false)`
2. 隐藏 legend tray
3. 把卡 `Move()` 回原 socket
4. 恢复 art layer
5. `HideCardHints()`
6. 关闭 lock mode canvas
7. `Data.TooltipParentComponent.HideCardTooltipController()`
8. 触发 `Events.TooltipUnlock`

参考：

- [DesktopLockModeController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/DesktopLockModeController.cs#L120)

---

## 6. TooltipParentComponent 在这条链路里的职责

`TooltipParentComponent` 是全局 tooltip 容器，不负责锁定动画本身，但负责：

- 持有主 `CardTooltipController`
- 提供 `GetCardTooltipController(card)`
- 判断 controller 是否 locked
- 负责统一 show / hide 主 tooltip

相关方法：

- `GetCardTooltipController(card)`
- `IsCardTooltipControllerLocked(ctrl)`
- `GetCurrentTooltipDataFromCardTooltipController(ctrl)`
- `UnlockCardTooltipController()`
- `HideCardTooltipController()`

参考：

- [TooltipParentComponent.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs#L433)

---

## 7. `CardTooltipsBlocked` 不是这次“屏蔽其他 hover”的主因

仓库里还有一个容易混淆的全局位：`TooltipParentComponent.CardTooltipsBlocked`。

这个标志主要在 PVP 过场时打开：

- `OnPvpTransitionBegan()` 里设为 `true`
- `OnCombatRevealCompleted()` 里设为 `false`

它确实会阻止 `ShowTooltips()` 和 `ShowCardHint()`，但它不是右键锁定后屏蔽其他 hover 的主机制。

参考：

- [TooltipParentComponent.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs#L700)
- [CardController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/CardController.cs#L1052)
- [CardController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/CardController.cs#L1132)

---

## 8. 对 mod 复现的启发

如果要在 mod 里复现“原生右键野怪放大并独占 hover”的效果，重点不是“禁掉别的 hover”，而是复用或模拟这几个条件：

1. 当前预览层必须在输入层前景，并且能 `blocksRaycasts`
2. 当前对象在锁定期间不能走普通的 `Deselect()`
3. 鼠标移动到预览层时，仍要视作属于当前对象
4. 解锁时再统一恢复 hover / tooltip / layer / position

也就是说，最小可行复刻更接近：

- 一个 lock overlay
- 一个当前对象 locked 状态
- 一个“pointer over preview == pointer over current target”的判定

而不是单独加一个“全局禁止 hover”。

---

## 9. 关键文件

- [CardController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/CardController.cs)
- [EncounterController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/EncounterController.cs)
- [CardTooltipController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipController.cs)
- [DesktopLockModeController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/DesktopLockModeController.cs)
- [BaseTooltipController.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/BaseTooltipController.cs)
- [TooltipParentComponent.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs)
- [InputManager.cs](/Users/yxinyu/codes/BazaarPlusPlus/decompiled/TheBazaarRuntime/TheBazaar.Inputs/InputManager.cs)

---

## 10. 一句话总结

原生“右键野怪后放大并屏蔽其他 hover”的原理，不是 Encounter 私有逻辑，也不是单独的全局禁 hover 开关，而是：

`右键触发 tooltip lock -> lock overlay 吃射线 -> 当前卡保持 locked hover 生命周期 -> 鼠标在 tooltip 上仍算属于当前卡`

最终表现成“当前右键目标独占交互焦点”。
