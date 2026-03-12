# Monster Lock Showcase Implementation Plan

## Current Status

2026-03-12 当前实际实现已经从“前景 fixed root”调整为“lock canvas 固定矩形挖洞”：

- 新运行时入口是 `MonsterLockShowcaseRuntime`
- 原有 showcase 仍然走现有 world anchor / 3D 渲染链路
- `LockCanvasHoleOverlay` 会在 monster lock 时向原生 lock canvas 注入 4 块透明 blocker
- blocker 中间保留一个固定矩形洞，让后方 showcase 区域能够收到 hover
- `LockCanvasHoleLayout` 负责纯逻辑 blocker 布局计算，并有独立单元测试

因此这份计划里关于“前景 root”的部分应视为早期方案记录；后续实现应以 hole overlay 路线为准。

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make monster showcase cards appear only during the native monster lock view, render with the existing BazaarPlusPlus showcase pipeline, and show the native main tooltip on hover.

**Architecture:** Reuse the existing showcase rendering path, but stop treating it as a fixed world-space overlay. Do not use `EncounterTooltipPreviewBridge` as the architectural foundation; instead, add a new lock-driven controller that listens to native `TooltipLock` / `TooltipUnlock`, mounts the showcase into a dedicated foreground preview root, and adds a lightweight hover adapter that opens the native main tooltip for each showcase card. Keep native lock/unlock in charge of entering and exiting the large monster view.

**Tech Stack:** C#, Unity MonoBehaviours, native TheBazaar tooltip APIs, existing BazaarPlusPlus overlay/showcase classes, xUnit tests in `tests/BazaarPlusPlus.Tests`

---

### Task 1: Create a new lock-driven controller and stop relying on the legacy bridge shape

**Files:**
- Create: `Game/Overlay/MonsterLockShowcaseController.cs`
- Modify: `Game/Overlay/EncounterTooltipPreviewBridge.cs`
- Test: `tests/BazaarPlusPlus.Tests/MonsterLockShowcaseControllerTests.cs`

**Step 1: Write the failing test**

Add tests covering the new controller's minimal responsibilities:

```csharp
using System;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class MonsterLockShowcaseControllerTests
{
    [Fact]
    public void ShouldShowForLock_returns_false_when_locked_card_is_null()
    {
        var controller = new MonsterLockShowcaseController();

        var result = controller.ShouldShowForLock(null, isShowcaseCard: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldHideForUnlock_returns_false_for_showcase_card()
    {
        var controller = new MonsterLockShowcaseController();

        var result = controller.ShouldHideForUnlock(Guid.NewGuid(), isShowcaseCard: true);

        Assert.False(result);
    }

    [Fact]
    public void ShouldHideForUnlock_returns_true_for_non_showcase_unlock()
    {
        var controller = new MonsterLockShowcaseController();

        var result = controller.ShouldHideForUnlock(null, isShowcaseCard: false);

        Assert.True(result);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter MonsterLockShowcaseControllerTests`

Expected: FAIL because `MonsterLockShowcaseController` does not exist yet.

**Step 3: Write minimal implementation**

Create a new controller entry point. Keep it intentionally thin at first:

```csharp
using System;

namespace BazaarPlusPlus;

internal sealed class MonsterLockShowcaseController
{
    public bool ShouldShowForLock(Guid? lockedCardId, bool isShowcaseCard)
    {
        if (!lockedCardId.HasValue || isShowcaseCard)
            return false;
        return true;
    }

    public bool ShouldHideForUnlock(Guid? currentCardId, bool isShowcaseCard)
    {
        return !isShowcaseCard;
    }
}
```

Then either:

- reduce `EncounterTooltipPreviewBridge` to a thin shim, or
- stop attaching it in the new path and let the new controller become the primary runtime entry

The key intent of this task is architectural: the new implementation should no longer depend on the legacy bridge shape.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter MonsterLockShowcaseControllerTests`

Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay/MonsterLockShowcaseController.cs Game/Overlay/EncounterTooltipPreviewBridge.cs tests/BazaarPlusPlus.Tests/MonsterLockShowcaseControllerTests.cs
git commit -m "refactor: add lock-driven monster showcase controller"
```

---

### Task 2: Replace fixed world anchor behavior with a dedicated foreground showcase root

**Files:**
- Create: `Game/Overlay/MonsterLockShowcaseRoot.cs`
- Modify: `Game/Overlay/MonsterLockShowcaseController.cs`
- Modify: `Game/Overlay/MonsterPreviewOverlayController.cs`
- Modify: `Game/Overlay/MonsterPreviewBoard.cs`
- Test: `tests/BazaarPlusPlus.Tests/MonsterLockShowcaseRootTests.cs`

**Step 1: Write the failing test**

