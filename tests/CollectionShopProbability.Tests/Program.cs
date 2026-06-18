using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.DealerModel;

Check.Section("state enum");
Check.Equal(
    0.0,
    (int)CollectionDealerProbabilityState.NotInPool,
    "NotInPool should be the default zero state."
);

Check.Section("estimate bucket");
var mediumBucket = EstimateBucket.Create(EstimateTier.Medium, 0.05, 0.15);
Check.Equal(EstimateTier.Medium, mediumBucket.Tier, "Estimate bucket should carry tier.");
Check.Equal(0.05, mediumBucket.Low, "Estimate bucket should carry low bound.");
Check.Equal(0.15, mediumBucket.High, "Estimate bucket should carry high bound.");
Check.Equal(
    "old-bazaar-card-dealer",
    mediumBucket.Model,
    "Estimate bucket should carry the dealer model."
);
Check.Equal(
    EstimateAuthority.Reference,
    mediumBucket.Authority,
    "Estimate bucket authority should be reference."
);
Check.Equal(
    "thebazaar.wiki.gg 0.1.9",
    mediumBucket.Source,
    "Estimate bucket should carry the reference source label."
);
Check.True(mediumBucket.ReferenceOnly, "Estimate bucket should be marked reference-only.");

Check.Section("reference weights");
var day3Weights = DealerTierWeightReference.ForDay(3);
Check.True(day3Weights.ContainsKey(ETier.Bronze), "Day 3 should include Bronze.");
Check.True(day3Weights.ContainsKey(ETier.Silver), "Day 3 should include Silver.");
Check.True(!day3Weights.ContainsKey(ETier.Diamond), "Day 3 should not include Diamond.");
Check.About(1.0, day3Weights.Values.Sum(), 0.05, "Day 3 weights should sum to roughly 1.");

Check.Section("explain DTOs");
var sourceContext = new CollectionDealerSourceContext
{
    SourceKey = "Goldie",
    Kind = CollectionDealerSourceKind.Merchant,
    Hero = EHero.Vanessa,
    Day = 3,
    SuppressDayGate = true,
    PinnedTier = ETier.Gold,
    EstimateEnabled = false,
    NativeAssumption = 0.8f,
};
var explain = new CollectionDealerCardExplain
{
    State = CollectionDealerProbabilityState.Explain,
    InPool = true,
    NativeEligible = false,
    LooseEligible = true,
    DayGatePass = true,
    Notes = new[] { "tier-specialist" },
};
Check.Equal(
    CollectionDealerProbabilityState.Explain,
    explain.State,
    "Explain DTO should carry state."
);
Check.True(explain.LooseEligible, "Explain DTO should carry loose eligibility.");
Check.True(!explain.NativeEligible, "Explain DTO should carry native eligibility.");
Check.Equal(0.8f, sourceContext.NativeAssumption, "Source context should carry native assumption.");

Check.Section("phase A resolver");
var bronzeId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
var silverId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
var goldId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
var day3Explains = CollectionDealerExplainResolver.Resolve(
    MerchantContext(day: 3),
    new[] { Card(bronzeId, ETier.Bronze), Card(goldId, ETier.Gold) }
);
Check.True(day3Explains[bronzeId].DayGatePass, "Day 3 should pass Bronze day gate.");
Check.True(!day3Explains[goldId].DayGatePass, "Day 3 should fail Gold day gate.");
Check.True(day3Explains[bronzeId].NativeEligible, "Day 3 Bronze should be native eligible.");
Check.True(day3Explains[bronzeId].LooseEligible, "Day 3 Bronze should be loose eligible.");
Check.True(!day3Explains[goldId].LooseEligible, "Day 3 Gold should not be loose eligible.");

