using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Game.CombatLog;
using System.Globalization;

var runtime = new CombatLogRuntime(ResolveCardDisplayInfo);
var timeline = BuildTimeline(runtime);
VerifyDisplayOptionsAndViewModels();
VerifyTimeline(timeline);
VerifyViewModelBuilder(timeline);
VerifyPanelState(timeline);
VerifyLargeTimelineSafeguards();
VerifyPanelFilters(timeline);
VerifyVisibleRowCaching(timeline);
VerifyViewportWindowing();
VerifySourceWiring();

Console.WriteLine("CombatLogRuntime checks passed.");

static void VerifyDisplayOptionsAndViewModels()
{
    var options = CombatLogDisplayOptions.ReleaseStandard;
    Assert(
        options.Mode == CombatLogDisplayMode.Release,
        "Release preset should default to release mode."
    );
    Assert(
        options.Verbosity == CombatLogVerbosity.Standard,
        "Release preset should default to standard verbosity."
    );

    var group = new CombatLogFrameGroupViewModel(
        frameIndex: 12,
        logicalTime: TimeSpan.FromMilliseconds(600),
        summaryText: "2 events, 1 combatant update",
        visualState: CombatLogRowVisualState.Played,
        isExpanded: false,
        visibleRowCount: 3,
        totalRowCount: 3,
        rows: new[]
        {
            new CombatLogDisplayRowViewModel(
                "primary",
                null,
                false,
                CombatLogRowVisualState.Played
            )
        }
    );
    Assert(group.FrameIndex == 12, "Frame group should expose frame metadata.");
    Assert(group.Rows.Count == 1, "Frame group should own its rendered rows.");
    Assert(
        group.VisualState == CombatLogRowVisualState.Played,
        "Frame group should expose playback visual state."
    );
}

static CombatLogTimeline BuildTimeline(CombatLogRuntime runtime)
{
    var combat = new CombatSim();
    combat.Frames = new List<CombatSimFrame>
    {
        new CombatSimFrame
        {
            Events = new List<ICombatSimEvent>
            {
                new CombatSimEventEffectExecuted
                {
                    ExecutionContextId = "ctx-1",
                    EffectId = "burn",
                    Source = new InstanceId("card-a"),
                    TriggerSource = new InstanceId("card-b"),
                    Target = new EffectTargetPlayer { Target = ECombatantId.Opponent },
                },
                new CombatSimEventMonsterGoldReceived { HealthAmount = 3 },
                new CombatSimEventEffectTriggered
                {
                    ExecutionContextId = "ctx-2",
                    EffectId = "poison",
                    Source = new InstanceId("card-a"),
                    Targets = new List<IEffectTarget>
                    {
                        new EffectTargetPlayer { Target = ECombatantId.Opponent },
                    },
                },
                new CombatSimEventCardEnchanted
                {
                    InstanceId = "card-a",
                    EnchantmentType = EEnchantmentType.Radiant,
                },
            },
            PlayerUpdates = new CombatSimPlayerUpdate
            {
                HealthAdjustments = new List<CombatSimPlayerHealthAdjustment>
                {
                    new CombatSimPlayerHealthAdjustment
                    {
                        AttributeChanged = EPlayerHealthChangeType.Health,
                        Amount = -6,
                    },
                },
                Attributes = new Dictionary<EPlayerAttributeType, CombatSimPlayerAttributeUpdate>
                {
                    {
                        EPlayerAttributeType.Poison,
                        new CombatSimPlayerAttributeUpdate
                        {
                            AttributeType = EPlayerAttributeType.Poison,
                            PreviousValue = 1,
                            CurrentValue = 4,
                        }
                    },
                },
            },
            CardUpdates = new Dictionary<InstanceId, CombatSimCardUpdate>
            {
                {
                    new InstanceId("card-a"),
                    new CombatSimCardUpdate
                    {
                        CardInstanceId = new InstanceId("card-a"),
                        Attributes = new Dictionary<
                            ECardAttributeType,
                            CombatSimCardAttributeUpdate
                        >
                        {
                            {
                                ECardAttributeType.CritChance,
                                new CombatSimCardAttributeUpdate
                                {
                                    AttributeType = ECardAttributeType.CritChance,
                                    PreviousValue = 1,
                                    CurrentValue = 2,
                                }
                            },
                        },
                    }
                },
            },
        },
        new CombatSimFrame
        {
            Events = new List<ICombatSimEvent>
            {
                new CombatSimEventCombatantDied { CombatantId = ECombatantId.Opponent },
                new CombatSimEventMonsterXpReceived { HealthAmount = 5 },
                new CombatSimEventCardQuestUpdated("card-a", 1, 2, 3, 4),
                new CombatSimEventCardTransformed(
                    "ctx-3",
                    "card-a",
                    new List<SimEventCardTransformation>
                    {
                        new("card-b", "tpl-b", ECardType.Item, ECombatantId.Player, null, null),
                    }
                ),
                new CombatSimEventSandstormStarted(),
            },
        },
    };

    runtime.ReplaceCombat(combat, CombatLogPlaybackPass.FirstPlay);
    return runtime.CurrentTimeline ?? throw new InvalidOperationException("Timeline missing.");
}

