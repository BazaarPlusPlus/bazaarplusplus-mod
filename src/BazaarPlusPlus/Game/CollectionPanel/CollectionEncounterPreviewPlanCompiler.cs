#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Game;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Spawning;
using BazaarGameShared.Domain.Spawning.SpawnFilters;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionEncounterPreviewCompileResult
{
    public CollectionEncounterPreviewCompileResult(
        CollectionEncounterPreviewSnapshot snapshot,
        IReadOnlyList<Guid> failedTemplateIds
    )
    {
        Snapshot = snapshot;
        FailedTemplateIds = failedTemplateIds;
    }

    public CollectionEncounterPreviewSnapshot Snapshot { get; }

    public IReadOnlyList<Guid> FailedTemplateIds { get; }

    public int FailureCount => FailedTemplateIds.Count;

    public int LevelUpFailureCount => Snapshot.Coverage.LevelUpFailureCount;
}

internal sealed class CollectionEncounterPreviewPlanCompiler
{
    private readonly Func<object, JToken?> _prepareToken;

    public CollectionEncounterPreviewPlanCompiler()
        : this(CollectionEncounterStructuredParser.TryPrepareToken) { }

    internal CollectionEncounterPreviewPlanCompiler(Func<object, JToken?> prepareToken)
    {
        _prepareToken = prepareToken ?? throw new ArgumentNullException(nameof(prepareToken));
    }

    public CollectionEncounterPreviewCompileResult Compile(
        IReadOnlyDictionary<Guid, ITCard> cardMap
    ) => Compile(cardMap, new Dictionary<int, TLevelUp>());

    public CollectionEncounterPreviewCompileResult Compile(
        IReadOnlyDictionary<Guid, ITCard> cardMap,
        IReadOnlyDictionary<int, TLevelUp> levelUpMap
    )
    {
        if (cardMap == null)
            throw new ArgumentNullException(nameof(cardMap));
        if (levelUpMap == null)
            throw new ArgumentNullException(nameof(levelUpMap));

        var events = new List<CollectionEncounterPreviewEventPlan>();
        var templates = new Dictionary<Guid, CollectionEncounterPreviewTemplatePlan>();
        var failures = new HashSet<Guid>();
        var missingReferencedTemplates = new HashSet<Guid>();
        var eventTemplates = cardMap
            .Values.OfType<TCardBase>()
            .Where(template => template.Type == ECardType.EventEncounter)
            .OrderBy(template => template.Id)
            .ToArray();

        foreach (var eventTemplate in eventTemplates)
        {
            try
            {
                var token = _prepareToken(eventTemplate);
                if (token == null)
                {
                    failures.Add(eventTemplate.Id);
                    continue;
                }

                if (!TryAddTemplate(eventTemplate, token, templates, failures))
                    continue;

                var isRandomSelectionEvent =
                    CollectionEncounterStructuredParser.TryParseEventOutcomeGroups(
                        token,
                        out var outcomeGroups
                    );
                var choiceGroups = isRandomSelectionEvent
                    ? Array.Empty<CollectionEncounterChoiceGroupData>()
                    : CollectionEncounterStructuredParser.TryParseEventChoiceGroups(token);
                var choiceLimit = CollectionEncounterStructuredParser.TryParseEventChoiceLimit(
                    token
                );

                foreach (var group in outcomeGroups)
                foreach (var id in group.Ids)
                    TryAddReferencedTemplate(
                        id,
                        cardMap,
                        templates,
                        failures,
                        missingReferencedTemplates
                    );
                foreach (var group in choiceGroups)
                foreach (var member in group.Members)
                    TryAddReferencedTemplate(
                        member.TemplateId,
                        cardMap,
                        templates,
                        failures,
                        missingReferencedTemplates
                    );

                events.Add(
                    new CollectionEncounterPreviewEventPlan(
                        eventTemplate.Id,
                        isRandomSelectionEvent,
                        suppressRandomOutcome: eventTemplate.Tags?.Contains(ECardTag.Merchant)
                            == true,
                        choiceLimit,
                        outcomeGroups,
                        choiceGroups
                    )
                );
            }
            catch (Exception)
            {
                failures.Add(eventTemplate.Id);
            }
        }

        var eventFailureCount = failures.Count;
        var levelUps = CompileLevelUps(
            levelUpMap,
            cardMap,
            templates,
            failures,
            missingReferencedTemplates,
            out var levelUpFailureCount,
            out var unsupportedLevelUpPartCount
        );

        var snapshot = new CollectionEncounterPreviewSnapshot(
            events.OrderBy(plan => plan.TemplateId),
            templates.Values.OrderBy(plan => plan.TemplateId),
            levelUps.OrderBy(plan => plan.Level),
            new CollectionPreviewCoverage(
                eventFailureCount,
                levelUpFailureCount,
                unsupportedLevelUpPartCount,
                missingReferencedTemplates.Count
            )
        );
        return new CollectionEncounterPreviewCompileResult(
            snapshot,
            failures.OrderBy(id => id).ToArray()
        );
    }

