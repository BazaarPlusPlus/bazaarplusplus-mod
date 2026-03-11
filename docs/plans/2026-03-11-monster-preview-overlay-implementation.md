# Monster Preview Overlay Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 构建一个新的 world-space monster preview overlay，支持固定锚点与跟踪锚点两种模式，以 `board` 为核心对象承载卡片，并将调试入口与正式逻辑隔离。

**Architecture:** 以 `MonsterPreviewOverlayController` 作为生命周期入口，注入 `IOverlayAnchorSource` 提供世界锚点，`MonsterPreviewBoard` 负责根节点与子卡片布局，`MonsterPreviewCardFactory` 负责卡片实例化。调试能力通过独立 debug 控制器接入，不反向依赖生产路径。

**Tech Stack:** C#, Unity, MonoBehaviour, existing game runtime APIs, Newtonsoft.Json, Harmony patches only where already required by the mod

---

### Task 1: 建立新 Overlay 结构骨架

**Files:**
- Create: `Game/Overlay/MonsterPreviewOverlayController.cs`
- Create: `Game/Overlay/IOverlayAnchorSource.cs`
- Create: `Game/Overlay/MonsterPreviewBoard.cs`
- Create: `Game/Overlay/PreviewBoardLayout.cs`
- Modify: `Plugin.cs`

**Step 1: Write the failing test**

如果仓库已有 Unity 测试基础设施，则新增最小测试文件：

```csharp
using NUnit.Framework;

public class MonsterPreviewOverlayControllerTests
{
    [Test]
    public void CanCreateControllerAndBoard()
    {
        Assert.Fail("Create controller and board lifecycle test");
    }
}
```

如果仓库没有测试基础设施，记录本任务先通过最小可运行集成验证，不额外引入新测试框架。

**Step 2: Run test to verify it fails**

Run: `dotnet test`

Expected: 若存在测试项目则 FAIL；若不存在测试项目，确认当前仓库没有可用测试入口并记录。

**Step 3: Write minimal implementation**

实现最小骨架：

- `IOverlayAnchorSource` 定义 `TryGetAnchor(out Vector3 position, out Quaternion rotation)`
- `PreviewBoardLayout` 定义 `LocalOffset / CardSpacing / CardScale`
- `MonsterPreviewBoard` 持有一个根 `GameObject`
- `MonsterPreviewOverlayController` 持有 `IOverlayAnchorSource`、`MonsterPreviewBoard`、显隐状态
- 在 `Plugin.cs` 中接入新的 controller，替换旧 `MonsterPreviewOverlay` 挂载点，或并行挂载后暂不启用旧类

最小骨架代码目标：

```csharp
public interface IOverlayAnchorSource
{
    bool TryGetAnchor(out Vector3 position, out Quaternion rotation);
}
```

```csharp
public sealed class PreviewBoardLayout
{
    public Vector3 LocalOffset = Vector3.zero;
    public Vector3 CardSpacing = new Vector3(1f, 0f, 0f);
    public Vector3 CardScale = Vector3.one;
}
```

**Step 4: Run verification**

Run: `dotnet build`

Expected: build succeeds without introducing new compile errors.

**Step 5: Commit**

```bash
git add Plugin.cs Game/Overlay/MonsterPreviewOverlayController.cs Game/Overlay/IOverlayAnchorSource.cs Game/Overlay/MonsterPreviewBoard.cs Game/Overlay/PreviewBoardLayout.cs
git commit -m "refactor: add monster preview overlay core structure"
```

### Task 2: 实现固定锚点模式

**Files:**
- Create: `Game/Overlay/FixedWorldAnchorSource.cs`
- Modify: `Game/Overlay/MonsterPreviewOverlayController.cs`
- Modify: `Game/Overlay/MonsterPreviewBoard.cs`

**Step 1: Write the failing test**

若有测试基础设施，添加一个固定锚点返回值测试：

```csharp
[Test]
public void FixedWorldAnchorReturnsConfiguredValues()
{
    Assert.Fail("Fixed anchor should return configured position and rotation");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test`

Expected: FAIL or no test entry available.

**Step 3: Write minimal implementation**

实现：

- `FixedWorldAnchorSource`
- controller 的 `SetAnchorSource`
- 在 `LateUpdate` 或合适时机调用 `TryGetAnchor`
- `MonsterPreviewBoard.UpdateAnchor(position, rotation)` 更新根节点 transform