static void VerifyTimeline(CombatLogTimeline timeline)
{
    Assert(
        timeline.Frames.Count == 2,
        "Runtime should map each sim frame into one combat log frame."
    );
    Assert(
        timeline.Frames[1].LogicalTime
            == TimeSpan.FromMilliseconds(CombatLogTiming.MillisecondsPerFrame),
        "Logical time should use frameIndex * 50ms."
    );
    Assert(
        timeline.Rows.Count >= 6,
        "Formatter should emit deterministic rows for supported events and updates."
    );
    Assert(
        timeline.Rows[0].Category == CombatLogRowCategory.Event
            && timeline.Rows[0].Text.Contains("炽焰匕首", StringComparison.Ordinal),
        "EffectExecuted should produce the first display row for the frame."
    );
    Assert(
        timeline.Rows.Any(row =>
            row.Category == CombatLogRowCategory.Health
            && row.Text.Contains("-6", StringComparison.Ordinal)
        ),
        "Player health adjustments should become display rows."
    );
    Assert(
        timeline.Rows.Any(row =>
            row.Category == CombatLogRowCategory.Attribute
            && row.Text.Contains("Poison", StringComparison.Ordinal)
        ),
        "Player attribute updates should become display rows."
    );
    Assert(
        timeline.Rows.Any(row => row.Category == CombatLogRowCategory.CardAttribute),
        "Card attribute updates should become display rows."
    );
    Assert(
        timeline.Rows.Any(row =>
            row.Category == CombatLogRowCategory.CardAttribute
            && row.Text.Contains("炽焰匕首", StringComparison.Ordinal)
            && !row.Text.Contains("Card card-a", StringComparison.Ordinal)
        ),
        "Card attribute rows should prefer resolved display names over raw instance IDs."
    );
    Assert(
        timeline.Rows.Any(row =>
            row.Category == CombatLogRowCategory.CardAttribute
            && row.SecondaryText != null
            && row.SecondaryText.Contains("card-a", StringComparison.Ordinal)
        ),
        "Card attribute rows should keep the raw instance ID as secondary debug context."
    );
    Assert(
        timeline.Rows.Any(row =>
            row.Category == CombatLogRowCategory.Death
            && (row.Text.Contains("对手", StringComparison.Ordinal)
                || row.Text.Contains("Opponent", StringComparison.Ordinal))
        ),
        "CombatantDied should become a death row."
    );
    Assert(
        timeline.Rows.Any(row =>
            row.Category == CombatLogRowCategory.Event
            && row.Text.Contains("炽焰匕首", StringComparison.Ordinal)
            && row.Text.Contains("对手", StringComparison.Ordinal)
        ),
        "EffectTriggered should become a display row that uses the resolved source card name."
    );
    var executed = timeline.Frames[0].Events.First(entry => entry.EventType == "EffectExecuted");
    Assert(
        executed.TriggerSourceId == "card-b",
        "Debug mode needs trigger-source chaining."
    );
    Assert(
        executed.TargetKind == "Player",
        "Formatter should know whether a target is a player or card."
    );
    Assert(
        executed.ExecutionContextId == "ctx-1",
        "Execution context should remain available to debug output."
    );
    Assert(
        timeline.Rows.Any(row =>
            row.Text.Contains("炽焰匕首", StringComparison.Ordinal)
            && (row.Text.Contains("任务更新", StringComparison.Ordinal)
                || row.Text.Contains("Quest updated", StringComparison.Ordinal))
            && row.Text.Contains("3 -> 4", StringComparison.Ordinal)
        ),
        "Quest updates should become display rows."
    );
    Assert(
        timeline.Rows.Any(row =>
            row.Text.Contains("炽焰匕首", StringComparison.Ordinal)
            && row.Text.Contains("灰烬回响", StringComparison.Ordinal)
            && !row.Text.Contains("tpl-b", StringComparison.Ordinal)
        ),
        "Transform events should become display rows."
    );
    Assert(
        timeline.Rows.Any(row => row.Category == CombatLogRowCategory.System),
        "System events should be categorized distinctly for filtering."
    );
}

