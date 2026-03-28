# Combat Log Panel Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rebuild the combat log into a frame-grouped, scrollable panel with Release/Debug display modes, Compact/Standard/Verbose verbosity levels, and lazy rendering that stays responsive on large combat timelines.

**Architecture:** Keep `CombatLogRuntime` focused on immutable structured combat data, add a view-model layer that filters and formats frame groups on demand, and split text generation into dedicated Release and Debug formatters. The panel should render only visible rows, cache view-model output by display options, and lazily expand verbose frame details instead of precomputing every line.

**Tech Stack:** C#, Unity IMGUI, existing `CombatLogRuntime` / `CombatLogPanel` pipeline, focused console-style test projects under `tests/`.

---

### Task 1: Add display options and frame-group view models

**Files:**
- Create: `Game/CombatLog/CombatLogDisplayOptions.cs`
- Create: `Game/CombatLog/CombatLogViewModels.cs`
- Modify: `Game/CombatLog/CombatLogModels.cs`
- Test: `tests/CombatLogRuntime.Tests/Program.cs`

**Step 1: Write the failing test**

Add assertions in `tests/CombatLogRuntime.Tests/Program.cs` that express the new view concepts without touching IMGUI:

```csharp
var options = CombatLogDisplayOptions.ReleaseStandard;
Assert(options.Mode == CombatLogDisplayMode.Release, "Release preset should default to release mode.");
Assert(options.Verbosity == CombatLogVerbosity.Standard, "Release preset should default to standard verbosity.");

var group = new CombatLogFrameGroupViewModel(
    frameIndex: 12,
    logicalTime: TimeSpan.FromMilliseconds(600),
    summaryText: "2 events, 1 combatant update",
    isExpanded: false,
    visibleRowCount: 3,
    rows: new[] { new CombatLogDisplayRowViewModel("primary", null, false) }
);
Assert(group.FrameIndex == 12, "Frame group should expose frame metadata.");
Assert(group.Rows.Count == 1, "Frame group should own its rendered rows.");
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: FAIL with missing `CombatLogDisplayOptions`, `CombatLogFrameGroupViewModel`, or related symbols.

**Step 3: Write minimal implementation**

Create immutable option and view-model types that will survive later refactors:

```csharp
internal enum CombatLogDisplayMode
{
    Release,
    Debug,
}

internal enum CombatLogVerbosity
{
    Compact,
    Standard,
    Verbose,
}

internal readonly record struct CombatLogDisplayOptions(
    CombatLogDisplayMode Mode,
    CombatLogVerbosity Verbosity,
    bool ShowEvents = true,
    bool ShowCombatants = true,
    bool ShowCards = true,
    bool ShowRewards = true,
    bool ShowSystem = true,
    bool ShowUnknown = false,
    bool ShowEmptyFrames = false
)
{
    public static CombatLogDisplayOptions ReleaseStandard =>
        new(CombatLogDisplayMode.Release, CombatLogVerbosity.Standard);

    public static CombatLogDisplayOptions DebugVerbose =>
        new(
            CombatLogDisplayMode.Debug,
            CombatLogVerbosity.Verbose,
            ShowUnknown: true
        );
}
```

```csharp
internal sealed class CombatLogDisplayRowViewModel
{
    public CombatLogDisplayRowViewModel(string primaryText, string? secondaryText, bool emphasize)
    {
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        Emphasize = emphasize;
    }

    public string PrimaryText { get; }
    public string? SecondaryText { get; }
    public bool Emphasize { get; }
}

internal sealed class CombatLogFrameGroupViewModel
{
    public CombatLogFrameGroupViewModel(
        int frameIndex,
        TimeSpan logicalTime,
        string summaryText,
        bool isExpanded,
        int visibleRowCount,
        IReadOnlyList<CombatLogDisplayRowViewModel> rows
    )
    {
        FrameIndex = frameIndex;
        LogicalTime = logicalTime;
        SummaryText = summaryText;
        IsExpanded = isExpanded;
        VisibleRowCount = visibleRowCount;
        Rows = rows;
    }

