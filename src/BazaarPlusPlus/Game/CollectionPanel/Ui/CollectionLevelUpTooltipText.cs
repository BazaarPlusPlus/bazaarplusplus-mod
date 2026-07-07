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

    // Board (carpet) slot unlocks are server-driven and not in TLevelUp; the native
    // tooltip hardcodes the same facts (a static "2" on the carpet icon, grayed once
    // the current level reaches 4), so mirror those constants here.
    private const int BoardSlotsPerLevel = 2;
    private const int LastBoardSlotLevel = 4;

    public static string Build(
        TLevelUp? levelUp,
        Func<Guid, TCardBase?> resolveTemplate,
        EHero? currentHero,
        Func<string, string>? colorizeResult = null,
        int? currentLevel = null
    )
    {
        if (levelUp == null)
            return string.Empty;

        var colorize = colorizeResult ?? (text => text);
        var lines = new List<string>();
        if (levelUp.HealthIncrease > 0)
            lines.Add(colorize(CollectionPanelText.LevelUpMaxHealth((int)levelUp.HealthIncrease)));
        if (currentLevel.HasValue && currentLevel.Value < LastBoardSlotLevel)
            lines.Add(colorize(CollectionPanelText.LevelUpBoardSlots(BoardSlotsPerLevel)));

        // Level-up rewards are a selection screen (LevelUpState allows
        // SelectItem/SelectSkill/SelectEncounter): every spawned group contributes
        // candidates and the player picks one — the native pack icon's "1". Render all
        // candidates as one "Choose one:" list; random pools contribute a count entry.
        var candidates = new List<string>();
        if (levelUp.Rewards is TSpawnContextQuery query)
        {
            foreach (var group in query.Groups)
                CollectGroup(candidates, group, resolveTemplate, currentHero, colorize);
        }

        if (candidates.Count == 1)
        {
            lines.Add(candidates[0]);
        }
        else if (candidates.Count > 1)
        {
            // One candidate per bulleted line under a shared header, as a single
            // block so the inter-line spacer stays between blocks only.
            var block = new StringBuilder(CollectionPanelText.LevelUpOneOf());
            foreach (var candidate in candidates)
            {
                // Hanging indent keeps soft-wrapped candidate lines aligned.
                block
                    .Append(CollectionTooltipMarkup.BulletBreak)
                    .Append("· <indent=1em>")
                    .Append(candidate)
                    .Append("</indent>");
            }
            lines.Add(block.ToString());
        }

        return lines.Count == 0
            ? string.Empty
            : CollectionTooltipMarkup.Wrap(string.Join(CollectionTooltipMarkup.BlockBreak, lines));
    }

    private static void CollectGroup(
        List<string> candidates,
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
                candidates.Add($"<color={AccentColor}>{title}</color>");
            else if (string.IsNullOrWhiteSpace(title))
                candidates.Add(colorize(description!));
            else
                candidates.Add(
                    $"<color={AccentColor}>{title}:</color> {colorize(description!)}"
                );
            return;
        }

        var optionCount = eligible.Count + unresolved;
        if (optionCount == 0)
            return;

        // Inside a choose-one list a single draw needs no "x1" marker.
        var limit = group.Limit is TFixedValue fixedValue ? (int)fixedValue.Value : 1;
        var draws = Math.Min(limit, optionCount);

        // After hero filtering most pools shrink to a handful of concrete rewards;
        // list those out instead of hiding them behind a count.
        if (unresolved == 0 && eligible.Count <= 8)
        {
            var block = new StringBuilder(
                draws == 1
                    ? CollectionPanelText.OutcomeSubPool(eligible.Count)
                    : CollectionPanelText.LevelUpRandomPool(draws, eligible.Count)
            );
            foreach (var template in eligible)
            {
                var entryTitle = CollectionLocalizationResolver.ResolveTitle(template)
                    ?? template.InternalName;
                var entryDescription = CollectionLocalizationResolver.ResolveDescription(template)
                    ?.Replace("\r", string.Empty)
                    .Replace('\n', ' ');
                block.Append(CollectionTooltipMarkup.SubItemBreak);
                block.Append("<indent=2.2em>- ");
                block.Append(
                    string.IsNullOrWhiteSpace(entryDescription)
                        ? $"<color={AccentColor}>{entryTitle}</color>"
                        : $"<color={AccentColor}>{entryTitle}:</color> {colorize(entryDescription!)}"
                );
                block.Append("</indent>");
            }
            candidates.Add(block.ToString());
            return;
        }

        candidates.Add(
            draws == 1
                ? CollectionPanelText.LevelUpRandomPoolSingle(optionCount)
                : CollectionPanelText.LevelUpRandomPool(draws, optionCount)
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