static void VerifyViewModelBuilder(CombatLogTimeline timeline)
{
    var previousCulture = CultureInfo.CurrentCulture;
    var previousUICulture = CultureInfo.CurrentUICulture;
    CultureInfo.CurrentCulture = new CultureInfo("zh-CN");
    CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");

    try
    {
    var builder = new CombatLogViewModelBuilder();
    var releaseGroups = builder.Build(timeline, CombatLogDisplayOptions.ReleaseStandard, []);
    Assert(releaseGroups.Count == 2, "Non-empty frames should become visible frame groups.");
    Assert(
        releaseGroups[0].Rows[0].PrimaryText.Contains("炽焰匕首", StringComparison.Ordinal),
        "Release formatter should prefer readable names."
    );
    Assert(
        releaseGroups[0].Rows.All(row => row.SecondaryText is null),
        "Release formatter should suppress debug metadata."
    );

    var debugGroups = builder.Build(timeline, CombatLogDisplayOptions.DebugVerbose, [0]);
    Assert(
        debugGroups[0].Rows.Any(row =>
            row.SecondaryText?.Contains("ctx=ctx-1", StringComparison.Ordinal) == true
        ),
        "Debug formatter should expose execution context."
    );
    Assert(
        debugGroups[0].SummaryText.Contains("事件", StringComparison.Ordinal)
            || debugGroups[0].SummaryText.Contains("event", StringComparison.Ordinal),
        "Each frame should expose a summary string."
    );
    Assert(
        releaseGroups[0].SummaryText.Contains("3 个事件", StringComparison.Ordinal)
            && releaseGroups[0].SummaryText.Contains("1 个奖励", StringComparison.Ordinal),
        "Summary should count action events separately from reward events and localize the count labels."
    );
    Assert(
        releaseGroups[1].Rows.Any(row =>
            row.PrimaryText.Contains("沙暴开始", StringComparison.Ordinal)
        ),
        "Localized release rows should not keep the English system-event sentence."
    );
    Assert(
        releaseGroups[1].Rows.Any(row =>
            row.PrimaryText.Contains("任务更新", StringComparison.Ordinal)
        ),
        "Localized release rows should not keep the English quest sentence."
    );
    var transformedReleaseRow = releaseGroups[1].Rows.First(row =>
        row.PrimaryText.Contains("变形", StringComparison.Ordinal)
    );
    Assert(
        !transformedReleaseRow.PrimaryText.Contains("tpl-b", StringComparison.Ordinal)
            && !transformedReleaseRow.PrimaryText.Contains("Player", StringComparison.Ordinal),
        "Release transform rows should not leak template IDs or combatant metadata."
    );

    var localizedRuntime = new CombatLogRuntime(ResolveCardDisplayInfo);
    var localizedTimeline = BuildTimeline(localizedRuntime);
    Assert(
        localizedTimeline.Rows.Any(row =>
            row.Text.Contains("沙暴开始", StringComparison.Ordinal)
        ),
        "The flat runtime rows should use the localized system-event text consumed by the current panel."
    );
    Assert(
        localizedTimeline.Rows.Any(row =>
            row.Text.Contains("任务更新", StringComparison.Ordinal)
        ),
        "The flat runtime rows should use localized quest text while the panel still renders timeline rows."
    );
    Assert(
        localizedTimeline.Rows.Any(row =>
            row.Text.Contains("变形", StringComparison.Ordinal)
            && !row.Text.Contains("tpl-b", StringComparison.Ordinal)
            && !row.Text.Contains("Player", StringComparison.Ordinal)
        ),
        "The flat runtime rows should not leak transform debug metadata in release-oriented text."
    );

    var syntheticTimeline = new CombatLogTimeline(
        CombatLogPlaybackPass.FirstPlay,
        new[]
        {
            new CombatLogFrame(
                frameIndex: 0,
                framesLeft: 0,
                logicalTime: TimeSpan.Zero,
                events: new[]
                {
                    new CombatLogEventEntry(
                        eventType: "EffectExecuted",
                        executionContextId: "ctx-localized",
                        sourceId: "card-a",
                        triggerSourceId: null,
                        targetId: "Opponent",
                        targetKind: "Player",
                        sourceDisplayName: "炽焰匕首",
                        targetDisplayName: "Opponent",
                        text: "LEGACY EVENT TEXT",
                        formatData: new CombatLogEventFormatData(
                            effectId: "burn",
                            actionType: "Cast"
                        )
                    )
                },
                player: null,
                opponent: null,
                cardUpdates: Array.Empty<CombatLogCardUpdateEntry>()
            )
        },
        Array.Empty<CombatLogRow>()
    );
    var collapsedVerboseGroups = builder.Build(
        syntheticTimeline,
        CombatLogDisplayOptions.DebugVerbose,
        []
    );
    Assert(
        collapsedVerboseGroups[0].VisibleRowCount == 0
            && collapsedVerboseGroups[0].TotalRowCount == 1
            && collapsedVerboseGroups[0].Rows.Count == 0,
        "Collapsed verbose groups should not claim hidden rows are already rendered."
    );

    var expandedSyntheticGroups = builder.Build(
        syntheticTimeline,
        CombatLogDisplayOptions.DebugVerbose,
        [0]
    );
    Assert(
        !expandedSyntheticGroups[0].Rows[0].PrimaryText.Contains(
            "LEGACY EVENT TEXT",
            StringComparison.Ordinal
        ),
        "Formatter should build event copy from structured data, not legacy text."
    );
    Assert(
        expandedSyntheticGroups[0].Rows[0].PrimaryText.Contains("炽焰匕首", StringComparison.Ordinal),
        "Formatter should use the localized card display name in primary event text."
    );
    }
    finally
    {
        CultureInfo.CurrentCulture = previousCulture;
        CultureInfo.CurrentUICulture = previousUICulture;
    }
}

