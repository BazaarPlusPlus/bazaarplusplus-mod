#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus;

internal static class SkillPreviewSpecBuilder
{
    public static List<PreviewCardSpec> Build(MonsterInfo monster)
    {
        var specs = new List<PreviewCardSpec>();
        if (monster?.Skills == null)
            return specs;

        foreach (var skill in monster.Skills)
        {
            if (skill == null || skill.SkillId == Guid.Empty)
                continue;

            specs.Add(
                new PreviewCardSpec
                {
                    TemplateId = skill.SkillId.ToString(),
                    Tier = ParseTier(skill.Tier),
                    SourceName = skill.Title ?? string.Empty,
                    Size = 1,
                    Enchant = "None",
                    Attributes = BuildAttributes(skill.SkillId, skill.Tier),
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
