# Preview Surface Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract a reusable preview rendering stack that accepts preview models and renders item/skill cards with carpet and hover tooltips, while leaving `MonsterPreview` as a data/trigger host only.

**Architecture:** Introduce a neutral `Game/PreviewSurface` area with model types, single-card preview surfaces, and a board surface/render target pair. Migrate `MonsterPreview` to depend on those abstractions instead of owning card creation and board rendering details directly. Preserve current hover tooltip behavior and keep first-pass rendering semantics simple with `cancel-and-replace`.

**Tech Stack:** C# 13 / .NET 10, Unity runtime types, existing Bazaar runtime controllers (`ItemController`, `SkillController`), targeted console-style test projects under `tests/`

---

### Task 1: Extract Neutral Models And Surface Interfaces

**Files:**
- Create: `Game/PreviewSurface/Models/PreviewBoardModel.cs`
- Create: `Game/PreviewSurface/Models/PreviewCardSpec.cs`
- Create: `Game/PreviewSurface/Models/PreviewBoardPresentation.cs`
- Create: `Game/PreviewSurface/Cards/IPreviewCardSurface.cs`
- Create: `Game/PreviewSurface/Board/IPreviewBoardSurface.cs`
- Modify: `Game/MonsterPreview/Architecture/PreviewBoardModel.cs`
- Modify: `Game/MonsterPreview/Architecture/PreviewCardSpec.cs`
- Modify: `Game/MonsterPreview/Architecture/PreviewBoardPresentation.cs`
- Modify: `tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`
- Modify: `tests/MonsterPreviewResilience.Tests/Program.cs`

- [ ] **Step 1: Confirm the extraction is a type-boundary change, not a behavior change**

No new test should be written for the pure file move itself. The meaningful verification seam here is “existing pure preview model/filter tests still compile and pass after the namespace/type relocation.”

- [ ] **Step 2: Create the neutral model and interface files under `Game/PreviewSurface`**

Use the existing `MonsterPreview` types as the source of truth, but move them to neutral names and namespaces. Start with code shaped like this:

```csharp
namespace BazaarPlusPlus.Game.PreviewSurface;

internal sealed class PreviewBoardModel
{
    public IReadOnlyList<PreviewCardSpec> ItemCards { get; set; } = new List<PreviewCardSpec>();
    public IReadOnlyList<PreviewCardSpec> SkillCards { get; set; } = new List<PreviewCardSpec>();
    public string Title { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; set; } =
        new Dictionary<string, string>();
}
```

```csharp
namespace BazaarPlusPlus.Game.PreviewSurface;

internal interface IPreviewCardSurface
{
    Task<GameObject> CreateAsync(PreviewCardSpec spec, Transform parent);
    Task UpdateAsync(GameObject cardObject, PreviewCardSpec spec);
    void Destroy(GameObject cardObject);
}
```

```csharp
namespace BazaarPlusPlus.Game.PreviewSurface;

internal interface IPreviewBoardSurface : IDisposable
{
    Transform RootTransform { get; }
    bool IsAlive { get; }

    void SetPresentation(PreviewBoardPresentation presentation);
    void SetDebugOptions(PreviewBoardDebugOptions debugOptions);
    void SetVisible(bool visible);
    void UpdateAnchor(Vector3 position, Quaternion rotation);
    Task RenderAsync(PreviewBoardModel model, CancellationToken cancellationToken = default);
    void Clear();
}
```

- [ ] **Step 3: Retarget the existing pure references to the new namespace**

Update the old `MonsterPreview` files to either become thin wrappers or stop being compiled from their old location, but do not leave two diverging copies of the same type. Adjust `tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj` to link the neutral files instead of the old monster-specific ones:

```xml
<ItemGroup>
  <Compile Include="../../Game/PreviewSurface/Models/PreviewCardSpec.cs" Link="PreviewCardSpec.cs" />
  <Compile Include="../../Game/MonsterPreview/Architecture/PreviewCardSpecFilter.cs" Link="PreviewCardSpecFilter.cs" />
</ItemGroup>
```

Update `tests/MonsterPreviewResilience.Tests/Program.cs` imports to the new namespace:

```csharp
using BazaarPlusPlus.Game.PreviewSurface;
using BazaarPlusPlus.Game.MonsterPreview;
```

- [ ] **Step 4: Run the resilience test project**

Run:

```bash
dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
```

Expected: `MonsterPreviewResilience checks passed.`

- [ ] **Step 5: Run a targeted compile check on the main project**

Run:

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

If `ManagedPath` auto-detection does not resolve locally, rerun with:

```bash
dotnet build BazaarPlusPlus.csproj -c Debug -p:ManagedPath="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed"
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add Game/PreviewSurface Game/MonsterPreview/Architecture tests/MonsterPreviewResilience.Tests
git commit -m "Extract preview surface models and interfaces"
```

### Task 2: Move Item And Skill Preview Construction Into Card Surfaces

