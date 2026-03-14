using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class CombatStatusBarStateTests : IDisposable
{
    public CombatStatusBarStateTests()
    {
        CombatStatusBar.ResetStateForTests();
    }

    public void Dispose()
    {
        CombatStatusBar.ResetStateForTests();
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsStandbyPlaceholderOutsideCombat()
    {
        Assert.Equal("LastCombat", CombatStatusBar.GetDisplayedTimeLabel());

        var result = CombatStatusBar.GetDisplayedTimeText();

        Assert.Equal("-:--:--", result);
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsLogicalCombatTimeDuringCombat()
    {
        CombatStatusBar.BeginCombatPlayback();
        for (var i = 0; i < 25; i++)
            CombatStatusBar.AdvanceCombatFrame();

        Assert.Equal("Time", CombatStatusBar.GetDisplayedTimeLabel());

        var result = CombatStatusBar.GetDisplayedTimeText();

        Assert.Equal("0:01:25", result);
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsLastCombatTimeAfterCombatEnds()
    {
        CombatStatusBar.BeginCombatPlayback();
        for (var i = 0; i < 25; i++)
            CombatStatusBar.AdvanceCombatFrame();

        CombatStatusBar.EndCombatPlayback();

        Assert.Equal("LastCombat", CombatStatusBar.GetDisplayedTimeLabel());
        Assert.Equal("0:01:25", CombatStatusBar.GetDisplayedTimeText());
    }

    [Fact]
    public void GetDisplayedFrameText_ReflectsStandbyAndProcessedFrames()
    {
        Assert.Equal("Standby", CombatStatusBar.GetDisplayedFrameText());

        CombatStatusBar.BeginCombatPlayback();
        CombatStatusBar.AdvanceCombatFrame();
        CombatStatusBar.AdvanceCombatFrame();

        Assert.Equal("2", CombatStatusBar.GetDisplayedFrameText());
    }

    [Fact]
    public void FormatCombatSpeedLabel_UsesTwoDecimalPlaces()
    {
        CombatStatusBar.SetCombatSpeed(1.5f);

        var result = CombatStatusBar.FormatCombatSpeedLabel();

        Assert.Equal("1.50x", result);
    }

    [Fact]
    public void NormalizeConfiguredDefaultSpeed_AcceptsNewSupportedStep()
    {
        var result = CombatStatusBar.NormalizeConfiguredDefaultSpeed(1.5f);

        Assert.Equal(1.5f, result);
    }

    [Fact]
    public void NormalizeConfiguredDefaultSpeed_RejectsRemovedStep()
    {
        var result = CombatStatusBar.NormalizeConfiguredDefaultSpeed(4f);

        Assert.Equal(1f, result);
    }

    [Theory]
    [InlineData(0.0f, true, 0.10f, 0.50f)]
    [InlineData(0.90f, true, 0.10f, 1.00f)]
    [InlineData(0.10f, false, 0.10f, 0.00f)]
    public void AdvanceVisualBlend_ApproachesTargetWithoutOvershooting(
        float current,
        bool active,
        float deltaTime,
        float expected)
    {
        var result = CombatStatusBar.AdvanceVisualBlend(current, active, deltaTime);

        Assert.Equal(expected, result, precision: 3);
    }
}