    private void TryAddReferencedTemplate(
        Guid templateId,
        IReadOnlyDictionary<Guid, ITCard> cardMap,
        Dictionary<Guid, CollectionEncounterPreviewTemplatePlan> templates,
        HashSet<Guid> failures,
        HashSet<Guid> missingReferencedTemplates
    )
    {
        if (templateId == Guid.Empty || templates.ContainsKey(templateId))
            return;
        if (!cardMap.TryGetValue(templateId, out var card) || card is not TCardBase template)
        {
            missingReferencedTemplates.Add(templateId);
            return;
        }

        try
        {
            JToken? preparedToken = null;
            if (
                template.Type == ECardType.EventEncounter
                || template.Type == ECardType.EncounterStep
            )
            {
                preparedToken = _prepareToken(template);
            }
            TryAddTemplate(template, preparedToken, templates, failures);
        }
        catch (Exception)
        {
            failures.Add(templateId);
        }
    }

    private void TryAddLevelUpReferencedTemplate(
        Guid templateId,
        IReadOnlyDictionary<Guid, ITCard> cardMap,
        Dictionary<Guid, CollectionEncounterPreviewTemplatePlan> templates,
        HashSet<Guid> failures,
        HashSet<Guid> missingReferencedTemplates
    ) =>
        TryAddReferencedTemplate(
            templateId,
            cardMap,
            templates,
            failures,
            missingReferencedTemplates
        );