Add a test that verifies the new root object uses explicit active/visible control and no longer requires a world anchor source to appear:

```csharp
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class MonsterLockShowcaseRootTests
{
    [Fact]
    public void SetVisible_false_hides_root_without_anchor_dependency()
    {
        var root = new MonsterLockShowcaseRoot();

        root.SetVisible(false);

        Assert.False(root.Visible);
    }

    [Fact]
    public void SetVisible_true_marks_root_visible()
    {
        var root = new MonsterLockShowcaseRoot();

        root.SetVisible(true);

        Assert.True(root.Visible);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter MonsterLockShowcaseRootTests`

Expected: FAIL because `MonsterLockShowcaseRoot` does not exist yet.

**Step 3: Write minimal implementation**

Create a dedicated root abstraction that owns:

- a visible flag
- a parent `Transform`
- a fixed local position/rotation/scale for the showcase area

Minimal shape:

```csharp
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class MonsterLockShowcaseRoot
{
    public bool Visible { get; private set; }

    public void AttachTo(Transform parent)
    {
        // store parent or create root object under it
    }

    public void ApplyLayout(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
    {
        // set transform values on the root
    }

    public void SetVisible(bool visible)
    {
        Visible = visible;
        // toggle active state on the root object
    }
}
```

Then update the runtime classes so that:

- `MonsterLockShowcaseController` becomes the runtime owner of the new root
- `MonsterPreviewOverlayController` can render into the new root without depending on `_anchorSource.TryGetAnchor(...)`
- `MonsterPreviewBoard` is attached to the showcase root transform instead of behaving like a tracked world object

Keep this step minimal: do not solve hover yet. Just ensure the showcase can be shown/hidden from a dedicated front-layer root.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter MonsterLockShowcaseRootTests`

Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay/MonsterLockShowcaseController.cs Game/Overlay/MonsterLockShowcaseRoot.cs Game/Overlay/MonsterPreviewOverlayController.cs Game/Overlay/MonsterPreviewBoard.cs tests/BazaarPlusPlus.Tests/MonsterLockShowcaseRootTests.cs
git commit -m "refactor: mount monster showcase into lock foreground root"
```

---

### Task 3: Keep existing showcase rendering, but add per-card hover adapters that open the native main tooltip

**Files:**
- Create: `Game/Overlay/ShowcaseHoverAdapter.cs`
- Modify: `Game/Overlay/MonsterPreviewCardFactory.cs`
- Modify: `Game/Overlay/SkillPreviewCardFactory.cs`
- Modify: `Game/ShowcaseCardMarker.cs`
- Test: `tests/BazaarPlusPlus.Tests/ShowcaseHoverAdapterTests.cs`

**Step 1: Write the failing test**

Add a test covering the small hover adapter rules:

```csharp
using System;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class ShowcaseHoverAdapterTests
{
    [Fact]
    public void BuildTooltipPayload_returns_false_when_template_id_is_missing()
    {
        var adapter = new ShowcaseHoverAdapter();

        var ok = adapter.TryBuildTooltipPayload(null, out _);

        Assert.False(ok);
    }

    [Fact]
    public void BuildTooltipPayload_returns_payload_when_template_id_exists()
    {
        var adapter = new ShowcaseHoverAdapter();

        var ok = adapter.TryBuildTooltipPayload(Guid.NewGuid().ToString(), out var payload);

        Assert.True(ok);
        Assert.NotNull(payload);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter ShowcaseHoverAdapterTests`

Expected: FAIL because `ShowcaseHoverAdapter` does not exist yet.

**Step 3: Write minimal implementation**

Create a hover adapter component that can be attached to each existing showcase card marker. It should:

- know the preview card template id / data needed to build a tooltip
- handle pointer enter / exit
- on enter, build the tooltip payload and call the native main tooltip path

Start with a small testable core:

```csharp
using System;

namespace BazaarPlusPlus;

internal sealed class ShowcaseHoverAdapter
{
    public bool TryBuildTooltipPayload(string templateId, out ShowcaseTooltipPayload payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(templateId))
            return false;

        payload = new ShowcaseTooltipPayload(templateId);
        return true;
    }
}

internal sealed record ShowcaseTooltipPayload(string TemplateId);
```

Then wire the runtime part into `MonsterPreviewCardFactory` and `SkillPreviewCardFactory` so every created showcase card:

- gets a `ShowcaseCardMarker`
- gets a hover adapter or hover handler component
- stores enough identity to reconstruct the card tooltip data on hover

Keep reusing the existing visual creation code. Do not rewrite card rendering.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter ShowcaseHoverAdapterTests`

Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay/ShowcaseHoverAdapter.cs Game/Overlay/MonsterPreviewCardFactory.cs Game/Overlay/SkillPreviewCardFactory.cs Game/ShowcaseCardMarker.cs tests/BazaarPlusPlus.Tests/ShowcaseHoverAdapterTests.cs
git commit -m "feat: add hover adapters for showcase cards"
```