static void VerifyPanelState(CombatLogTimeline timeline)
{
    var firstPlayState = new CombatLogPanelState();
    Assert(
        firstPlayState.DisplayOptions.Mode == CombatLogDisplayMode.Debug,
        "Debug builds should start in debug mode."
    );
    Assert(
        firstPlayState.DisplayOptions.Verbosity == CombatLogVerbosity.Verbose,
        "Debug builds should start verbose."
    );
    firstPlayState.Refresh(1);
    var firstPlayRows = firstPlayState.BuildVisibleRows(timeline);
    Assert(
        firstPlayRows.Any(row =>
            row.Row.FrameIndex == 0 && row.VisualState == CombatLogRowVisualState.Current
        ),
        "Processed frame count should highlight the last processed frame, not the next frame."
    );
    Assert(
        firstPlayRows.All(row => row.Row.FrameIndex <= 0),
        "First play should hide future rows."
    );

    var visibleGroups = firstPlayState.BuildVisibleFrameGroups(timeline);
    Assert(
        visibleGroups.Any(group => group.FrameIndex == 0 && group.IsExpanded),
        "Verbose mode should expand the current processed frame by default."
    );
    Assert(
        visibleGroups.Any(group => group.FrameIndex == 0 && group.VisualState == CombatLogRowVisualState.Current),
        "Visible frame groups should expose the current playback state."
    );
    Assert(
        visibleGroups.All(group => group.VisibleRowCount == group.Rows.Count),
        "Visible row count should match the rows actually rendered for each group."
    );

    firstPlayState.ToggleFrameExpanded(0);
    var collapsedGroups = firstPlayState.BuildVisibleFrameGroups(timeline);
    Assert(
        collapsedGroups.Any(group =>
            group.FrameIndex == 0
            && !group.IsExpanded
            && group.VisibleRowCount == 0
            && group.TotalRowCount > 0
        ),
        "Users should be able to explicitly collapse the default-expanded current frame."
    );

    firstPlayState.ToggleFrameExpanded(0);
    var reExpandedGroups = firstPlayState.BuildVisibleFrameGroups(timeline);
    Assert(
        reExpandedGroups.Any(group => group.FrameIndex == 0 && group.IsExpanded),
        "Toggling the same frame again should re-expand it."
    );

    var sparseTimeline = new CombatLogTimeline(
        timeline.PlaybackPass,
        new[]
        {
            timeline.Frames[0],
            new CombatLogFrame(
                frameIndex: 2,
                framesLeft: 0,
                logicalTime: TimeSpan.FromMilliseconds(100),
                events: Array.Empty<CombatLogEventEntry>(),
                player: null,
                opponent: null,
                cardUpdates: Array.Empty<CombatLogCardUpdateEntry>()
            )
        },
        timeline.Rows
    );
    var sparseGroups = firstPlayState.BuildVisibleFrameGroups(sparseTimeline);
    Assert(
        sparseGroups.All(group => group.TotalRowCount > 0),
        "Empty frames should not be rendered by default."
    );

    var cachedGroups = firstPlayState.BuildVisibleFrameGroups(timeline);
    firstPlayState.SetDisplayOptions(
        new CombatLogDisplayOptions(
            CombatLogDisplayMode.Release,
            CombatLogVerbosity.Standard,
            firstPlayState.DisplayOptions.ShowEvents,
            firstPlayState.DisplayOptions.ShowCombatants,
            firstPlayState.DisplayOptions.ShowCards,
            firstPlayState.DisplayOptions.ShowRewards,
            firstPlayState.DisplayOptions.ShowSystem,
            firstPlayState.DisplayOptions.ShowUnknown,
            firstPlayState.DisplayOptions.ShowEmptyFrames
        )
    );
    var updatedGroups = firstPlayState.BuildVisibleFrameGroups(timeline);
    Assert(
        !object.ReferenceEquals(cachedGroups, updatedGroups),
        "Changing mode or verbosity should invalidate the cached frame-group projection."
    );

    var replayTimeline = new CombatLogTimeline(
        CombatLogPlaybackPass.Replay,
        timeline.Frames,
        timeline.Rows
    );
    var replayState = new CombatLogPanelState();
    replayState.Refresh(1);
    var replayRows = replayState.BuildVisibleRows(replayTimeline);
    Assert(
        replayRows.Any(row =>
            row.Row.FrameIndex == 1 && row.VisualState == CombatLogRowVisualState.FutureDimmed
        ),
        "Replay should keep future rows visible and dimmed."
    );

    var replayGroups = replayState.BuildVisibleFrameGroups(replayTimeline);
    Assert(
        replayGroups.Any(group =>
            group.FrameIndex == 1 && group.VisualState == CombatLogRowVisualState.FutureDimmed
        ),
        "Replay frame groups should keep future frames visible and dimmed."
    );
}

