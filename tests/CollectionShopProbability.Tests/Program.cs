using BazaarPlusPlus.Game.CollectionPanel.DealerModel;
using BazaarGameShared.Domain.Core.Types;

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

Check.Finish();

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