var specialistExplains = CollectionDealerExplainResolver.Resolve(
    MerchantContext(day: 2, suppressDayGate: true, pinnedTier: ETier.Gold),
    new[] { Card(bronzeId, ETier.Bronze), Card(goldId, ETier.Gold) }
);
Check.True(specialistExplains[goldId].DayGatePass, "Tier specialist should suppress day gate.");
Check.True(specialistExplains[goldId].LooseEligible, "Pinned Gold should allow Gold loosely.");
Check.True(!specialistExplains[goldId].NativeEligible, "Tier specialist should not be native.");
Check.True(specialistExplains[bronzeId].LooseEligible, "Pinned Gold should allow Bronze loosely.");
Check.True(
    specialistExplains[goldId].Notes.Contains("tier-specialist-loose"),
    "Tier specialist notes should explain loose eligibility."
);

var missingWeightsExplains = CollectionDealerExplainResolver.Resolve(
    MerchantContext(day: 3, estimateEnabled: true),
    new[] { Card(bronzeId, ETier.Bronze) }
);
Check.Equal(
    CollectionDealerProbabilityState.WeightsMissing,
    missingWeightsExplains[bronzeId].State,
    "Estimate-enabled merchant without hint should report missing weights."
);

Check.Equal(2, day3Explains.Count, "Resolver should return one explain per offered card.");
Check.True(day3Explains.ContainsKey(bronzeId), "Resolver key should be the real Bronze VM id.");
Check.True(day3Explains.ContainsKey(goldId), "Resolver key should be the real Gold VM id.");
Check.True(
    !day3Explains.ContainsKey(Guid.Empty),
    "Resolver should not synthesize placeholder ids."
);

Check.Section("seeded rng");
IRng firstRng = new SeededRng(20260615);
IRng secondRng = new SeededRng(20260615);
Check.Equal(firstRng.NextDouble(), secondRng.NextDouble(), "Same seed should match first double.");
Check.Equal(firstRng.NextDouble(), secondRng.NextDouble(), "Same seed should match second double.");
Check.Equal(firstRng.NextInt(7), secondRng.NextInt(7), "Same seed should match bounded int.");
var bounded = new SeededRng(9).NextInt(3);
Check.True(bounded >= 0 && bounded < 3, "NextInt should respect exclusive upper bound.");
Check.Throws<InvalidOperationException>(
    () => new ScriptedRng(doubles: Array.Empty<double>(), ints: new[] { 3 }).NextInt(3),
    "ScriptedRng should reject scripted ints outside the requested bound."
);

Check.Section("simulation DTOs");
var shopDefinition = new DealerShopDefinition();
Check.Equal(3, shopDefinition.NumberCardsToSpawn, "Shop definition should default to 3 spawns.");
Check.Equal(
    "old-bazaar-card-dealer",
    shopDefinition.Model,
    "Shop definition should default to old dealer model."
);
var playerState = new DealerPlayerState();
Check.True(playerState.PlayerSkillCardIds is not null, "Player skill ids should default non-null.");
var candidate = new DealerCandidate
{
    Id = bronzeId,
    Type = ECardType.Item,
    Size = ECardSize.Small,
    StartingTier = ETier.Bronze,
};
Check.Equal(bronzeId, candidate.Id, "Candidate should carry id.");

Check.Section("dealer probability core");
var t1 = Guid.Parse("10000000-0000-0000-0000-000000000001");
var t2 = Guid.Parse("10000000-0000-0000-0000-000000000002");
var t3 = Guid.Parse("10000000-0000-0000-0000-000000000003");
var legendaryStartId = Guid.Parse("10000000-0000-0000-0000-000000000004");
var fixedDeal = DealerProbabilityCore.SimulateOneDeal(
    Shop(spawn: 2, filters: new[] { t1, t2 }),
    Player(day: 3, skills: new[] { t1 }),
    new[] { Candidate(t1, ETier.Bronze), Candidate(t2, ETier.Silver) },
    new SeededRng(1)
);
Check.Values(
    new[] { t1, t2 },
    fixedDeal.ToArray(),
    "Fixed direct deal should return exact filters when post-skill-exclusion pool is nonempty."
);