static void VerifyLargeTimelineSafeguards()
{
    var denseTimeline = BuildLargeTimeline(frameCount: 500, rowsPerFrame: 8);
    var state = new CombatLogPanelState();
    state.Refresh(250);
    state.SetDisplayOptions(CombatLogDisplayOptions.DebugVerbose);
    var groups = state.BuildVisibleFrameGroups(denseTimeline);

    Assert(groups.Count > 0, "Large timelines should still produce visible frame groups.");
    Assert(
        groups.Count < denseTimeline.Frames.Count + 1,
        "Empty frames should stay filtered."
    );
    Assert(
        groups.Count(group => group.IsExpanded) <= 1,
        "Verbose mode should not eagerly expand every frame."
    );
}

static void VerifyPanelFilters(CombatLogTimeline timeline)
{
    var state = new CombatLogPanelState();
    state.Refresh(99);

    var defaultRows = state.BuildVisibleRows(timeline);
    Assert(defaultRows.Count > 0, "Default filters should keep combat rows visible.");

    state.ToggleCombatants();
    var withoutCombatantRows = state.BuildVisibleRows(timeline);
    Assert(
        withoutCombatantRows.All(row =>
            row.Row.Category != CombatLogRowCategory.Health
            && row.Row.Category != CombatLogRowCategory.Attribute
        ),
        "Combatant filter should hide health and combatant attribute rows."
    );
    Assert(
        withoutCombatantRows.Any(row => row.Row.Category == CombatLogRowCategory.CardAttribute),
        "Combatant filter should keep card attribute rows visible."
    );

    state.ToggleCards();
    var withoutCombatantOrCardRows = state.BuildVisibleRows(timeline);
    Assert(
        withoutCombatantOrCardRows.All(row =>
            row.Row.Category != CombatLogRowCategory.CardAttribute
        ),
        "Card filter should hide card attribute rows independently."
    );

    state.ToggleSystem();
    var withoutCombatantCardOrSystemRows = state.BuildVisibleRows(timeline);
    Assert(
        withoutCombatantCardOrSystemRows.All(row =>
            row.Row.Category != CombatLogRowCategory.System
        ),
        "System filter should hide system rows."
    );
}