    public int FrameIndex { get; }
    public TimeSpan LogicalTime { get; }
    public string SummaryText { get; }
    public bool IsExpanded { get; }
    public int VisibleRowCount { get; }
    public IReadOnlyList<CombatLogDisplayRowViewModel> Rows { get; }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS with the new option and view-model assertions succeeding.

**Step 5: Commit**

```bash
git add Game/CombatLog/CombatLogDisplayOptions.cs Game/CombatLog/CombatLogViewModels.cs Game/CombatLog/CombatLogModels.cs tests/CombatLogRuntime.Tests/Program.cs
git commit -m "Add combat log display option models"
```

### Task 2: Preserve the extra structured metadata needed by Debug mode

**Files:**
- Modify: `Game/CombatLog/CombatLogModels.cs`
- Modify: `Game/CombatLog/CombatLogRuntime.cs`
- Test: `tests/CombatLogRuntime.Tests/Program.cs`

**Step 1: Write the failing test**

Extend `tests/CombatLogRuntime.Tests/Program.cs` so the sample combat includes trigger sources and mixed target types, then assert the timeline preserves them:

```csharp
new CombatSimEventEffectExecuted
{
    ExecutionContextId = "ctx-1",
    EffectId = "burn",
    Source = new InstanceId("card-a"),
    TriggerSource = new InstanceId("card-b"),
    Target = new EffectTargetPlayer { Target = ECombatantId.Opponent },
},
```

```csharp
var executed = timeline.Frames[0].Events.First(entry => entry.EventType == "EffectExecuted");
Assert(executed.TriggerSourceId == "card-b", "Debug mode needs trigger-source chaining.");
Assert(executed.TargetKind == "Player", "Formatter should know whether a target is a player or card.");
Assert(executed.ExecutionContextId == "ctx-1", "Execution context should remain available to debug output.");
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: FAIL because `CombatLogEventEntry` does not yet preserve `TriggerSourceId` or `TargetKind`.

**Step 3: Write minimal implementation**

Extend `CombatLogEventEntry` to retain structured metadata instead of only preformatted text:

```csharp
internal sealed class CombatLogEventEntry
{
    public CombatLogEventEntry(
        string eventType,
        string? executionContextId,
        string? sourceId,
        string? triggerSourceId,
        string? targetId,
        string? targetKind,
        string? sourceDisplayName,
        string? targetDisplayName,
        string text
    )
    {
        EventType = eventType;
        ExecutionContextId = executionContextId;
        SourceId = sourceId;
        TriggerSourceId = triggerSourceId;
        TargetId = targetId;
        TargetKind = targetKind;
        SourceDisplayName = sourceDisplayName;
        TargetDisplayName = targetDisplayName;
        Text = text;
    }