var skillExcludedFixedDeal = DealerProbabilityCore.SimulateOneDeal(
    Shop(spawn: 1, filters: new[] { t1 }),
    Player(day: 3, skills: new[] { t1 }),
    new[] { Candidate(t1, ETier.Bronze) },
    new SeededRng(101)
);
Check.Equal(
    0,
    skillExcludedFixedDeal.Count,
    "Fixed direct deal should return empty when skill exclusion empties the filtered pool."
);

var fixedDealWithReversedPool = DealerProbabilityCore.SimulateOneDeal(
    Shop(spawn: 2, filters: new[] { t1, t2 }),
    Player(day: 3),
    new[] { Candidate(t2, ETier.Silver), Candidate(t1, ETier.Bronze) },
    new SeededRng(102)
);
Check.Values(
    new[] { t1, t2 },
    fixedDealWithReversedPool.ToArray(),
    "Fixed direct deal should return exact CardIdFilters order, not filtered pool order."
);

var fixedSkillDeal = DealerProbabilityCore.SimulateOneDeal(
    Shop(spawn: 1, filters: new[] { t1 }),
    Player(day: 3),
    new[] { Candidate(t1, ETier.Bronze, ECardType.Skill) },
    new SeededRng(103)
);
Check.Values(
    new[] { t1 },
    fixedSkillDeal.ToArray(),
    "Fixed direct deal should run before the pure-skill boundary."
);

var bronzeForcedShop = Shop(
    spawn: 1,
    nativeProbability: 1,
    weights: new Dictionary<ETier, double> { [ETier.Bronze] = 1 }
);
Check.Equal(
    0.0,
    DealerProbabilityCore.AppearanceFrequency(
        t2,
        bronzeForcedShop,
        Player(day: 3),
        new[] { Candidate(t1, ETier.Bronze), Candidate(t2, ETier.Gold) },
        trials: 200,
        seed: 10
    ),
    "Native path should only deal cards whose starting tier equals the rolled tier."
);

var silverLooseShop = Shop(
    spawn: 1,
    nativeProbability: 0,
    weights: new Dictionary<ETier, double> { [ETier.Silver] = 1 }
);
Check.Equal(
    0.0,
    DealerProbabilityCore.AppearanceFrequency(
        t3,
        silverLooseShop,
        Player(day: 3),
        new[]
        {
            Candidate(t1, ETier.Bronze),
            Candidate(t2, ETier.Silver),
            Candidate(t3, ETier.Gold),
        },
        trials: 200,
        seed: 11
    ),
    "Loose path should destructively narrow to cards at or below the selected tier."
);

var filteredShop = Shop(spawn: 1, filters: new[] { t1, t2 }, nativeProbability: 0);
Check.True(
    DealerProbabilityCore.AppearanceFrequency(
        t1,
        filteredShop,
        Player(day: 3),
        new[]
        {
            Candidate(t1, ETier.Bronze),
            Candidate(t2, ETier.Silver),
            Candidate(t3, ETier.Bronze),
        },
        trials: 200,
        seed: 12
    ) > 0,
    "Non-fixed CardIdFilters should keep matching cards reachable."
);
Check.Equal(
    0.0,
    DealerProbabilityCore.AppearanceFrequency(
        t3,
        filteredShop,
        Player(day: 3),
        new[]
        {
            Candidate(t1, ETier.Bronze),
            Candidate(t2, ETier.Silver),
            Candidate(t3, ETier.Bronze),
        },
        trials: 200,
        seed: 12
    ),
    "Non-fixed CardIdFilters should exclude cards outside the filter."
);