    private List<CollectionLevelUpPreviewPlan> CompileLevelUps(
        IReadOnlyDictionary<int, TLevelUp> levelUpMap,
        IReadOnlyDictionary<Guid, ITCard> cardMap,
        Dictionary<Guid, CollectionEncounterPreviewTemplatePlan> templates,
        HashSet<Guid> failures,
        HashSet<Guid> missingReferencedTemplates,
        out int levelUpFailureCount,
        out int unsupportedPartCount
    )
    {
        var plans = new List<CollectionLevelUpPreviewPlan>();
        levelUpFailureCount = 0;
        unsupportedPartCount = 0;

        foreach (var (key, levelUp) in levelUpMap.OrderBy(pair => pair.Key))
        {
            try
            {
                var level = checked((int)levelUp.Level);
                if (key != level)
                {
                    levelUpFailureCount++;
                    continue;
                }

                var groups = new List<CollectionLevelUpPreviewGroup>();
                var isRandomSelection = false;
                if (levelUp.Rewards is TSpawnContextQuery query)
                {
                    isRandomSelection = query.SelectionMethod == ESpawnSelectionMethod.Random;
                    foreach (var group in query.Groups)
                    {
                        var heroConditions = new List<CollectionLevelUpPreviewHeroCondition>();
                        var skipBoardConditionalGroup = false;
                        if (group.Prerequisites != null)
                        {
                            foreach (var prerequisite in group.Prerequisites)
                            {
                                switch (prerequisite)
                                {
                                    case TPrerequisiteCardCount:
                                        skipBoardConditionalGroup = true;
                                        unsupportedPartCount++;
                                        break;
                                    case TPrerequisiteRun
                                    {
                                        Conditions: TRunConditionalPlayerHero heroCondition
                                    }:
                                        heroConditions.Add(
                                            new CollectionLevelUpPreviewHeroCondition(
                                                heroCondition.Heroes,
                                                heroCondition.Operator.ToString()
                                            )
                                        );
                                        break;
                                    default:
                                        unsupportedPartCount++;
                                        break;
                                }
                            }
                        }
                        if (skipBoardConditionalGroup)
                            continue;

                        var ids = new List<Guid>();
                        foreach (var filter in group.Filters)
                        {
                            if (filter is TSpawnFilterIdList idList)
                                ids.AddRange(idList.Ids);
                            else
                                unsupportedPartCount++;
                        }
                        if (ids.Count == 0)
                            continue;

                        var limit = 1;
                        if (group.Limit is TFixedValue fixedLimit)
                            limit = Math.Max(1, checked((int)fixedLimit.Value));
                        else if (group.Limit != null)
                            unsupportedPartCount++;

                        foreach (var id in ids)
                        {
                            TryAddLevelUpReferencedTemplate(
                                id,
                                cardMap,
                                templates,
                                failures,
                                missingReferencedTemplates
                            );
                        }
                        groups.Add(
                            new CollectionLevelUpPreviewGroup(
                                group.RandomWeight,
                                limit,
                                ids,
                                heroConditions
                            )
                        );
                    }
                }
                else
                {
                    unsupportedPartCount++;
                }

                if (levelUp.TutorialRewards != null)
                    unsupportedPartCount++;

                plans.Add(
                    new CollectionLevelUpPreviewPlan(
                        level,
                        checked((int)levelUp.HealthIncrease),
                        isRandomSelection,
                        groups
                    )
                );
            }
            catch (Exception)
            {
                levelUpFailureCount++;
            }
        }

        return plans;
    }

    private static bool TryAddTemplate(
        TCardBase template,
        JToken? preparedToken,
        Dictionary<Guid, CollectionEncounterPreviewTemplatePlan> templates,
        HashSet<Guid> failures
    )
    {
        if (templates.ContainsKey(template.Id))
            return true;

        try
        {
            CollectionEncounterRewardFilter? rewardFilter = null;
            if (
                template.Type == ECardType.EventEncounter
                || template.Type == ECardType.EncounterStep
            )
            {
                rewardFilter =
                    CollectionEncounterStructuredParser.TryParseRewardFilterWithPreparedToken(
                        template,
                        preparedToken
                    );
            }

            var localization = template.Localization;
            templates.Add(
                template.Id,
                new CollectionEncounterPreviewTemplatePlan(
                    template.Id,
                    Classify(template.Type),
                    template.Heroes?.OrderBy(hero => (int)hero).ToArray() ?? Array.Empty<EHero>(),
                    template.InternalName,
                    new CollectionEncounterPreviewLocalizedText(
                        localization?.Title?.Key,
                        localization?.Title?.Text
                    ),
                    new CollectionEncounterPreviewLocalizedText(
                        localization?.Description?.Key,
                        localization?.Description?.Text
                    ),
                    CollectionLocalizationResolver.CaptureAbilityValues(template),
                    rewardFilter
                )
            );
            return true;
        }
        catch (Exception)
        {
            failures.Add(template.Id);
            return false;
        }
    }

    private static CollectionEncounterPreviewTemplateKind Classify(ECardType type) =>
        type switch
        {
            ECardType.EventEncounter => CollectionEncounterPreviewTemplateKind.Event,
            ECardType.EncounterStep => CollectionEncounterPreviewTemplateKind.EncounterStep,
            ECardType.CombatEncounter => CollectionEncounterPreviewTemplateKind.CombatEncounter,
            ECardType.Skill => CollectionEncounterPreviewTemplateKind.Skill,
            ECardType.Item => CollectionEncounterPreviewTemplateKind.Item,
            _ => CollectionEncounterPreviewTemplateKind.Other,
        };
}