    public string? TriggerSourceId { get; }
    public string? TargetKind { get; }
}
```

Update `BuildEvents(...)`, `JoinTargets(...)`, and target-format helpers so they preserve:

- `TriggerSourceId`
- `TargetKind` such as `Player`, `Card`, or a fallback type name
- future-friendly structured values for aura targets and transformations

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS with the added metadata assertions succeeding.

**Step 5: Commit**

```bash
git add Game/CombatLog/CombatLogModels.cs Game/CombatLog/CombatLogRuntime.cs tests/CombatLogRuntime.Tests/Program.cs
git commit -m "Preserve structured combat log debug metadata"
```

### Task 3: Split formatting into Release and Debug formatters plus a view-model builder

**Files:**
- Create: `Game/CombatLog/CombatLogFrameSummaryBuilder.cs`
- Create: `Game/CombatLog/CombatLogReleaseFormatter.cs`
- Create: `Game/CombatLog/CombatLogDebugFormatter.cs`
- Create: `Game/CombatLog/CombatLogViewModelBuilder.cs`
- Modify: `Game/CombatLog/CombatLogFormatter.cs`
- Modify: `Game/CombatLog/CombatLogPanelState.cs`
- Test: `tests/CombatLogRuntime.Tests/Program.cs`

**Step 1: Write the failing test**

Add tests that build frame groups under several display options and verify:

```csharp
var builder = new CombatLogViewModelBuilder();
var releaseGroups = builder.Build(timeline, CombatLogDisplayOptions.ReleaseStandard, expandedFrames: []);
Assert(releaseGroups.Count == 2, "Non-empty frames should become visible frame groups.");
Assert(releaseGroups[0].Rows[0].PrimaryText.Contains("Fiery Dagger", StringComparison.Ordinal), "Release formatter should prefer readable names.");
Assert(releaseGroups[0].Rows.All(row => row.SecondaryText is null), "Release formatter should suppress debug metadata.");

var debugGroups = builder.Build(timeline, CombatLogDisplayOptions.DebugVerbose, expandedFrames: [0]);
Assert(debugGroups[0].Rows.Any(row => row.SecondaryText?.Contains("ctx=ctx-1", StringComparison.Ordinal) == true), "Debug formatter should expose execution context.");
Assert(debugGroups[0].SummaryText.Contains("events", StringComparison.Ordinal), "Each frame should expose a summary string.");
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: FAIL because the builder and formatter types do not exist yet.

**Step 3: Write minimal implementation**

Add a formatter interface and two implementations:

```csharp
internal interface ICombatLogLineFormatter
{
    CombatLogDisplayRowViewModel FormatEvent(CombatLogFrame frame, CombatLogEventEntry entry, CombatLogDisplayOptions options);
    CombatLogDisplayRowViewModel FormatCombatant(CombatLogFrame frame, string side, CombatLogHealthAdjustment health);
    CombatLogDisplayRowViewModel FormatCombatant(CombatLogFrame frame, string side, CombatLogAttributeChange attribute);
    CombatLogDisplayRowViewModel FormatCard(CombatLogFrame frame, CombatLogCardUpdateEntry entry, CombatLogAttributeChange attribute);
    CombatLogDisplayRowViewModel FormatCardDetail(CombatLogFrame frame, CombatLogCardUpdateEntry entry, string detail);
}
```

In `CombatLogViewModelBuilder`, implement this flow:

```csharp
foreach (var frame in timeline.Frames)
{
    if (!ShouldIncludeFrame(frame, options))
        continue;

    var summary = CombatLogFrameSummaryBuilder.Build(frame, options);
    var expanded = expandedFrames.Contains(frame.FrameIndex) || options.Verbosity != CombatLogVerbosity.Verbose;
    var rows = expanded ? BuildExpandedRows(frame, options) : BuildSummaryRows(frame, options);
    groups.Add(new CombatLogFrameGroupViewModel(frame.FrameIndex, frame.LogicalTime, summary, expanded, rows.Count, rows));
}
```

Keep `CombatLogFormatter` only if it still adds value as a shared helper; otherwise reduce it to shared categorization logic used by the new builder.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS with separate Release and Debug formatting behavior verified.

**Step 5: Commit**

```bash
git add Game/CombatLog/CombatLogFrameSummaryBuilder.cs Game/CombatLog/CombatLogReleaseFormatter.cs Game/CombatLog/CombatLogDebugFormatter.cs Game/CombatLog/CombatLogViewModelBuilder.cs Game/CombatLog/CombatLogFormatter.cs Game/CombatLog/CombatLogPanelState.cs tests/CombatLogRuntime.Tests/Program.cs
git commit -m "Build combat log frame-group view models"
```

### Task 4: Refactor the panel into a fixed-header, scrollable frame-group browser

**Files:**
- Modify: `Game/CombatLog/CombatLogPanel.cs`
- Modify: `Game/CombatLog/CombatLogPanelState.cs`
- Modify: `Game/CombatLog/CombatLogViewport.cs`
- Test: `tests/CombatLogRuntime.Tests/Program.cs`
- Test: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`

**Step 1: Write the failing test**

Add tests around panel state that verify:

- default mode selection
- current-frame expansion in verbose mode
- empty frames are hidden by default
- toggling mode/verbosity invalidates the visible-row cache

Example assertions:

```csharp
var state = new CombatLogPanelState();
state.Refresh(processedFrameCount: 1);
Assert(state.DisplayOptions.Mode == CombatLogDisplayMode.Debug, "Debug builds should start in debug mode.");
Assert(state.DisplayOptions.Verbosity == CombatLogVerbosity.Verbose, "Debug builds should start verbose.");

var groups = state.BuildVisibleFrameGroups(timeline);
Assert(groups.All(group => group.VisibleRowCount > 0), "Empty frames should not be rendered by default.");
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: FAIL because panel state still exposes only flat visible rows.

**Step 3: Write minimal implementation**

Refactor `CombatLogPanelState` to own:

- current `CombatLogDisplayOptions`
- expanded frame IDs
- cached `CombatLogFrameGroupViewModel` list
- a flattened viewport projection only for currently expanded, visible groups

Refactor `CombatLogPanel` so the header is fixed and the scroll area renders frame groups:

```csharp
GUILayout.Label("Combat Log", HeaderStyle);
DrawModeButtons();
DrawVerbosityButtons();
DrawSectionFilters();

_scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(viewportHeight));
foreach (var group in visibleGroups)
{
    DrawFrameHeader(group);
    if (group.IsExpanded)
        DrawFrameRows(group.Rows);
}
GUILayout.EndScrollView();
```

Update `CombatLogViewport` only if needed so virtualization uses group-aware heights instead of assuming a flat list.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: PASS with cache invalidation and playback-state tests still succeeding.

**Step 5: Commit**

```bash
git add Game/CombatLog/CombatLogPanel.cs Game/CombatLog/CombatLogPanelState.cs Game/CombatLog/CombatLogViewport.cs tests/CombatLogRuntime.Tests/Program.cs tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs
git commit -m "Refactor combat log panel into frame groups"
```

### Task 5: Add safeguards for large timelines and finish focused verification

**Files:**
- Modify: `tests/CombatLogRuntime.Tests/Program.cs`
- Modify: `Game/CombatLog/CombatLogViewModelBuilder.cs`
- Modify: `Game/CombatLog/CombatLogPanelState.cs`

**Step 1: Write the failing test**

Add a stress-style test that builds many frames and asserts the builder/panel state avoid eagerly expanding everything:

```csharp
var denseTimeline = BuildLargeTimeline(runtime, frameCount: 500, rowsPerFrame: 8);
var state = new CombatLogPanelState();
state.SetDisplayOptions(CombatLogDisplayOptions.DebugVerbose);
var groups = state.BuildVisibleFrameGroups(denseTimeline);

Assert(groups.Count > 0, "Large timelines should still produce visible frame groups.");
Assert(groups.Count < denseTimeline.Frames.Count + 1, "Empty frames should stay filtered.");
Assert(groups.Count(group => group.IsExpanded) <= 1, "Verbose mode should not eagerly expand every frame.");
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: FAIL if the builder eagerly formats every row for every frame.

**Step 3: Write minimal implementation**

Ensure the builder and state cache are lazy:

```csharp
if (!expanded && options.Verbosity == CombatLogVerbosity.Verbose)
    return BuildSummaryOnlyRows(frame, options);

if (_cachedTimeline == timeline && _cachedOptions.Equals(options) && _cachedExpandedFrames.SetEquals(expandedFrames))
    return _cachedGroups;
```

Prefer a single expanded frame by default:

```csharp
if (options.Verbosity == CombatLogVerbosity.Verbose && expandedFrames.Count == 0 && TryGetCurrentFrameIndex(out var currentFrame))
    expandedFrames.Add(currentFrame);
```

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: PASS with the new large-timeline assertions green and no regressions in playback-state logic.

**Step 5: Commit**

```bash
git add Game/CombatLog/CombatLogViewModelBuilder.cs Game/CombatLog/CombatLogPanelState.cs tests/CombatLogRuntime.Tests/Program.cs
git commit -m "Optimize combat log rendering for large timelines"
```

### Task 6: Final verification and cleanup

**Files:**
- Modify: any touched files from Tasks 1-5

**Step 1: Run focused verification**

Run: `dotnet run --project tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS with all combat-log-specific assertions green.

**Step 2: Run secondary verification**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: PASS with no regressions in playback visual-state logic.

**Step 3: Manual sanity-check targets**

Verify in-game or with screenshots, if convenient:

- `Release + Standard` shows readable frame-grouped rows
- `Debug + Verbose` shows structured secondary text
- empty frames are hidden by default
- scrolling remains smooth on long replays
- expanding one verbose frame does not expand the whole timeline

**Step 4: Commit final integration**

```bash
git add Game/CombatLog tests/CombatLogRuntime.Tests tests/CombatStatusBarState.Tests
git commit -m "Implement frame-grouped combat log panel"
```