var nativeMissShop = Shop(
    spawn: 1,
    nativeProbability: 1,
    weights: new Dictionary<ETier, double> { [ETier.Gold] = 1 }
);
Check.True(
    DealerProbabilityCore.AppearanceFrequency(
        t1,
        nativeMissShop,
        Player(day: 3),
        new[] { Candidate(t1, ETier.Bronze), Candidate(t2, ETier.Silver) },
        trials: 200,
        seed: 13
    ) > 0,
    "Native miss should latch into loose retry instead of returning no deal."
);

var tierFilterShop = Shop(
    spawn: 1,
    nativeProbability: 1,
    weights: new Dictionary<ETier, double> { [ETier.Diamond] = 1 },
    tierFilters: new[] { ETier.Silver }
);
Check.True(
    DealerProbabilityCore.AppearanceFrequency(
        t1,
        tierFilterShop,
        Player(day: 3),
        new[]
        {
            Candidate(t1, ETier.Bronze),
            Candidate(t2, ETier.Silver),
            Candidate(t3, ETier.Gold),
        },
        trials: 200,
        seed: 14
    ) > 0,
    "ItemTierFilters should disable native and allow loose cards at or below the filter tier."
);

var legendaryLooseDeal = DealerProbabilityCore.SimulateOneDeal(
    Shop(
        spawn: 1,
        nativeProbability: 0,
        weights: new Dictionary<ETier, double> { [ETier.Diamond] = 1 }
    ),
    Player(day: 8),
    new[] { Candidate(t1, ETier.Diamond), Candidate(legendaryStartId, ETier.Legendary) },
    new ScriptedRng(doubles: new[] { 0.5, 0.5 }, ints: new[] { 1 })
);
Check.Values(
    new[] { legendaryStartId },
    legendaryLooseDeal.ToArray(),
    "Loose Diamond rolls should treat Legendary-start cards as Diamond-reachable."
);

var rerollDeal = DealerProbabilityCore.SimulateOneDeal(
    Shop(spawn: 2, rerollRepeats: false),
    Player(day: 3, rerollExclusions: new[] { t1, t2 }),
    new[] { Candidate(t1, ETier.Bronze), Candidate(t2, ETier.Silver) },
    new SeededRng(15)
);
Check.True(rerollDeal.Contains(t1), "Reroll exclusion should clear when it would starve spawn.");
Check.Equal(2, rerollDeal.Count, "Reroll starvation fallback should still deal spawn count.");

Check.Equal(
    0,
    DealerProbabilityCore.AppearanceFrequency(
        t1,
        Shop(spawn: 1, nativeProbability: 0),
        Player(day: 3, skills: new[] { t1 }),
        new[] { Candidate(t1, ETier.Bronze), Candidate(t2, ETier.Bronze) },
        trials: 200,
        seed: 16
    ),
    "Player skill-equipped cards should never be dealt."
);
Check.Equal(
    0,
    DealerProbabilityCore
        .SimulateOneDeal(
            Shop(spawn: 1),
            Player(day: 3),
            new[] { Candidate(t1, ETier.Bronze, ECardType.Skill) },
            new SeededRng(17)
        )
        .Count,
    "Pure skill pools should return empty because trainer simulation is out of scope."
);
Check.Equal(
    0,
    DealerProbabilityCore
        .SimulateOneDeal(
            Shop(spawn: 2, filters: new[] { t1 }),
            Player(day: 3),
            new[]
            {
                Candidate(t1, ETier.Bronze, ECardType.Skill),
                Candidate(t2, ETier.Bronze, ECardType.Item),
            },
            new SeededRng(171)
        )
        .Count,
    "Pure skill boundary should apply after CardIdFilters narrow a mixed pool."
);

var nativeGateBeforeTierDeal = DealerProbabilityCore.SimulateOneDeal(
    Shop(
        spawn: 1,
        nativeProbability: 0.5f,
        weights: new Dictionary<ETier, double> { [ETier.Gold] = 1 }
    ),
    Player(day: 3),
    new[] { Candidate(t1, ETier.Bronze), Candidate(t2, ETier.Gold) },
    new ScriptedRng(doubles: new[] { 0.2, 0.75 }, ints: new[] { 0 })
);
Check.Values(
    new[] { t2 },
    nativeGateBeforeTierDeal.ToArray(),
    "Native gate should consume RNG before tier selection each slot."
);