**Files:**
- Create: `Game/PreviewSurface/Cards/PreviewItemCardSurface.cs`
- Create: `Game/PreviewSurface/Cards/PreviewSkillCardSurface.cs`
- Modify: `Game/MonsterPreview/GameObjectFactory/MonsterPreviewItemCardFactory.cs`
- Modify: `Game/MonsterPreview/GameObjectFactory/MonsterPreviewSkillCardFactory.cs`
- Modify: `Game/MonsterPreview/GameObjectFactory/IPreviewCardFactory.cs`
- Modify: `Game/MonsterPreview/GameObjectFactory/PreviewCardLifecyclePolicy.cs`
- Modify: `Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs`

- [ ] **Step 1: Preserve the current behavior seam before editing**

No new unit test should be added here unless a pure extraction seam appears naturally. This task is Unity object lifecycle migration around `ItemController` / `SkillController`, so the primary verification is targeted build plus existing behavior reuse.

- [ ] **Step 2: Create neutral card surface implementations**

Move the existing factory logic into `Game/PreviewSurface/Cards`, renaming the types but keeping the runtime behavior the same:

```csharp
namespace BazaarPlusPlus.Game.PreviewSurface;

internal sealed class PreviewItemCardSurface : IPreviewCardSurface
{
    public async Task<GameObject> CreateAsync(PreviewCardSpec spec, Transform parent)
    {
        // existing MonsterPreviewItemCardFactory.CreateCardAsync body, adjusted to neutral names
    }

    public Task UpdateAsync(GameObject cardObject, PreviewCardSpec spec)
    {
        return Task.CompletedTask;
    }

    public void Destroy(GameObject cardObject)
    {
        // existing cleanup/pool logic
    }
}
```

Do the same for `PreviewSkillCardSurface`, preserving:

- `ItemController` / `SkillController` setup and cleanup
- `ShowcaseCardMarker`
- movement disable/enable behavior
- hover tooltip reuse

- [ ] **Step 3: Retarget board construction to the neutral card surfaces**

Update the current board construction site to stop newing monster-specific factories:

```csharp
var board = new MonsterPreviewBoard(
    "MonsterPreviewBoard",
    new PreviewItemCardSurface(),
    new PreviewSkillCardSurface()
);
```

If the old `IPreviewCardFactory` name becomes misleading at this point, rename it to align with `IPreviewCardSurface` and update the board constructor signature in the same change.

- [ ] **Step 4: Run a targeted main-project build**

Run:

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

If needed:

```bash
dotnet build BazaarPlusPlus.csproj -c Debug -p:ManagedPath="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed"
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add Game/PreviewSurface/Cards Game/MonsterPreview/GameObjectFactory
git commit -m "Extract preview card surfaces"
```

### Task 3: Introduce Board Surface And Cancel-And-Replace Render Semantics

**Files:**
- Create: `Game/PreviewSurface/Board/PreviewBoardSurface.cs`
- Create: `Game/PreviewSurface/Board/PreviewBoardRenderTarget.cs`
- Create: `tests/PreviewSurfaceHost.Tests/PreviewSurfaceHost.Tests.csproj`
- Create: `tests/PreviewSurfaceHost.Tests/Program.cs`
- Modify: `Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs`
- Modify: `Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs`
- Modify: `Game/MonsterPreview/Architecture/IBoardRenderTarget.cs`
- Modify: `Game/MonsterPreview/Architecture/BoardRenderModel.cs`
- Modify: `Game/MonsterPreview/Architecture/PreviewRenderGenerationGate.cs`

- [ ] **Step 1: Write the failing host-level tests for render cancellation and replacement**

Create `tests/PreviewSurfaceHost.Tests/Program.cs` with a fake board surface that records render calls and cancellation states. Cover at least these cases:

- a second render cancels the first in-flight render
- `SetVisible(false)` clears the surface and cancels the current render
- the latest render request is the only one allowed to commit

Start with a test shape like this:

```csharp
var surface = new RecordingBoardSurface();
var target = new PreviewBoardRenderTarget(surface);

target.Render(firstModel);
target.Render(secondModel);

Assert(surface.RenderCallCount == 2, "Each request should be forwarded.");
Assert(surface.CancelledRenderCount >= 1, "A newer request should cancel the previous one.");
Assert(surface.LastRenderedSignature == "second", "The latest request should win.");
```

- [ ] **Step 2: Run the new host test project to verify it fails**

Run:

```bash
dotnet run --project tests/PreviewSurfaceHost.Tests/PreviewSurfaceHost.Tests.csproj
```

Expected: FAIL because `PreviewBoardRenderTarget` does not yet expose the new board-surface constructor or `CancellationToken`-based render path.

- [ ] **Step 3: Move the board implementation to `PreviewBoardSurface` and update the render target**

Create the neutral board surface by moving the current `MonsterPreviewBoard` implementation into `Game/PreviewSurface/Board/PreviewBoardSurface.cs`, keeping the current layout, carpet, metadata text, visibility, and anchor behavior.

Then create a neutral render target with `CancellationTokenSource`-based replacement:

