using BazaarBattleService;
using BazaarBattleService.Models;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Encounters;
using BazaarPlusPlus.GameInterop.EncounterOffers;

var ailaId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
var ailaId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
var globalMerchantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
var pygTrainerId = Guid.Parse("44444444-4444-4444-4444-444444444444");

var entries = MerchantTrainerCatalog.Build(
    $$"""
    {
      "entries": [
        {
          "name": "Aila",
          "kind": "Merchant",
          "tier": "Silver",
          "heroes": ["Vanessa"],
          "description": "Sells Crit items",
          "templateIds": ["{{ailaId1}}", "{{ailaId2}}"]
        },
        {
          "name": "Aila",
          "kind": "Merchant",
          "tier": "Silver",
          "heroes": ["Dooley"],
          "description": "Sells Crit items",
          "templateIds": ["55555555-5555-5555-5555-555555555555"]
        },
        {
          "name": "Nufu",
          "kind": "Merchant",
          "tier": "Gold",
          "heroes": [],
          "description": "Sells common items",
          "templateIds": ["{{globalMerchantId}}"]
        },
        {
          "name": "Professor Riggs",
          "kind": "Trainer",
          "tier": "Bronze",
          "heroes": ["Pygmalien"],
          "description": "Teaches skills",
          "templateIds": ["{{pygTrainerId}}"]
        }
      ]
    }
    """
);

AssertEqual(4, entries.Count, "Catalog build should keep valid entries.");
AssertTrue(
    entries.All(entry => !string.IsNullOrWhiteSpace(entry.SourceKey)),
    "Every source entry should receive a stable source key."
);
AssertEqual(
    entries.Count,
    entries.Select(entry => entry.SourceKey).Distinct(StringComparer.Ordinal).Count(),
    "Source keys should be unique across current catalog identities."
);
AssertValues(
    entries[0].TemplateIds.ToArray(),
    new[] { ailaId1, ailaId2 },
    "Source identity must preserve all template ids for resolver union."
);

var noHeroCacheKey = CollectionSourceOfferPoolCacheKey.Build(
    entries[0],
    Array.Empty<EHero>()
);
AssertTrue(
    noHeroCacheKey.StartsWith(entries[0].SourceKey + "|", StringComparison.Ordinal),
    "Source offer cache key should include the stable source key."
);
AssertTrue(
    noHeroCacheKey.Contains(ailaId1.ToString("N")[..12], StringComparison.Ordinal)
        && noHeroCacheKey.Contains(ailaId2.ToString("N")[..12], StringComparison.Ordinal),
    "Source offer cache key should include a fingerprint of all source template ids."
);
AssertTrue(
    noHeroCacheKey.EndsWith("|no-hero-filter", StringComparison.Ordinal),
    "Source offer cache key should distinguish the no-hero-filter state."
);

var heroCacheKey = CollectionSourceOfferPoolCacheKey.Build(
    entries[0],
    new[] { EHero.Vanessa, EHero.Common }
);
var reorderedHeroCacheKey = CollectionSourceOfferPoolCacheKey.Build(
    entries[0],
    new[] { EHero.Common, EHero.Vanessa }
);
AssertEqual(
    reorderedHeroCacheKey,
    heroCacheKey,
    "Source offer cache key should canonicalize hero filter order."
);
AssertFalse(
    string.Equals(noHeroCacheKey, heroCacheKey, StringComparison.Ordinal),
    "Source offer cache key should vary by hero filter."
);

var visibleForVanessa = MerchantTrainerCatalog
    .VisibleEntries(entries, EncounterPortraitKind.Merchant, EHero.Vanessa)
    .Select(entry => entry.Name)
    .ToArray();
AssertValues(
    visibleForVanessa,
    new[] { "Aila", "Nufu" },
    "Visible merchant sources should include selected hero sources plus global sources."
);

var visibleWithoutHero = MerchantTrainerCatalog
    .VisibleEntries(entries, EncounterPortraitKind.Merchant, selectedHero: null)
    .Select(entry => entry.Name)
    .ToArray();
AssertValues(
    visibleWithoutHero,
    new[] { "Aila", "Aila", "Nufu" },
    "Without a concrete hero selected, merchant source selector should show all merchants."
);

var visibleTrainers = MerchantTrainerCatalog
    .VisibleEntries(entries, EncounterPortraitKind.Trainer, EHero.Vanessa)
    .ToArray();
AssertEqual(
    0,
    visibleTrainers.Length,
    "Item and Skill source selectors should stay kind-specific."
);

AssertTrue(
    EncounterOfferHeroMapper.TryToRuntime(EHero.Karnok, out var runtimeKarnok),
    "Karnok should have an explicit runtime hero mapping."
);
AssertEqual(
    BazaarTypes.EBazaarHero.Hero7,
    runtimeKarnok,
    "Karnok should map to old-runtime Hero7."
);
AssertTrue(
    EncounterOfferHeroMapper.TryFromRuntime(BazaarTypes.EBazaarHero.Hero7, out var uiKarnok),
    "Old-runtime Hero7 should map back to Karnok."
);
AssertEqual(EHero.Karnok, uiKarnok, "Old-runtime Hero7 should map back to Karnok.");
AssertFalse(
    EncounterOfferHeroMapper.TryToRuntime((EHero)999, out _),
    "Unknown heroes should be unsupported rather than silently mapped by enum value."
);

