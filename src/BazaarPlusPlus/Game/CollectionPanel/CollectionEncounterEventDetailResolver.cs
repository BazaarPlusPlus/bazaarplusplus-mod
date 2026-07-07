#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.StaticCards;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal static class CollectionEncounterEventDetailResolver
{
    public static CollectionEncounterOption? TryResolve(
        TCardBase? eventTemplate,
        object? staticData,
        EHero? currentHero,
        CollectionEncounterInventory? inventory = null,
        int? currentDay = null
    )
    {
        if (eventTemplate == null || !IsEncounterEventTemplate(eventTemplate))
            return null;

        var resultText = CollectionLocalizationResolver.ResolveDescription(eventTemplate) ?? string.Empty;
        var rewardFilter = ResolveRewardFilter(eventTemplate, resultText);
        var outcomeGroups = ResolveOutcomeGroups(
            eventTemplate,
            staticData,
            currentHero,
            inventory,
            currentDay
        );
        var choiceDetails = outcomeGroups != null
            ? Array.Empty<CollectionEncounterChoiceDetail>()
            : ResolveChoiceDetails(eventTemplate, staticData, currentHero, inventory);
        return new CollectionEncounterOption(
            eventTemplate.Id,
            CollectionLocalizationResolver.ResolveTitle(eventTemplate) ?? eventTemplate.InternalName,
            sourceKey: null,
            sourceKind: null,
            eventTemplate.Id,
            resultText,
            rewardFilter,
            choiceDetails,
            outcomeGroups
        );
    }

    // Random-outcome events roll one weighted group: percentages normalize over the
    // groups actually in the roll (day condition matching, ownership prerequisites
    // met); prerequisite-unmet groups render dimmed without a percentage.
    private static IReadOnlyList<CollectionEncounterOutcomeView>? ResolveOutcomeGroups(
        TCardBase eventTemplate,
        object? staticData,
        EHero? currentHero,
        CollectionEncounterInventory? inventory,
        int? currentDay
    )
    {
        if (!CollectionEncounterStructuredParser.TryParseEventOutcomeGroups(
                eventTemplate,
                out var groups
            ))
            return null;

        var active = new List<(CollectionEncounterOutcomeGroupData Group, bool Eligible)>();
        uint totalWeight = 0;
        foreach (var group in groups)
        {
            if (group.DayCondition is { } dayCondition
                && currentDay.HasValue
                && !dayCondition.Matches(currentDay.Value))
                continue;

            var eligible = MeetsOutcomePrerequisites(group, inventory);
            active.Add((group, eligible));
            if (eligible)
                totalWeight += group.Weight;
        }

        if (active.Count == 0)
            return null;

        var views = new List<CollectionEncounterOutcomeView>();
        foreach (var (group, eligible) in active)
        {
            var details = new List<CollectionEncounterChoiceDetail>();
            var combatCount = 0;
            var resolvedCount = 0;
            foreach (var id in group.Ids)
            {
                var template = BppStaticDataAccess.GetCardTemplate(staticData, id);
                if (template == null)
                    continue;
                resolvedCount++;
                if (IsEncounterCombatTemplate(template))
                {
                    combatCount++;
                    continue;
                }
                if (!CollectionEncounterHeroEligibility.Matches(template.Heroes, currentHero))
                    continue;
                AddChoiceDetail(details, template, isEligible: true);
            }

            var isCombatPool = resolvedCount > 0 && combatCount * 2 > resolvedCount;
            var optionCount = isCombatPool ? combatCount : details.Count;
            if (optionCount == 0)
                continue;

            int? percent = eligible && totalWeight > 0
                ? (int)Math.Round(group.Weight * 100.0 / totalWeight)
                : null;
            views.Add(
                new CollectionEncounterOutcomeView(percent, eligible, isCombatPool, optionCount, details)
            );
        }

        return views.Count == 0 ? null : views;
    }

    private static bool MeetsOutcomePrerequisites(
        CollectionEncounterOutcomeGroupData group,
        CollectionEncounterInventory? inventory
    )
    {
        if (inventory == null)
            return true;

        foreach (var idGroup in group.PrerequisiteIdGroups)
            if (!inventory.OwnsAnyTemplate(idGroup))
                return false;
        foreach (var tagGroup in group.PrerequisiteTagGroups)
            if (!inventory.OwnsAnyTag(tagGroup))
                return false;
        return true;
    }

    private static bool IsEncounterCombatTemplate(TCardBase template) =>
        string.Equals(template.GetType().Name, "TCardEncounterCombat", StringComparison.Ordinal)
        || string.Equals(template.Type.ToString(), "CombatEncounter", StringComparison.Ordinal);

    private static IReadOnlyList<CollectionEncounterChoiceDetail> ResolveChoiceDetails(
        TCardBase eventTemplate,
        object? staticData,
        EHero? currentHero,
        CollectionEncounterInventory? inventory
    )
    {
        var structuredReferences = CollectionEncounterStructuredParser.TryParseEventStepReferences(
            eventTemplate
        );

        // First pass: hero-visible steps in data order, with prerequisite evaluation.
        var candidates = new List<(TCardBase Step, bool MeetsPrerequisites)>();
        foreach (var reference in structuredReferences)
        {
            var rawStep = BppStaticDataAccess.GetCardTemplate(staticData, reference.TemplateId);
            if (rawStep == null || !IsEncounterStepTemplate(rawStep))
                continue;
            if (!CollectionEncounterHeroEligibility.Matches(rawStep.Heroes, currentHero))
                continue;

            candidates.Add((rawStep, MeetsOwnershipPrerequisites(reference, inventory)));
        }

        // The event only presents the first <limit> prerequisite-passing steps
        // (Sequential spawn); the rest — prerequisite-unmet or beyond the limit —
        // render dimmed at the bottom.
        var choiceLimit =
            CollectionEncounterStructuredParser.TryParseEventChoiceLimit(eventTemplate)
            ?? int.MaxValue;
        var presented = new List<CollectionEncounterChoiceDetail>();
        var dimmed = new List<CollectionEncounterChoiceDetail>();
        foreach (var (step, meetsPrerequisites) in candidates)
        {
            var isPresented = meetsPrerequisites && presented.Count < choiceLimit;
            AddChoiceDetail(isPresented ? presented : dimmed, step, isPresented);
        }

        presented.AddRange(dimmed);
        return presented;
    }

    // Ownership prerequisites combine with AND across groups; each group is any-of
    // (a conditional may list alternatives, e.g. "Powder Keg or the Big One").
    // Without inventory access (or for run-state prerequisites, which carry neither
    // ids nor tags) the option counts as eligible rather than guessing.
    private static bool MeetsOwnershipPrerequisites(
        CollectionEncounterStepReference reference,
        CollectionEncounterInventory? inventory
    )
    {
        if (inventory == null)
            return true;

        foreach (var idGroup in reference.PrerequisiteIdGroups)
            if (!inventory.OwnsAnyTemplate(idGroup))
                return false;

        foreach (var tagGroup in reference.PrerequisiteTagGroups)
            if (!inventory.OwnsAnyTag(tagGroup))
                return false;

        return true;
    }

    private static void AddChoiceDetail(
        List<CollectionEncounterChoiceDetail> result,
        TCardBase stepTemplate,
        bool isEligible
    )
    {
        var resultText = CollectionLocalizationResolver.ResolveDescription(stepTemplate) ?? string.Empty;
        result.Add(
            new CollectionEncounterChoiceDetail(
                stepTemplate.Id,
                CollectionLocalizationResolver.ResolveTitle(stepTemplate) ?? stepTemplate.InternalName,
                StripHeroConditionPrefix(resultText, stepTemplate.Heroes),
                ResolveRewardFilter(stepTemplate, resultText),
                isSourceMatch: false,
                prerequisiteSummary: "",
                isEligible
            )
        );
    }

    // Hero-restricted step descriptions start with a condition like "(if you are Jules) ";
    // choices are already filtered to the current hero, so the prefix is just noise.
    private static string StripHeroConditionPrefix(string text, IReadOnlyCollection<EHero> heroes)
    {
        if (string.IsNullOrEmpty(text) || !IsHeroRestricted(heroes))
            return text;

        var trimmed = text.TrimStart();
        var close = trimmed.Length == 0
            ? '\0'
            : trimmed[0] switch
            {
                '(' => ')',
                '（' => '）',
                _ => '\0',
            };
        if (close == '\0')
            return text;

        var closeIndex = trimmed.IndexOf(close);
        if (closeIndex < 0 || closeIndex + 1 >= trimmed.Length)
            return text;

        var remainder = trimmed[(closeIndex + 1)..].TrimStart();
        return remainder.Length == 0 ? text : remainder;
    }

    private static bool IsHeroRestricted(IReadOnlyCollection<EHero> heroes)
    {
        if (heroes.Count == 0)
            return false;
        foreach (var hero in heroes)
            if (hero == EHero.Common)
                return false;
        return true;
    }

    // Only structured constraints are trusted for the displayed pool summary; the
    // text parser cannot represent negations ("non-Weapon") and would show inverted
    // filters. It is still consulted for the "from any Hero" phrasing, which the
    // structured data does not carry.
    private static CollectionEncounterRewardFilter? ResolveRewardFilter(
        TCardBase template,
        string resultText
    )
    {
        var rewardFilter = CollectionEncounterStructuredParser.TryParseRewardFilter(template);
        if (rewardFilter == null)
            return null;

        var textRewardFilter = CollectionEncounterRewardParser.TryParse(resultText);
        return textRewardFilter?.FromAnyHero == true
            ? rewardFilter.WithFromAnyHero(true)
            : rewardFilter;
    }

    private static bool IsEncounterEventTemplate(TCardBase template) =>
        string.Equals(template.GetType().Name, "TCardEncounterEvent", StringComparison.Ordinal)
        || string.Equals(template.Type.ToString(), "EventEncounter", StringComparison.Ordinal);

    private static bool IsEncounterStepTemplate(TCardBase template) =>
        string.Equals(template.GetType().Name, "TCardEncounterStep", StringComparison.Ordinal)
        || string.Equals(template.Type.ToString(), "EncounterStep", StringComparison.Ordinal);
}
