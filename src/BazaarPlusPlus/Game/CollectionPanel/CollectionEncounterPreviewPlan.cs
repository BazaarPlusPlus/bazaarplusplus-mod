#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal enum CollectionEncounterPreviewTemplateKind
{
    Other = 0,
    Event = 1,
    EncounterStep = 2,
    CombatEncounter = 3,
    Skill = 4,
    Item = 5,
}

internal sealed class CollectionEncounterPreviewLocalizedText
{
    public CollectionEncounterPreviewLocalizedText(string? key, string? fallbackText)
    {
        Key = key;
        FallbackText = fallbackText;
    }

    public string? Key { get; }

    public string? FallbackText { get; }
}

internal sealed class CollectionEncounterPreviewAbilityValue
{
    public CollectionEncounterPreviewAbilityValue(string valueText, string? unit = null)
    {
        ValueText = valueText ?? string.Empty;
        Unit = unit;
    }

    public string ValueText { get; }

    public string? Unit { get; }
}

internal sealed class CollectionEncounterPreviewTemplatePlan
{
    public CollectionEncounterPreviewTemplatePlan(
        Guid templateId,
        CollectionEncounterPreviewTemplateKind kind,
        IReadOnlyCollection<EHero>? heroes,
        string? internalName,
        CollectionEncounterPreviewLocalizedText? title,
        CollectionEncounterPreviewLocalizedText? description,
        IReadOnlyDictionary<string, CollectionEncounterPreviewAbilityValue>? abilityValues,
        CollectionEncounterRewardFilter? rewardFilter
    )
    {
        TemplateId = templateId;
        Kind = kind;
        Heroes = CopyHeroes(heroes);
        InternalName = internalName ?? string.Empty;
        Title = title ?? new CollectionEncounterPreviewLocalizedText(null, null);
        Description = description ?? new CollectionEncounterPreviewLocalizedText(null, null);
        AbilityValues = CopyAbilityValues(abilityValues);
        RewardFilter = CollectionEncounterPreviewPlanCopies.CopyRewardFilter(rewardFilter);
    }

    public Guid TemplateId { get; }

    public CollectionEncounterPreviewTemplateKind Kind { get; }

    public IReadOnlyList<EHero> Heroes { get; }

    public string InternalName { get; }

    public CollectionEncounterPreviewLocalizedText Title { get; }

    public CollectionEncounterPreviewLocalizedText Description { get; }

    public IReadOnlyDictionary<
        string,
        CollectionEncounterPreviewAbilityValue
    > AbilityValues { get; }

    public CollectionEncounterRewardFilter? RewardFilter { get; }

    private static IReadOnlyList<EHero> CopyHeroes(IReadOnlyCollection<EHero>? heroes)
    {
        if (heroes == null || heroes.Count == 0)
            return Array.Empty<EHero>();

        var result = new EHero[heroes.Count];
        var index = 0;
        foreach (var hero in heroes)
            result[index++] = hero;
        return Array.AsReadOnly(result);
    }

    private static IReadOnlyDictionary<
        string,
        CollectionEncounterPreviewAbilityValue
    > CopyAbilityValues(
        IReadOnlyDictionary<string, CollectionEncounterPreviewAbilityValue>? abilityValues
    )
    {
        if (abilityValues == null || abilityValues.Count == 0)
        {
            return new ReadOnlyDictionary<string, CollectionEncounterPreviewAbilityValue>(
                new Dictionary<string, CollectionEncounterPreviewAbilityValue>(
                    StringComparer.Ordinal
                )
            );
        }

        var result = new Dictionary<string, CollectionEncounterPreviewAbilityValue>(
            abilityValues.Count,
            StringComparer.Ordinal
        );
        foreach (var pair in abilityValues)
        {
            if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                continue;
            result[pair.Key] = new CollectionEncounterPreviewAbilityValue(
                pair.Value.ValueText,
                pair.Value.Unit
            );
        }

        return new ReadOnlyDictionary<string, CollectionEncounterPreviewAbilityValue>(result);
    }
}

internal sealed class CollectionEncounterPreviewEventPlan
{
    public CollectionEncounterPreviewEventPlan(
        Guid templateId,
        bool isRandomSelectionEvent,
        bool suppressRandomOutcome,
        int? choiceLimit,
        IReadOnlyList<CollectionEncounterOutcomeGroupData>? outcomeGroups,
        IReadOnlyList<CollectionEncounterChoiceGroupData>? choiceGroups
    )
    {
        TemplateId = templateId;
        IsRandomSelectionEvent = isRandomSelectionEvent;
        SuppressRandomOutcome = suppressRandomOutcome;
        ChoiceLimit = choiceLimit;
        OutcomeGroups = CollectionEncounterPreviewPlanCopies.CopyOutcomeGroups(outcomeGroups);
        ChoiceGroups = CollectionEncounterPreviewPlanCopies.CopyChoiceGroups(choiceGroups);
    }

