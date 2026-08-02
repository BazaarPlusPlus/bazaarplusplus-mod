using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactMetricFormatterTests
{
    private const string CritIcon = "<sprite name=Crit>";

    private static readonly CombatImpactEntity Target = new("target", "Target", "Item", null, 0);

    [Fact]
    public void Group_formats_fractional_seconds_to_two_decimal_places()
    {
        var group = new CombatImpactGroup(
            CombatImpactKind.Freeze,
            "FreezeAmount",
            3,
            8800,
            CombatImpactValueUnit.Milliseconds,
            CombatImpactCoverage.Exact,
            null,
            0,
            Array.Empty<CombatImpactTarget>()
        );

        Assert.Equal("×3 · 8.80s", CombatImpactMetricFormatter.Group(group, chinese: false));
        Assert.Equal("×3 · 8.80s", CombatImpactMetricFormatter.Group(group, chinese: true));
    }

    [Fact]
    public void Partial_attribute_change_keeps_sign_without_mathematical_approximation_symbol()
    {
        Assert.Equal(
            "+20%",
            CombatImpactMetricFormatter.Value(
                20,
                CombatImpactValueUnit.PercentagePoints,
                CombatImpactCoverage.Partial,
                showSign: true
            )
        );
    }

    [Fact]
    public void Lower_bound_decrease_preserves_negative_direction()
    {
        var group = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "HealthMaxDecrease",
            1,
            -10,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.LowerBound,
            null,
            0,
            []
        )
        {
            Surface = CombatImpactEventSurface.CardAttribute,
        };

        Assert.Equal("-10", CombatImpactMetricFormatter.Group(group, chinese: false));
    }

    [Fact]
    public void Applied_regen_nests_critical_count_in_its_event_count()
    {
        var group = new CombatImpactGroup(
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

        Assert.Equal(
            "×2 (1 <sprite name=Crit>) · +12 total",
            CombatImpactMetricFormatter.Group(group, chinese: false, CritIcon)
        );
        Assert.Equal(
            "×2（1 <sprite name=Crit>） · 总计 +12",
            CombatImpactMetricFormatter.Group(group, chinese: true, CritIcon)
        );
    }

    [Fact]
    public void Target_without_quantified_value_still_reports_count()
    {
        var target = new CombatImpactTarget(
            Target,
            4,
            null,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.None
        );
        var group = Group(
            CombatImpactKind.Destroy,
            count: 4,
            observedValue: null,
            CombatImpactValueUnit.Amount
        );

        Assert.Equal("×4", CombatImpactMetricFormatter.Target(group, target, chinese: false));
    }

    [Fact]
    public void Total_and_observed_target_are_labeled_as_different_bases()
    {
        var authoritative = new CombatImpactAuthoritativeMetric(
            CombatImpactKind.Burn,
            "BurnApplyAmount",
            76,
            CombatImpactValueUnit.Amount,
            CombatImpactAuthoritativeBasis.TotalAmount
        );
        var target = new CombatImpactTarget(
            Target,
            10,
            69,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact
        );
        var group = new CombatImpactGroup(
            CombatImpactKind.Burn,
            "BurnApplyAmount",
            10,
            69,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Partial,
            authoritative,
            0,
            [target]
        );

        Assert.Equal("×10 · 76 total", CombatImpactMetricFormatter.Group(group, chinese: false));
        Assert.Equal(
            "×10 · 69 recorded",
            CombatImpactMetricFormatter.Target(group, target, chinese: false)
        );
    }

    [Fact]
    public void Application_count_stays_separate_from_lower_bound_duration()
    {
        var authoritative = new CombatImpactAuthoritativeMetric(
            CombatImpactKind.Haste,
            "HasteAmount",
            10,
            CombatImpactValueUnit.Applications,
            CombatImpactAuthoritativeBasis.ApplicationCount
        );
        var group = new CombatImpactGroup(
            CombatImpactKind.Haste,
            "HasteAmount",
            10,
            9500,
            CombatImpactValueUnit.Milliseconds,
            CombatImpactCoverage.LowerBound,
            authoritative,
            0,
            Array.Empty<CombatImpactTarget>()
        );

        var formatted = CombatImpactMetricFormatter.Group(group, chinese: false);
        Assert.Equal("×10 · 9.50s*", formatted);
        Assert.DoesNotContain("10ms", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Divergent_application_count_is_labeled_without_retyping_duration()
    {
        var group = new CombatImpactGroup(
            CombatImpactKind.Haste,
            "HasteAmount",
            7,
            9500,
            CombatImpactValueUnit.Milliseconds,
            CombatImpactCoverage.LowerBound,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Haste,
                "HasteAmount",
                10,
                CombatImpactValueUnit.Applications,
                CombatImpactAuthoritativeBasis.ApplicationCount
            ),
            0,
            []
        );

        Assert.Equal(
            "×7 · 9.50s* · 10 applications",
            CombatImpactMetricFormatter.Group(group, chinese: false)
        );
    }

    [Theory]
    [InlineData(950, (int)CombatImpactCoverage.Exact, false, "0.95s")]
    [InlineData(1000, (int)CombatImpactCoverage.Exact, false, "1s")]
    [InlineData(1100, (int)CombatImpactCoverage.Exact, false, "1.10s")]
    [InlineData(950, (int)CombatImpactCoverage.LowerBound, false, "0.95s*")]
    [InlineData(950, (int)CombatImpactCoverage.Partial, true, "0.95s*")]
    public void Duration_is_always_seconds_and_marks_reconstructed_values(
        int milliseconds,
        int coverageValue,
        bool chinese,
        string expected
    )
    {
        var coverage = (CombatImpactCoverage)coverageValue;
        var formatted = CombatImpactMetricFormatter.Value(
            milliseconds,
            CombatImpactValueUnit.Milliseconds,
            coverage,
            showSign: false,
            chinese
        );

        Assert.Equal(expected, formatted);
        Assert.DoesNotContain("ms", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("≥", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("≈", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("estimated", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("估算", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Caused_disclosures_explain_missing_targets_and_reconstructed_details()
    {
        var group = new CombatImpactGroup(
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
        var source = new CombatImpactSource(Target, 1, 3, [group]);

        Assert.Equal(
            [
                "* Partial breakdown: 2 effects had no target data.",
                "* Some details and durations are reconstructed from combat events and may not match the game totals exactly.",
            ],
            CombatImpactMetricFormatter.CausedDisclosures(source, chinese: false)
        );
        Assert.Equal(
            [
                "* 明细不完整：2 个效果缺少目标数据。",
                "* 部分明细及持续时间由战斗事件推算，可能与游戏总计不完全一致。",
            ],
            CombatImpactMetricFormatter.CausedDisclosures(source, chinese: true)
        );
    }

    [Fact]
    public void Exact_details_do_not_show_a_disclosure()
    {
        var group = Group(
            CombatImpactKind.Burn,
            count: 1,
            observedValue: 4,
            CombatImpactValueUnit.Amount
        );
        var source = new CombatImpactSource(Target, 1, 1, [group]);
        var received = new CombatImpactReceived(
            Target,
            1,
            [
                new CombatImpactIncomingGroup(
                    CombatImpactKind.Burn,
                    "BurnApplyAmount",
                    1,
                    4,
                    CombatImpactValueUnit.Amount,
                    CombatImpactCoverage.Exact,
                    []
                ),
            ]
        );

        Assert.Empty(CombatImpactMetricFormatter.CausedDisclosures(source, chinese: false));
        Assert.Empty(CombatImpactMetricFormatter.ReceivedDisclosures(received, chinese: false));
    }

    [Fact]
    public void Received_reconstruction_uses_the_same_localized_disclosure()
    {
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
    public void Authoritative_only_group_omits_fake_zero_occurrence_count()
    {
        var group = new CombatImpactGroup(
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

        Assert.Equal("76 total", CombatImpactMetricFormatter.Group(group, chinese: false));
    }

    [Fact]
    public void Incoming_group_omits_redundant_observed_label_without_source_card_stats()
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

    [Fact]
    public void Critical_count_uses_the_supplied_native_icon_without_copy()
    {
        var group = Group(
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
            "×8 (4 <sprite name=Crit>) · 640",
            CombatImpactMetricFormatter.Group(group, chinese: false, CritIcon)
        );
        Assert.Equal(
            "×8（4 <sprite name=Crit>） · 640",
            CombatImpactMetricFormatter.Group(group, chinese: true, CritIcon)
        );
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
