using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactMetricFormatterTests
{
    private static readonly CombatImpactEntity Target = new(
        "target",
        "Target",
        "Item",
        null,
        null,
        ECombatantId.Player,
        0
    );

    [Fact]
    public void Group_formats_count_and_duration_without_inventing_precision()
    {
        var group = new CombatImpactGroup(
            CombatImpactKind.Freeze,
            "FreezeAmount",
            3,
            8800,
            CombatImpactValueUnit.Milliseconds,
            false,
            Array.Empty<CombatImpactTarget>()
        );

        Assert.Equal("3× · 8.8s", CombatImpactMetricFormatter.Group(group, chinese: false));
        Assert.Equal("3 次 · 8.8s", CombatImpactMetricFormatter.Group(group, chinese: true));
    }

    [Fact]
    public void Partial_attribute_change_keeps_sign_and_marks_approximation()
    {
        Assert.Equal(
            "≈+20%",
            CombatImpactMetricFormatter.Value(
                20,
                CombatImpactValueUnit.PercentagePoints,
                partial: true,
                showSign: true
            )
        );
    }

    [Fact]
    public void Target_without_quantified_value_still_reports_count()
    {
        var target = new CombatImpactTarget(Target, 4, null, CombatImpactValueUnit.Amount, true);

        Assert.Equal("×4", CombatImpactMetricFormatter.Target(CombatImpactKind.Destroy, target));
    }
}