---

### Task 4: Connect hover adapters to the native main tooltip and preserve native unlock behavior

**Files:**
- Modify: `Game/Overlay/MonsterLockShowcaseController.cs`
- Modify: `Game/Overlay/ShowcaseHoverAdapter.cs`
- Modify: `Models/ModState.cs`
- Test: `tests/BazaarPlusPlus.Tests/TestModStateShim.cs`

**Step 1: Write the failing test**

Add a behavior test that the unlock path ignores showcase-card hover state but still closes on real native unlock:

```csharp
using System;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class ShowcaseUnlockIntegrationTests
{
    [Fact]
    public void Unlock_is_ignored_for_showcase_hover_card()
    {
        var controller = new MonsterLockShowcaseController();

        var closed = controller.ShouldHideForUnlock(Guid.NewGuid(), isShowcaseCard: true);

        Assert.False(closed);
    }

    [Fact]
    public void Unlock_closes_for_non_showcase_card()
    {
        var controller = new MonsterLockShowcaseController();

        var closed = controller.ShouldHideForUnlock(null, isShowcaseCard: false);

        Assert.True(closed);
    }
}
```

If these tests already exist from Task 1, extend them instead of duplicating.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter ShowcaseUnlockIntegrationTests`

Expected: FAIL until the adapter and bridge are wired together consistently.

**Step 3: Write minimal implementation**

Implement the runtime wiring:

- on showcase hover enter:
  - resolve or build the runtime card data
  - build `CardTooltipData`
  - call the native main tooltip show path
- on showcase hover exit:
  - either do nothing or restore the locked monster tooltip if already available in the same frame; keep this phase minimal
- in the new controller:
  - continue ignoring `TooltipUnlock` events caused by showcase hover
  - close the showcase only on real native unlock

Store only the minimum shared state needed in `ModState` or bridge-local fields.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter MonsterLockShowcaseStateTests|ShowcaseUnlockIntegrationTests|ShowcaseHoverAdapterTests`

Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay/MonsterLockShowcaseController.cs Game/Overlay/ShowcaseHoverAdapter.cs Models/ModState.cs tests/BazaarPlusPlus.Tests/MonsterLockShowcaseControllerTests.cs tests/BazaarPlusPlus.Tests/ShowcaseHoverAdapterTests.cs
git commit -m "feat: show native tooltip for lock showcase hovers"
```

---

### Task 5: Verify layering, interaction boundaries, and cleanup in-game

**Files:**
- Modify: `Game/Overlay/MonsterLockShowcaseController.cs`
- Modify: `Game/Overlay/MonsterPreviewOverlayController.cs`
- Modify: `docs/encounter-lock-mode.md`

**Step 1: Add temporary diagnostics if needed**

Add narrow debug logs only where needed to confirm:

- lock enter detected
- showcase root shown
- hover enter/exit firing
- unlock cleanup firing

Example:

```csharp
BppLog.Debug("MonsterLockShowcaseController", "TooltipLock received; showing showcase root");
```

**Step 2: Run in-game manual verification**

Manual checklist:

1. Open a scene with 3 monsters
2. Confirm normal screen is unchanged
3. Right-click one monster
4. Confirm native large monster lock view appears
5. Confirm showcase cards appear in front of the lock view
6. Confirm hovering a showcase card opens the native main tooltip
7. Confirm empty showcase-space does not click through to the board
8. Confirm right-click exits the lock view and removes showcase cards
9. Confirm native exit button still works

Expected: all steps succeed without leaving orphan showcase objects or broken lock state.

**Step 3: Remove any unnecessary temporary diagnostics**

Keep only durable, useful debug logs. Delete noisy one-off tracing.

**Step 4: Update docs**

Extend `docs/encounter-lock-mode.md` with a short section describing the final BazaarPlusPlus integration:

- showcase root is foreground-mounted
- hover reuses native main tooltip
- native lock remains the only enter/exit authority

**Step 5: Commit**

```bash
git add Game/Overlay/MonsterLockShowcaseController.cs Game/Overlay/MonsterPreviewOverlayController.cs docs/encounter-lock-mode.md
git commit -m "docs: document lock showcase integration behavior"
```

---

## Verification Commands

Run the focused test suite after each task:

```bash
dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter MonsterLockShowcaseControllerTests -v
dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter MonsterLockShowcaseRootTests -v
dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter ShowcaseHoverAdapterTests -v
```

Run the full test project before calling the feature done:

```bash
dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj -v
```

Manual verification is required because the lock-mode integration depends on native runtime UI behavior.