static void VerifyVisibleRowCaching(CombatLogTimeline timeline)
{
    var state = new CombatLogPanelState();
    state.Refresh(99);

    var firstRows = state.BuildVisibleRows(timeline);
    var secondRows = state.BuildVisibleRows(timeline);
    Assert(
        ReferenceEquals(firstRows, secondRows),
        "Visible-row projection should be cached when playback state and filters are unchanged."
    );

    state.ToggleCards();
    var filteredRows = state.BuildVisibleRows(timeline);
    Assert(
        !ReferenceEquals(firstRows, filteredRows),
        "Changing a filter should invalidate the cached visible rows."
    );
}

static CombatLogTimeline BuildLargeTimeline(int frameCount, int rowsPerFrame)
{
    var frames = new List<CombatLogFrame>(frameCount);
    for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
    {
        var isEmptyFrame = frameIndex % 5 == 0;
        var events = new List<CombatLogEventEntry>();
        var cardUpdates = new List<CombatLogCardUpdateEntry>();
        if (!isEmptyFrame)
        {
            for (var rowIndex = 0; rowIndex < rowsPerFrame / 2; rowIndex++)
            {
                events.Add(
                    new CombatLogEventEntry(
                        eventType: "EffectExecuted",
                        executionContextId: $"ctx-{frameIndex}-{rowIndex}",
                        sourceId: "card-a",
                        triggerSourceId: null,
                        targetId: "Opponent",
                        targetKind: "Player",
                        sourceDisplayName: "炽焰匕首",
                        targetDisplayName: "对手",
                        text: $"legacy-{frameIndex}-{rowIndex}",
                        formatData: new CombatLogEventFormatData(
                            effectId: "burn",
                            actionType: "Cast"
                        )
                    )
                );
            }

            cardUpdates.Add(
                new CombatLogCardUpdateEntry(
                    new CombatLogCardDisplayInfo("card-a", "tpl-a", "炽焰匕首"),
                    new[]
                    {
                        new CombatLogAttributeChange("CritChance", frameIndex, frameIndex + 1)
                    },
                    new[] { $"Tier -> {frameIndex % 3}" }
                )
            );
        }

        frames.Add(
            new CombatLogFrame(
                frameIndex: frameIndex,
                framesLeft: Math.Max(frameCount - frameIndex - 1, 0),
                logicalTime: TimeSpan.FromMilliseconds(
                    frameIndex * CombatLogTiming.MillisecondsPerFrame
                ),
                events: events,
                player: null,
                opponent: null,
                cardUpdates: cardUpdates
            )
        );
    }

    return new CombatLogTimeline(
        CombatLogPlaybackPass.Replay,
        frames,
        CombatLogFormatter.BuildRows(frames)
    );
}