    public Guid TemplateId { get; }

    public bool IsRandomSelectionEvent { get; }

    public bool SuppressRandomOutcome { get; }

    public int? ChoiceLimit { get; }

    public IReadOnlyList<CollectionEncounterOutcomeGroupData> OutcomeGroups { get; }

    public IReadOnlyList<CollectionEncounterChoiceGroupData> ChoiceGroups { get; }
}

internal sealed class CollectionLevelUpPreviewHeroCondition
{
    public CollectionLevelUpPreviewHeroCondition(
        IReadOnlyCollection<EHero>? heroes,
        string? comparisonOperator
    )
    {
        Heroes = new List<EHero>(heroes ?? Array.Empty<EHero>()).AsReadOnly();
        ComparisonOperator = comparisonOperator ?? string.Empty;
    }

    public IReadOnlyList<EHero> Heroes { get; }

    public string ComparisonOperator { get; }
}

internal sealed class CollectionLevelUpPreviewGroup
{
    public CollectionLevelUpPreviewGroup(
        uint randomWeight,
        int limit,
        IReadOnlyCollection<Guid>? templateIds,
        IReadOnlyCollection<CollectionLevelUpPreviewHeroCondition>? heroConditions
    )
    {
        RandomWeight = randomWeight;
        Limit = Math.Max(1, limit);
        TemplateIds = new List<Guid>(templateIds ?? Array.Empty<Guid>()).AsReadOnly();
        HeroConditions = new List<CollectionLevelUpPreviewHeroCondition>(
            heroConditions ?? Array.Empty<CollectionLevelUpPreviewHeroCondition>()
        ).AsReadOnly();
    }

    public uint RandomWeight { get; }

    public int Limit { get; }

    public IReadOnlyList<Guid> TemplateIds { get; }

    public IReadOnlyList<CollectionLevelUpPreviewHeroCondition> HeroConditions { get; }
}

internal sealed class CollectionLevelUpPreviewPlan
{
    public CollectionLevelUpPreviewPlan(
        int level,
        int healthIncrease,
        bool isRandomSelection,
        IReadOnlyCollection<CollectionLevelUpPreviewGroup>? groups
    )
    {
        Level = level;
        HealthIncrease = healthIncrease;
        IsRandomSelection = isRandomSelection;
        Groups = new List<CollectionLevelUpPreviewGroup>(
            groups ?? Array.Empty<CollectionLevelUpPreviewGroup>()
        ).AsReadOnly();
    }

    public int Level { get; }

    public int HealthIncrease { get; }

    public bool IsRandomSelection { get; }

    public IReadOnlyList<CollectionLevelUpPreviewGroup> Groups { get; }
}

internal sealed class CollectionPreviewCoverage
{
    public CollectionPreviewCoverage(
        int eventFailureCount,
        int levelUpFailureCount,
        int unsupportedLevelUpPartCount,
        int missingReferencedTemplateCount
    )
    {
        EventFailureCount = Math.Max(0, eventFailureCount);
        LevelUpFailureCount = Math.Max(0, levelUpFailureCount);
        UnsupportedLevelUpPartCount = Math.Max(0, unsupportedLevelUpPartCount);
        MissingReferencedTemplateCount = Math.Max(0, missingReferencedTemplateCount);
    }

    public int EventFailureCount { get; }

    public int LevelUpFailureCount { get; }

    public int UnsupportedLevelUpPartCount { get; }

    public int MissingReferencedTemplateCount { get; }
}

internal sealed class CollectionEncounterPreviewSnapshot
{
    private readonly Dictionary<Guid, CollectionEncounterPreviewEventPlan> _eventsById;
    private readonly Dictionary<int, CollectionLevelUpPreviewPlan> _levelUpsByLevel;
    private readonly Dictionary<Guid, CollectionEncounterPreviewTemplatePlan> _templatesById;