var probabilityOrderedTierDeal = DealerProbabilityCore.SimulateOneDeal(
    Shop(
        spawn: 1,
        nativeProbability: 0,
        weights: new Dictionary<ETier, double> { [ETier.Bronze] = 0.9, [ETier.Gold] = 0.1 }
    ),
    Player(day: 3),
    new[] { Candidate(t1, ETier.Bronze), Candidate(t2, ETier.Gold) },
    new ScriptedRng(doubles: new[] { 0.9, 0.05 }, ints: new[] { 1 })
);
Check.Values(
    new[] { t2 },
    probabilityOrderedTierDeal.ToArray(),
    "Tier selection should accumulate weights ordered by probability value."
);

var normalizedTierDeal = DealerProbabilityCore.SimulateOneDeal(
    Shop(
        spawn: 1,
        nativeProbability: 0,
        weights: new Dictionary<ETier, double> { [ETier.Bronze] = 0.2, [ETier.Gold] = 0.3 }
    ),
    Player(day: 3),
    new[] { Candidate(t1, ETier.Bronze), Candidate(t2, ETier.Gold) },
    new ScriptedRng(doubles: new[] { 0.9, 0.9 }, ints: new[] { 1 })
);
Check.Values(
    new[] { t2 },
    normalizedTierDeal.ToArray(),
    "Tier selection should normalize rounded weights before accumulating."
);

var nonPositiveWeightsDeal = DealerProbabilityCore.SimulateOneDeal(
    Shop(
        spawn: 1,
        nativeProbability: 0,
        weights: new Dictionary<ETier, double> { [ETier.Bronze] = 0, [ETier.Gold] = -0.1 }
    ),
    Player(day: 3),
    new[] { Candidate(t1, ETier.Bronze), Candidate(t2, ETier.Gold) },
    new ScriptedRng(doubles: new[] { 0.9, 0.9 }, ints: new[] { 0 })
);
Check.Values(
    new[] { t1 },
    nonPositiveWeightsDeal.ToArray(),
    "Tier selection should fall back deterministically when weights are non-positive."
);

var deterministicShop = Shop(
    spawn: 1,
    nativeProbability: 0,
    weights: DealerTierWeightReference.ForDay(3)
);
var deterministicPool = new[] { Candidate(t1, ETier.Bronze), Candidate(t2, ETier.Silver) };
var deterministicRngA = new SeededRng(18);
var sequenceA = Enumerable
    .Range(0, 8)
    .Select(_ =>
        DealerProbabilityCore.SimulateOneDeal(
            deterministicShop,
            Player(day: 3),
            deterministicPool,
            deterministicRngA
        )[0]
    )
    .ToArray();
var deterministicRngB = new SeededRng(18);
var sequenceB = Enumerable
    .Range(0, 8)
    .Select(_ =>
        DealerProbabilityCore.SimulateOneDeal(
            deterministicShop,
            Player(day: 3),
            deterministicPool,
            deterministicRngB
        )[0]
    )
    .ToArray();
Check.Values(sequenceA, sequenceB, "Same seed should produce identical simulated deals.");
var bronzeFrequency = DealerProbabilityCore.AppearanceFrequency(
    t1,
    deterministicShop,
    Player(day: 3),
    deterministicPool,
    trials: 4000,
    seed: 19
);
var silverFrequency = DealerProbabilityCore.AppearanceFrequency(
    t2,
    deterministicShop,
    Player(day: 3),
    deterministicPool,
    trials: 4000,
    seed: 19
);
Check.True(
    bronzeFrequency > silverFrequency,
    "All-loose day 3 Bronze/Silver pool should favor Bronze over Silver."
);