最小实现草图：

```csharp
public sealed class FixedWorldAnchorSource : IOverlayAnchorSource
{
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.identity;

    public bool TryGetAnchor(out Vector3 position, out Quaternion rotation)
    {
        position = Position;
        rotation = Rotation;
        return true;
    }
}
```

**Step 4: Run verification**

Run: `dotnet build`

Expected: build succeeds.

**Step 5: Commit**

```bash
git add Game/Overlay/FixedWorldAnchorSource.cs Game/Overlay/MonsterPreviewOverlayController.cs Game/Overlay/MonsterPreviewBoard.cs
git commit -m "feat: add fixed world anchor support"
```

### Task 3: 实现 Board 根节点与 Slot 布局

**Files:**
- Modify: `Game/Overlay/MonsterPreviewBoard.cs`
- Modify: `Game/Overlay/PreviewBoardLayout.cs`

**Step 1: Write the failing test**

若有测试基础设施，添加布局测试：

```csharp
[Test]
public void SetCardsCreatesSlotsUnderBoardRoot()
{
    Assert.Fail("Board should create slot transforms for cards");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test`

Expected: FAIL or no available test project.

**Step 3: Write minimal implementation**

给 `MonsterPreviewBoard` 增加：

- `BoardRoot`
- `CardSlot_i`
- `SetLayout`
- `SetVisible`
- `ClearCards`
- 仅创建 slot，不接卡片资源

布局规则：

- board 根节点位置取锚点
- `LocalOffset.y` 用于抬高
- `LocalOffset.z` 用于上下偏移
- slot 使用 `CardSpacing.x` 横向排列

**Step 4: Run verification**

Run: `dotnet build`

Expected: build succeeds.

**Step 5: Commit**

```bash
git add Game/Overlay/MonsterPreviewBoard.cs Game/Overlay/PreviewBoardLayout.cs
git commit -m "feat: add preview board slot layout"
```

### Task 4: 定义卡片数据模型和工厂接口

**Files:**
- Create: `Game/Overlay/PreviewCardSpec.cs`
- Create: `Game/Overlay/IPreviewCardFactory.cs`
- Create: `Game/Overlay/MonsterPreviewCardFactory.cs`
- Modify: `Game/Overlay/MonsterPreviewBoard.cs`

**Step 1: Write the failing test**

若有测试基础设施，添加最小工厂交互测试：

```csharp
[Test]
public void BoardUsesFactoryToCreateCardInstances()
{
    Assert.Fail("Board should request card objects from factory");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test`

Expected: FAIL or no test project.

**Step 3: Write minimal implementation**

定义：

```csharp
public sealed class PreviewCardSpec
{
    public string TemplateId;
    public int Tier;
    public string Enchant;
    public Dictionary<int, int> Attributes;
}
```

定义工厂接口：

```csharp
public interface IPreviewCardFactory
{
    GameObject CreateCard(PreviewCardSpec spec, Transform parent);
    void UpdateCard(GameObject cardObject, PreviewCardSpec spec);
    void DestroyCard(GameObject cardObject);
}
```

`MonsterPreviewBoard` 接入工厂，完成：

- slot 创建
- card instance 挂到 slot 下
- clear 时通过 factory 销毁

**Step 4: Run verification**

Run: `dotnet build`

Expected: build succeeds.

**Step 5: Commit**

```bash
git add Game/Overlay/PreviewCardSpec.cs Game/Overlay/IPreviewCardFactory.cs Game/Overlay/MonsterPreviewCardFactory.cs Game/Overlay/MonsterPreviewBoard.cs
git commit -m "feat: add preview card spec and factory"
```

### Task 5: 接入游戏卡片实例化逻辑

**Files:**
- Modify: `Game/Overlay/MonsterPreviewCardFactory.cs`
- Modify: `Game/Overlay/PreviewCardSpec.cs`
- Modify: `Game/Overlay/MonsterPreviewOverlayController.cs`
- Reference: `Game/MonsterPreviewOverlay.cs`

**Step 1: Write the failing test**

若无法做自动化测试，先定义手工验证场景：

- 固定锚点下创建 1 张卡
- 固定锚点下创建多张卡
- 销毁后卡片不残留

