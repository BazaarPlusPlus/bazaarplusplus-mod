using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class CollectionEncounterPreviewEvaluatorTests
{
    private static readonly Guid EventId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid StepId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid RequiredItemId = Guid.Parse(
        "30000000-0000-0000-0000-000000000003"
    );

    [Fact]
    public void Same_static_plan_re_evaluates_hero_day_and_inventory()
    {
        var requirement = new CollectionEncounterCardRequirement(
            new[] { RequiredItemId },
            Array.Empty<IReadOnlyList<string>>(),
            "Any",
            "GreaterThanOrEqual",
            1
        );
        var group = new CollectionEncounterChoiceGroupData(
            isRandomPool: false,
            new[] { new CollectionEncounterStepReference(StepId, new[] { requirement }) },
            new CollectionEncounterDayCondition(day: 3, comparison: "GreaterThanOrEqual")
        );
        var (snapshot, plan) = Plan(group);

        var wrongHero = CollectionEncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Vanessa,
            Inventory(RequiredItemId),
            currentDay: 3
        );
        Assert.NotNull(wrongHero);
        Assert.Empty(wrongHero.ChoiceDetails);

        var wrongDay = CollectionEncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Jules,
            Inventory(RequiredItemId),
            currentDay: 2
        );
        Assert.NotNull(wrongDay);
        Assert.Empty(wrongDay.ChoiceDetails);

        var missingItem = CollectionEncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Jules,
            Inventory(),
            currentDay: 3
        );
        var dimmed = Assert.Single(
            Assert.IsType<CollectionEncounterOption>(missingItem).ChoiceDetails
        );
        Assert.False(dimmed.IsEligible);

        var eligible = CollectionEncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Jules,
            Inventory(RequiredItemId),
            currentDay: 3
        );
        var shown = Assert.Single(Assert.IsType<CollectionEncounterOption>(eligible).ChoiceDetails);
        Assert.True(shown.IsEligible);
        Assert.Equal("Jules Step", shown.DisplayName);
        Assert.Equal("Take it", shown.ResultText);
    }

    [Fact]
    public void Outcome_percentages_are_recomputed_after_dynamic_requirements()
    {
        var requirement = new CollectionEncounterCardRequirement(
            new[] { RequiredItemId },
            Array.Empty<IReadOnlyList<string>>(),
            "Any",
            "GreaterThanOrEqual",
            1
        );
        var root = Template(
            EventId,
            CollectionEncounterPreviewTemplateKind.Event,
            Array.Empty<EHero>(),
            "Event",
            "Event result"
        );
        var rewardA = Template(
            StepId,
            CollectionEncounterPreviewTemplateKind.EncounterStep,
            new[] { EHero.Common },
            "A",
            "Gain A"
        );
        var rewardBId = Guid.Parse("30000000-0000-0000-0000-000000000004");
        var rewardB = Template(
            rewardBId,
            CollectionEncounterPreviewTemplateKind.EncounterStep,
            new[] { EHero.Common },
            "B",
            "Gain B"
        );
        var plan = new CollectionEncounterPreviewEventPlan(
            EventId,
            isRandomSelectionEvent: true,
            suppressRandomOutcome: false,
            choiceLimit: 1,
            outcomeGroups: new[]
            {
                new CollectionEncounterOutcomeGroupData(
                    50,
                    new[] { StepId },
                    Array.Empty<CollectionEncounterOutcomeQueryPool>(),
                    Array.Empty<CollectionEncounterCardRequirement>(),
                    dayCondition: null
                ),
                new CollectionEncounterOutcomeGroupData(
                    50,
                    new[] { rewardBId },
                    Array.Empty<CollectionEncounterOutcomeQueryPool>(),
                    new[] { requirement },
                    dayCondition: null
                ),
            },
            choiceGroups: Array.Empty<CollectionEncounterChoiceGroupData>()
        );
        var snapshot = new CollectionEncounterPreviewSnapshot(
            new[] { plan },
            new[] { root, rewardA, rewardB }
        );

        var withoutItem = CollectionEncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Jules,
            Inventory(),
            currentDay: 1
        );
        Assert.Equal(100, withoutItem!.OutcomeGroups![0].Percent);
        Assert.Null(withoutItem.OutcomeGroups[1].Percent);
        Assert.False(withoutItem.OutcomeGroups[1].IsEligible);

        var withItem = CollectionEncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Jules,
            Inventory(RequiredItemId),
            currentDay: 1
        );
        Assert.Equal(new int?[] { 50, 50 }, withItem!.OutcomeGroups!.Select(x => x.Percent));
    }

    private static (
        CollectionEncounterPreviewSnapshot Snapshot,
        CollectionEncounterPreviewEventPlan Plan
    ) Plan(CollectionEncounterChoiceGroupData group)
    {
        var root = Template(
            EventId,
            CollectionEncounterPreviewTemplateKind.Event,
            Array.Empty<EHero>(),
            "Event",
            "Pick one"
        );
        var step = Template(
            StepId,
            CollectionEncounterPreviewTemplateKind.EncounterStep,
            new[] { EHero.Jules },
            "Jules Step",
            "Take it"
        );
        var plan = new CollectionEncounterPreviewEventPlan(
            EventId,
            isRandomSelectionEvent: false,
            suppressRandomOutcome: false,
            choiceLimit: 1,
            outcomeGroups: Array.Empty<CollectionEncounterOutcomeGroupData>(),
            choiceGroups: new[] { group }
        );
        return (new CollectionEncounterPreviewSnapshot(new[] { plan }, new[] { root, step }), plan);
    }

    private static CollectionEncounterPreviewTemplatePlan Template(
        Guid id,
        CollectionEncounterPreviewTemplateKind kind,
        IReadOnlyList<EHero> heroes,
        string title,
        string description
    ) =>
        new(
            id,
            kind,
            heroes,
            title,
            new CollectionEncounterPreviewLocalizedText(string.Empty, title),
            new CollectionEncounterPreviewLocalizedText(string.Empty, description),
            new Dictionary<string, CollectionEncounterPreviewAbilityValue>(),
            rewardFilter: null
        );

    private static CollectionEncounterInventory Inventory(params Guid[] ids) =>
        new(
            ids.Select(id => new CollectionEncounterInventoryCard(id, Array.Empty<string>()))
                .ToArray()
        );
}
