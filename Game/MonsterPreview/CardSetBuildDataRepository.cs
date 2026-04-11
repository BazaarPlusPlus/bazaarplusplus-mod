#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.ItemBoard;
using BazaarPlusPlus.Game.Settings;
using Newtonsoft.Json;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class CardSetBuildDataRepository
{
    private const string FinalBuildsResourceSuffix = "final-builds-top50.json";
    private const string WinnerBuildsResourceSuffix = "winner-builds-top50.json";
    private static readonly LocalizedTextSet FinalBuildLabel = new(
        "Ten-Win Build",
        "十胜阵容",
        "十勝陣容",
        "十勝陣容"
    );
    private static readonly LocalizedTextSet WinnerDayLabel = new(
        "Winner Day",
        "胜场第",
        "勝場第",
        "勝場第"
    );
    private static readonly object SyncRoot = new();
    private static FinalBuildRoot? _finalRoot;
    private static WinnerBuildRoot? _winnerRoot;
    private static bool _attemptedLoad;

    public bool TryFindFinalRecommendation(
        string? hero,
        IReadOnlyCollection<Guid> templateIds,
        out CardSetBuildRecommendation recommendation
    )
    {
        var recommendations = FindFinalRecommendations(hero, templateIds);
        recommendation = recommendations.FirstOrDefault()!;
        return recommendation != null;
    }

    public bool TryFindWinnerRecommendation(
        string? hero,
        int? day,
        IReadOnlyCollection<Guid> templateIds,
        out CardSetBuildRecommendation recommendation
    )
    {
        var recommendations = FindWinnerRecommendations(hero, day, templateIds);
        recommendation = recommendations.FirstOrDefault()!;
        return recommendation != null;
    }

    public IReadOnlyList<CardSetBuildRecommendation> FindFinalRecommendations(
        string? hero,
        IReadOnlyCollection<Guid> templateIds
    )
    {
        var finalRoot = EnsureFinalRoot();
        if (finalRoot?.Heroes == null || string.IsNullOrWhiteSpace(hero))
            return Array.Empty<CardSetBuildRecommendation>();

        return finalRoot.Heroes.TryGetValue(hero, out var heroBucket)
            ? FindRecommendations(heroBucket, templateIds, ResolveFinalBuildLabel())
            : Array.Empty<CardSetBuildRecommendation>();
    }

    public IReadOnlyList<CardSetBuildRecommendation> FindWinnerRecommendations(
        string? hero,
        int? day,
        IReadOnlyCollection<Guid> templateIds
    )
    {
        var root = EnsureWinnerRoot();
        if (
            root?.Heroes == null
            || string.IsNullOrWhiteSpace(hero)
            || !day.HasValue
            || day.Value <= 0
        )
        {
            return Array.Empty<CardSetBuildRecommendation>();
        }

        if (
            !root.Heroes.TryGetValue(hero, out var heroBucket)
            || heroBucket?.Days == null
            || !heroBucket.Days.TryGetValue(day.Value.ToString(), out var dayBucket)
        )
        {
            return Array.Empty<CardSetBuildRecommendation>();
        }

        return FindRecommendations(dayBucket, templateIds, ResolveWinnerDayLabel(day.Value));
    }

    private static FinalBuildRoot? EnsureFinalRoot()
    {
        EnsureLoaded();
        return _finalRoot;
    }

    private static WinnerBuildRoot? EnsureWinnerRoot()
    {
        EnsureLoaded();
        return _winnerRoot;
    }

    private static void EnsureLoaded()
    {
        lock (SyncRoot)
        {
            if (_attemptedLoad)
                return;

            _attemptedLoad = true;
            _finalRoot = LoadEmbeddedJson<FinalBuildRoot>(FinalBuildsResourceSuffix);
            _winnerRoot = LoadEmbeddedJson<WinnerBuildRoot>(WinnerBuildsResourceSuffix);
        }
    }

    private static T? LoadEmbeddedJson<T>(string resourceSuffix)
        where T : class
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name =>
                    name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase)
                );
            if (resourceName == null)
            {
                BppLog.Warn(
                    "CardSetBuildDataRepository",
                    $"Embedded resource not found suffix={resourceSuffix}"
                );
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return null;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var parsed = JsonConvert.DeserializeObject<T>(json);
            BppLog.Info(
                "CardSetBuildDataRepository",
                $"Loaded embedded build data resource={resourceName} parsed={(parsed != null)}"
            );
            return parsed;
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "CardSetBuildDataRepository",
                $"Failed to load embedded build data suffix={resourceSuffix}",
                ex
            );
            return null;
        }
    }

    private static IReadOnlyList<CardSetBuildRecommendation> FindRecommendations(
        BuildQueryBucket? bucket,
        IReadOnlyCollection<Guid> templateIds,
        string modeLabel
    )
    {
        if (bucket?.Builds == null || templateIds == null || templateIds.Count == 0)
            return Array.Empty<CardSetBuildRecommendation>();

        var selectedCardIds = templateIds
            .Where(id => id != Guid.Empty)
            .Select(id => id.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (selectedCardIds.Length == 0)
            return Array.Empty<CardSetBuildRecommendation>();

        IReadOnlyList<int>? matchedBuildIds = null;
        var preserveIncomingOrder = false;
        if (
            selectedCardIds.Length <= 3
            && bucket.SubsetIndex != null
            && bucket.SubsetIndex.TryGetValue(string.Join("|", selectedCardIds), out var subsetEntry)
            && subsetEntry?.MatchedBuildIds?.Count > 0
        )
        {
            matchedBuildIds = subsetEntry.MatchedBuildIds;
            preserveIncomingOrder = true;
        }
        else if (bucket.CardIndex != null)
        {
            HashSet<int>? intersection = null;
            foreach (var cardId in selectedCardIds)
            {
                if (!bucket.CardIndex.TryGetValue(cardId, out var buildIds) || buildIds.Count == 0)
                    return Array.Empty<CardSetBuildRecommendation>();

                if (intersection == null)
                {
                    intersection = new HashSet<int>(buildIds);
                    continue;
                }

                intersection.IntersectWith(buildIds);
                if (intersection.Count == 0)
                    return Array.Empty<CardSetBuildRecommendation>();
            }

            matchedBuildIds = intersection?
                .OrderBy(id => id)
                .ToArray();
        }

        if (matchedBuildIds == null || matchedBuildIds.Count == 0)
            return Array.Empty<CardSetBuildRecommendation>();

        var candidates = matchedBuildIds
            .Select((buildId, index) => new
            {
                Build = buildId >= 0 && buildId < bucket.Builds.Count ? bucket.Builds[buildId] : null,
                BuildId = buildId,
                OriginalIndex = index,
            })
            .Where(candidate => candidate.Build?.PlayerCards?.Count > 0)
            .ToList();
        if (candidates.Count == 0)
            return Array.Empty<CardSetBuildRecommendation>();

        var orderedCandidates = preserveIncomingOrder
            ? candidates
            : candidates
                .OrderByDescending(candidate => candidate.Build!.GoldScore)
                .ThenBy(candidate => candidate.BuildId)
                .ToList();

        var recommendations = orderedCandidates
            .Select((candidate, index) => new CardSetBuildRecommendation
            {
                ModeLabel = modeLabel,
                SetSignature = candidate.Build!.SetSignature ?? string.Empty,
                GoldScore = candidate.Build.GoldScore,
                ResultIndex = index,
                ResultCount = orderedCandidates.Count,
                Items = ProjectPlayerCards(candidate.Build.PlayerCards),
            })
            .Where(recommendation => recommendation.Items.Count > 0)
            .ToArray();
        return recommendations;
    }

    private static IReadOnlyList<ItemBoardItemSpec> ProjectPlayerCards(
        IReadOnlyList<PlayerCardEntry>? playerCards
    )
    {
        if (playerCards == null || playerCards.Count == 0)
            return Array.Empty<ItemBoardItemSpec>();

        return playerCards
            .Where(entry => entry != null && Guid.TryParse(entry.CardId, out _))
            .OrderBy(entry => entry!.Slot)
            .Select(entry =>
            {
                Enum.TryParse<EEnchantmentType>(entry!.Enchant ?? string.Empty, true, out var enchantType);
                var hasEnchant =
                    !string.IsNullOrWhiteSpace(entry.Enchant)
                    && !string.Equals(entry.Enchant, "None", StringComparison.OrdinalIgnoreCase);
                return new ItemBoardItemSpec
                {
                    TemplateId = Guid.Parse(entry.CardId!),
                    Tier = (ETier)Mathf.Clamp(entry.Tier ?? 0, 0, 5),
                    SocketId = entry.Slot.HasValue
                        ? (EContainerSocketId?)Mathf.Clamp(entry.Slot.Value, 0, 9)
                        : null,
                    EnchantmentType = hasEnchant ? enchantType : null,
                };
            })
            .ToArray();
    }

    private static string ResolveFinalBuildLabel()
    {
        return FinalBuildLabel.Resolve(PlayerPreferences.Data?.LanguageCode ?? string.Empty);
    }

    private static string ResolveWinnerDayLabel(int day)
    {
        var languageCode = PlayerPreferences.Data?.LanguageCode ?? string.Empty;
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return $"{WinnerDayLabel.Resolve(languageCode)} {day} 天";

        return $"{WinnerDayLabel.Resolve(languageCode)} {day}";
    }

    private sealed class FinalBuildRoot
    {
        [JsonProperty("heroes")]
        public Dictionary<string, BuildQueryBucket>? Heroes { get; set; }
    }

    private sealed class WinnerBuildRoot
    {
        [JsonProperty("heroes")]
        public Dictionary<string, WinnerHeroBucket>? Heroes { get; set; }
    }

    private sealed class WinnerHeroBucket
    {
        [JsonProperty("days")]
        public Dictionary<string, BuildQueryBucket>? Days { get; set; }
    }

    private sealed class BuildQueryBucket
    {
        [JsonProperty("builds")]
        public List<BuildRecord> Builds { get; set; } = new();

        [JsonProperty("cardIndex")]
        public Dictionary<string, List<int>>? CardIndex { get; set; }

        [JsonProperty("subsetIndex")]
        public Dictionary<string, SubsetRecord>? SubsetIndex { get; set; }
    }

    private sealed class BuildRecord
    {
        [JsonProperty("setSignature")]
        public string? SetSignature { get; set; }

        [JsonProperty("goldScore")]
        public double GoldScore { get; set; }

        [JsonProperty("playerCards")]
        public List<PlayerCardEntry> PlayerCards { get; set; } = new();
    }

    private sealed class SubsetRecord
    {
        [JsonProperty("matchedBuildIds")]
        public List<int> MatchedBuildIds { get; set; } = new();
    }

    private sealed class PlayerCardEntry
    {
        [JsonProperty("cardId")]
        public string? CardId { get; set; }

        [JsonProperty("slot")]
        public int? Slot { get; set; }

        [JsonProperty("tier")]
        public int? Tier { get; set; }

        [JsonProperty("enchant")]
        public string? Enchant { get; set; }
    }
}