Check.Section("resolver estimates");
var estimateHint = new DealerShopHint { NumberCardsToSpawn = 1, Verified = true };
var estimateContext = MerchantContext(day: 3, estimateEnabled: true, hint: estimateHint);
var estimateCards = new[] { Card(bronzeId, ETier.Bronze), Card(goldId, ETier.Silver) };
var estimateExplains = CollectionDealerExplainResolver.Resolve(estimateContext, estimateCards);
Check.Equal(
    CollectionDealerProbabilityState.Estimate,
    estimateExplains[bronzeId].State,
    "Verified merchant hints should put non-fixed estimate-enabled cards in Estimate state."
);
Check.True(
    estimateExplains[bronzeId].Estimate is null,
    "Resolve should defer Monte Carlo bucket construction."
);
var estimateBucket = CollectionDealerExplainResolver.EstimateForCard(
    estimateContext,
    bronzeId,
    estimateCards
);
Check.True(estimateBucket is not null, "EstimateForCard should return a deferred bucket.");
Check.True(estimateBucket!.ReferenceOnly, "Deferred estimate bucket should remain reference-only.");
Check.Equal(
    EstimateAuthority.Reference,
    estimateBucket.Authority,
    "Deferred estimate bucket should carry reference authority."
);

var nonFixedFilterHint = new DealerShopHint
{
    NumberCardsToSpawn = 1,
    CardIdFilters = new[] { bronzeId, silverId },
    Verified = true,
};
var nonFixedFilterContext = MerchantContext(
    day: 3,
    estimateEnabled: true,
    hint: nonFixedFilterHint
);
var nonFixedFilterCards = new[]
{
    Card(bronzeId, ETier.Bronze),
    Card(silverId, ETier.Silver),
    Card(goldId, ETier.Gold),
};
var nonFixedFilterExplains = CollectionDealerExplainResolver.Resolve(
    nonFixedFilterContext,
    nonFixedFilterCards
);
Check.Equal(
    CollectionDealerProbabilityState.Estimate,
    nonFixedFilterExplains[bronzeId].State,
    "Non-fixed CardIdFilters should keep matching cards in Estimate state."
);
Check.Equal(
    CollectionDealerProbabilityState.NotInPool,
    nonFixedFilterExplains[goldId].State,
    "Non-fixed CardIdFilters should mark outside cards as NotInPool."
);
Check.True(
    !nonFixedFilterExplains[goldId].InPool,
    "Cards outside a non-fixed CardIdFilters list should not be in pool."
);
Check.True(
    CollectionDealerExplainResolver.EstimateForCard(
        nonFixedFilterContext,
        goldId,
        nonFixedFilterCards
    )
        is null,
    "Cards outside a non-fixed CardIdFilters list should not produce estimate buckets."
);

var day5GoldEstimateContext = MerchantContext(day: 5, estimateEnabled: true, hint: estimateHint);
var day5GoldExplains = CollectionDealerExplainResolver.Resolve(
    day5GoldEstimateContext,
    new[] { Card(goldId, ETier.Gold) }
);
Check.True(
    day5GoldExplains[goldId].DayGatePass,
    "Verified estimate eligibility should use day 5 reference weights that include Gold."
);
Check.True(
    day5GoldExplains[goldId].LooseEligible,
    "Verified estimate loose eligibility should allow Gold on day 5 reference weights."
);
Check.True(
    day5GoldExplains[goldId].NativeEligible,
    "Verified estimate native eligibility should allow Gold on day 5 reference weights."
);

var legendaryEstimateId = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
var day8LegendaryEstimateContext = MerchantContext(
    day: 8,
    estimateEnabled: true,
    hint: estimateHint
);
var day8LegendaryExplains = CollectionDealerExplainResolver.Resolve(
    day8LegendaryEstimateContext,
    new[] { Card(legendaryEstimateId, ETier.Legendary) }
);
Check.True(
    !day8LegendaryExplains[legendaryEstimateId].NativeEligible,
    "Verified estimate native eligibility should not treat Legendary as native."
);

