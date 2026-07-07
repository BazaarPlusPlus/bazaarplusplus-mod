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
            currentDay,
            out var isRandomSelectionEvent
        );
        // A suppressed random-selection event (shop stock generation) must not fall
        // back to rendering its spawn groups as choices either.
        var choiceDetails = outcomeGroups != null || isRandomSelectionEvent
            ? Array.Empty<CollectionEncounterChoiceDetail>()
            : ResolveChoiceDetails(eventTemplate, staticData, currentHero, inventory, currentDay);
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
        int? currentDay,
        out bool isRandomSelectionEvent
    )
    {
        isRandomSelectionEvent = CollectionEncounterStructuredParser.TryParseEventOutcomeGroups(
            eventTemplate,
            out var groups
        );
        if (!isRandomSelectionEvent)
            return null;

        // Shops use the same outer Random spawn context — for stock generation, not
        // a one-shot roll; "100% random item" explains nothing the description
        // ("Sells Small items") doesn't already say.
        if (HasMerchantTag(eventTemplate))
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

        var resolutions = new List<OutcomeGroupResolution>();
        foreach (var (group, eligible) in active)
        {
            var details = new List<CollectionEncounterChoiceDetail>();
            var combatIds = new HashSet<Guid>();
            var resolvedCount = 0;
            foreach (var id in group.Ids)
            {
                var template = BppStaticDataAccess.GetCardTemplate(staticData, id);
                if (template == null)
                    continue;
                resolvedCount++;
                if (IsEncounterCombatTemplate(template))
                {
                    combatIds.Add(template.Id);
                    continue;
                }
                if (!CollectionEncounterHeroEligibility.Matches(template.Heroes, currentHero))
                    continue;
                if (IsSkillTemplate(template))
                {
                    var skillName = CollectionLocalizationResolver.ResolveTitle(template)
                        ?? template.InternalName;
                    details.Add(
                        new CollectionEncounterChoiceDetail(
                            template.Id,
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
                details.Add(
                    new CollectionEncounterChoiceDetail(
                        Guid.Empty,
                        displayName: string.Empty,
                        resultText: QueryPoolResultText(pool),
                        rewardFilter: pool.Filter,
                        isSourceMatch: false
                    )
                );

            // Variant cards sharing one title+text (e.g. two "Aila's Package" ids in
            // a single Farai group) read as duplicates; keep one.
            DedupeDetails(details);

            var isCombatPool = group.QueryPools.Count == 0
                && resolvedCount > 0
                && combatIds.Count * 2 > resolvedCount;
            resolutions.Add(
                new OutcomeGroupResolution(group.Weight, eligible, isCombatPool, combatIds, details)
            );
        }

        var views = BuildOutcomeViews(resolutions, totalWeight);
        if (views.Count == 0)
            return null;

        var spawnLimit =
            CollectionEncounterStructuredParser.TryParseEventChoiceLimit(eventTemplate) ?? 1;
        return ShouldSuppressOutcomeViews(views, spawnLimit) ? null : views;
    }

    private static bool HasMerchantTag(TCardBase eventTemplate)
    {
        try
        {
            var tags = eventTemplate.GetType().GetProperty("Tags")?.GetValue(eventTemplate);
            if (tags is not System.Collections.IEnumerable enumerable)
                return false;
            foreach (var tag in enumerable)
                if (string.Equals(tag?.ToString(), "Merchant", StringComparison.Ordinal))
                    return true;
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // A spawn limit above one means the roll composes multiple spawns (shop stock,
    // multi-offer events) rather than picking one outcome. When such an event has no
    // real alternatives to explain — a single view, or nothing but nameless random
    // pools (Seraphim's two 50% item pools) — the breakdown is stock composition
    // noise, not outcome odds.
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
                if (string.Equals(details[i].DisplayName, details[j].DisplayName, StringComparison.Ordinal)
                    && string.Equals(details[i].ResultText, details[j].ResultText, StringComparison.Ordinal))
                {
                    details.RemoveAt(i);
                    break;
                }
            }
        }
    }

    // Per-group resolution before percentage math and combat-pool merging.
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

    // Random-outcome events often split "fight a monster" across several weighted
    // groups; the tooltip shows no monster names, so per-group combat lines are pure
    // redundancy ("25% fight + 25% fight"). Same-eligibility combat pools collapse
    // into one line at the first group's position — weights summed before rounding
    // (33+33 rounds to 67, not 66) and monster ids unioned so overlapping pools
    // don't inflate the "N possible" count.
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
                if (combatSlots.TryGetValue(resolution.Eligible, out var slot))
                {
                    combatWeights[resolution.Eligible] += resolution.Weight;
                    combatIds[resolution.Eligible].UnionWith(resolution.CombatIds);
                }
                else
                {
                    combatSlots[resolution.Eligible] = views.Count;
                    combatWeights[resolution.Eligible] = resolution.Weight;
                    combatIds[resolution.Eligible] = new HashSet<Guid>(resolution.CombatIds);
                    // Placeholder patched below once all combat groups are merged in.
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

            // Groups rendering identically (e.g. Farai's weight-2 pool of two
            // "Aila's Package" variants next to a weight-1 single) merge into one
            // line, weights summed before rounding — separate lines would show the
            // same text several times with misleadingly split percentages.
            var signature = ContentSignature(resolution);
            if (contentSlots.TryGetValue(signature, out var contentSlot))
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
            views[slot] = new CollectionEncounterOutcomeView(
                Percent(eligible, combatWeights[eligible]),
                eligible,
                isCombatPool: true,
                combatIds[eligible].Count,
                Array.Empty<CollectionEncounterChoiceDetail>()
            );

        var titleOnlySlots = new List<(int Slot, uint Weight)>();
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
            if (view.IsEligible
                && view.Details.Count == 1
                && string.IsNullOrEmpty(view.Details[0].ResultText)
                && !string.IsNullOrEmpty(view.Details[0].DisplayName))
                titleOnlySlots.Add((slot, contentWeights[signature]));
        }

        // A large cluster of same-shaped title-only outcomes (Farai's ~26 NPC
        // packages, Underground Resistance's 22) reads as a wall; collapse it into
        // one pool line with the summed probability.
        if (titleOnlySlots.Count > 8)
        {
            titleOnlySlots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            uint pooledWeight = 0;
            foreach (var (_, weight) in titleOnlySlots)
                pooledWeight += weight;
            views[titleOnlySlots[0].Slot] = new CollectionEncounterOutcomeView(
                Percent(true, pooledWeight),
                isEligible: true,
                isCombatPool: false,
                titleOnlySlots.Count,
                Array.Empty<CollectionEncounterChoiceDetail>()
            );
            for (var i = titleOnlySlots.Count - 1; i >= 1; i--)
                views.RemoveAt(titleOnlySlots[i].Slot);
        }

        return views;
    }

    private static string ContentSignature(OutcomeGroupResolution resolution)
    {
        var builder = new System.Text.StringBuilder(resolution.Eligible ? "e" : "i");
        foreach (var detail in resolution.Details)
            builder
                .Append('\x1f')
                .Append(detail.DisplayName)
                .Append('\x1e')
                .Append(detail.ResultText);
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

    private static bool IsSkillTemplate(TCardBase template) =>
        string.Equals(template.GetType().Name, "TCardSkill", StringComparison.Ordinal)
        || string.Equals(template.Type.ToString(), "Skill", StringComparison.Ordinal);

    private static bool IsEncounterCombatTemplate(TCardBase template) =>
        string.Equals(template.GetType().Name, "TCardEncounterCombat", StringComparison.Ordinal)
        || string.Equals(template.Type.ToString(), "CombatEncounter", StringComparison.Ordinal);

    private static IReadOnlyList<CollectionEncounterChoiceDetail> ResolveChoiceDetails(
        TCardBase eventTemplate,
        object? staticData,
        EHero? currentHero,
        CollectionEncounterInventory? inventory,
        int? currentDay
    )
    {
        var choiceGroups = CollectionEncounterStructuredParser.TryParseEventChoiceGroups(
            eventTemplate
        );

        // First pass in data order: fixed steps become candidates subject to the
        // presentation limit; a Random-selection group is one rolled pool line.
        // Day-gated groups outside the current day (Wishing Fountain's per-day
        // price tiers) are not offered at all.
        var candidates = new List<(TCardBase Step, bool MeetsPrerequisites)>();
        var pools = new List<CollectionEncounterChoiceDetail>();
        var eventDescription =
            CollectionLocalizationResolver.ResolveDescription(eventTemplate) ?? string.Empty;
        foreach (var group in choiceGroups)
        {
            if (group.DayCondition is { } dayCondition
                && currentDay.HasValue
                && !dayCondition.Matches(currentDay.Value))
                continue;

            if (group.IsRandomPool)
            {
                if (ResolveChoicePool(group, staticData, currentHero, inventory, eventDescription)
                    is { } pool)
                    pools.Add(pool);
                continue;
            }

            foreach (var reference in group.Members)
            {
                var rawStep = BppStaticDataAccess.GetCardTemplate(staticData, reference.TemplateId);
                if (rawStep == null || !IsEncounterStepTemplate(rawStep))
                    continue;
                if (!CollectionEncounterHeroEligibility.Matches(rawStep.Heroes, currentHero))
                    continue;

                candidates.Add((rawStep, MeetsOwnershipPrerequisites(reference, inventory)));
            }
        }

        // The event only presents the first <limit> prerequisite-passing steps
        // (Sequential spawn); the rest — prerequisite-unmet or beyond the limit —
        // render dimmed at the bottom. Rolled pools always render (their members
        // compete for the remaining presentation slots at random).
        var choiceLimit =
            CollectionEncounterStructuredParser.TryParseEventChoiceLimit(eventTemplate)
            ?? int.MaxValue;
        var titled = new List<(string Title, bool MeetsPrerequisites)>(candidates.Count);
        foreach (var (step, meetsPrerequisites) in candidates)
            titled.Add((
                CollectionLocalizationResolver.ResolveTitle(step) ?? step.InternalName,
                meetsPrerequisites
            ));
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
        // Default array value must not collide with Presented (set in a first pass
        // that later passes key off).
        Hidden = 0,
        Presented,
        Dimmed,
    }

    // Splits choice candidates into offered / dimmed / hidden. Beyond-limit steps
    // whose title repeats an already-visible option are escalation variants of the
    // same action (Wishing Fountain lists 18 "Make a Wish" price tiers behind the
    // 3 offered ones) and hide entirely; prerequisite-unmet steps always show
    // dimmed — knowing what you're missing is the point of that section.
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

    // A Random-selection spawn group inside a choice event: the offered choices are
    // rolled from its members, which may be encounter steps, skills, items or
    // combats. Renders as one pool line: a combat roll, a small expandable list, or
    // a bare option count.
    private static CollectionEncounterChoiceDetail? ResolveChoicePool(
        CollectionEncounterChoiceGroupData group,
        object? staticData,
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
            var template = BppStaticDataAccess.GetCardTemplate(staticData, member.TemplateId);
            if (template == null)
                continue;
            resolvedCount++;
            if (IsEncounterCombatTemplate(template))
            {
                combatIds.Add(template.Id);
                continue;
            }
            if (!CollectionEncounterHeroEligibility.Matches(template.Heroes, currentHero))
                continue;
            if (!MeetsOwnershipPrerequisites(member, inventory))
                continue;
            if (IsSkillTemplate(template))
            {
                var skillName = CollectionLocalizationResolver.ResolveTitle(template)
                    ?? template.InternalName;
                entries.Add(
                    new CollectionEncounterChoiceDetail(
                        template.Id,
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
            return PoolDetail(new CollectionEncounterChoicePool(
                isCombat: true,
                combatIds.Count,
                Array.Empty<CollectionEncounterChoiceDetail>()
            ));

        if (entries.Count == 0)
            return null;

        // A pool that dedupes to one entry is no pool ("one of 1:") — and when that
        // entry is a bare title the event description already spells out ("Get 4
        // Bronze-tier Spare Change" / "Spare Change"), the line says nothing at all.
        if (entries.Count == 1)
        {
            var single = entries[0];
            if (string.IsNullOrEmpty(single.ResultText)
                && !string.IsNullOrEmpty(single.DisplayName)
                && eventDescription.Contains(single.DisplayName, StringComparison.OrdinalIgnoreCase))
                return null;
            return single;
        }

        // Small pools expand into their entries; large ones stay a count summary.
        return PoolDetail(new CollectionEncounterChoicePool(
            isCombat: false,
            entries.Count,
            entries.Count <= 8 ? entries : Array.Empty<CollectionEncounterChoiceDetail>()
        ));
    }

    private static CollectionEncounterChoiceDetail PoolDetail(CollectionEncounterChoicePool pool) =>
        new(
            Guid.Empty,
            displayName: string.Empty,
            resultText: string.Empty,
            rewardFilter: null,
            isSourceMatch: false,
            pool: pool
        );

    // Card-count prerequisites combine with AND. Without inventory access (or for
    // run-state prerequisites, which yield no requirements) the option counts as
    // eligible rather than guessing.
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
