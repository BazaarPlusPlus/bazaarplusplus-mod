#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Game;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Spawning.SpawnFilters;
using BazaarGameShared.Domain.Spawning.SpawnGroups;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// Content for the BPP section appended below the native "next level rewards" tooltip:
// the max-health gain plus one line per reward the player can actually receive,
// resolved from the level-up spawn groups (single reward -> its card title and
// description; random pools -> a count summary). Board-conditional bonus groups
// (e.g. "Inspired by ..." skills gated on specific board cards) are omitted.
internal static class CollectionLevelUpTooltipText
{
    private const string AccentColor = "#FFD37E";

    public static string Build(
        TLevelUp? levelUp,
        Func<Guid, TCardBase?> resolveTemplate,
        EHero? currentHero,
        Func<string, string>? colorizeResult = null
    )
    {
        if (levelUp == null)
            return string.Empty;

        var colorize = colorizeResult ?? (text => text);
        var lines = new List<string>();
        if (levelUp.HealthIncrease > 0)
            lines.Add(colorize(CollectionPanelText.LevelUpMaxHealth((int)levelUp.HealthIncrease)));

        if (levelUp.Rewards is TSpawnContextQuery query)
        {
            foreach (var group in query.Groups)
                AppendGroup(lines, group, resolveTemplate, currentHero, colorize);
        }

        return lines.Count == 0 ? string.Empty : string.Join("\n<size=45%> </size>\n", lines);
    }

    private static void AppendGroup(
        List<string> lines,
        TSpawnGroup group,
        Func<Guid, TCardBase?> resolveTemplate,
        EHero? currentHero,
        Func<string, string> colorize
    )
    {
        if (!PassesPrerequisites(group, currentHero))
            return;

        var ids = new List<Guid>();
        foreach (var filter in group.Filters)
            if (filter is TSpawnFilterIdList idList)
                ids.AddRange(idList.Ids);

        if (ids.Count == 0)
            return;

        if (ids.Count == 1)
        {
            var template = resolveTemplate(ids[0]);
            if (template == null)
                return;

            var title = CollectionLocalizationResolver.ResolveTitle(template)
                ?? template.InternalName;
            var description = CollectionLocalizationResolver.ResolveDescription(template);
            lines.Add(
                string.IsNullOrWhiteSpace(description)
                    ? $"<color={AccentColor}>{title}</color>"
                    : $"<color={AccentColor}>{title}:</color> {colorize(description!)}"
            );
            return;
        }

        var limit = group.Limit is TFixedValue fixedValue ? (int)fixedValue.Value : 1;
        lines.Add(CollectionPanelText.LevelUpRandomPool(limit, ids.Count));
    }

    // Only hero conditions are evaluated; groups gated on board state ("Inspired by"
    // bonus skills) are omitted, and unknown run conditions — including an unknown
    // current hero — keep the group visible rather than silently dropping rewards.
    private static bool PassesPrerequisites(TSpawnGroup group, EHero? currentHero)
    {
        if (group.Prerequisites == null)
            return true;

        foreach (var prerequisite in group.Prerequisites)
        {
            switch (prerequisite)
            {
                case TPrerequisiteCardCount:
                    return false;
                case TPrerequisiteRun { Conditions: TRunConditionalPlayerHero heroCondition }:
                    if (!PassesHeroCondition(heroCondition, currentHero))
                        return false;
                    break;
            }
        }

        return true;
    }

    private static bool PassesHeroCondition(
        TRunConditionalPlayerHero condition,
        EHero? currentHero
    )
    {
        // Hero detection failed: keep hero-gated groups visible (consistent with
        // CollectionEncounterHeroEligibility) instead of hiding them all.
        if (currentHero == null)
            return true;

        var contains = condition.Heroes.Contains(currentHero.Value);
        return condition.Operator switch
        {
            EListComparisonOperator.None => !contains,
            _ => contains,
        };
    }
}