var trainerContext = new CollectionDealerSourceContext
{
    SourceKey = "Trainer",
    Kind = CollectionDealerSourceKind.Trainer,
    Day = 3,
    EstimateEnabled = true,
    Hint = estimateHint,
};
var trainerExplains = CollectionDealerExplainResolver.Resolve(trainerContext, estimateCards);
Check.Equal(
    CollectionDealerProbabilityState.Explain,
    trainerExplains[bronzeId].State,
    "Trainer sources should never enter Estimate state."
);
Check.True(
    CollectionDealerExplainResolver.EstimateForCard(trainerContext, bronzeId, estimateCards)
        is null,
    "Trainer sources should not produce estimate buckets."
);

var unverifiedFixedHint = new DealerShopHint
{
    NumberCardsToSpawn = 1,
    CardIdFilters = new[] { bronzeId },
    Verified = false,
};
var unverifiedFixedContext = MerchantContext(
    day: 3,
    estimateEnabled: true,
    hint: unverifiedFixedHint
);
var unverifiedFixedExplains = CollectionDealerExplainResolver.Resolve(
    unverifiedFixedContext,
    estimateCards
);
Check.Equal(
    CollectionDealerProbabilityState.WeightsMissing,
    unverifiedFixedExplains[bronzeId].State,
    "Unverified fixed hints should stay WeightsMissing rather than Fixed."
);

var verifiedFixedHint = new DealerShopHint
{
    NumberCardsToSpawn = 1,
    CardIdFilters = new[] { bronzeId },
    Verified = true,
};
var verifiedFixedContext = MerchantContext(day: 3, estimateEnabled: true, hint: verifiedFixedHint);
var verifiedFixedExplains = CollectionDealerExplainResolver.Resolve(
    verifiedFixedContext,
    estimateCards
);
Check.Equal(
    CollectionDealerProbabilityState.Fixed,
    verifiedFixedExplains[bronzeId].State,
    "Verified fixed direct deals should enter Fixed state."
);
Check.Equal(
    CollectionDealerProbabilityState.NotInPool,
    verifiedFixedExplains[goldId].State,
    "Verified fixed direct deals should treat cards outside the fixed filter as not produced."
);
Check.True(
    !verifiedFixedExplains[goldId].InPool,
    "Cards outside a verified fixed direct filter should not be in pool."
);
Check.Equal(
    true,
    verifiedFixedExplains[bronzeId].FixedDealVerified,
    "Verified fixed direct deals should carry FixedDealVerified."
);
Check.True(
    CollectionDealerExplainResolver.EstimateForCard(verifiedFixedContext, bronzeId, estimateCards)
        is null,
    "Fixed direct cards should not produce estimate buckets."
);
Check.True(
    CollectionDealerExplainResolver.EstimateForCard(verifiedFixedContext, goldId, estimateCards)
        is null,
    "Cards outside a verified fixed direct filter should not produce estimate buckets."
);

Check.Finish();

static CollectionCardVm Card(Guid id, ETier tier, ECardType type = ECardType.Item) =>
    new()
    {
        Id = id,
        Type = type,
        StartingTier = tier,
    };

static CollectionDealerSourceContext MerchantContext(
    int day,
    bool suppressDayGate = false,
    ETier? pinnedTier = null,
    bool estimateEnabled = false,
    DealerShopHint? hint = null
) =>
    new()
    {
        SourceKey = "Goldie",
        Kind = CollectionDealerSourceKind.Merchant,
        Day = day,
        SuppressDayGate = suppressDayGate,
        PinnedTier = pinnedTier,
        EstimateEnabled = estimateEnabled,
        Hint = hint,
    };

