using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class CollectionEncounterPreviewPlanRegistryTests
{
    [Fact]
    public void Late_generation_cannot_publish_over_the_current_manager()
    {
        var registry = new CollectionEncounterPreviewPlanRegistry();
        var sourceA = new object();
        var sourceB = new object();
        var snapshotA = Snapshot(Guid.Parse("40000000-0000-0000-0000-000000000001"));
        var snapshotB = Snapshot(Guid.Parse("40000000-0000-0000-0000-000000000002"));

        var generationA = registry.BeginGeneration(sourceA);
        var generationB = registry.BeginGeneration(sourceB);

        Assert.False(registry.TryPublish(sourceA, generationA, snapshotA));
        Assert.False(registry.TryGet(sourceA, out _));
        Assert.True(registry.TryPublish(sourceB, generationB, snapshotB));
        Assert.True(registry.TryGet(sourceB, out var current));
        Assert.Same(snapshotB, current);
    }

    [Fact]
    public void Beginning_a_new_generation_immediately_unpublishes_the_old_snapshot()
    {
        var registry = new CollectionEncounterPreviewPlanRegistry();
        var sourceA = new object();
        var sourceB = new object();
        var generationA = registry.BeginGeneration(sourceA);
        Assert.True(registry.TryPublish(sourceA, generationA, Snapshot(Guid.NewGuid())));

        registry.BeginGeneration(sourceB);

        Assert.False(registry.TryGet(sourceA, out _));
        Assert.False(registry.TryGet(sourceB, out _));
    }

    [Fact]
    public void Commit_callback_only_runs_for_the_current_generation()
    {
        var registry = new CollectionEncounterPreviewPlanRegistry();
        var sourceA = new object();
        var sourceB = new object();
        var generationA = registry.BeginGeneration(sourceA);
        var generationB = registry.BeginGeneration(sourceB);
        var commits = 0;

        Assert.False(
            registry.TryCommitAndPublish(
                sourceA,
                generationA,
                Snapshot(Guid.NewGuid()),
                () => commits++
            )
        );
        Assert.True(
            registry.TryCommitAndPublish(
                sourceB,
                generationB,
                Snapshot(Guid.NewGuid()),
                () => commits++
            )
        );
        Assert.Equal(1, commits);
    }

    private static CollectionEncounterPreviewSnapshot Snapshot(Guid eventId)
    {
        var template = new CollectionEncounterPreviewTemplatePlan(
            eventId,
            CollectionEncounterPreviewTemplateKind.Event,
            Array.Empty<EHero>(),
            "Event",
            new CollectionEncounterPreviewLocalizedText(string.Empty, "Event"),
            new CollectionEncounterPreviewLocalizedText(string.Empty, "Event"),
            new Dictionary<string, CollectionEncounterPreviewAbilityValue>(),
            rewardFilter: null
        );
        var plan = new CollectionEncounterPreviewEventPlan(
            eventId,
            isRandomSelectionEvent: false,
            suppressRandomOutcome: false,
            choiceLimit: 1,
            Array.Empty<CollectionEncounterOutcomeGroupData>(),
            Array.Empty<CollectionEncounterChoiceGroupData>()
        );
        return new CollectionEncounterPreviewSnapshot(new[] { plan }, new[] { template });
    }
}