若存在测试基础设施，则增加针对 `BuildCard` 和实例化返回对象的测试桩。

**Step 2: Run test to verify it fails**

Run: `dotnet build`

Expected: 在工厂未接入实际实例化前，手工验证无法生成真实卡片。

**Step 3: Write minimal implementation**

从旧实现中只复用必要知识，不复用整体结构：

- 将 `PreviewCardSpec` 转成 runtime card model
- 使用现有游戏资源实例化 API 创建卡片对象
- 将卡片实例挂到传入的 slot parent 下
- 将旧代码里和 socket、layout、全局状态耦合的部分全部丢弃

要求：

- 工厂只负责卡片对象本身
- 不能在工厂里决定 board 位置
- 不能在工厂里依赖 debug 输入格式

**Step 4: Run verification**

Run: `dotnet build`

Expected: build succeeds.

手工验证：

- 在固定锚点下能看到生成的单张卡片
- 多张卡片都在 board 下
- 调整 `LocalOffset.y` 可明显改善遮挡

**Step 5: Commit**

```bash
git add Game/Overlay/MonsterPreviewCardFactory.cs Game/Overlay/PreviewCardSpec.cs Game/Overlay/MonsterPreviewOverlayController.cs
git commit -m "feat: integrate game card instantiation for preview overlay"
```

### Task 6: 实现结构化卡片同步接口

**Files:**
- Modify: `Game/Overlay/MonsterPreviewBoard.cs`
- Modify: `Game/Overlay/MonsterPreviewOverlayController.cs`
- Create: `Game/Overlay/PreviewCardCollectionSync.cs` (optional)

**Step 1: Write the failing test**

若有测试基础设施，添加集合同步测试：

```csharp
[Test]
public void SetCardsReconcilesBoardChildren()
{
    Assert.Fail("Board should reconcile added, updated, and removed cards");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test`

Expected: FAIL or no available test project.

**Step 3: Write minimal implementation**

实现 `SetCards(IReadOnlyList<PreviewCardSpec>)` 的同步行为：

- 新卡创建新 slot 和新 card
- 已存在卡更新内容
- 多余卡删除

如果当前 spec 没有稳定 ID，可先采用“按顺序全量重建”的最小策略，但必须把集合同步边界收敛在 `PreviewBoard` 内，而不是散落到 controller。

**Step 4: Run verification**

Run: `dotnet build`

Expected: build succeeds.

手工验证：

- 卡片数量变多时新增正确
- 卡片数量变少时旧对象被清理
- 重复刷新不会无限残留对象

**Step 5: Commit**

```bash
git add Game/Overlay/MonsterPreviewBoard.cs Game/Overlay/MonsterPreviewOverlayController.cs Game/Overlay/PreviewCardCollectionSync.cs
git commit -m "feat: add preview card collection synchronization"
```

### Task 7: 实现跟踪锚点模式

**Files:**
- Create: `Game/Overlay/TrackedObjectAnchorSource.cs`
- Modify: `Game/Overlay/IOverlayAnchorSource.cs`
- Modify: `Game/Overlay/MonsterPreviewOverlayController.cs`

**Step 1: Write the failing test**

若有测试基础设施，添加跟踪源测试：

```csharp
[Test]
public void TrackedAnchorReturnsTargetTransformValues()
{
    Assert.Fail("Tracked anchor should resolve and return target transform");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test`

Expected: FAIL or no test project.

**Step 3: Write minimal implementation**

实现 `TrackedObjectAnchorSource`：

- 接收一个解析函数或 provider
- 由外部代码解析当前要跟踪的 `Transform`
- 解析成功时返回目标的 `position / rotation`
- 解析失败时返回 false

建议接口：

```csharp
public sealed class TrackedObjectAnchorSource : IOverlayAnchorSource
{
    private readonly Func<Transform> _resolver;
}
```

**Step 4: Run verification**

Run: `dotnet build`

Expected: build succeeds.

手工验证：

- 当目标对象移动时，board 跟随更新
- 当目标对象销毁或不可用时，overlay 不崩溃

**Step 5: Commit**

```bash
git add Game/Overlay/TrackedObjectAnchorSource.cs Game/Overlay/IOverlayAnchorSource.cs Game/Overlay/MonsterPreviewOverlayController.cs
git commit -m "feat: add tracked object anchor source"
```