static void VerifyViewportWindowing()
{
    var topWindow = CombatLogViewport.CalculateVisibleRowRange(
        totalRowCount: 500,
        scrollY: 0f,
        viewportHeight: 220f
    );
    Assert(topWindow.StartIndex == 0, "Top-of-list rendering should start at the first row.");
    Assert(
        topWindow.EndIndex > topWindow.StartIndex && topWindow.EndIndex < 500,
        "Windowed rendering should only draw a subset of the full row list."
    );

    var middleWindow = CombatLogViewport.CalculateVisibleRowRange(
        totalRowCount: 500,
        scrollY: 2400f,
        viewportHeight: 220f
    );
    Assert(
        middleWindow.StartIndex > 0,
        "Scrolled rendering should skip rows before the visible viewport."
    );
    Assert(
        middleWindow.EndIndex > middleWindow.StartIndex && middleWindow.EndIndex < 500,
        "Scrolled rendering should still cap the draw range to a small window."
    );

    var overscrolledWindow = CombatLogViewport.CalculateVisibleRowRange(
        totalRowCount: 12,
        scrollY: 10000f,
        viewportHeight: 220f
    );
    Assert(
        overscrolledWindow.StartIndex < 12 && overscrolledWindow.EndIndex == 12,
        "Windowed rendering should clamp overscrolled positions back to the last available rows."
    );

    var collapsedGroup = new CombatLogFrameGroupViewModel(
        frameIndex: 0,
        logicalTime: TimeSpan.Zero,
        summaryText: "summary",
        visualState: CombatLogRowVisualState.Current,
        isExpanded: false,
        visibleRowCount: 0,
        totalRowCount: 2,
        rows: Array.Empty<CombatLogDisplayRowViewModel>()
    );
    var expandedGroup = new CombatLogFrameGroupViewModel(
        frameIndex: 1,
        logicalTime: TimeSpan.FromMilliseconds(50),
        summaryText: "summary",
        visualState: CombatLogRowVisualState.FutureDimmed,
        isExpanded: true,
        visibleRowCount: 1,
        totalRowCount: 1,
        rows: new[]
        {
            new CombatLogDisplayRowViewModel(
                "primary",
                "secondary",
                false,
                CombatLogRowVisualState.FutureDimmed
            )
        }
    );

    var collapsedHeight = CombatLogViewport.EstimateGroupHeight(collapsedGroup);
    var expandedHeight = CombatLogViewport.EstimateGroupHeight(expandedGroup);
    Assert(
        collapsedHeight > 38f,
        "Collapsed groups should account for both header and summary height."
    );
    Assert(
        expandedHeight > collapsedHeight,
        "Expanded groups with secondary text should estimate more height than collapsed groups."
    );
}

