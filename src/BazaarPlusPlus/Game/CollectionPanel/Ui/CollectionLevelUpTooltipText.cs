#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
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
// the max-health gain plus the rewards the player can actually receive, resolved from
// the level-up spawn groups. Weighted single-reward groups are random alternatives, so
// they collapse into one "One of: A / B" sentence instead of one line each; random
// pools become a count summary. Board-conditional bonus groups (e.g. "Inspired by ..."
// skills gated on specific board cards) are omitted.
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

        // Weight-0 groups spawn deterministically (own line each); weighted groups are
        // random alternatives and collapse into one "One of:" block.
        var certainRewards = new List<string>();
        var alternativeRewards = new List<string>();
        var poolLines = new List<string>();
        if (levelUp.Rewards is TSpawnContextQuery query)
        {
            foreach (var group in query.Groups)
                CollectGroup(
                    group.RandomWeight == 0 ? certainRewards : alternativeRewards,
                    poolLines,
                    group,
                    resolveTemplate,
                    currentHero,
                    colorize
                );
        }

        lines.AddRange(certainRewards);
        if (alternativeRewards.Count == 1)
        {
            lines.Add(alternativeRewards[0]);
        }
        else if (alternativeRewards.Count > 1)
        {
            // One alternative per bulleted line under a shared header, as a single
            // block so the inter-line spacer stays between blocks only.
            var block = new StringBuilder(CollectionPanelText.LevelUpOneOf());
            foreach (var reward in alternativeRewards)
                block.Append('\n').Append("· ").Append(reward);
            lines.Add(block.ToString());
        }
        lines.AddRange(poolLines);

        return lines.Count == 0 ? string.Empty : string.Join("\n<size=45%> </size>\n", lines);
    }

    private static void CollectGroup(
        List<string> singleRewards,
        List<string> poolLines,
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

        // Spawn filtering also honours each reward card's own Heroes field (the group's
        // run prerequisite alone is coarser: e.g. "Core Initialization" sits in a
        // Dooley-or-Jules group but the card itself is Dooley-only). Unresolvable
        // templates cannot be judged and stay counted.
        var eligible = new List<TCardBase>();
        var unresolved = 0;
        foreach (var id in ids)
        {
            var template = resolveTemplate(id);
            if (template == null)
                unresolved++;
            else if (CollectionEncounterHeroEligibility.Matches(template.Heroes, currentHero))
                eligible.Add(template);
        }

        if (ids.Count == 1)
        {
            if (eligible.Count == 0)
                return;

            var template = eligible[0];
            var title = CollectionLocalizationResolver.ResolveTitle(template)
                ?? template.InternalName;
            var description = CollectionLocalizationResolver.ResolveDescription(template);
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
                return;

            if (string.IsNullOrWhiteSpace(description))
                singleRewards.Add($"<color={AccentColor}>{title}</color>");
            else if (string.IsNullOrWhiteSpace(title))
                singleRewards.Add(colorize(description!));
            else
                singleRewards.Add(
                    $"<color={AccentColor}>{title}:</color> {colorize(description!)}"
                );
            return;
        }

        var optionCount = eligible.Count + unresolved;
        if (optionCount == 0)
            return;

        var limit = group.Limit is TFixedValue fixedValue ? (int)fixedValue.Value : 1;
        poolLines.Add(
            CollectionPanelText.LevelUpRandomPool(Math.Min(limit, optionCount), optionCount)
        );
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
