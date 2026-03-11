#pragma warning disable CS0436
using System;
using System.Collections.Generic;

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
                    Enchant = "None",
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
}
