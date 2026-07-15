using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class CollectionEncounterPreviewCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"bpp-encounter-preview-{Guid.NewGuid():N}"
    );

    [Fact]
    public void Matching_identity_round_trips_one_snapshot()
    {
        var store = Store();
        var identity = Identity(etag: "etag-a");
        var expected = Snapshot();

        store.Save(identity, expected);

        Assert.True(store.TryLoad(identity, out var actual, out var missReason), missReason);
        Assert.NotNull(actual);
        Assert.Equal(expected.EventCount, actual.EventCount);
        Assert.Equal(expected.TemplateCount, actual.TemplateCount);
        Assert.True(actual.TryGetEvent(EventId, out _));
        Assert.True(actual.TryGetTemplate(StepId, out var step));
        Assert.Equal("Take the reward", step.Description.FallbackText);
        Assert.NotNull(step.RewardFilter);
        Assert.False(step.RewardFilter.UsesDayTierTable);
        Assert.True(step.RewardFilter.UsesDayTierDistribution);
        Assert.True(actual.TryGetLevelUp(2, out var levelUp));
        Assert.Equal(150, levelUp.HealthIncrease);
        Assert.Equal(3, actual.Coverage.UnsupportedLevelUpPartCount);
    }

    [Fact]
    public void GameData_or_game_build_mismatch_is_a_cache_miss()
    {
        var store = Store();
        store.Save(Identity(etag: "etag-a"), Snapshot());

        Assert.False(store.TryLoad(Identity(etag: "etag-b"), out _, out var etagReason));
        Assert.Equal("identity-mismatch", etagReason);

        Assert.False(
            store.TryLoad(
                new CollectionEncounterPreviewCacheIdentity(
                    "etag",
                    "https://example.invalid/GameData.db.zip",
                    "etag-a",
                    "build-b",
                    "Online"
                ),
                out _,
                out var buildReason
            )
        );
        Assert.Equal("identity-mismatch", buildReason);
    }

    [Fact]
    public void Schema_mismatch_and_corrupt_json_are_cache_misses()
    {
        var store = Store();
        var identity = Identity(etag: "etag-a");
        store.Save(identity, Snapshot());

        var document = JObject.Parse(File.ReadAllText(store.CachePath));
        document["schemaVersion"] = CollectionEncounterPreviewCacheStore.SchemaVersion + 1;
        File.WriteAllText(store.CachePath, document.ToString());

        Assert.False(store.TryLoad(identity, out _, out var schemaReason));
        Assert.Equal("schema-mismatch", schemaReason);

        store.Save(identity, Snapshot());
        document = JObject.Parse(File.ReadAllText(store.CachePath));
        document.Remove("levelUps");
        File.WriteAllText(store.CachePath, document.ToString());
        Assert.False(store.TryLoad(identity, out _, out var missingLevelUpsReason));
        Assert.Equal("invalid-json", missingLevelUpsReason);

        File.WriteAllText(store.CachePath, "{truncated");
        Assert.False(store.TryLoad(identity, out _, out var corruptReason));
        Assert.Equal("invalid-json", corruptReason);
    }

    [Fact]
    public void Save_overwrites_the_single_snapshot_and_cleans_orphan_temp_files()
    {
        var store = Store();
        var orphan = store.CachePath + ".orphan.tmp";
        Directory.CreateDirectory(_directory);
        File.WriteAllText(orphan, "orphan");

        store.CleanupOrphanedTempFiles();
        store.Save(Identity(etag: "etag-a"), Snapshot());
        store.Save(Identity(etag: "etag-b"), Snapshot());

        Assert.False(File.Exists(orphan));
        Assert.Single(Directory.GetFiles(_directory, "preview-plans.json"));
        Assert.Empty(Directory.GetFiles(_directory, "preview-plans.json.*.tmp"));
        Assert.True(store.TryLoad(Identity(etag: "etag-b"), out _, out _));
    }

    private CollectionEncounterPreviewCacheStore Store() =>
        new(Path.Combine(_directory, "preview-plans.json"));

    private static readonly Guid EventId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid StepId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static CollectionEncounterPreviewSnapshot Snapshot()
    {
        var eventTemplate = new CollectionEncounterPreviewTemplatePlan(
            EventId,
            CollectionEncounterPreviewTemplateKind.Event,
            Array.Empty<EHero>(),
            "Test Event",
            new CollectionEncounterPreviewLocalizedText("event-title", "Test Event"),
            new CollectionEncounterPreviewLocalizedText("event-description", "Pick one"),
            new Dictionary<string, CollectionEncounterPreviewAbilityValue>(),
            rewardFilter: null
        );
        var stepTemplate = new CollectionEncounterPreviewTemplatePlan(
            StepId,
            CollectionEncounterPreviewTemplateKind.EncounterStep,
            new[] { EHero.Jules },
            "Test Step",
            new CollectionEncounterPreviewLocalizedText("step-title", "Test Step"),
            new CollectionEncounterPreviewLocalizedText("step-description", "Take the reward"),
            new Dictionary<string, CollectionEncounterPreviewAbilityValue>(),
            rewardFilter: new CollectionEncounterRewardFilter(
                ECardType.Item,
                quantity: 1,
                fromAnyHero: true,
                Array.Empty<ECardSize>(),
                Array.Empty<ETier>(),
                Array.Empty<ECardTag>(),
                Array.Empty<EHiddenTag>(),
                "Item",
                usesDayTierTable: false,
                usesDayTierDistribution: true
            )
        );
        var eventPlan = new CollectionEncounterPreviewEventPlan(
            EventId,
            isRandomSelectionEvent: false,
            suppressRandomOutcome: false,
            choiceLimit: 1,
            outcomeGroups: Array.Empty<CollectionEncounterOutcomeGroupData>(),
            choiceGroups: new[]
            {
                new CollectionEncounterChoiceGroupData(
                    isRandomPool: false,
                    new[] { new CollectionEncounterStepReference(StepId) }
                ),
            }
        );
        return new CollectionEncounterPreviewSnapshot(
            new[] { eventPlan },
            new[] { eventTemplate, stepTemplate },
            new[]
            {
                new CollectionLevelUpPreviewPlan(
                    2,
                    150,
                    isRandomSelection: false,
                    new[]
                    {
                        new CollectionLevelUpPreviewGroup(
                            randomWeight: 0,
                            limit: 1,
                            new[] { StepId },
                            new[]
                            {
                                new CollectionLevelUpPreviewHeroCondition(
                                    new[] { EHero.Jules },
                                    "Any"
                                ),
                            }
                        ),
                    }
                ),
            },
            new CollectionPreviewCoverage(0, 0, 3, 1)
        );
    }

    private static CollectionEncounterPreviewCacheIdentity Identity(string etag) =>
        new("etag", "https://example.invalid/GameData.db.zip", etag, "build-a", "Online");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