### Task 8: 将调试入口与正式入口隔离

**Files:**
- Create: `Game/Overlay/Debug/OverlayDebugController.cs`
- Modify: `Game/DebugOverlay.cs`
- Modify: `Plugin.cs`

**Step 1: Write the failing test**

如无自动化测试，先定义手工验证标准：

- 正式路径关闭 debug 代码后仍可运行
- debug 路径可以改固定锚点和卡片数据
- debug 入口不需要被正式代码引用

**Step 2: Run test to verify it fails**

Run: `dotnet build`

Expected: 在 debug 入口未拆分前，仍存在混用风险。

**Step 3: Write minimal implementation**

实现独立 debug 控制器：

- 只通过 `MonsterPreviewOverlayController` 的公开接口工作
- 可设置固定锚点
- 可注入测试卡片列表
- 可开关 overlay

要求：

- 正式路径不得依赖 debug 类
- debug 类不得直接操作 `MonsterPreviewBoard` 私有状态

**Step 4: Run verification**

Run: `dotnet build`

Expected: build succeeds.

手工验证：

- debug 模式能调坐标和卡片
- 关闭 debug 入口后正式 overlay 仍正常

**Step 5: Commit**

```bash
git add Game/Overlay/Debug/OverlayDebugController.cs Game/DebugOverlay.cs Plugin.cs
git commit -m "refactor: isolate monster preview debug controls"
```

### Task 9: 移除旧实现并完成接线

**Files:**
- Modify: `Plugin.cs`
- Modify: `Game/DebugOverlay.cs`
- Delete or deprecate: `Game/MonsterPreviewOverlay.cs`
- Modify: any references found by `rg "MonsterPreviewOverlay"`

**Step 1: Write the failing test**

定义最终验收场景：

- overlay 可以开关
- 固定锚点可显示卡片
- 跟踪锚点可跟随对象
- 卡片全部作为 board 子对象存在
- debug 入口与正式入口分离

**Step 2: Run test to verify it fails**

Run: `rg -n "MonsterPreviewOverlay" .`

Expected: 仍能看到旧实现被生产路径引用。

**Step 3: Write minimal implementation**

完成：

- 把生产路径切到新 controller
- 清理旧 `MonsterPreviewOverlay` 的引用
- 确认不存在对 `playerStorageSockets` 的依赖
- 如旧类暂时保留，标记为 deprecated 并完全脱离接线

**Step 4: Run verification**

Run: `dotnet build`

Expected: build succeeds.

Run: `rg -n "playerStorageSockets|MonsterPreviewOverlay" Game Plugin.cs`

Expected: 不再有生产逻辑依赖旧 overlay 和 socket 锚点。

**Step 5: Commit**

```bash
git add Plugin.cs Game/DebugOverlay.cs Game/MonsterPreviewOverlay.cs Game/Overlay
git commit -m "refactor: switch monster preview to board-based overlay architecture"
```

### Task 10: 最终验证与文档收尾

**Files:**
- Modify: `README.md` (only if overlay usage needs documentation)
- Modify: `docs/plans/2026-03-11-monster-preview-overlay-design.md` (only if design changed during implementation)

**Step 1: Run build verification**

Run: `dotnet build`

Expected: PASS

**Step 2: Run targeted regression verification**

Run:

```bash
rg -n "playerStorageSockets|OnDisable\\(|SetJson\\(" Game Plugin.cs
```

Expected:

- 新路径不再依赖 socket 锚点
- 新路径不再以 JSON 字符串作为核心内部接口
- 不再保留旧实现中的危险生命周期手法

**Step 3: Run manual validation checklist**

手工验证：

- overlay 开关正常
- 固定锚点模式可见
- 跟踪锚点模式可见
- 提高 `Y` 会改善遮挡
- 调整 `Z` 会改变屏幕上下位置
- 卡片以 board 为父对象存在
- 清空卡片后对象被正确清理

**Step 4: Update docs if needed**

如实现偏离设计，更新：

- `docs/plans/2026-03-11-monster-preview-overlay-design.md`
- `README.md`

**Step 5: Commit**

```bash
git add README.md docs/plans/2026-03-11-monster-preview-overlay-design.md
git commit -m "docs: finalize monster preview overlay plan and usage notes"
```
