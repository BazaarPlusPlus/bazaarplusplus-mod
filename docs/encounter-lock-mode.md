# Encounter 右键 Lock Mode 机制分析

## 概述

在游戏中右键点击任意卡片（包括 Encounter 卡），会触发 **Tooltip Lock Mode**。
卡片移动到屏幕中央，Tooltip 固定显示，背景出现半透明遮罩。
再次右键或按 Lock 键可解除。

这是 mod 中"展示 Encounter 怪物阵容卡牌"功能的接入点。

---

## 触发流程

```
用户右键 / 按 Lock 键
  └→ CardController.LockTooltipToggle()
       └→ DesktopLockModeController.Lock(cardTooltipController)
            ├→ 卡片 DOTween 移动到 CardLockPosition（屏幕中央偏上）
            ├→ LockModeContainer.SetActive(true)   // 半透明遮罩 Canvas
            ├→ SetArtLayerAboveBoard()              // 提升卡片渲染层
            └→ Events.TooltipLock.Trigger()        // 无参数

用户再次右键 / 按 Lock 键
  └→ DesktopLockModeController.Unlock(cardTooltipController)
       ├→ 卡片归位 cardController.Move()
       ├→ LockModeContainer.SetActive(false)
       ├→ SetArtLayerDefault()
       └→ Events.TooltipUnlock.Trigger()           // 无参数
```

---

## 关键类与字段

### `CardTooltipController`
路径：`TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipController.cs`

| 成员 | 访问性 | 说明 |
|------|--------|------|
| `CurrentCard` | `internal` | 当前正在显示 tooltip 的卡片 |
| `IsLocked` | `internal` | 是否处于 lock mode |
| `CardLockPosition` | `public RectTransform` | 卡片锁定后移动到的屏幕位置 |
| `LockModeContainer` | `public GameObject` | 锁定时激活的遮罩容器 |
| `LockModeCanvas` | `public Canvas` | 锁定模式使用的 Canvas |
| `LockModeCanvasGroup` | `public CanvasGroup` | 控制遮罩透明度 |

### `TooltipParentComponent`
路径：`TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs`

通过 `Data.TooltipParentComponent` 访问。

| 成员 | 访问性 | 说明 |
|------|--------|------|
| `_cardTooltipController` | `private` | 当前 CardTooltipController 实例（需反射） |
| `IsCardTooltipControllerLocked(ctrl)` | `public` | 查询某 controller 是否锁定 |
| `GetCurrentTooltipDataFromCardTooltipController(ctrl)` | `public` | 获取当前 tooltip 数据 |
| `GetCardTooltipController(card)` | `public` | 根据 Card 获取对应 controller |

### `DesktopLockModeController`
路径：`TheBazaarRuntime/TheBazaar.UI.Tooltips/DesktopLockModeController.cs`

实现 `ILockModeController` 接口，由 `CardTooltipController` 持有并调用。
Lock 逻辑中会计算卡片的 OBB bounds，将其移到屏幕中央合适位置。

### `Events`（相关条目）
路径：`TheBazaarRuntime/TheBazaar/Events.cs`

```csharp
public static Event TooltipLock   = new Event();  // 无参数，lock 时触发
public static Event TooltipUnlock = new Event();  // 无参数，unlock 时触发
```

---

## 从 Mod 侧读取锁定的卡片

`CardTooltipController.CurrentCard` 是 `internal`，`TooltipParentComponent._cardTooltipController` 是 `private`，均需反射访问。

推荐方式：

```csharp
// 1. 拿到 CardTooltipController 实例
var tooltipField = typeof(TooltipParentComponent)
    .GetField("_cardTooltipController",
        BindingFlags.Instance | BindingFlags.NonPublic);
var ctrl = tooltipField?.GetValue(Data.TooltipParentComponent)
    as CardTooltipController;

// 2. 拿到当前锁定的 Card
var currentCardProp = typeof(CardTooltipController)
    .GetProperty("CurrentCard",
        BindingFlags.Instance | BindingFlags.NonPublic);
var card = currentCardProp?.GetValue(ctrl) as Card;

// 3. 判断是否是 CombatEncounter
if (card?.Type == ECardType.CombatEncounter)
{
    // 可以开始 spawn 展示卡
}
```

---

## Encounter 怪物阵容数据来源

mod 自有数据库：`BazaarPlusPlus_monsters.json`
对应类：`ModState.EncounterMonsterPreviews`（`List<RunInfo.MonsterPreview>`）

```csharp
public class MonsterPreview
{
    public string EncounterName;
    public Guid   EncounterTemplateId;
    public string MonsterTemplateId;
    public List<string> Items;   // Item 名称列表
    public List<string> Skills;
    public int?   CombatLevel;
    public int?   RewardGold;
    public int?   RewardXp;
    public bool?  SandstormEnabled;
}
```

通过 `card.TemplateId` 或 `card.Name` 匹配到对应的 `MonsterPreview`，再用 `Items` 列表查找对应的卡片模板 ID，即可传给现有的 `BuildRuntimeCardFromJson` 逻辑 spawn 出展示卡。

---

## 展示卡接入方案（计划）

```
Events.TooltipLock 触发
  └→ 读取 CurrentCard（反射）
       └→ 判断是否 CombatEncounter
            └→ 从 MonsterPreview 取 Items 列表
                 └→ 构造 ShowcaseCardJson[]
                      └→ SyncShowcaseEntitiesAsync()
                           └→ 展示卡 spawn，定位到 CardLockPosition 附近

Events.TooltipUnlock 触发
  └→ HideShowcaseEntities()
```

展示卡位置参考：`cardTooltipController.CardLockPosition`（屏幕空间 RectTransform），
需将其从 Canvas 屏幕坐标转换为世界坐标后再做排列布局。

---

## 相关源文件

| 文件 | 说明 |
|------|------|
| `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipController.cs` | Tooltip 主控制器 |
| `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/DesktopLockModeController.cs` | Lock/Unlock 逻辑 |
| `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/TooltipParentComponent.cs` | Tooltip 容器，通过 `Data.TooltipParentComponent` 访问 |
| `decompiled/TheBazaarRuntime/TheBazaar/Events.cs` | 事件定义，含 `TooltipLock` / `TooltipUnlock` |
| `decompiled/TheBazaarRuntime/EncounterController.cs` | Encounter 卡片控制器，处理点击/hover |
| `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/MonsterRewardRenderer.cs` | Tooltip 内怪物奖励渲染 |
| `Game/DebugOverlay.cs` | mod 展示卡 spawn 逻辑 |
| `Models/RunInfo.cs` | `MonsterPreview` 数据结构 |
