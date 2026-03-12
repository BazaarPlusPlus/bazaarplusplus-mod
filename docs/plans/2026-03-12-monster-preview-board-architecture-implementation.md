# Monster Preview Board Architecture Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 monster preview board 重构为以 `PreviewBoardSession` 为核心的统一架构，清晰拆分 board UI、anchor、data 和 debug 逻辑。

**Architecture:** 引入 session 作为唯一入口，数据来源统一为 `PreviewBoardModel`，anchor 统一为 `BoardPose`，渲染统一由 `BoardView` 消费 `BoardRenderModel`。正式展示和 debug 展示只在 request 输入上不同，共用同一套渲染与生命周期。

**Tech Stack:** C#, Unity, existing BazaarPlusPlus overlay/card factory infrastructure, NUnit test project

---

### Task 1: 建立新领域模型

**Files:**
- Create: `Game/Overlay/Architecture/PreviewBoardRequest.cs`
- Create: `Game/Overlay/Architecture/PreviewBoardModel.cs`
- Create: `Game/Overlay/Architecture/BoardPose.cs`
- Create: `Game/Overlay/Architecture/BoardRenderModel.cs`
- Create: `Game/Overlay/Architecture/PreviewBoardDebugOptions.cs`
- Create: `Game/Overlay/Architecture/PreviewBoardPresentation.cs`
- Test: `tests/BazaarPlusPlus.Tests/Overlay/PreviewBoardArchitectureModelsTests.cs`

**Step 1: Write the failing test**

写测试覆盖默认值和基本字段语义，确保新模型可以表示：
- items + skills
- title + signature
- presentation/debug flags
- board pose

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter PreviewBoardArchitectureModelsTests -v minimal`
Expected: FAIL with missing types

**Step 3: Write minimal implementation**

新增上述类型，先只实现测试需要的字段和默认值，不引入行为。

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter PreviewBoardArchitectureModelsTests -v minimal`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay/Architecture tests/BazaarPlusPlus.Tests/Overlay/PreviewBoardArchitectureModelsTests.cs
git commit -m "refactor: add preview board architecture models"
```

### Task 2: 引入统一 data source 边界

**Files:**
- Create: `Game/Overlay/Architecture/IPreviewDataSource.cs`
- Create: `Game/Overlay/Architecture/CompositePreviewDataSource.cs`
- Create: `Game/Overlay/DataSources/MonsterDatabasePreviewDataSource.cs`
- Create: `Game/Overlay/DataSources/EncounterCachePreviewDataSource.cs`
- Create: `Game/Overlay/DataSources/PlayerHandPreviewDataSource.cs`
- Modify: `Game/Overlay/MonsterPreviewSpecBuilder.cs`
- Modify: `Game/Overlay/SkillPreviewSpecBuilder.cs`
- Test: `tests/BazaarPlusPlus.Tests/Overlay/PreviewDataSourceTests.cs`

**Step 1: Write the failing test**

写测试验证：
- data source 能返回统一 `PreviewBoardModel`
- composite source 会按顺序挑选第一个成功来源
- signature 在相同输入下稳定

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter PreviewDataSourceTests -v minimal`
Expected: FAIL with missing interface/classes

**Step 3: Write minimal implementation**

实现 `IPreviewDataSource` 和基础 data source，先不改旧调用方。

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter PreviewDataSourceTests -v minimal`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay/Architecture Game/Overlay/DataSources Game/Overlay/MonsterPreviewSpecBuilder.cs Game/Overlay/SkillPreviewSpecBuilder.cs tests/BazaarPlusPlus.Tests/Overlay/PreviewDataSourceTests.cs
git commit -m "refactor: add preview board data sources"
```

### Task 3: 引入新的 anchor strategy 管线

**Files:**
- Create: `Game/Overlay/Architecture/IBoardAnchorStrategy.cs`
- Create: `Game/Overlay/Anchor/FixedAnchorStrategy.cs`
- Create: `Game/Overlay/Anchor/TrackedTransformAnchorStrategy.cs`
- Create: `Game/Overlay/Anchor/AnchorAdjustment.cs`
- Create: `Game/Overlay/Anchor/AdjustableAnchorStrategy.cs`
- Modify: `Game/Overlay/FixedWorldAnchorSource.cs`
- Test: `tests/BazaarPlusPlus.Tests/Overlay/AnchorStrategyTests.cs`

**Step 1: Write the failing test**

