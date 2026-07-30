using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactAggregatorTests
{
    [Fact]
    public void Groups_effects_by_source_kind_and_target()
    {
        var report = Aggregate([
            Event(CombatImpactKind.Slow, "fairies", "eclipse", 2900, milliseconds: true),
            Event(CombatImpactKind.Slow, "fairies", "eclipse", 2950, milliseconds: true),
            Event(CombatImpactKind.Slow, "fairies", "bread", 1950, milliseconds: true),
            Event(CombatImpactKind.Freeze, "fairies", "eclipse", 3850, milliseconds: true),
        ]);

        var source = Assert.Single(report.Sources);
        Assert.Equal("Fairies", source.Entity.Name);
        Assert.Equal(4, source.TotalCount);
        Assert.Collection(
            source.Groups,
            slow =>
            {
                Assert.Equal(CombatImpactKind.Slow, slow.Kind);
                Assert.Equal(3, slow.Count);
                Assert.Equal(7800, slow.AggregateValue);
                Assert.Collection(
                    slow.Targets,
                    eclipse =>
                    {
                        Assert.Equal("The Eclipse", eclipse.Entity.Name);
                        Assert.Equal(2, eclipse.Count);
                        Assert.Equal(5850, eclipse.AggregateValue);
                    },
                    bread =>
                    {
                        Assert.Equal("Bread Knife", bread.Entity.Name);
                        Assert.Equal(1, bread.Count);
                        Assert.Equal(1950, bread.AggregateValue);
                    }
                );
            },
            freeze =>
            {
                Assert.Equal(CombatImpactKind.Freeze, freeze.Kind);
                Assert.Equal(1, freeze.Count);
            }
        );
    }

    [Fact]
    public void Authoritative_total_wins_without_inventing_target_values()
    {
        var report = Aggregate(
            [
                Event(CombatImpactKind.DirectDamage, "fairies", "opponent", 80),
                Event(CombatImpactKind.DirectDamage, "fairies", "opponent"),
            ],
            authoritative: new Dictionary<CombatImpactKind, int>
            {
                [CombatImpactKind.DirectDamage] = 240,
            }
        );

        var group = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(240, group.AggregateValue);
        Assert.False(group.ValueIsPartial);
        var target = Assert.Single(group.Targets);
        Assert.Equal(80, target.AggregateValue);
        Assert.True(target.ValueIsPartial);
    }

    [Fact]
    public void Keeps_attribute_changes_separate_by_native_attribute()
    {
        var report = Aggregate([
            Event(
                CombatImpactKind.AttributeChange,
                "fairies",
                "bread",
                -20,
                nativeKey: "DamageAmount"
            ),
            Event(CombatImpactKind.AttributeChange, "fairies", "bread", 2, nativeKey: "CritChance"),
        ]);

        var groups = Assert.Single(report.Sources).Groups;
        Assert.Equal(2, groups.Count);
        var damage = Assert.Single(groups, group => group.NativeAttributeKey == "DamageAmount");
        Assert.Equal(-20, damage.AggregateValue);
        Assert.Equal(-20, Assert.Single(damage.Targets).AggregateValue);
        Assert.Contains(groups, group => group.NativeAttributeKey == "CritChance");
    }

    [Fact]
    public void Critical_effect_is_a_real_effect_with_target_and_value()
    {
        var report = Aggregate([
            Event(CombatImpactKind.DirectDamage, "fairies", "opponent", 160),
            Event(CombatImpactKind.Critical, "fairies", "opponent", 160),
        ]);

        var source = Assert.Single(report.Sources);
        var critical = Assert.Single(
            source.Groups,
            group => group.Kind == CombatImpactKind.Critical
        );
        Assert.Equal(1, source.TotalCount);
        Assert.Equal(160, critical.AggregateValue);
        Assert.Equal("Opponent", Assert.Single(critical.Targets).Entity.Name);
    }

    [Fact]
    public void Omits_unknown_sources_and_sorts_nonzero_sources_by_effect_count()
    {
        var report = Aggregate([
            Event(CombatImpactKind.Burn, "unknown", "opponent", 10),
            Event(CombatImpactKind.Burn, "bread", "opponent", 10),
            Event(CombatImpactKind.Burn, "fairies", "opponent", 10),
            Event(CombatImpactKind.Freeze, "fairies", "bread", 1000, milliseconds: true),
        ]);

        Assert.Equal(
            ["Fairies", "Bread Knife"],
            report.Sources.Select(source => source.Entity.Name)
        );
    }

    [Fact]
    public void Collapses_large_event_volume_into_bounded_source_group_and_target_rows()
    {
        var events = Enumerable
            .Range(0, 10_000)
            .Select(_ => Event(CombatImpactKind.Burn, "fairies", "opponent", 1))
            .ToArray();

        var source = Assert.Single(Aggregate(events).Sources);
        var group = Assert.Single(source.Groups);
        var target = Assert.Single(group.Targets);

        Assert.Equal(10_000, source.TotalCount);
        Assert.Equal(10_000, group.Count);
        Assert.Equal(10_000, target.Count);
    }

    private static CombatImpactReport Aggregate(
        IReadOnlyList<CombatImpactEvent> events,
        IReadOnlyDictionary<CombatImpactKind, int>? authoritative = null
    )
    {
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            ["fairies"] = Entity("fairies", "Fairies", "Skill", 0),
            ["bread"] = Entity("bread", "Bread Knife", "Item", 1),
            ["eclipse"] = Entity("eclipse", "The Eclipse", "Item", 2),
            ["opponent"] = Entity("opponent", "Opponent", "Hero", 3),
        };
        var totals =
            authoritative == null
                ? new Dictionary<string, IReadOnlyDictionary<CombatImpactKind, int>>(
                    StringComparer.Ordinal
                )
                : new Dictionary<string, IReadOnlyDictionary<CombatImpactKind, int>>(
                    StringComparer.Ordinal
                )
                {
                    ["fairies"] = authoritative,
                };
        return CombatImpactAggregator.Aggregate(
            new CombatImpactProjectionInput(
                entities,
                events,
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                totals
            )
        );
    }

    private static CombatImpactEvent Event(
        CombatImpactKind kind,
        string source,
        string target,
        int? value = null,
        bool milliseconds = false,
        string? nativeKey = null
    ) =>
        new(
            kind,
            source,
            target,
            value,
            milliseconds ? CombatImpactValueUnit.Milliseconds : CombatImpactValueUnit.Amount,
            nativeKey
        );

    private static CombatImpactEntity Entity(string id, string name, string type, int order) =>
        new(id, name, type, null, null, ECombatantId.Player, order);
}
