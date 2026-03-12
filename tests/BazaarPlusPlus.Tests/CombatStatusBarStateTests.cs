using System;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class CombatStatusBarStateTests
{
    [Fact]
    public void Begin_playback_resets_processed_frames_and_marks_playback_active()
    {
        CombatStatusBar.ResetStateForTests();
        CombatStatusBar.SetCombatFrameTotal(12);
        CombatStatusBar.AdvanceCombatFrame();

        CombatStatusBar.BeginCombatPlayback();

        Assert.True(CombatStatusBar.IsCombatPlaybackActive);
        Assert.True(CombatStatusBar.HasUnlockedStandbyDisplay);
        Assert.Equal(0, CombatStatusBar.ProcessedCombatFrames);
        Assert.Equal(12, CombatStatusBar.TotalCombatFrames);
    }

    [Fact]
    public void Standby_display_stays_hidden_until_first_combat_has_started()
    {
        CombatStatusBar.ResetStateForTests();

        Assert.False(CombatStatusBar.ShouldRenderForState(overlayVisible: true, enabled: true));

        CombatStatusBar.BeginCombatPlayback();
        CombatStatusBar.EndCombatPlayback();

        Assert.True(CombatStatusBar.ShouldRenderForState(overlayVisible: true, enabled: true));
        Assert.False(CombatStatusBar.ShouldRenderForState(overlayVisible: false, enabled: true));
        Assert.False(CombatStatusBar.ShouldRenderForState(overlayVisible: true, enabled: false));
    }

    [Fact]
    public void Set_frame_total_clamps_to_zero_and_resets_processed_frames()
    {
        CombatStatusBar.ResetStateForTests();
        CombatStatusBar.BeginCombatPlayback();
        CombatStatusBar.AdvanceCombatFrame();

        CombatStatusBar.SetCombatFrameTotal(-4);

        Assert.Equal(0, CombatStatusBar.TotalCombatFrames);
        Assert.Equal(0, CombatStatusBar.ProcessedCombatFrames);
    }

    [Fact]
    public void Advance_frame_clamps_at_total_frame_count()
    {
        CombatStatusBar.ResetStateForTests();
        CombatStatusBar.BeginCombatPlayback();
        CombatStatusBar.SetCombatFrameTotal(2);

        CombatStatusBar.AdvanceCombatFrame();
        CombatStatusBar.AdvanceCombatFrame();
        CombatStatusBar.AdvanceCombatFrame();

        Assert.Equal(2, CombatStatusBar.ProcessedCombatFrames);
    }

    [Fact]
    public void Step_combat_speed_moves_across_discrete_steps_and_clamps_at_boundaries()
    {
        CombatStatusBar.ResetStateForTests();
        CombatStatusBar.SetCombatSpeed(1f);

        Assert.Equal(0.5f, CombatStatusBar.StepCombatSpeed(-1));
        Assert.Equal(0.25f, CombatStatusBar.StepCombatSpeed(-1));
        Assert.Equal(0.25f, CombatStatusBar.StepCombatSpeed(-1));

        CombatStatusBar.SetCombatSpeed(3f);
        Assert.Equal(5f, CombatStatusBar.StepCombatSpeed(1));
        Assert.Equal(5f, CombatStatusBar.StepCombatSpeed(1));
    }

    [Fact]
    public void Logical_elapsed_time_is_derived_from_processed_frames()
    {
        CombatStatusBar.ResetStateForTests();
        CombatStatusBar.BeginCombatPlayback();
        CombatStatusBar.SetCombatFrameTotal(10);
        CombatStatusBar.AdvanceCombatFrame();
        CombatStatusBar.AdvanceCombatFrame();
        CombatStatusBar.AdvanceCombatFrame();

        Assert.Equal(TimeSpan.FromMilliseconds(150), CombatStatusBar.GetCombatLogicalElapsed());
    }

    [Fact]
    public void Standby_display_shows_placeholder_time_and_standby_frame_text()
    {
        CombatStatusBar.ResetStateForTests();

        Assert.Equal("-:--:--", CombatStatusBar.GetDisplayedTimeText());
        Assert.Equal("Standby", CombatStatusBar.GetDisplayedFrameText());
    }

    [Fact]
    public void Active_display_shows_processed_frame_count_only()
    {
        CombatStatusBar.ResetStateForTests();
        CombatStatusBar.BeginCombatPlayback();
        CombatStatusBar.SetCombatFrameTotal(99);
        CombatStatusBar.AdvanceCombatFrame();
        CombatStatusBar.AdvanceCombatFrame();

        Assert.Equal("0:00:10", CombatStatusBar.GetDisplayedTimeText());
        Assert.Equal("2", CombatStatusBar.GetDisplayedFrameText());
    }

    [Fact]
    public void Combat_speed_label_is_always_rendered_with_two_decimal_places()
    {
        CombatStatusBar.ResetStateForTests();

        CombatStatusBar.SetCombatSpeed(0.5f);
        Assert.Equal("0.50x", CombatStatusBar.FormatCombatSpeedLabel());

        CombatStatusBar.SetCombatSpeed(1f);
        Assert.Equal("1.00x", CombatStatusBar.FormatCombatSpeedLabel());

        CombatStatusBar.SetCombatSpeed(5f);
        Assert.Equal("5.00x", CombatStatusBar.FormatCombatSpeedLabel());
    }

    [Fact]
    public void Combat_speed_rejects_values_outside_supported_steps()
    {
        CombatStatusBar.ResetStateForTests();
        CombatStatusBar.SetCombatSpeed(1f);

        var actual = CombatStatusBar.SetCombatSpeed(4.2f);

        Assert.Equal(1f, actual);
        Assert.Equal("1.00x", CombatStatusBar.FormatCombatSpeedLabel());
    }

    [Theory]
    [InlineData(0.25f, 0.25f)]
    [InlineData(1f, 1f)]
    [InlineData(5f, 5f)]
    [InlineData(4.2f, 1f)]
    [InlineData(8f, 1f)]
    public void Configured_default_speed_is_normalized_to_supported_steps_or_falls_back_to_one(float configured, float expected)
    {
        var actual = CombatStatusBar.NormalizeConfiguredDefaultSpeed(configured);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0f, true, 0.10f, 0.5f)]
    [InlineData(1f, false, 0.10f, 0.5f)]
    [InlineData(0.8f, true, 0.10f, 1f)]
    [InlineData(0.2f, false, 0.10f, 0f)]
    public void Visual_blend_progresses_toward_target_state(float current, bool active, float deltaTime, float expected)
    {
        var next = CombatStatusBar.AdvanceVisualBlend(current, active, deltaTime);

        Assert.Equal(expected, next, 3);
    }
}
