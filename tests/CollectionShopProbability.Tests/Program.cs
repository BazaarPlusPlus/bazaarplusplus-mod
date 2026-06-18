using BazaarPlusPlus.Game.CollectionPanel.DealerModel;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

Check.Section("state enum");
Check.Equal(
    0,
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
Check.About(
    1.0,
    day3Weights.Values.Sum(),
    0.05,
    "Day 3 weights should sum to roughly 1."
);

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
Check.True(!day3Explains.ContainsKey(Guid.Empty), "Resolver should not synthesize placeholder ids.");

Check.Section("seeded rng");
IRng firstRng = new SeededRng(20260615);
IRng secondRng = new SeededRng(20260615);
Check.Equal(firstRng.NextDouble(), secondRng.NextDouble(), "Same seed should match first double.");
Check.Equal(firstRng.NextDouble(), secondRng.NextDouble(), "Same seed should match second double.");
Check.Equal(firstRng.NextInt(7), secondRng.NextInt(7), "Same seed should match bounded int.");
var bounded = new SeededRng(9).NextInt(3);
Check.True(bounded >= 0 && bounded < 3, "NextInt should respect exclusive upper bound.");

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
    bool estimateEnabled = false
) =>
    new()
    {
        SourceKey = "Goldie",
        Kind = CollectionDealerSourceKind.Merchant,
        Day = day,
        SuppressDayGate = suppressDayGate,
        PinnedTier = pinnedTier,
        EstimateEnabled = estimateEnabled,
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
