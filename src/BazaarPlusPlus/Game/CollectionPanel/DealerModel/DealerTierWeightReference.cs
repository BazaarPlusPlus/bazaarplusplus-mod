#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal static class DealerTierWeightReference
{
    public const string SourceLabel = "thebazaar.wiki.gg 0.1.9";
    public const string RecordedAgainstGameVersion = "0.1.9";

    private static readonly IReadOnlyDictionary<ETier, double>[] Weights =
    {
        new Dictionary<ETier, double> { [ETier.Bronze] = 1.00 },
        new Dictionary<ETier, double> { [ETier.Bronze] = 0.91, [ETier.Silver] = 0.09 },
        new Dictionary<ETier, double> { [ETier.Bronze] = 0.61, [ETier.Silver] = 0.39 },
        new Dictionary<ETier, double> { [ETier.Bronze] = 0.36, [ETier.Silver] = 0.64 },
        new Dictionary<ETier, double>
        {
            [ETier.Bronze] = 0.19,
            [ETier.Silver] = 0.70,
            [ETier.Gold] = 0.12,
        },
        new Dictionary<ETier, double>
        {
            [ETier.Bronze] = 0.09,
            [ETier.Silver] = 0.73,
            [ETier.Gold] = 0.18,
        },
        new Dictionary<ETier, double> { [ETier.Silver] = 0.81, [ETier.Gold] = 0.19 },
        new Dictionary<ETier, double>
        {
            [ETier.Silver] = 0.60,
            [ETier.Gold] = 0.32,
            [ETier.Diamond] = 0.08,
        },
        new Dictionary<ETier, double>
        {
            [ETier.Silver] = 0.46,
            [ETier.Gold] = 0.39,
            [ETier.Diamond] = 0.15,
        },
        new Dictionary<ETier, double>
        {
            [ETier.Silver] = 0.40,
            [ETier.Gold] = 0.45,
            [ETier.Diamond] = 0.15,
        },
    };

    public static IReadOnlyDictionary<ETier, double> ForDay(int day)
    {
        var clampedDay = Math.Max(1, Math.Min(10, day));
        return Weights[clampedDay - 1];
    }
}