写测试验证：
- fixed strategy 直接输出固定 pose
- adjustable strategy 会应用 offset 和 rotation 修正
- strategy 失败时不会产出 pose

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter AnchorStrategyTests -v minimal`
Expected: FAIL with missing strategy types

**Step 3: Write minimal implementation**

补齐最小策略实现，并把旧 fixed anchor source 适配到新模型，暂时保留兼容层。

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter AnchorStrategyTests -v minimal`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay/Architecture Game/Overlay/Anchor Game/Overlay/FixedWorldAnchorSource.cs tests/BazaarPlusPlus.Tests/Overlay/AnchorStrategyTests.cs
git commit -m "refactor: add preview board anchor strategies"
```

### Task 4: 提取 BoardView

**Files:**
- Create: `Game/Overlay/View/BoardView.cs`
- Create: `Game/Overlay/View/BoardDebugOverlay.cs`
- Modify: `Game/Overlay/MonsterPreviewBoard.cs`
- Test: `tests/BazaarPlusPlus.Tests/Overlay/BoardViewTests.cs`

**Step 1: Write the failing test**

写测试验证：
- `BoardView` 能根据 render model 控制 visible
- render 时能区分 item slots、skill slots、debug visibility
- debug options 切换时不会影响数据层内容

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter BoardViewTests -v minimal`
Expected: FAIL with missing view types

**Step 3: Write minimal implementation**

将 `MonsterPreviewBoard` 中视觉结构、slot 布局、marker 控制迁入 `BoardView` 和 `BoardDebugOverlay`，先保证行为等价。

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter BoardViewTests -v minimal`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay/View Game/Overlay/MonsterPreviewBoard.cs tests/BazaarPlusPlus.Tests/Overlay/BoardViewTests.cs
git commit -m "refactor: extract preview board view"
```

### Task 5: 引入 PreviewBoardSession

**Files:**
- Create: `Game/Overlay/Architecture/PreviewBoardSession.cs`
- Modify: `Game/Overlay/MonsterPreviewOverlayController.cs`
- Test: `tests/BazaarPlusPlus.Tests/Overlay/PreviewBoardSessionTests.cs`

**Step 1: Write the failing test**

写测试验证：
- `Show(request)` 会缓存 request 并开始驱动 session
- `Hide()` 会清空 view 可见状态
- 数据未变化时不会重复 rebuild
- anchor 变化会继续更新 pose

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter PreviewBoardSessionTests -v minimal`
Expected: FAIL with missing session type

**Step 3: Write minimal implementation**

实现 session，把旧 controller 内的数据 cloning、visible 状态、sync pending/in flight 协调逐步迁入。

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter PreviewBoardSessionTests -v minimal`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay/Architecture/PreviewBoardSession.cs Game/Overlay/MonsterPreviewOverlayController.cs tests/BazaarPlusPlus.Tests/Overlay/PreviewBoardSessionTests.cs
git commit -m "refactor: add preview board session"
```

### Task 6: 迁移正式路径和 debug 路径

**Files:**
- Modify: `Game/Overlay/Debug/OverlayDebugController.cs`
- Modify: `Game/Overlay/EncounterTooltipPreviewBridge.cs`
- Modify: `Game/Overlay/MonsterLockShowcaseRuntime.cs`
- Test: `tests/BazaarPlusPlus.Tests/MonsterLockShowcaseRootTests.cs`
- Test: `tests/BazaarPlusPlus.Tests/Overlay/OverlayDebugControllerTests.cs`

**Step 1: Write the failing test**

写测试验证：
- debug 路径通过 request 注入 debug options 和 debug anchor strategy
- tooltip/showcase 路径通过 request 注入正式 data source 和固定 presentation
- show/hide 行为保持一致

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter \"MonsterLockShowcaseRootTests|OverlayDebugControllerTests\" -v minimal`
Expected: FAIL with outdated integration assumptions

**Step 3: Write minimal implementation**

把三个入口都改成“构建 request -> 交给 session”，去掉重复 layout/fixed anchor/data build 逻辑。

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter \"MonsterLockShowcaseRootTests|OverlayDebugControllerTests\" -v minimal`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay/Debug/OverlayDebugController.cs Game/Overlay/EncounterTooltipPreviewBridge.cs Game/Overlay/MonsterLockShowcaseRuntime.cs tests/BazaarPlusPlus.Tests/MonsterLockShowcaseRootTests.cs tests/BazaarPlusPlus.Tests/Overlay/OverlayDebugControllerTests.cs
git commit -m "refactor: route preview flows through board session"
```

### Task 7: 清理兼容层和旧职责

**Files:**
- Modify: `Game/Overlay/IOverlayAnchorSource.cs`
- Modify: `Game/Overlay/IPreviewCardFactory.cs`
- Modify: `Game/Overlay/PreviewBoardLayout.cs`
- Modify: `Game/Overlay/MonsterPreviewBoard.cs`
- Test: `tests/BazaarPlusPlus.Tests/Overlay/*.cs`

**Step 1: Write the failing test**

补测试覆盖旧兼容层移除后仍保留的公共行为，特别是：
- visible 切换
- rebuild/cancel
- debug 可见性

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter Overlay -v minimal`
Expected: FAIL with leftover references to old responsibilities

**Step 3: Write minimal implementation**

删除不再需要的旧边界，收敛接口命名和依赖方向。

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests --filter Overlay -v minimal`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Overlay tests/BazaarPlusPlus.Tests/Overlay
git commit -m "refactor: remove legacy preview board responsibilities"
```