    public CollectionEncounterPreviewSnapshot(
        IEnumerable<CollectionEncounterPreviewEventPlan>? events,
        IEnumerable<CollectionEncounterPreviewTemplatePlan>? templates,
        IEnumerable<CollectionLevelUpPreviewPlan>? levelUps = null,
        CollectionPreviewCoverage? coverage = null
    )
    {
        _eventsById = new Dictionary<Guid, CollectionEncounterPreviewEventPlan>();
        _levelUpsByLevel = new Dictionary<int, CollectionLevelUpPreviewPlan>();
        _templatesById = new Dictionary<Guid, CollectionEncounterPreviewTemplatePlan>();

        var eventList = new List<CollectionEncounterPreviewEventPlan>();
        if (events != null)
        {
            foreach (var eventPlan in events)
            {
                if (eventPlan == null)
                    continue;
                if (!_eventsById.TryAdd(eventPlan.TemplateId, eventPlan))
                {
                    throw new ArgumentException(
                        $"Duplicate encounter-preview event template id '{eventPlan.TemplateId:D}'.",
                        nameof(events)
                    );
                }
                eventList.Add(eventPlan);
            }
        }

        var levelUpList = new List<CollectionLevelUpPreviewPlan>();
        if (levelUps != null)
        {
            foreach (var levelUpPlan in levelUps)
            {
                if (levelUpPlan == null)
                    continue;
                if (!_levelUpsByLevel.TryAdd(levelUpPlan.Level, levelUpPlan))
                {
                    throw new ArgumentException(
                        $"Duplicate level-up preview level '{levelUpPlan.Level}'.",
                        nameof(levelUps)
                    );
                }
                levelUpList.Add(levelUpPlan);
            }
        }

        var templateList = new List<CollectionEncounterPreviewTemplatePlan>();
        if (templates != null)
        {
            foreach (var templatePlan in templates)
            {
                if (templatePlan == null)
                    continue;
                if (!_templatesById.TryAdd(templatePlan.TemplateId, templatePlan))
                {
                    throw new ArgumentException(
                        $"Duplicate encounter-preview template id '{templatePlan.TemplateId:D}'.",
                        nameof(templates)
                    );
                }
                templateList.Add(templatePlan);
            }
        }

        Events = eventList.AsReadOnly();
        LevelUps = levelUpList.AsReadOnly();
        Templates = templateList.AsReadOnly();
        Coverage = coverage ?? new CollectionPreviewCoverage(0, 0, 0, 0);
    }

    public IReadOnlyList<CollectionEncounterPreviewEventPlan> Events { get; }

    public IReadOnlyList<CollectionEncounterPreviewTemplatePlan> Templates { get; }

    public IReadOnlyList<CollectionLevelUpPreviewPlan> LevelUps { get; }

    public CollectionPreviewCoverage Coverage { get; }

    public int EventCount => _eventsById.Count;

    public int TemplateCount => _templatesById.Count;

    public int LevelUpCount => _levelUpsByLevel.Count;

    public bool TryGetEvent(Guid templateId, out CollectionEncounterPreviewEventPlan eventPlan) =>
        _eventsById.TryGetValue(templateId, out eventPlan!);

    public bool TryGetTemplate(
        Guid templateId,
        out CollectionEncounterPreviewTemplatePlan templatePlan
    ) => _templatesById.TryGetValue(templateId, out templatePlan!);

    public bool TryGetLevelUp(int level, out CollectionLevelUpPreviewPlan levelUpPlan) =>
        _levelUpsByLevel.TryGetValue(level, out levelUpPlan!);
}

internal sealed class CollectionEncounterPreviewCacheIdentity
    : IEquatable<CollectionEncounterPreviewCacheIdentity>
{
    public CollectionEncounterPreviewCacheIdentity(
        string kind,
        string resource,
        string value,
        string gameBuild,
        string buildChannel
    )
    {
        Kind = kind ?? string.Empty;
        Resource = resource ?? string.Empty;
        Value = value ?? string.Empty;
        GameBuild = gameBuild ?? string.Empty;
        BuildChannel = buildChannel ?? string.Empty;
    }

    public string Kind { get; }

    public string Resource { get; }

    public string Value { get; }

    public string GameBuild { get; }

    public string BuildChannel { get; }

    public bool Equals(CollectionEncounterPreviewCacheIdentity? other) =>
        other != null
        && string.Equals(Kind, other.Kind, StringComparison.Ordinal)
        && string.Equals(Resource, other.Resource, StringComparison.Ordinal)
        && string.Equals(Value, other.Value, StringComparison.Ordinal)
        && string.Equals(GameBuild, other.GameBuild, StringComparison.Ordinal)
        && string.Equals(BuildChannel, other.BuildChannel, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is CollectionEncounterPreviewCacheIdentity other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Kind);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Resource);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Value);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(GameBuild);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(BuildChannel);
            return hash;
        }
    }
}

