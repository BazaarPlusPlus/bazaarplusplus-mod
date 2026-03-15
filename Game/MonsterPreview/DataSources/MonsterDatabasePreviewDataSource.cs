#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterDatabasePreviewDataSource : IPreviewDataSource
{
    private readonly string _encounterId;

    public MonsterDatabasePreviewDataSource(string encounterId)
    {
        _encounterId = encounterId ?? string.Empty;
    }

    public bool TryBuild(out PreviewBoardModel model)
    {
        model = null;
        if (!MonsterDatabase.TryGetByEncounterId(_encounterId, out var monster))
            return false;

        model = BuildModel(monster, "monster_db");
        return model.ItemCards.Count > 0 || model.SkillCards.Count > 0;
    }

    public static PreviewBoardModel BuildModel(MonsterInfo monster, string source)
    {
        var itemCards = BuildItemCards(monster);
        var skillCards = BuildSkillCards(monster);
        var model = new PreviewBoardModel
        {
            Title = monster?.Title ?? string.Empty,
            ItemCards = itemCards,
            SkillCards = skillCards,
            Metadata = new Dictionary<string, string>
            {
                ["source"] = source ?? "monster_db",
                ["encounter"] = monster?.EncounterShortId ?? string.Empty,
                ["health"] = monster?.Health?.ToString() ?? string.Empty,
                ["reward_gold"] = monster?.RewardGold?.ToString() ?? string.Empty,
                ["reward_xp"] = monster?.RewardXp?.ToString() ?? string.Empty,
            },
        };
        model.Signature = PreviewBoardSignature.Build(model);
        return model;
    }

    private static List<PreviewCardSpec> BuildItemCards(MonsterInfo monster)
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

    private static List<PreviewCardSpec> BuildSkillCards(MonsterInfo monster)
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
