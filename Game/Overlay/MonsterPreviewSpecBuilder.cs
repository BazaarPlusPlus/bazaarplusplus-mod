#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus;

internal static class MonsterPreviewSpecBuilder
{
    public static List<PreviewCardSpec> Build(MonsterInfo monster)
    {
        var specs = new List<PreviewCardSpec>();
        if (monster?.BoardCards == null)
            return specs;

        foreach (var card in monster.BoardCards)
        {
            if (card == null || card.CardId == Guid.Empty)
                continue;

            specs.Add(
                new PreviewCardSpec
                {
                    TemplateId = card.CardId.ToString(),
                    Tier = ParseTier(card.Tier),
                    Size = ParseSize(card.Size),
                    Enchant = "None",
                    Attributes = BuildAttributes(card.CardId, card.Tier),
                }
            );
        }

        return specs;
    }

    private static int ParseTier(string tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
            return 0;

        switch (tier.Trim().ToLowerInvariant())
        {
            case "bronze":
                return 0;
            case "silver":
                return 1;
            case "gold":
                return 2;
            case "diamond":
                return 3;
            case "legendary":
                return 4;
            default:
                return 0;
        }
    }

    private static int ParseSize(string size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return 1;

        switch (size.Trim().ToLowerInvariant())
        {
            case "small":
                return 1;
            case "medium":
                return 2;
            case "large":
                return 3;
            default:
                return 1;
        }
    }

    private static Dictionary<int, int> BuildAttributes(Guid templateId, string tier)
    {
        var result = new Dictionary<int, int>();
        foreach (var pair in ItemAttr.GetAttributes(templateId, tier))
        {
            if (!Enum.TryParse<ECardAttributeType>(pair.Key, out var attributeType))
                continue;

            result[(int)attributeType] = pair.Value;
        }

        return result;
    }
}
