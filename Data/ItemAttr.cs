#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus;

internal static class ItemAttr
{
    private static readonly string[] TierOrder =
    [
        "Bronze",
        "Silver",
        "Gold",
        "Diamond",
        "Legendary",
    ];
    private static readonly object SyncRoot = new();
    private static IReadOnlyDictionary<Guid, CardAttributes> _cardsByTemplateId =
        new Dictionary<Guid, CardAttributes>();
    private static string? _loadedPath;

    public static IReadOnlyDictionary<string, int> GetAttributes(Guid templateId, string tier)
    {
        if (!EnsureLoaded())
            return new Dictionary<string, int>();

        if (!_cardsByTemplateId.TryGetValue(templateId, out var card))
            return new Dictionary<string, int>();

        var cappedTier = NormalizeTier(tier);
        if (cappedTier == null)
            return new Dictionary<string, int>();

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tierName in TierOrder)
        {
            if (CompareTier(tierName, cappedTier) > 0)
                break;

            if (!card.AttributesByTier.TryGetValue(tierName, out var attributes))
                continue;

            foreach (var pair in attributes)
                result[pair.Key] = pair.Value;
        }

        return result;
    }

    internal static bool Warm()
    {
        return EnsureLoaded();
    }

    internal static void ResetForTests()
    {
        lock (SyncRoot)
        {
            _cardsByTemplateId = new Dictionary<Guid, CardAttributes>();
            _loadedPath = null;
        }
    }

    private static bool EnsureLoaded()
    {
        var path = ModState.CardsJsonPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
            return true;

        lock (SyncRoot)
        {
            if (string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                _cardsByTemplateId = LoadCards(path);
                _loadedPath = path;
                return true;
            }
            catch (Exception ex)
            {
                BppLog.Error("ItemAttr", $"Failed to load card attributes from '{path}'", ex);
                return false;
            }
        }
    }

    private static IReadOnlyDictionary<Guid, CardAttributes> LoadCards(string path)
    {
        var root = JObject.Parse(File.ReadAllText(path));
        var versionNode =
            root["5.0.0"] as JArray ?? root.Properties().FirstOrDefault()?.Value as JArray;
        if (versionNode == null)
            return new Dictionary<Guid, CardAttributes>();

        var result = new Dictionary<Guid, CardAttributes>();
        foreach (var token in versionNode.OfType<JObject>())
        {
            var idText = token.Value<string>("Id");
            if (!Guid.TryParse(idText, out var templateId))
                continue;

            var tiers = new Dictionary<string, IReadOnlyDictionary<string, int>>(
                StringComparer.Ordinal
            );
            var tiersObject = token["Tiers"] as JObject;
            if (tiersObject != null)
            {
                foreach (var property in tiersObject.Properties())
                {
                    var normalizedTier = NormalizeTier(property.Name);
                    if (normalizedTier == null)
                        continue;

                    var attributesObject = property.Value["Attributes"] as JObject;
                    tiers[normalizedTier] = ParseAttributes(attributesObject);
                }
            }

            result[templateId] = new CardAttributes { AttributesByTier = tiers };
        }

        return result;
    }

    private static IReadOnlyDictionary<string, int> ParseAttributes(JObject? attributesObject)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (attributesObject == null)
            return result;

        foreach (var property in attributesObject.Properties())
        {
            if (property.Value.Type != JTokenType.Integer)
                continue;

            result[property.Name] = property.Value.Value<int>();
        }

        return result;
    }

    private static string? NormalizeTier(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
            return null;

        return TierOrder.FirstOrDefault(candidate =>
            string.Equals(candidate, tier.Trim(), StringComparison.OrdinalIgnoreCase)
        );
    }

    private static int CompareTier(string left, string right)
    {
        return Array.IndexOf(TierOrder, left) - Array.IndexOf(TierOrder, right);
    }

    private sealed class CardAttributes
    {
        public IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, int>
        > AttributesByTier { get; set; } =
            new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
    }
}
