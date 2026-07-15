#nullable enable
using BazaarPlusPlus.GameInterop.SteamTimeline;
using Xunit;

namespace BazaarPlusPlus.Game.SteamTimeline;

public sealed class SteamTimelineSessionCoreTests
{
    [Fact]
    public void Run_phase_starts_once_and_assigns_an_early_run_id_in_order()
    {
        var core = new SteamTimelineSessionCore();

        Assert.Empty(core.ObserveRunId("bpp-run-one"));
        var commands = core.StartRun();

        Assert.Equal(
            [SteamTimelineCommandKind.StartPhase, SteamTimelineCommandKind.SetPhaseId],
            commands.Select(command => command.Kind)
        );
        Assert.Equal("bpp-run-one", commands[1].PhaseId);
        Assert.Empty(core.StartRun());
        Assert.Empty(core.ObserveRunId("bpp-run-one"));
    }

    [Fact]
    public void Late_run_id_is_assigned_once_to_the_active_phase()
    {
        var core = new SteamTimelineSessionCore();
        core.StartRun();

        var command = Assert.Single(core.ObserveRunId("bpp-late-id"));

        Assert.Equal(SteamTimelineCommandKind.SetPhaseId, command.Kind);
        Assert.Equal("bpp-late-id", command.PhaseId);
        Assert.Empty(core.ObserveRunId(" bpp-late-id "));
    }

    [Theory]
    [InlineData((int)SteamTimelineBattleResult.Victory)]
    [InlineData((int)SteamTimelineBattleResult.Defeat)]
    public void Visible_battle_range_closes_with_the_observed_result(int resultValue)
    {
        var result = (SteamTimelineBattleResult)resultValue;
        var core = ActiveCore();
        var battle = Battle("battle-1");

        Assert.Empty(core.PrepareBattle(battle));
        core.ObserveBattleResult(result);
        var start = Assert.Single(core.StartBattlePlayback());
        var complete = Assert.Single(core.EndBattlePlayback());

        Assert.Equal(SteamTimelineCommandKind.StartBattleRange, start.Kind);
        Assert.Same(battle, start.Battle);
        Assert.Equal(SteamTimelineCommandKind.CompleteBattleRange, complete.Kind);
        Assert.Equal(result, complete.BattleResult);
        Assert.False(core.HasActiveBattle);
    }

    [Fact]
    public void Battle_without_a_result_is_closed_as_interrupted()
    {
        var core = ActiveCore();
        core.PrepareBattle(Battle("battle-1"));
        core.StartBattlePlayback();

        var command = Assert.Single(core.EndBattlePlayback());

        Assert.Equal(SteamTimelineCommandKind.InterruptBattleRange, command.Kind);
    }

    [Fact]
    public void Replacing_an_active_battle_interrupts_the_old_range()
    {
        var core = ActiveCore();
        var oldBattle = Battle("battle-1");
        var newBattle = Battle("battle-2");
        core.PrepareBattle(oldBattle);
        core.StartBattlePlayback();

        var command = Assert.Single(core.PrepareBattle(newBattle));

        Assert.Equal(SteamTimelineCommandKind.InterruptBattleRange, command.Kind);
        Assert.Same(oldBattle, command.Battle);
        Assert.Equal(
            SteamTimelineCommandKind.StartBattleRange,
            Assert.Single(core.StartBattlePlayback()).Kind
        );
    }

    [Fact]
    public void Duplicate_battle_identity_is_ignored_before_during_and_after_playback()
    {
        var core = ActiveCore();
        var battle = Battle("battle-1");

        Assert.Empty(core.PrepareBattle(battle));
        Assert.Empty(core.PrepareBattle(Battle("battle-1")));
        Assert.Single(core.StartBattlePlayback());
        Assert.Empty(core.PrepareBattle(Battle("battle-1")));
        core.ObserveBattleResult(SteamTimelineBattleResult.Victory);
        Assert.Single(core.EndBattlePlayback());
        Assert.Empty(core.PrepareBattle(Battle("battle-1")));
        Assert.Empty(core.StartBattlePlayback());
    }

    [Fact]
    public void Ending_a_run_interrupts_an_open_battle_before_the_phase()
    {
        var core = ActiveCore();
        core.PrepareBattle(Battle("battle-1"));
        core.StartBattlePlayback();

        var commands = core.EndRun(SteamTimelineRunExit.Interrupted);

        Assert.Equal(
            [SteamTimelineCommandKind.InterruptBattleRange, SteamTimelineCommandKind.EndPhase],
            commands.Select(command => command.Kind)
        );
        Assert.Equal(SteamTimelineRunExit.Interrupted, commands[1].RunExit);
        Assert.False(core.IsPhaseActive);
    }

    [Fact]
    public void Level_markers_require_an_increase_and_are_deduplicated_per_run()
    {
        var core = ActiveCore();

        Assert.Empty(core.ObserveLevelIncrease(5, 5, 3, "Vanessa"));
        var command = Assert.Single(core.ObserveLevelIncrease(5, 6, 3, "Vanessa"));
        Assert.Empty(core.ObserveLevelIncrease(5, 6, 3, "Vanessa"));
        Assert.Empty(core.ObserveLevelIncrease(6, 5, 3, "Vanessa"));

        Assert.Equal(SteamTimelineCommandKind.AddLevelMarker, command.Kind);
        Assert.Equal(6, command.Level);
        Assert.Equal(3, command.Day);
        Assert.Equal("Vanessa", command.Hero);

        core.EndRun(SteamTimelineRunExit.Completed);
        core.StartRun();
        Assert.Single(core.ObserveLevelIncrease(5, 6, 1, "Vanessa"));
    }

    private static SteamTimelineSessionCore ActiveCore()
    {
        var core = new SteamTimelineSessionCore();
        core.StartRun();
        return core;
    }

    private static SteamTimelineBattleContext Battle(string id) =>
        new(id, day: 7, playerHero: "Vanessa", opponentHero: "Pygmalien");
}