var sourceAndUiHeroes = EncounterOfferPoolRules.ResolveRuntimeHeroFilters(
    new[] { BazaarTypes.EBazaarHero.Vanessa, BazaarTypes.EBazaarHero.Pygmalien },
    new[] { EHero.Common, EHero.Vanessa }
);
AssertEqual(
    EncounterOfferHeroFilterStatus.Ready,
    sourceAndUiHeroes.Status,
    "Source hero filters and UI hero filters should intersect when both exist."
);
AssertValues(
    sourceAndUiHeroes.RuntimeHeroes.ToArray(),
    new[] { BazaarTypes.EBazaarHero.Vanessa },
    "Hero filter intersection should preserve only shared heroes."
);

var emptyIntersection = EncounterOfferPoolRules.ResolveRuntimeHeroFilters(
    new[] { BazaarTypes.EBazaarHero.Dooley },
    new[] { EHero.Vanessa }
);
AssertEqual(
    EncounterOfferHeroFilterStatus.EmptyIntersection,
    emptyIntersection.Status,
    "Disjoint source and UI hero filters should produce no candidates."
);

var unsupportedHero = EncounterOfferPoolRules.ResolveRuntimeHeroFilters(
    Array.Empty<BazaarTypes.EBazaarHero>(),
    new[] { (EHero)999 }
);
AssertEqual(
    EncounterOfferHeroFilterStatus.UnsupportedHero,
    unsupportedHero.Status,
    "Unsupported UI heroes should be reported instead of ignored."
);

AssertTrue(
    EncounterOfferPoolRules.IsCandidateTierEligible(
        BazaarCard.EItemTier.Silver,
        new[] { BazaarCard.EItemTier.Gold }
    ),
    "Source ItemTierFilters should allow candidates at or below any source tier."
);
AssertFalse(
    EncounterOfferPoolRules.IsCandidateTierEligible(
        BazaarCard.EItemTier.Diamond,
        new[] { BazaarCard.EItemTier.Gold }
    ),
    "Source ItemTierFilters should reject candidates above all source tiers."
);
AssertTrue(
    EncounterOfferPoolRules.IsCandidateTierEligible(
        BazaarCard.EItemTier.Diamond,
        Array.Empty<BazaarCard.EItemTier>()
    ),
    "Missing ItemTierFilters should not crop the source pool."
);

var retry = new CollectionSourcePoolRetryState();
var firstSchedule = retry.Schedule("source-a", now: 10f, retrySeconds: 0.25f, maxAttempts: 2);
AssertFalse(firstSchedule.IsExhausted, "First loading retry should schedule a retry.");
AssertFalse(
    retry.TryConsumeDueRetry(10.20f),
    "Retry should not fire before its due time."
);
AssertTrue(retry.TryConsumeDueRetry(10.25f), "Retry should fire at its due time.");
AssertEqual(1, retry.Attempts, "Due retry should increment the attempt count.");
AssertTrue(
    float.IsNaN(retry.NextRetryAt),
    "Consumed retry should clear the pending due time."
);

var secondSchedule = retry.Schedule("source-a", now: 10.25f, retrySeconds: 0.25f, maxAttempts: 2);
AssertFalse(secondSchedule.IsExhausted, "Retry below max attempts should reschedule.");
AssertTrue(retry.TryConsumeDueRetry(10.50f), "Second retry should fire.");
AssertEqual(2, retry.Attempts, "Second due retry should reach the max attempt count.");

var exhausted = retry.Schedule("source-a", now: 10.50f, retrySeconds: 0.25f, maxAttempts: 2);
AssertTrue(exhausted.IsExhausted, "Retry should report exhaustion at max attempts.");
AssertTrue(exhausted.ShouldLogWarning, "First exhaustion should request one warning log.");
var exhaustedAgain = retry.Schedule("source-a", now: 10.75f, retrySeconds: 0.25f, maxAttempts: 2);
AssertTrue(exhaustedAgain.IsExhausted, "Same exhausted key should stay exhausted.");
AssertFalse(
    exhaustedAgain.ShouldLogWarning,
    "Repeated exhaustion for the same key should not request another warning log."
);

var changedKey = retry.Schedule("source-b", now: 11f, retrySeconds: 0.25f, maxAttempts: 2);
AssertFalse(changedKey.IsExhausted, "Changing retry key should reset exhaustion state.");
AssertEqual(0, retry.Attempts, "Changing retry key should reset attempt count.");
AssertEqual("source-b", retry.PendingKey, "Changing retry key should update the pending key.");
retry.Reset();
AssertEqual(null, retry.PendingKey, "Reset should clear retry key.");
AssertEqual(0, retry.Attempts, "Reset should clear retry attempts.");
AssertTrue(float.IsNaN(retry.NextRetryAt), "Reset should clear scheduled retry time.");

Console.WriteLine("Collection source filtering checks passed.");

static void AssertValues<T>(IReadOnlyList<T> actual, IReadOnlyList<T> expected, string message)
{
    if (actual.Count != expected.Count)
        throw new InvalidOperationException(
            $"{message} Expected {expected.Count} values, got {actual.Count}."
        );
    for (var i = 0; i < actual.Count; i++)
    {
        if (!EqualityComparer<T>.Default.Equals(actual[i], expected[i]))
            throw new InvalidOperationException(
                $"{message} At {i}: expected {expected[i]}, got {actual[i]}."
            );
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
