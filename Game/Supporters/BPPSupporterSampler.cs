#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterSampler
{
    internal static BPPSupporterSample Sample(
        IReadOnlyList<BPPSupporterEntry> entries,
        Func<float> randomValue
    )
    {
        if (entries == null || entries.Count == 0)
            return NoSample();

        var buckets = entries
            .Where(IsRenderable)
            .GroupBy(entry => entry.Tier)
            .Select(group => new TierBucket { Tier = group.Key, Entries = group.ToList() })
            .Where(bucket => bucket.Entries.Count > 0)
            .ToList();
        if (buckets.Count == 0)
            return NoSample();

        var selectedBucket = PickWeighted(
            buckets,
            bucket => ResolveTierWeight(bucket.Tier),
            randomValue
        );
        if (selectedBucket == null)
            return NoSample();

        var selectedEntry = PickWeighted(selectedBucket.Entries, _ => 1f, randomValue);
        if (selectedEntry == null)
            return NoSample();

        return new BPPSupporterSample
        {
            Name = selectedEntry.Name.Trim(),
            Tier = selectedEntry.Tier,
        };
    }

    private static BPPSupporterSample NoSample()
    {
        return new BPPSupporterSample { Name = string.Empty, Tier = 0 };
    }

    private static bool IsRenderable(BPPSupporterEntry? entry)
    {
        return entry != null && !string.IsNullOrWhiteSpace(entry.Name) && entry.Tier > 0;
    }

    private static int ResolveTierWeight(int tier)
    {
        return tier switch
        {
            4 => 6,
            3 => 3,
            2 => 2,
            _ => 1,
        };
    }

    private static T? PickWeighted<T>(
        IReadOnlyList<T> items,
        Func<T, float> weightSelector,
        Func<float> randomValue
    )
        where T : class
    {
        if (items == null || items.Count == 0)
            return null;

        var totalWeight = 0f;
        for (var i = 0; i < items.Count; i++)
            totalWeight += Math.Max(0f, weightSelector(items[i]));

        if (totalWeight <= 0f)
            return null;

        var roll = randomValue() * totalWeight;
        for (var i = 0; i < items.Count; i++)
        {
            roll -= Math.Max(0f, weightSelector(items[i]));
            if (roll <= 0f)
                return items[i];
        }

        return items[items.Count - 1];
    }

    private sealed class TierBucket
    {
        public int Tier { get; init; }

        public IReadOnlyList<BPPSupporterEntry> Entries { get; init; } =
            Array.Empty<BPPSupporterEntry>();
    }
}
