using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactMetricFormatterTests
{
    private const string CritIcon = "<sprite name=Crit>";

    private static readonly CombatImpactEntity Target = new("target", "Target", "Item", null, 0);

    [Theory]
    [InlineData(
        950,
        (int)CombatImpactValueUnit.Milliseconds,
        (int)CombatImpactCoverage.Exact,
        false,
        false,
        "0.95s"
    )]
    [InlineData(
        1000,
        (int)CombatImpactValueUnit.Milliseconds,
        (int)CombatImpactCoverage.Exact,
        false,
        false,
        "1s"
    )]
    [InlineData(
        1100,
        (int)CombatImpactValueUnit.Milliseconds,
        (int)CombatImpactCoverage.Exact,
        false,
        false,
        "1.10s"
    )]
    [InlineData(
        950,
        (int)CombatImpactValueUnit.Milliseconds,
        (int)CombatImpactCoverage.LowerBound,
        false,
        false,
        "0.95s*"
    )]
    [InlineData(
        950,
        (int)CombatImpactValueUnit.Milliseconds,
        (int)CombatImpactCoverage.Partial,
        false,
        true,
        "0.95s*"
    )]
    [InlineData(
        20,
        (int)CombatImpactValueUnit.PercentagePoints,
        (int)CombatImpactCoverage.Partial,
        true,
        false,
        "+20%"
    )]
    [InlineData(
        -10,
        (int)CombatImpactValueUnit.Amount,
        (int)CombatImpactCoverage.LowerBound,
        true,
        false,
        "-10"
    )]
    public void Values_use_product_sign_duration_and_precision_rules(
        int value,
        int unitValue,
        int coverageValue,
        bool showSign,
        bool chinese,
        string expected
    )
    {
        var formatted = CombatImpactMetricFormatter.Value(
            value,
            (CombatImpactValueUnit)unitValue,
            (CombatImpactCoverage)coverageValue,
            showSign,
            chinese
        );

        Assert.Equal(expected, formatted);
        Assert.DoesNotContain("≥", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("≈", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("estimated", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("估算", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Critical_counts_use_the_native_icon_inside_the_event_count()
    {
        var appliedRegen = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "RegenApplyAmount",
            2,
            12,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.AttributeChange,
                "RegenApplyAmount",
                12,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            []
        )
        {
            CriticalCount = 1,
            CriticalObservedValue = 8,
        };
        var damage = Group(
            CombatImpactKind.DirectDamage,
            count: 8,
            observedValue: 640,
            CombatImpactValueUnit.Amount
        ) with
        {
            CriticalCount = 4,
            CriticalObservedValue = 640,
        };

        Assert.Equal(
            "×2 (1 <sprite name=Crit>) · +12 total",
            CombatImpactMetricFormatter.Group(appliedRegen, chinese: false, CritIcon)
        );
        Assert.Equal(
            "×2（1 <sprite name=Crit>） · 总计 +12",
            CombatImpactMetricFormatter.Group(appliedRegen, chinese: true, CritIcon)
        );
        Assert.Equal(
            "×8 (4 <sprite name=Crit>) · 640",
            CombatImpactMetricFormatter.Group(damage, chinese: false, CritIcon)
        );
        Assert.Equal(
            "×8（4 <sprite name=Crit>） · 640",
            CombatImpactMetricFormatter.Group(damage, chinese: true, CritIcon)
        );
    }

    [Fact]
    public void Target_breakdown_distinguishes_counts_from_authoritative_totals()
    {
        var unquantified = new CombatImpactTarget(
            Target,
            4,
            null,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.None
        );
        var unquantifiedGroup = Group(
            CombatImpactKind.Destroy,
            count: 4,
            observedValue: null,
            CombatImpactValueUnit.Amount
        );
        var observed = new CombatImpactTarget(
            Target,
            10,
            69,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact
        );
        var divergentGroup = new CombatImpactGroup(
            CombatImpactKind.Burn,
            "BurnApplyAmount",
            10,
            69,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Partial,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Burn,
                "BurnApplyAmount",
                76,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            [observed]
        );

        Assert.Equal(
            "×4",
            CombatImpactMetricFormatter.Target(unquantifiedGroup, unquantified, chinese: false)
        );
        Assert.Equal(
            "×10 · 76 total",
            CombatImpactMetricFormatter.Group(divergentGroup, chinese: false)
        );
        Assert.Equal(
            "×10 · 69 recorded",
            CombatImpactMetricFormatter.Target(divergentGroup, observed, chinese: false)
        );
    }

    [Fact]
    public void Authoritative_bases_do_not_retype_observed_duration_or_invent_counts()
    {
        var applications = new CombatImpactAuthoritativeMetric(
            CombatImpactKind.Haste,
            "HasteAmount",
            10,
            CombatImpactValueUnit.Applications,
            CombatImpactAuthoritativeBasis.ApplicationCount
        );
        var matching = new CombatImpactGroup(
            CombatImpactKind.Haste,
            "HasteAmount",
            10,
            9500,
            CombatImpactValueUnit.Milliseconds,
            CombatImpactCoverage.LowerBound,
            applications,
            0,
            []
        );
        var divergent = matching with { Count = 7 };
        var authoritativeOnly = new CombatImpactGroup(
            CombatImpactKind.Burn,
            "BurnApplyAmount",
            0,
            null,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.None,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Burn,
                "BurnApplyAmount",
                76,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            []
        );

        Assert.Equal("×10 · 9.50s*", CombatImpactMetricFormatter.Group(matching, chinese: false));
        Assert.Equal(
            "×7 · 9.50s* · 10 applications",
            CombatImpactMetricFormatter.Group(divergent, chinese: false)
        );
        Assert.Equal(
            "76 total",
            CombatImpactMetricFormatter.Group(authoritativeOnly, chinese: false)
        );
    }

    [Fact]
    public void Disclosures_are_conditional_and_localized_for_both_perspectives()
    {
        var reconstructed = new CombatImpactGroup(
            CombatImpactKind.Haste,
            "HasteAmount",
            3,
            2950,
            CombatImpactValueUnit.Milliseconds,
            CombatImpactCoverage.LowerBound,
            null,
            2,
            []
        );
        var exact = Group(
            CombatImpactKind.Burn,
            count: 1,
            observedValue: 4,
            CombatImpactValueUnit.Amount
        );
        var received = new CombatImpactReceived(
            Target,
            1,
            [
                new CombatImpactIncomingGroup(
                    CombatImpactKind.Haste,
                    "HasteAmount",
                    1,
                    950,
                    CombatImpactValueUnit.Milliseconds,
                    CombatImpactCoverage.LowerBound,
                    []
                ),
            ]
        );

        Assert.Equal(
            [
                "* Partial breakdown: 2 effects had no target data.",
                "* Some details and durations are reconstructed from combat events and may not match the game totals exactly.",
            ],
            CombatImpactMetricFormatter.CausedDisclosures(
                new CombatImpactSource(Target, 1, 3, [reconstructed]),
                chinese: false
            )
        );
        Assert.Equal(
            [
                "* 明细不完整：2 个效果缺少目标数据。",
                "* 部分明细及持续时间由战斗事件推算，可能与游戏总计不完全一致。",
            ],
            CombatImpactMetricFormatter.CausedDisclosures(
                new CombatImpactSource(Target, 1, 3, [reconstructed]),
                chinese: true
            )
        );
        Assert.Empty(
            CombatImpactMetricFormatter.CausedDisclosures(
                new CombatImpactSource(Target, 1, 1, [exact]),
                chinese: false
            )
        );
        Assert.Equal(
            ["* 部分明细及持续时间由战斗事件推算，可能与游戏总计不完全一致。"],
            CombatImpactMetricFormatter.ReceivedDisclosures(received, chinese: true)
        );
    }

    [Fact]
    public void Authoritative_basis_rejects_incompatible_metric_units()
    {
        Assert.Throws<ArgumentException>(() =>
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Burn,
                "BurnApplyAmount",
                10,
                CombatImpactValueUnit.Applications,
                CombatImpactAuthoritativeBasis.TotalAmount
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Haste,
                "HasteAmount",
                10,
                CombatImpactValueUnit.Milliseconds,
                CombatImpactAuthoritativeBasis.ApplicationCount
            )
        );
    }

    [Fact]
    public void Incoming_group_uses_the_same_count_and_value_notation_in_both_languages()
    {
        var group = new CombatImpactIncomingGroup(
            CombatImpactKind.Burn,
            "BurnApplyAmount",
            10,
            69,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            []
        );

        Assert.Equal("×10 · 69", CombatImpactMetricFormatter.IncomingGroup(group, chinese: false));
        Assert.Equal("×10 · 69", CombatImpactMetricFormatter.IncomingGroup(group, chinese: true));
    }

    private static CombatImpactGroup Group(
        CombatImpactKind kind,
        int count,
        int? observedValue,
        CombatImpactValueUnit unit
    ) =>
        new(
            kind,
            CombatImpactAggregator.NativeKey(kind),
            count,
            observedValue,
            unit,
            observedValue.HasValue ? CombatImpactCoverage.Exact : CombatImpactCoverage.None,
            null,
            0,
            []
        );
}