static DealerCandidate Candidate(Guid id, ETier tier, ECardType type = ECardType.Item) =>
    new()
    {
        Id = id,
        Type = type,
        Size = ECardSize.Small,
        StartingTier = tier,
    };

static DealerShopDefinition Shop(
    int spawn,
    IReadOnlyList<Guid>? filters = null,
    float nativeProbability = 0.8f,
    IReadOnlyDictionary<ETier, double>? weights = null,
    IReadOnlyList<ETier>? tierFilters = null,
    bool rerollRepeats = true
) =>
    new()
    {
        SourceKey = "Goldie",
        NumberCardsToSpawn = spawn,
        CardIdFilters = filters ?? Array.Empty<Guid>(),
        ItemTierFilters = tierFilters ?? Array.Empty<ETier>(),
        RerollRepeats = rerollRepeats,
        NativeItemTierProbability = nativeProbability,
        TierWeights = weights ?? new Dictionary<ETier, double> { [ETier.Bronze] = 1 },
    };

static DealerPlayerState Player(
    int day,
    IReadOnlyCollection<Guid>? skills = null,
    IReadOnlyCollection<Guid>? rerollExclusions = null
) =>
    new()
    {
        Day = day,
        PlayerSkillCardIds = skills ?? Array.Empty<Guid>(),
        RerollExclusionIds = rerollExclusions ?? Array.Empty<Guid>(),
    };

internal static class Check
{
    private static int _failures;

    public static void Section(string name)
    {
        Console.WriteLine($"== {name} ==");
    }

    public static void True(bool condition, string message)
    {
        if (condition)
        {
            return;
        }

        _failures++;
        Console.Error.WriteLine($"FAIL: {message}");
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            return;
        }

        _failures++;
        Console.Error.WriteLine($"FAIL: {message} Expected={expected} Actual={actual}");
    }

    public static void About(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) <= tolerance)
        {
            return;
        }

        _failures++;
        Console.Error.WriteLine(
            $"FAIL: {message} Expected~={expected} Actual={actual} Tolerance={tolerance}"
        );
    }

    public static void Values<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
    {
        if (expected.Count == actual.Count && expected.SequenceEqual(actual))
        {
            return;
        }

        _failures++;
        Console.Error.WriteLine(
            $"FAIL: {message} Expected=[{string.Join(", ", expected)}] Actual=[{string.Join(", ", actual)}]"
        );
    }

    public static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            _failures++;
            Console.Error.WriteLine(
                $"FAIL: {message} ExpectedException={typeof(TException).Name} ActualException={ex.GetType().Name}"
            );
            return;
        }

        _failures++;
        Console.Error.WriteLine(
            $"FAIL: {message} ExpectedException={typeof(TException).Name} ActualException=<none>"
        );
    }

    public static void Finish()
    {
        if (_failures == 0)
        {
            Console.WriteLine("All checks passed.");
            return;
        }

        Console.Error.WriteLine($"{_failures} check(s) failed.");
        Environment.Exit(1);
    }
}

internal sealed class ScriptedRng : IRng
{
    private readonly Queue<double> _doubles;
    private readonly Queue<int> _ints;

    public ScriptedRng(IEnumerable<double> doubles, IEnumerable<int> ints)
    {
        _doubles = new Queue<double>(doubles);
        _ints = new Queue<int>(ints);
    }

    public double NextDouble()
    {
        if (_doubles.Count == 0)
        {
            throw new InvalidOperationException("No scripted doubles remain.");
        }

        return _doubles.Dequeue();
    }

    public int NextInt(int exclusiveMax)
    {
        if (_ints.Count == 0)
        {
            throw new InvalidOperationException("No scripted ints remain.");
        }

        var next = _ints.Dequeue();
        if (next < 0 || next >= exclusiveMax)
        {
            throw new InvalidOperationException(
                $"Scripted int {next} is outside [0, {exclusiveMax})."
            );
        }

        return next;
    }
}