internal static class CollectionEncounterPreviewPlanCopies
{
    public static IReadOnlyList<CollectionEncounterOutcomeGroupData> CopyOutcomeGroups(
        IReadOnlyList<CollectionEncounterOutcomeGroupData>? groups
    )
    {
        if (groups == null || groups.Count == 0)
            return Array.Empty<CollectionEncounterOutcomeGroupData>();

        var result = new CollectionEncounterOutcomeGroupData[groups.Count];
        for (var i = 0; i < groups.Count; i++)
            result[i] = CopyOutcomeGroup(groups[i]);
        return Array.AsReadOnly(result);
    }

    public static IReadOnlyList<CollectionEncounterChoiceGroupData> CopyChoiceGroups(
        IReadOnlyList<CollectionEncounterChoiceGroupData>? groups
    )
    {
        if (groups == null || groups.Count == 0)
            return Array.Empty<CollectionEncounterChoiceGroupData>();

        var result = new CollectionEncounterChoiceGroupData[groups.Count];
        for (var i = 0; i < groups.Count; i++)
            result[i] = CopyChoiceGroup(groups[i]);
        return Array.AsReadOnly(result);
    }

    public static CollectionEncounterRewardFilter? CopyRewardFilter(
        CollectionEncounterRewardFilter? filter
    )
    {
        if (filter == null)
            return null;

        return new CollectionEncounterRewardFilter(
            filter.CardType,
            filter.Quantity,
            filter.FromAnyHero,
            CopyList(filter.Sizes),
            CopyList(filter.Tiers),
            CopyList(filter.Tags),
            CopyList(filter.Keywords),
            filter.FilterSummary,
            CopyList(filter.ExcludedTags),
            CopyList(filter.ExcludedKeywords),
            filter.UsesDayTierTable,
            filter.UsesDayTierDistribution
        );
    }

    public static IReadOnlyList<CollectionEncounterCardRequirement> CopyRequirements(
        IReadOnlyList<CollectionEncounterCardRequirement>? requirements
    )
    {
        if (requirements == null || requirements.Count == 0)
            return Array.Empty<CollectionEncounterCardRequirement>();

        var result = new CollectionEncounterCardRequirement[requirements.Count];
        for (var i = 0; i < requirements.Count; i++)
            result[i] = CopyRequirement(requirements[i]);
        return Array.AsReadOnly(result);
    }

    private static CollectionEncounterOutcomeGroupData CopyOutcomeGroup(
        CollectionEncounterOutcomeGroupData group
    )
    {
        var queryPools = new CollectionEncounterOutcomeQueryPool[group.QueryPools.Count];
        for (var i = 0; i < queryPools.Length; i++)
        {
            var pool = group.QueryPools[i];
            queryPools[i] = new CollectionEncounterOutcomeQueryPool(
                CopyRewardFilter(pool.Filter),
                pool.Quantity
            );
        }

        return new CollectionEncounterOutcomeGroupData(
            group.Weight,
            CopyList(group.Ids),
            Array.AsReadOnly(queryPools),
            CopyRequirements(group.Requirements),
            CopyDayCondition(group.DayCondition)
        );
    }

    private static CollectionEncounterChoiceGroupData CopyChoiceGroup(
        CollectionEncounterChoiceGroupData group
    )
    {
        var members = new CollectionEncounterStepReference[group.Members.Count];
        for (var i = 0; i < members.Length; i++)
        {
            var member = group.Members[i];
            members[i] = new CollectionEncounterStepReference(
                member.TemplateId,
                CopyRequirements(member.Requirements)
            );
        }

        return new CollectionEncounterChoiceGroupData(
            group.IsRandomPool,
            Array.AsReadOnly(members),
            CopyDayCondition(group.DayCondition)
        );
    }

    private static CollectionEncounterCardRequirement CopyRequirement(
        CollectionEncounterCardRequirement requirement
    )
    {
        var tagGroups = new IReadOnlyList<string>[requirement.TagCandidateGroups.Count];
        for (var i = 0; i < tagGroups.Length; i++)
            tagGroups[i] = CopyList(requirement.TagCandidateGroups[i]);

        return new CollectionEncounterCardRequirement(
            CopyList(requirement.Ids),
            Array.AsReadOnly(tagGroups),
            requirement.TagOperator,
            requirement.Comparison,
            requirement.Amount
        );
    }

    private static CollectionEncounterDayCondition? CopyDayCondition(
        CollectionEncounterDayCondition? condition
    ) =>
        condition is { } value
            ? new CollectionEncounterDayCondition(value.Day, value.Comparison)
            : null;

    private static IReadOnlyList<T> CopyList<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
            return Array.Empty<T>();
        var result = new T[values.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = values[i];
        return Array.AsReadOnly(result);
    }
}