static void VerifySourceWiring()
{
    var pluginSource = ReadSource("Plugin.cs");
    var debugBranch = ReadBraceBlock(pluginSource, "if (BppBuild.IsDebug)");
    Assert(
        pluginSource.Contains("AddComponent<CombatLogController>()", StringComparison.Ordinal)
            && pluginSource.Contains("AddComponent<CombatLogOverlay>()", StringComparison.Ordinal),
        "Plugin should attach both combat log components."
    );
    Assert(
        !debugBranch.Contains("CombatLogController", StringComparison.Ordinal)
            && !debugBranch.Contains("CombatLogOverlay", StringComparison.Ordinal),
        "DebugPanel debug-only wiring should no longer own combat log components."
    );

    var debugPanelSource = ReadSource("Game/DebugPanel/DebugPanel.cs");
    Assert(
        !debugPanelSource.Contains("CombatLog", StringComparison.Ordinal),
        "Debug panel should not reference combat log code or copy."
    );

    var overlaySource = ReadSource("Game/CombatLog/CombatLogOverlay.cs");
    Assert(
        overlaySource.Contains("Toggle.CombatLogPanel", StringComparison.Ordinal)
            && overlaySource.Contains("DrawStandalone", StringComparison.Ordinal),
        "Combat log overlay should own the toggle and standalone drawing path."
    );

    var debugPanelStateSource = ReadSource("Game/DebugPanel/DebugPanelState.cs");
    Assert(
        !debugPanelStateSource.Contains("CombatLog", StringComparison.Ordinal),
        "Combat log should not be implemented as another DebugPanelSection."
    );

    var controllerSource = ReadSource("Game/CombatLog/CombatLogController.cs");
    Assert(
        controllerSource.Contains("Events.CombatSimReceived.AddListener", StringComparison.Ordinal),
        "Combat log controller should subscribe to CombatSimReceived."
    );

    var runtimeSource = ReadSource("Game/CombatLog/CombatLogRuntime.cs");
    Assert(
        runtimeSource.Contains("Data.Run?.Player?.Skills", StringComparison.Ordinal)
            && runtimeSource.Contains("Data.Run?.Opponent?.Skills", StringComparison.Ordinal),
        "Combat log runtime should include player and opponent skills in live display-name resolution."
    );

    var panelSource = ReadSource("Game/CombatLog/CombatLogPanel.cs");
    Assert(
        panelSource.Contains("DrawFilterButton", StringComparison.Ordinal)
            && panelSource.Contains("ToggleCombatants", StringComparison.Ordinal)
            && panelSource.Contains("ToggleCards", StringComparison.Ordinal),
        "Combat log panel should expose separate combatant and card filter toggles."
    );

    Assert(
        panelSource.Contains("CombatLogViewport.CalculateVisibleRowRange", StringComparison.Ordinal),
        "Combat log panel should use windowed rendering helpers for large timelines."
    );
}

static CombatLogCardDisplayInfo? ResolveCardDisplayInfo(string instanceId)
{
    return instanceId switch
    {
        "card-a" => new CombatLogCardDisplayInfo("card-a", "tpl-a", "炽焰匕首"),
        "card-b" => new CombatLogCardDisplayInfo("card-b", "tpl-b", "灰烬回响"),
        _ => null,
    };
}

static string ReadSource(string relativePath)
{
    return File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath))
    );
}

static string ReadBraceBlock(string source, string anchor)
{
    var anchorIndex = source.IndexOf(anchor, StringComparison.Ordinal);
    Assert(anchorIndex >= 0, $"Could not find source anchor: {anchor}");

    var openBraceIndex = source.IndexOf('{', anchorIndex);
    Assert(openBraceIndex >= 0, $"Could not find block start for anchor: {anchor}");

    var depth = 0;
    for (var index = openBraceIndex; index < source.Length; index++)
    {
        if (source[index] == '{')
            depth++;
        else if (source[index] == '}')
            depth--;

        if (depth == 0)
            return source.Substring(openBraceIndex, index - openBraceIndex + 1);
    }

    throw new InvalidOperationException($"Could not find block end for anchor: {anchor}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
