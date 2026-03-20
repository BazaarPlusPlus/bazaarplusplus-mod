using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Game.CombatLog;

var runtime = new CombatLogRuntime(ResolveCardDisplayInfo);
var timeline = BuildTimeline(runtime);
VerifyTimeline(timeline);
VerifyPanelState(timeline);
VerifyPanelFilters(timeline);
VerifySourceWiring();

Console.WriteLine("CombatLogRuntime checks passed.");

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
                    Target = new EffectTargetPlayer { Target = ECombatantId.Opponent },
                },
                new CombatSimEventMonsterGoldReceived { HealthAmount = 3 },
                new CombatSimEventEffectTriggered
                {
                    ExecutionContextId = "ctx-2",
                    EffectId = "poison",
                    Source = new InstanceId("card-a"),
                    Targets = new List<IEffectTarget> { new EffectTargetPlayer { Target = ECombatantId.Opponent } },
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
                        Attributes = new Dictionary<ECardAttributeType, CombatSimCardAttributeUpdate>
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
    Assert(timeline.Frames.Count == 2, "Runtime should map each sim frame into one combat log frame.");
    Assert(
        timeline.Frames[1].LogicalTime == TimeSpan.FromMilliseconds(CombatLogTiming.MillisecondsPerFrame),
        "Logical time should use frameIndex * 50ms."
    );
    Assert(timeline.Rows.Count >= 6, "Formatter should emit deterministic rows for supported events and updates.");
    Assert(
        timeline.Rows[0].Category == CombatLogRowCategory.Event
            && timeline.Rows[0].Text.Contains("burn", StringComparison.Ordinal),
        "EffectExecuted should produce the first display row for the frame."
    );
    Assert(
        timeline.Rows.Any(row => row.Category == CombatLogRowCategory.Health && row.Text.Contains("-6", StringComparison.Ordinal)),
        "Player health adjustments should become display rows."
    );
    Assert(
        timeline.Rows.Any(row => row.Category == CombatLogRowCategory.Attribute && row.Text.Contains("Poison", StringComparison.Ordinal)),
        "Player attribute updates should become display rows."
    );
    Assert(
        timeline.Rows.Any(row => row.Category == CombatLogRowCategory.CardAttribute),
        "Card attribute updates should become display rows."
    );
    Assert(
        timeline.Rows.Any(
            row =>
                row.Category == CombatLogRowCategory.CardAttribute
                && row.Text.Contains("Fiery Dagger", StringComparison.Ordinal)
                && !row.Text.Contains("Card card-a", StringComparison.Ordinal)
        ),
        "Card attribute rows should prefer resolved display names over raw instance IDs."
    );
    Assert(
        timeline.Rows.Any(
            row =>
                row.Category == CombatLogRowCategory.CardAttribute
                && row.SecondaryText != null
                && row.SecondaryText.Contains("card-a", StringComparison.Ordinal)
        ),
        "Card attribute rows should keep the raw instance ID as secondary debug context."
    );
    Assert(
        timeline.Rows.Any(row => row.Category == CombatLogRowCategory.Death && row.Text.Contains("Opponent died", StringComparison.Ordinal)),
        "CombatantDied should become a death row."
    );
    Assert(
        timeline.Rows.Any(
            row =>
                row.Text.Contains("Triggered poison", StringComparison.Ordinal)
                && row.Text.Contains("Fiery Dagger", StringComparison.Ordinal)
        ),
        "EffectTriggered should become a display row that uses the resolved source card name."
    );
    Assert(
        timeline.Rows.Any(row => row.Text.Contains("Quest updated", StringComparison.Ordinal)),
        "Quest updates should become display rows."
    );
    Assert(
        timeline.Rows.Any(row => row.Text.Contains("Transform Fiery Dagger", StringComparison.Ordinal)),
        "Transform events should become display rows."
    );
    Assert(
        timeline.Rows.Any(row => row.Text.Contains("Sandstorm started", StringComparison.Ordinal)),
        "Sandstorm events should become display rows."
    );
    Assert(
        timeline.Rows.Any(row => row.Category == CombatLogRowCategory.System),
        "System events should be categorized distinctly for filtering."
    );
}

static void VerifyPanelState(CombatLogTimeline timeline)
{
    var firstPlayState = new CombatLogPanelState();
    firstPlayState.Refresh(1);
    var firstPlayRows = firstPlayState.BuildVisibleRows(timeline);
    Assert(
        firstPlayRows.Any(row => row.Row.FrameIndex == 0 && row.VisualState == CombatLogRowVisualState.Current),
        "Processed frame count should highlight the last processed frame, not the next frame."
    );
    Assert(
        firstPlayRows.All(row => row.Row.FrameIndex <= 0),
        "First play should hide future rows."
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
        replayRows.Any(row => row.Row.FrameIndex == 1 && row.VisualState == CombatLogRowVisualState.FutureDimmed),
        "Replay should keep future rows visible and dimmed."
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
        withoutCombatantRows.All(
            row =>
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
        withoutCombatantOrCardRows.All(row => row.Row.Category != CombatLogRowCategory.CardAttribute),
        "Card filter should hide card attribute rows independently."
    );

    state.ToggleSystem();
    var withoutCombatantCardOrSystemRows = state.BuildVisibleRows(timeline);
    Assert(
        withoutCombatantCardOrSystemRows.All(row => row.Row.Category != CombatLogRowCategory.System),
        "System filter should hide system rows."
    );
}

static void VerifySourceWiring()
{
    var pluginSource = ReadSource("Plugin.cs");
    Assert(
        pluginSource.Contains("AddComponent<CombatLogController>()", StringComparison.Ordinal),
        "Plugin should attach the combat log controller."
    );

    var debugPanelSource = ReadSource("Game/DebugPanel/DebugPanel.cs");
    Assert(
        !debugPanelSource.Contains("CombatLogPanel", StringComparison.Ordinal),
        "Debug panel should not own the combat log overlay."
    );

    Assert(
        pluginSource.Contains("AddComponent<CombatLogOverlay>()", StringComparison.Ordinal),
        "Plugin should attach the standalone combat log overlay in debug builds."
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
}

static CombatLogCardDisplayInfo? ResolveCardDisplayInfo(string instanceId)
{
    return instanceId switch
    {
        "card-a" => new CombatLogCardDisplayInfo("card-a", "tpl-a", "Fiery Dagger"),
        "card-b" => new CombatLogCardDisplayInfo("card-b", "tpl-b", "Ashen Echo"),
        _ => null,
    };
}

static string ReadSource(string relativePath)
{
    return File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath))
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