```csharp
internal sealed class PreviewBoardRenderTarget : IBoardRenderTarget, IDisposable
{
    private readonly IPreviewBoardSurface _surface;
    private CancellationTokenSource? _renderCts;

    public void Render(BoardRenderModel renderModel)
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        _ = _surface.RenderAsync(renderModel.Data, _renderCts.Token);
    }

    public void SetVisible(bool visible)
    {
        if (!visible)
        {
            _renderCts?.Cancel();
            _surface.Clear();
        }

        _surface.SetVisible(visible);
    }
}
```

If `PreviewRenderGenerationGate` becomes redundant after the `CancellationTokenSource` migration, remove it from the runtime path in the same task. Do not keep both mechanisms active unless there is a verified gap that one cannot cover.

- [ ] **Step 4: Run the host test project again**

Run:

```bash
dotnet run --project tests/PreviewSurfaceHost.Tests/PreviewSurfaceHost.Tests.csproj
```

Expected: PASS with a console line confirming the preview host checks passed.

- [ ] **Step 5: Run the existing resilience test project**

Run:

```bash
dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
```

Expected: `MonsterPreviewResilience checks passed.`

- [ ] **Step 6: Commit**

```bash
git add Game/PreviewSurface/Board Game/MonsterPreview/Architecture Game/MonsterPreview/GameObjectFactory tests/PreviewSurfaceHost.Tests tests/MonsterPreviewResilience.Tests
git commit -m "Extract preview board surface and render host"
```

### Task 4: Retarget MonsterPreview To The New Preview Surface Stack

**Files:**
- Modify: `Game/MonsterPreview/MonsterPreviewController.cs`
- Modify: `Game/MonsterPreview/Architecture/MonsterPreviewOverlayCoordinator.cs`
- Modify: `Game/MonsterPreview/Architecture/PreviewBoardSession.cs`
- Modify: `Game/MonsterPreview/Architecture/InMemoryPreviewDataSource.cs`
- Modify: `Game/MonsterPreview/Architecture/PreviewBoardRequest.cs`
- Modify: `Game/MonsterPreview/Architecture/PreviewBoardSignature.cs`
- Modify: `Game/MonsterPreview/MonsterLockShowcaseRuntime.cs`
- Modify: `Game/MonsterPreview/DataSources/MonsterPreviewProjector.cs`

- [ ] **Step 1: Retarget `MonsterPreview` to depend on preview-surface abstractions only**

Update the runtime-facing code so `MonsterPreview` no longer knows about concrete card creation or board object classes. The host layer should only deal with:

- `PreviewBoardModel`
- `PreviewBoardPresentation`
- `PreviewBoardRequest`
- `IBoardRenderTarget` / `IPreviewBoardSurface`

The runtime should no longer directly construct or manipulate card objects. The dependency direction should look like this:

```csharp
MonsterLockShowcaseRuntime
    -> MonsterPreviewController
    -> MonsterPreviewOverlayCoordinator
    -> PreviewBoardSession
    -> PreviewBoardRenderTarget
    -> PreviewBoardSurface
```

- [ ] **Step 2: Keep `MonsterPreview` behavior stable while shrinking its responsibility**

Preserve the current flow of:

- setting cards and skill cards
- applying presentation and debug options
- setting visibility
- refreshing and ticking

but make sure the actual board implementation now lives under `Game/PreviewSurface`.

- [ ] **Step 3: Run the targeted test projects**

Run:

```bash
dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
dotnet run --project tests/PreviewSurfaceHost.Tests/PreviewSurfaceHost.Tests.csproj
```

Expected: both PASS.

- [ ] **Step 4: Run a final targeted build of the mod**

Run:

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
```

If needed:

```bash
dotnet build BazaarPlusPlus.csproj -c Debug -p:ManagedPath="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed"
```

Expected: `Build succeeded.`

- [ ] **Step 5: Verify dependency direction before closing**

Run:

```bash
rg -n "new (PreviewItemCardSurface|PreviewSkillCardSurface|PreviewBoardSurface|ItemController|SkillController)" Game/MonsterPreview
```

Expected: no direct creation of preview card or board implementation from `Game/MonsterPreview` runtime files.

- [ ] **Step 6: Commit**

```bash
git add Game/PreviewSurface Game/MonsterPreview tests/MonsterPreviewResilience.Tests tests/PreviewSurfaceHost.Tests
git commit -m "Retarget monster preview to preview surface"
```

## Self-Review

- Spec coverage: the plan covers neutral models, card surfaces, board surface, render cancellation semantics, `MonsterPreview` retargeting, and the dependency-direction verification called out in the spec.
- Placeholder scan: no `TODO`/`TBD` placeholders remain; each task names files, commands, and expected outcomes.
- Type consistency: the plan consistently uses `PreviewCardSpec`, `PreviewBoardModel`, `IPreviewCardSurface`, `IPreviewBoardSurface`, and `PreviewBoardRenderTarget` as the neutral names.

