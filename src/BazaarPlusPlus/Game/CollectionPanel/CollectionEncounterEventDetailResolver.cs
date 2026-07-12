#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal static class CollectionEncounterEventDetailResolver
{
    public static CollectionEncounterOption? TryResolve(
        CollectionEncounterPreviewEventPlan? eventPlan,
        CollectionEncounterPreviewSnapshot snapshot,
        EHero? currentHero,
        CollectionEncounterInventory? inventory = null,
        int? currentDay = null
    )
    {
        if (
            eventPlan == null
            || snapshot == null
            || !snapshot.TryGetTemplate(eventPlan.TemplateId, out var eventTemplate)
            || eventTemplate.Kind != CollectionEncounterPreviewTemplateKind.Event
        )
            return null;

        var resultText =
            CollectionLocalizationResolver.ResolveDescription(eventTemplate) ?? string.Empty;
        var rewardFilter = ResolveRewardFilter(eventTemplate, resultText);
        var outcomeGroups = ResolveOutcomeGroups(
            eventPlan,
            snapshot,
            currentHero,
            inventory,
            currentDay
        );

        // A suppressed random-selection event (shop stock generation) must not fall
        // back to rendering its spawn groups as choices either.
        var choiceDetails = outcomeGroups != null || eventPlan.IsRandomSelectionEvent
            ? Array.Empty<CollectionEncounterChoiceDetail>()
            : ResolveChoiceDetails(eventPlan, snapshot, currentHero, inventory, currentDay);
        return new CollectionEncounterOption(
            eventTemplate.TemplateId,
            CollectionLocalizationResolver.ResolveTitle(eventTemplate)
                ?? eventTemplate.InternalName,
            sourceKey: null,
            sourceKind: null,
            eventTemplate.TemplateId,
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
        CollectionEncounterPreviewEventPlan eventPlan,
        CollectionEncounterPreviewSnapshot snapshot,
        EHero? currentHero,
        CollectionEncounterInventory? inventory,
        int? currentDay
    )
    {
        if (!eventPlan.IsRandomSelectionEvent || eventPlan.SuppressRandomOutcome)
            return null;

        var active = new List<(CollectionEncounterOutcomeGroupData Group, bool Eligible)>();
        uint totalWeight = 0;
        foreach (var group in eventPlan.OutcomeGroups)
        {
            if (
                group.DayCondition is { } dayCondition
                && currentDay.HasValue
                && !dayCondition.Matches(currentDay.Value)
            )
                continue;

            var eligible = MeetsOutcomePrerequisites(group, inventory);
            active.Add((group, eligible));
            if (eligible)
                totalWeight += group.Weight;
        }

        if (active.Count == 0)
            return null;

        var resolutions = new List<OutcomeGroupResolution>();
        foreach (var (group, eligible) in active)
        {
            var details = new List<CollectionEncounterChoiceDetail>();
            var combatIds = new HashSet<Guid>();
            var resolvedCount = 0;
            foreach (var id in group.Ids)
            {
                if (!snapshot.TryGetTemplate(id, out var template))
                    continue;
                resolvedCount++;
                if (template.Kind == CollectionEncounterPreviewTemplateKind.CombatEncounter)
                {
                    combatIds.Add(template.TemplateId);
                    continue;
                }
                if (!CollectionEncounterHeroEligibility.Matches(template.Heroes, currentHero))
                    continue;
                if (template.Kind == CollectionEncounterPreviewTemplateKind.Skill)
                {
                    var skillName =
                        CollectionLocalizationResolver.ResolveTitle(template)
                        ?? template.InternalName;
                    details.Add(
                        new CollectionEncounterChoiceDetail(
                            template.TemplateId,
                            CollectionPanelText.OutcomeGainSkill(skillName),
                            resultText: string.Empty,
                            rewardFilter: null,
                            isSourceMatch: false
                        )
                    );
                    continue;
                }
                AddChoiceDetail(details, template, isEligible: true);
            }

            // Dynamic pools roll a summary line; the reward filter drives the
            // day-tier suffix exactly like card-text rewards.
            foreach (var pool in group.QueryPools)
            {
                details.Add(
                    new CollectionEncounterChoiceDetail(
                        Guid.Empty,
                        displayName: string.Empty,
                        resultText: QueryPoolResultText(pool),
                        rewardFilter: pool.Filter,
                        isSourceMatch: false
                    )
                );
            }

            DedupeDetails(details);

            var isCombatPool =
                group.QueryPools.Count == 0
                && resolvedCount > 0
                && combatIds.Count * 2 > resolvedCount;
            resolutions.Add(
                new OutcomeGroupResolution(group.Weight, eligible, isCombatPool, combatIds, details)
            );
        }

        var views = BuildOutcomeViews(resolutions, totalWeight);
        if (views.Count == 0)
            return null;

        return ShouldSuppressOutcomeViews(views, eventPlan.ChoiceLimit ?? 1) ? null : views;
    }

    // A spawn limit above one means the roll composes multiple spawns (shop stock,
    // multi-offer events) rather than picking one outcome. When such an event has no
    // real alternatives to explain — a single view, or nothing but nameless random
    // pools — the breakdown is stock composition noise, not outcome odds.
    internal static bool ShouldSuppressOutcomeViews(
        IReadOnlyList<CollectionEncounterOutcomeView> views,
        int spawnLimit
    )
    {
        if (spawnLimit <= 1)
            return false;
        if (views.Count == 1)
            return true;

        foreach (var view in views)
        {
            if (view.IsCombatPool)
                return false;
            foreach (var detail in view.Details)
                if (!string.IsNullOrEmpty(detail.DisplayName))
                    return false;
        }
        return true;
    }

    private static string QueryPoolResultText(CollectionEncounterOutcomeQueryPool pool)
    {
        var baseText = pool.Filter?.CardType switch
        {
            ECardType.Skill => CollectionPanelText.OutcomeRandomSkill(),
            ECardType.Item => CollectionPanelText.OutcomeRandomItem(),
            _ => CollectionPanelText.OutcomeRandomReward(),
        };
        var quantity = pool.Quantity ?? pool.Filter?.Quantity;
        return quantity is > 1 ? $"{quantity}× {baseText}" : baseText;
    }

    private static void DedupeDetails(List<CollectionEncounterChoiceDetail> details)
    {
        for (var i = details.Count - 1; i > 0; i--)
        {
            for (var j = 0; j < i; j++)
            {
                if (
                    string.Equals(
                        details[i].DisplayName,
                        details[j].DisplayName,
                        StringComparison.Ordinal
                    )
                    && string.Equals(
                        details[i].ResultText,
                        details[j].ResultText,
                        StringComparison.Ordinal
                    )
                )
                {
                    details.RemoveAt(i);
                    break;
                }
            }
        }
    }

    internal readonly struct OutcomeGroupResolution
    {
        public OutcomeGroupResolution(
            uint weight,
            bool eligible,
            bool isCombatPool,
            HashSet<Guid> combatIds,
            List<CollectionEncounterChoiceDetail> details
        )
        {
            Weight = weight;
            Eligible = eligible;
            IsCombatPool = isCombatPool;
            CombatIds = combatIds;
            Details = details;
        }

        public uint Weight { get; }
        public bool Eligible { get; }
        public bool IsCombatPool { get; }
        public HashSet<Guid> CombatIds { get; }
        public List<CollectionEncounterChoiceDetail> Details { get; }
    }

    // Same-shaped outcome groups are merged after current-locale text resolution so
    // locale changes never require rebuilding the static plan.
    internal static List<CollectionEncounterOutcomeView> BuildOutcomeViews(
        List<OutcomeGroupResolution> resolutions,
        uint totalWeight
    )
    {
        var views = new List<CollectionEncounterOutcomeView>();
        var combatSlots = new Dictionary<bool, int>();
        var combatWeights = new Dictionary<bool, uint>();
        var combatIds = new Dictionary<bool, HashSet<Guid>>();
        var contentSlots = new Dictionary<string, int>(StringComparer.Ordinal);
        var contentWeights = new Dictionary<string, uint>(StringComparer.Ordinal);

        int? Percent(bool eligible, uint weight) =>
            eligible && totalWeight > 0 ? (int)Math.Round(weight * 100.0 / totalWeight) : null;

        foreach (var resolution in resolutions)
        {
            if (resolution.IsCombatPool)
            {
                if (resolution.CombatIds.Count == 0)
                    continue;
                if (combatSlots.TryGetValue(resolution.Eligible, out _))
                {
                    combatWeights[resolution.Eligible] += resolution.Weight;
                    combatIds[resolution.Eligible].UnionWith(resolution.CombatIds);
                }
                else
                {
                    combatSlots[resolution.Eligible] = views.Count;
                    combatWeights[resolution.Eligible] = resolution.Weight;
                    combatIds[resolution.Eligible] = new HashSet<Guid>(resolution.CombatIds);
                    views.Add(
                        new CollectionEncounterOutcomeView(
                            null,
                            resolution.Eligible,
                            isCombatPool: true,
                            optionCount: 0,
                            Array.Empty<CollectionEncounterChoiceDetail>()
                        )
                    );
                }
                continue;
            }

            if (resolution.Details.Count == 0)
                continue;

            var signature = ContentSignature(resolution);
            if (contentSlots.TryGetValue(signature, out _))
            {
                contentWeights[signature] += resolution.Weight;
                continue;
            }
            contentSlots[signature] = views.Count;
            contentWeights[signature] = resolution.Weight;
            views.Add(
                new CollectionEncounterOutcomeView(
                    null,
                    resolution.Eligible,
                    isCombatPool: false,
                    resolution.Details.Count,
                    resolution.Details
                )
            );
        }

        foreach (var (eligible, slot) in combatSlots)
        {
            views[slot] = new CollectionEncounterOutcomeView(
                Percent(eligible, combatWeights[eligible]),
                eligible,
                isCombatPool: true,
                combatIds[eligible].Count,
                Array.Empty<CollectionEncounterChoiceDetail>()
            );
        }

        var titleOnlySlotsByEligibility = new Dictionary<bool, List<(int Slot, uint Weight)>>();
        foreach (var (signature, slot) in contentSlots)
        {
            var view = views[slot];
            views[slot] = new CollectionEncounterOutcomeView(
                Percent(view.IsEligible, contentWeights[signature]),
                view.IsEligible,
                isCombatPool: false,
                view.OptionCount,
                view.Details
            );
            if (
                view.Details.Count == 1
                && string.IsNullOrEmpty(view.Details[0].ResultText)
                && !string.IsNullOrEmpty(view.Details[0].DisplayName)
            )
            {
                if (!titleOnlySlotsByEligibility.TryGetValue(view.IsEligible, out var slots))
                {
                    slots = new List<(int Slot, uint Weight)>();
                    titleOnlySlotsByEligibility[view.IsEligible] = slots;
                }
                slots.Add((slot, contentWeights[signature]));
            }
        }

        // A large cluster of title-only outcomes is a wall; collapse eligible and
        // ineligible clusters separately so unavailable results stay dimmed.
        var slotsToRemove = new List<int>();
        foreach (var (eligible, titleOnlySlots) in titleOnlySlotsByEligibility)
        {
            if (titleOnlySlots.Count <= 8)
                continue;

            titleOnlySlots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            uint pooledWeight = 0;
            foreach (var (_, weight) in titleOnlySlots)
                pooledWeight += weight;
            views[titleOnlySlots[0].Slot] = new CollectionEncounterOutcomeView(
                Percent(eligible, pooledWeight),
                eligible,
                isCombatPool: false,
                titleOnlySlots.Count,
                Array.Empty<CollectionEncounterChoiceDetail>()
            );
            for (var i = 1; i < titleOnlySlots.Count; i++)
                slotsToRemove.Add(titleOnlySlots[i].Slot);
        }
        slotsToRemove.Sort();
        for (var i = slotsToRemove.Count - 1; i >= 0; i--)
            views.RemoveAt(slotsToRemove[i]);

        return views;
    }

    private static string ContentSignature(OutcomeGroupResolution resolution)
    {
        var builder = new System.Text.StringBuilder(resolution.Eligible ? "e" : "i");
        foreach (var detail in resolution.Details)
        {
            builder
                .Append('\x1f')
                .Append(detail.DisplayName)
                .Append('\x1e')
                .Append(detail.ResultText);
        }
        return builder.ToString();
    }

    private static bool MeetsOutcomePrerequisites(
        CollectionEncounterOutcomeGroupData group,
        CollectionEncounterInventory? inventory
    )
    {
        if (inventory == null)
            return true;

        foreach (var requirement in group.Requirements)
            if (!requirement.Matches(inventory))
                return false;
        return true;
    }

    private static IReadOnlyList<CollectionEncounterChoiceDetail> ResolveChoiceDetails(
        CollectionEncounterPreviewEventPlan eventPlan,
        CollectionEncounterPreviewSnapshot snapshot,
        EHero? currentHero,
        CollectionEncounterInventory? inventory,
        int? currentDay
    )
    {
        var candidates = new List<(
            CollectionEncounterPreviewTemplatePlan Step,
            bool MeetsPrerequisites
        )>();
        var pools = new List<CollectionEncounterChoiceDetail>();
        var eventDescription = snapshot.TryGetTemplate(eventPlan.TemplateId, out var eventTemplate)
            ? CollectionLocalizationResolver.ResolveDescription(eventTemplate) ?? string.Empty
            : string.Empty;
        foreach (var group in eventPlan.ChoiceGroups)
        {
            if (
                group.DayCondition is { } dayCondition
                && currentDay.HasValue
                && !dayCondition.Matches(currentDay.Value)
            )
                continue;

            if (group.IsRandomPool)
            {
                if (
                    ResolveChoicePool(group, snapshot, currentHero, inventory, eventDescription)
                    is { } pool
                )
                    pools.Add(pool);
                continue;
            }

            foreach (var reference in group.Members)
            {
                if (
                    !snapshot.TryGetTemplate(reference.TemplateId, out var step)
                    || step.Kind != CollectionEncounterPreviewTemplateKind.EncounterStep
                    || !CollectionEncounterHeroEligibility.Matches(step.Heroes, currentHero)
                )
                    continue;

                candidates.Add((step, MeetsOwnershipPrerequisites(reference, inventory)));
            }
        }

        var choiceLimit = eventPlan.ChoiceLimit ?? int.MaxValue;
        var titled = new List<(string Title, bool MeetsPrerequisites)>(candidates.Count);
        foreach (var (step, meetsPrerequisites) in candidates)
            titled.Add(
                (
                    CollectionLocalizationResolver.ResolveTitle(step) ?? step.InternalName,
                    meetsPrerequisites
                )
            );
        var dispositions = ResolvePresentation(titled, choiceLimit);
        var presented = new List<CollectionEncounterChoiceDetail>();
        var dimmed = new List<CollectionEncounterChoiceDetail>();
        for (var i = 0; i < candidates.Count; i++)
        {
            switch (dispositions[i])
            {
                case ChoicePresentation.Presented:
                    AddChoiceDetail(presented, candidates[i].Step, isEligible: true);
                    break;
                case ChoicePresentation.Dimmed:
                    AddChoiceDetail(dimmed, candidates[i].Step, isEligible: false);
                    break;
            }
        }

        presented.AddRange(pools);
        presented.AddRange(dimmed);
        return presented;
    }

    internal enum ChoicePresentation
    {
        Hidden = 0,
        Presented,
        Dimmed,
    }

    internal static ChoicePresentation[] ResolvePresentation(
        IReadOnlyList<(string Title, bool MeetsPrerequisites)> candidates,
        int limit
    )
    {
        var result = new ChoicePresentation[candidates.Count];
        var seenTitles = new HashSet<string>(StringComparer.Ordinal);
        var presentedCount = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].MeetsPrerequisites && presentedCount < limit)
            {
                result[i] = ChoicePresentation.Presented;
                presentedCount++;
                seenTitles.Add(candidates[i].Title);
            }
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            if (result[i] == ChoicePresentation.Presented)
                continue;
            if (candidates[i].MeetsPrerequisites && !seenTitles.Add(candidates[i].Title))
            {
                result[i] = ChoicePresentation.Hidden;
                continue;
            }
            result[i] = ChoicePresentation.Dimmed;
        }

        return result;
    }

    private static CollectionEncounterChoiceDetail? ResolveChoicePool(
        CollectionEncounterChoiceGroupData group,
        CollectionEncounterPreviewSnapshot snapshot,
        EHero? currentHero,
        CollectionEncounterInventory? inventory,
        string eventDescription
    )
    {
        var entries = new List<CollectionEncounterChoiceDetail>();
        var combatIds = new HashSet<Guid>();
        var resolvedCount = 0;
        foreach (var member in group.Members)
        {
            if (!snapshot.TryGetTemplate(member.TemplateId, out var template))
                continue;
            resolvedCount++;
            if (template.Kind == CollectionEncounterPreviewTemplateKind.CombatEncounter)
            {
                combatIds.Add(template.TemplateId);
                continue;
            }
            if (!CollectionEncounterHeroEligibility.Matches(template.Heroes, currentHero))
                continue;
            if (!MeetsOwnershipPrerequisites(member, inventory))
                continue;
            if (template.Kind == CollectionEncounterPreviewTemplateKind.Skill)
            {
                var skillName =
                    CollectionLocalizationResolver.ResolveTitle(template) ?? template.InternalName;
                entries.Add(
                    new CollectionEncounterChoiceDetail(
                        template.TemplateId,
                        CollectionPanelText.OutcomeGainSkill(skillName),
                        resultText: string.Empty,
                        rewardFilter: null,
                        isSourceMatch: false
                    )
                );
                continue;
            }
            AddChoiceDetail(entries, template, isEligible: true);
        }

        DedupeDetails(entries);

        if (combatIds.Count * 2 > resolvedCount && combatIds.Count > 0)
            return PoolDetail(
                new CollectionEncounterChoicePool(
                    isCombat: true,
                    combatIds.Count,
                    Array.Empty<CollectionEncounterChoiceDetail>()
                )
            );

        if (entries.Count == 0)
            return null;

        if (entries.Count == 1)
        {
            var single = entries[0];
            if (
                string.IsNullOrEmpty(single.ResultText)
                && !string.IsNullOrEmpty(single.DisplayName)
                && eventDescription.Contains(single.DisplayName, StringComparison.OrdinalIgnoreCase)
            )
                return null;
            return single;
        }

        // Small pools expand into their entries; large ones stay a count summary.
        return PoolDetail(
            new CollectionEncounterChoicePool(
                isCombat: false,
                entries.Count,
                entries.Count <= 8 ? entries : Array.Empty<CollectionEncounterChoiceDetail>()
            )
        );
    }

    private static CollectionEncounterChoiceDetail PoolDetail(
        CollectionEncounterChoicePool pool
    ) =>
        new(
            Guid.Empty,
            displayName: string.Empty,
            resultText: string.Empty,
            rewardFilter: null,
            isSourceMatch: false,
            pool: pool
        );

    private static bool MeetsOwnershipPrerequisites(
        CollectionEncounterStepReference reference,
        CollectionEncounterInventory? inventory
    )
    {
        if (inventory == null)
            return true;

        foreach (var requirement in reference.Requirements)
            if (!requirement.Matches(inventory))
                return false;
        return true;
    }

    private static void AddChoiceDetail(
        List<CollectionEncounterChoiceDetail> result,
        CollectionEncounterPreviewTemplatePlan template,
        bool isEligible
    )
    {
        var resultText =
            CollectionLocalizationResolver.ResolveDescription(template) ?? string.Empty;
        result.Add(
            new CollectionEncounterChoiceDetail(
                template.TemplateId,
                CollectionLocalizationResolver.ResolveTitle(template) ?? template.InternalName,
                StripHeroConditionPrefix(resultText, template.Heroes),
                ResolveRewardFilter(template, resultText),
                isSourceMatch: false,
                prerequisiteSummary: "",
                isEligible
            )
        );
    }

    private static string StripHeroConditionPrefix(
        string text,
        IReadOnlyCollection<EHero> heroes
    )
    {
        if (string.IsNullOrEmpty(text) || !IsHeroRestricted(heroes))
            return text;

        var trimmed = text.TrimStart();
        var close =
            trimmed.Length == 0
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

    private static CollectionEncounterRewardFilter? ResolveRewardFilter(
        CollectionEncounterPreviewTemplatePlan template,
        string resultText
    )
    {
        var rewardFilter = template.RewardFilter;
        if (rewardFilter == null)
            return null;

        var textRewardFilter = CollectionEncounterRewardParser.TryParse(resultText);
        return textRewardFilter?.FromAnyHero == true
            ? rewardFilter.WithFromAnyHero(true)
            : rewardFilter;
    }
}
