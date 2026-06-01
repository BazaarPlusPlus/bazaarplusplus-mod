#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterSampler
{
    internal static IReadOnlyList<BPPSupporterSample> SampleMany(
        IReadOnlyList<BPPSupporterEntry> entries,
        int count,
        int startIndex,
        int shuffleSeed
    )
    {
        if (entries == null || count <= 0)
            return Array.Empty<BPPSupporterSample>();

        var shuffled = BuildShuffledBag(entries, shuffleSeed);
        if (shuffled.Count == 0)
            return Array.Empty<BPPSupporterSample>();

        var samples = new List<BPPSupporterSample>(Math.Min(count, shuffled.Count));
        var cursor = PositiveModulo(startIndex, shuffled.Count);
        while (samples.Count < count && samples.Count < shuffled.Count)
        {
            var entry = shuffled[cursor];
            samples.Add(new BPPSupporterSample { Name = entry.Name.Trim(), Tier = entry.Tier });
            cursor = (cursor + 1) % shuffled.Count;
        }

        return samples;
    }

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

    private static IReadOnlyList<BPPSupporterEntry> BuildShuffledBag(
        IReadOnlyList<BPPSupporterEntry> entries,
        int shuffleSeed
    )
    {
        var unique = new Dictionary<string, BPPSupporterEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!IsRenderable(entry))
                continue;

            var name = entry.Name.Trim();
            if (!unique.TryGetValue(name, out var existing) || entry.Tier > existing.Tier)
                unique[name] = new BPPSupporterEntry { Name = name, Tier = entry.Tier };
        }

        return unique
            .Values.OrderBy(entry => StableHash(entry.Name, entry.Tier, shuffleSeed))
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int PositiveModulo(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static uint StableHash(string name, int tier, int seed)
    {
        unchecked
        {
            var hash = 2166136261u;
            hash = Mix(hash, (uint)seed);
            hash = Mix(hash, (uint)tier);
            foreach (var ch in name)
                hash = Mix(hash, char.ToUpperInvariant(ch));
            return hash;
        }
    }

    private static uint Mix(uint hash, uint value)
    {
        unchecked
        {
            hash ^= value;
            return hash * 16777619u;
        }
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
