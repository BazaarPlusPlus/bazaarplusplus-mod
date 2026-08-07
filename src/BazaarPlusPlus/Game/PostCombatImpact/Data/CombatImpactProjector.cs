#nullable enable
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.GameInterop.CombatSimulation;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactProjector
{
    private static readonly Guid FMinorTemplateId = Guid.Parse(
        "37251594-5ff0-4604-804e-7259ee666f60"
    );
    private static readonly Guid FNoteSocketEffectTemplateId = Guid.Parse(
        "04eca54a-69bf-4874-8b6d-56d284bb58be"
    );

    internal static CombatImpactReport Project(
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var executions = ProjectExecutions(simulation, entities);
        var events = new List<CombatImpactEvent>();

        foreach (var execution in executions)
        {
            if (
                execution.SourceId == null
                || execution.TargetId == null
                || !execution.Kind.HasValue
            )
                continue;

            var kind = execution.Kind.Value;
            var resolved = execution.Resolved;
            if (kind == CombatImpactKind.AttributeChange && !IsDisplayableAttributeChange(resolved))
                continue;
            var triggerProvenance = ResolveTriggerProvenance(execution, entities);

            events.Add(
                new CombatImpactEvent(
                    kind,
                    execution.SourceId,
                    execution.TargetId,
                    resolved.Value.HasValue ? SaturatingInt(resolved.Value.Value) : null,
                    resolved.Unit,
                    resolved.NativeAttributeKey,
                    resolved.IsCritical,
                    resolved.Basis
                )
                {
                    Surface = resolved.Surface,
                    OccurrenceBasis = CombatImpactOccurrenceBasis.ExplicitExecution,
                    CriticalCount = resolved.CriticalCount,
                    CriticalValue = resolved.CriticalValue,
                    NonCriticalValue = resolved.NonCriticalValue,
                    AlternateNonCriticalValue = resolved.AlternateNonCriticalValue,
                    HasCriticalAdjustmentCandidate = resolved.HasCriticalAdjustmentCandidate,
                    RawDirectSourceId = execution.DirectSourceId,
                    TriggerSourceId = execution.TriggerSourceId,
                    TriggerFrameIndex = execution.FrameIndex,
                    ActivitySourceResolution = triggerProvenance.SourceResolution,
                    TriggerScope = triggerProvenance.Scope,
                }
            );
        }

        AddCardActionCostEvents(simulation, entities, events);
        AddAuraAttributeEvents(simulation, entities, events);

        var useCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var authoritative = new Dictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>>(
            StringComparer.Ordinal
        );
        foreach (var (sourceId, stats) in simulation.CardStats)
        {
            if (stats.TryGetValue(ECardStats.UseCount, out var uses))
                useCounts[sourceId] = uses;

            var metrics = new List<CombatImpactAuthoritativeMetric>();
            AddAmountMetric(
                stats,
                ECardStats.DamageDone,
                CombatImpactKind.DirectDamage,
                CombatImpactAggregator.NativeKey(CombatImpactKind.DirectDamage),
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.BurnAdded,
                CombatImpactKind.Burn,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Burn),
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.PoisonAdded,
                CombatImpactKind.Poison,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Poison),
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.HealAdded,
                CombatImpactKind.Healing,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Healing),
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.ShieldAdded,
                CombatImpactKind.Shield,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Shield),
                metrics
            );
            AddApplicationMetric(
                stats,
                ECardStats.HastedCardsCount,
                CombatImpactKind.Haste,
                metrics
            );
            AddApplicationMetric(
                stats,
                ECardStats.SlowedCardsCount,
                CombatImpactKind.Slow,
                metrics
            );
            AddApplicationMetric(
                stats,
                ECardStats.FrozenCardsCount,
                CombatImpactKind.Freeze,
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.RegenAdded,
                CombatImpactKind.AttributeChange,
                "RegenApplyAmount",
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.RageAdded,
                CombatImpactKind.AttributeChange,
                "RageApplyAmount",
                metrics
            );
            if (OptionalCombatTempoTypes.TryGetAddedStat(out var tempoAdded))
            {
                AddAmountMetric(
                    stats,
                    tempoAdded,
                    CombatImpactKind.AttributeChange,
                    "TempoApplyAmount",
                    metrics
                );
            }
            if (OptionalCombatTempoTypes.TryGetSpentStat(out var tempoSpent))
            {
                AddAmountMetric(
                    stats,
                    tempoSpent,
                    CombatImpactKind.AttributeChange,
                    "TempoRemoveAmount",
                    metrics
                );
            }
            if (metrics.Count > 0)
                authoritative[sourceId] = metrics;
        }

        AddFMinorTempoEvents(entities, useCounts, events);
        RecoverDroppedAppliedEffectCriticals(events, authoritative);

        var report = CombatImpactAggregator.Aggregate(
            new CombatImpactProjectionInput(entities, events, useCounts, authoritative)
        );
        report = AttachPeriodicImpacts(
            report,
            PeriodicEffectAttribution.Project(simulation, entities)
        );
        return report;
    }

    private static void AddFMinorTempoEvents(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, int> useCounts,
        ICollection<CombatImpactEvent> events
    )
    {
        foreach (
            var skill in entities.Values.Where(entity =>
                entity.TemplateId == FMinorTemplateId
                && entity.TypeLabel == "Skill"
                && entity.CombatantId.HasValue
                && entity.Attributes?.TryGetValue(ECardAttributeType.Custom_0, out var amount)
                    == true
                && amount > 0
            )
        )
        {
            var combatantId = skill.CombatantId!.Value;
            var noteSockets = entities
                .Values.Where(entity =>
                    entity.TemplateId == FNoteSocketEffectTemplateId
                    && entity.CombatantId == combatantId
                    && entity.SocketId.HasValue
                )
                .Select(entity => entity.SocketId!.Value)
                .ToHashSet();
            if (noteSockets.Count == 0)
                continue;

            foreach (
                var item in entities.Values.Where(entity =>
                    entity.TypeLabel == "Item"
                    && entity.CombatantId == combatantId
                    && entity.SocketId.HasValue
                    && entity.HiddenTags?.Contains(EHiddenTag.Haste) == true
                    && OccupiesAnySocket(entity, noteSockets)
                    && useCounts.TryGetValue(entity.Id, out var uses)
                    && uses > 0
                )
            )
            {
                var amount = skill.Attributes![ECardAttributeType.Custom_0];
                var uses = useCounts[item.Id];
                for (var index = 0; index < uses; index++)
                {
                    events.Add(
                        new CombatImpactEvent(
                            CombatImpactKind.AttributeChange,
                            skill.Id,
                            PlayerId(combatantId),
                            amount,
                            CombatImpactValueUnit.Amount,
                            "TempoApplyAmount",
                            ValueBasis: CombatImpactValueBasis.ConfiguredActionAmount
                        )
                        {
                            Surface = CombatImpactEventSurface.AppliedEffect,
                            OccurrenceBasis = CombatImpactOccurrenceBasis.ReconstructedTransition,
                        }
                    );
                }
            }
        }
    }

    private static bool OccupiesAnySocket(
        CombatImpactEntity item,
        IReadOnlyCollection<EContainerSocketId> sockets
    )
    {
        var start = (int)item.SocketId!.Value;
        var end = start + Math.Max(1, item.DisplaySpan);
        return sockets.Any(socket => (int)socket >= start && (int)socket < end);
    }

    private static CombatImpactReport AttachPeriodicImpacts(
        CombatImpactReport report,
        PeriodicAttributionReport periodic
    )
    {
        return report with
        {
            PeriodicResiduals = periodic.Residuals,
            Sources = report
                .Sources.Select(source =>
                    source with
                    {
                        Groups = source
                            .Groups.Select(group =>
                            {
                                var kind = PeriodicKind(group);
                                var impact = kind.HasValue
                                    ? RollUpPeriodicImpact(
                                        periodic.SourceImpacts,
                                        source.Entity.Id,
                                        kind.Value,
                                        group
                                            .Targets.Select(target => target.Entity.CombatantId)
                                            .OfType<ECombatantId>()
                                            .ToHashSet()
                                    )
                                    : null;
                                return impact != null
                                    ? group with
                                    {
                                        PeriodicImpact = impact,
                                    }
                                    : group;
                            })
                            .ToArray(),
                    }
                )
                .ToArray(),
        };
    }

    private static CombatImpactPeriodicImpact? RollUpPeriodicImpact(
        IReadOnlyDictionary<PeriodicImpactKey, CombatImpactPeriodicImpact> sourceImpacts,
        string sourceId,
        CombatImpactPeriodicKind kind,
        IReadOnlyCollection<ECombatantId> targetCombatants
    )
    {
        var matches = sourceImpacts
            .Where(pair =>
                string.Equals(pair.Key.SourceId, sourceId, StringComparison.Ordinal)
                && pair.Key.Kind == kind
                && (targetCombatants.Count == 0 || targetCombatants.Contains(pair.Key.Combatant))
            )
            .Select(pair => pair.Value)
            .ToArray();
        if (matches.Length == 0)
            return null;

        return new CombatImpactPeriodicImpact(
            SaturatingInt(matches.Sum(impact => (long)impact.HealthAmount)),
            SaturatingInt(matches.Sum(impact => (long)impact.ShieldAmount)),
            matches.Any(impact => impact.Proof == CombatImpactPeriodicProof.Proportional)
                ? CombatImpactPeriodicProof.Proportional
                : CombatImpactPeriodicProof.Exact,
            PeriodicEffectAttribution.ModelVersion
        );
    }

    private static CombatImpactPeriodicKind? PeriodicKind(CombatImpactGroup group) =>
        group.Surface != CombatImpactEventSurface.AppliedEffect
            ? null
            : group.Kind switch
            {
                CombatImpactKind.Burn
                    when group.NativeAttributeKey
                        == CombatImpactAggregator.NativeKey(CombatImpactKind.Burn) =>
                    CombatImpactPeriodicKind.Burn,
                CombatImpactKind.Poison
                    when group.NativeAttributeKey
                        == CombatImpactAggregator.NativeKey(CombatImpactKind.Poison) =>
                    CombatImpactPeriodicKind.Poison,
                CombatImpactKind.AttributeChange
                    when group.NativeAttributeKey == "RegenApplyAmount" =>
                    CombatImpactPeriodicKind.Regen,
                _ => null,
            };

    private static IReadOnlyList<ProjectedExecution> ProjectExecutions(
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var projections = new List<ProjectedExecution>();
        var cardAttributes = CreateCardAttributeTimeline(entities);
        for (var frameIndex = 0; frameIndex < simulation.Frames.Count; frameIndex++)
        {
            var frame = simulation.Frames[frameIndex];
            ReconcileCardAttributeTimeline(frame, cardAttributes, usePreviousValue: true);
            var executed = frame.Events.OfType<CombatSimEventEffectExecuted>().ToArray();
            foreach (var item in executed)
            {
                var attributedSourceId = ResolveActivitySource(
                    item.Source?.Value,
                    item.TriggerSource?.Value,
                    entities
                );
                var targetId = ResolveTargetId(item.Target);
                var hasKind = TryResolveKind(item.ActionType, out var kind);
                var configuredActionValue = default(ResolvedImpactValue);
                var hasConfiguredDuration =
                    hasKind
                    && attributedSourceId != null
                    && TryResolveConfiguredDurationAction(
                        item.ActionType,
                        attributedSourceId,
                        cardAttributes,
                        out configuredActionValue
                    );
                var resolved =
                    hasConfiguredDuration ? configuredActionValue
                    : hasKind
                        ? ResolveValue(
                            frame,
                            item,
                            kind,
                            IsTransitionUnique(frame, executed, item, entities),
                            executed,
                            entities
                        )
                    : default;
                if (
                    hasKind
                    && kind == CombatImpactKind.DirectDamage
                    && HasCriticalHealthAdjustment(frame, item, kind)
                )
                    resolved = resolved with { HasCriticalAdjustmentCandidate = true };
                if (
                    attributedSourceId != null
                    && TryResolveNonCriticalValue(
                        item.ActionType,
                        attributedSourceId,
                        cardAttributes,
                        frame,
                        out var nonCriticalValue,
                        out var alternateNonCriticalValue
                    )
                )
                    resolved = resolved with
                    {
                        NonCriticalValue = nonCriticalValue,
                        AlternateNonCriticalValue = alternateNonCriticalValue,
                    };
                projections.Add(
                    new ProjectedExecution(
                        attributedSourceId,
                        targetId,
                        hasKind ? kind : null,
                        resolved,
                        item.Source?.Value,
                        item.TriggerSource?.Value,
                        frameIndex
                    )
                );
            }
            ReconcileCardAttributeTimeline(frame, cardAttributes, usePreviousValue: false);
        }
        return projections;
    }

    private static void AddCardActionCostEvents(
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        ICollection<CombatImpactEvent> events
    )
    {
        foreach (var frame in simulation.Frames)
        {
            foreach (var candidate in frame.Events)
            {
                if (
                    !CardActionCostSpentEventReader.TryRead(candidate, out var costSpent)
                    || costSpent.PlayerAttributeSpent != EPlayerAttributeType.Tempo
                )
                    continue;

                var sourceId = costSpent.ExecutingCard.Value;
                var targetId = ResolveCardActionCostTargetId(costSpent, entities);
                if (
                    !entities.TryGetValue(sourceId, out var source)
                    || source.TypeLabel is not ("Item" or "Skill")
                    || targetId == null
                )
                    continue;

                events.Add(
                    new CombatImpactEvent(
                        CombatImpactKind.AttributeChange,
                        sourceId,
                        targetId,
                        null,
                        CombatImpactValueUnit.Amount,
                        "TempoRemoveAmount",
                        ValueBasis: CombatImpactValueBasis.None
                    )
                    {
                        OccurrenceBasis = CombatImpactOccurrenceBasis.ExplicitExecution,
                    }
                );
            }
        }
    }

    private static Dictionary<
        string,
        Dictionary<ECardAttributeType, int>
    > CreateCardAttributeTimeline(IReadOnlyDictionary<string, CombatImpactEntity> entities)
    {
        var timeline = new Dictionary<string, Dictionary<ECardAttributeType, int>>(
            StringComparer.Ordinal
        );
        foreach (var entity in entities.Values)
        {
            if (entity.Attributes == null)
                continue;
            timeline[entity.Id] = new Dictionary<ECardAttributeType, int>(entity.Attributes);
        }

        return timeline;
    }

    private static void ReconcileCardAttributeTimeline(
        CombatSimFrame frame,
        IDictionary<string, Dictionary<ECardAttributeType, int>> timeline,
        bool usePreviousValue
    )
    {
        foreach (var (instanceId, cardUpdate) in frame.CardUpdates)
        {
            var cardId = instanceId.Value;
            if (!timeline.TryGetValue(cardId, out var attributes))
            {
                attributes = new Dictionary<ECardAttributeType, int>();
                timeline[cardId] = attributes;
            }

            foreach (var update in cardUpdate.Attributes.Values)
            {
                attributes[update.AttributeType] = usePreviousValue
                    ? update.PreviousValue
                    : update.CurrentValue;
            }
        }
    }

    private static bool TryResolveConfiguredDurationAction(
        EActionCommandType action,
        string sourceId,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        out ResolvedImpactValue resolved
    )
    {
        (ECardAttributeType Attribute, CombatImpactKind Kind)? configuration = action switch
        {
            EActionCommandType.CardCharge => (
                ECardAttributeType.ChargeAmount,
                CombatImpactKind.Charge
            ),
            EActionCommandType.CardHaste => (
                ECardAttributeType.HasteAmount,
                CombatImpactKind.Haste
            ),
            EActionCommandType.CardSlow => (ECardAttributeType.SlowAmount, CombatImpactKind.Slow),
            EActionCommandType.CardFreeze => (
                ECardAttributeType.FreezeAmount,
                CombatImpactKind.Freeze
            ),
            _ => null,
        };
        if (!configuration.HasValue)
        {
            resolved = default;
            return false;
        }

        var (attribute, kind) = configuration.Value;
        resolved = ResolvedImpactValue.Empty(kind, action);

        // Target frame deltas include fixed ticking and cannot reliably split concurrent actions.
        // The source value is the nominal outgoing duration, before target-side mitigation,
        // immunity, overlap, or truncation.
        if (
            cardAttributes.TryGetValue(sourceId, out var attributes)
            && attributes.TryGetValue(attribute, out var configuredAmount)
            && configuredAmount > 0
        )
        {
            resolved = new ResolvedImpactValue(
                configuredAmount,
                CombatImpactValueUnit.Milliseconds,
                CombatImpactAggregator.NativeKey(kind),
                false,
                CombatImpactValueBasis.ConfiguredActionAmount
            );
        }

        return true;
    }

    private static bool TryResolveNonCriticalValue(
        EActionCommandType action,
        string sourceId,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        CombatSimFrame frame,
        out int value,
        out int? alternateValue
    )
    {
        var attribute = action switch
        {
            EActionCommandType.PlayerDamage => ECardAttributeType.DamageAmount,
            EActionCommandType.PlayerBurnApply => ECardAttributeType.BurnApplyAmount,
            EActionCommandType.PlayerPoisonApply => ECardAttributeType.PoisonApplyAmount,
            EActionCommandType.PlayerRegenApply => ECardAttributeType.RegenApplyAmount,
            _ => (ECardAttributeType?)null,
        };
        if (
            !attribute.HasValue
            && OptionalCombatTempoTypes.TryGetCardAttribute(action, out var tempoAttribute)
        )
            attribute = tempoAttribute;
        if (
            attribute.HasValue
            && cardAttributes.TryGetValue(sourceId, out var attributes)
            && attributes.TryGetValue(attribute.Value, out value)
            && value > 0
        )
        {
            alternateValue = null;
            var sourceInstance = InstanceId.TryParse(sourceId);
            if (
                action == EActionCommandType.PlayerDamage
                && frame.CardUpdates.TryGetValue(sourceInstance, out var update)
                && update.Attributes.TryGetValue(attribute.Value, out var attributeUpdate)
                && attributeUpdate.CurrentValue > 0
                && attributeUpdate.CurrentValue != value
            )
                alternateValue = attributeUpdate.CurrentValue;
            return true;
        }

        value = 0;
        alternateValue = null;
        return false;
    }

    private static void RecoverDroppedAppliedEffectCriticals(
        IList<CombatImpactEvent> events,
        IReadOnlyDictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>> authoritative
    )
    {
        var candidates = events
            .Select((item, index) => new IndexedImpactEvent(index, item))
            .Where(item => IsRecoverableAppliedEffect(item.Event))
            .GroupBy(item => new CriticalRecoveryKey(
                item.Event.SourceId,
                item.Event.Kind,
                item.Event.NativeAttributeKey ?? string.Empty
            ));

        foreach (var group in candidates)
        {
            var indexedEvents = group.ToArray();
            if (
                group.Key.Kind == CombatImpactKind.DirectDamage
                && !indexedEvents.Any(item => item.Event.HasCriticalAdjustmentCandidate)
            )
                continue;
            if (
                indexedEvents.Any(item =>
                    item.Event.IsCritical
                    || item.Event.CriticalCount > 0
                    || item.Event.NonCriticalValue is not > 0
                ) || !authoritative.TryGetValue(group.Key.SourceId, out var sourceMetrics)
            )
                continue;

            var metric = sourceMetrics.SingleOrDefault(item =>
                item.Basis == CombatImpactAuthoritativeBasis.TotalAmount
                && item.Kind == group.Key.Kind
                && string.Equals(
                    item.NativeAttributeKey,
                    group.Key.NativeAttributeKey,
                    StringComparison.Ordinal
                )
            );
            if (metric == null)
                continue;

            var recovery = ResolveUniqueCriticalRecovery(
                indexedEvents.Select(item => item.Event).ToArray(),
                metric.Value
            );
            if (recovery is not { CriticalCount: > 0 })
                continue;

            var first = indexedEvents[0];
            events[first.Index] = first.Event with
            {
                CriticalCount = recovery.Value.CriticalCount,
                CriticalValue = recovery.Value.CriticalValue,
            };
        }
    }

    private static bool IsRecoverableAppliedEffect(CombatImpactEvent item) =>
        item.Surface == CombatImpactEventSurface.AppliedEffect
        && (
            item.Kind == CombatImpactKind.DirectDamage
                && string.Equals(
                    item.NativeAttributeKey,
                    CombatImpactAggregator.NativeKey(CombatImpactKind.DirectDamage),
                    StringComparison.Ordinal
                )
            || item.Kind == CombatImpactKind.Burn
                && string.Equals(
                    item.NativeAttributeKey,
                    CombatImpactAggregator.NativeKey(CombatImpactKind.Burn),
                    StringComparison.Ordinal
                )
            || item.Kind == CombatImpactKind.Poison
                && string.Equals(
                    item.NativeAttributeKey,
                    CombatImpactAggregator.NativeKey(CombatImpactKind.Poison),
                    StringComparison.Ordinal
                )
            || item.Kind == CombatImpactKind.AttributeChange
                && string.Equals(
                    item.NativeAttributeKey,
                    "RegenApplyAmount",
                    StringComparison.Ordinal
                )
        );

    private static bool HasCriticalHealthAdjustment(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind
    )
    {
        if (item.Target is not EffectTargetPlayer playerTarget)
            return false;

        var update =
            playerTarget.Target == ECombatantId.Player
                ? frame.PlayerUpdates
                : frame.OpponentUpdates;
        return update?.HealthAdjustments.Any(adjustment =>
                adjustment.IsCrit && Matches(kind, adjustment)
            ) == true;
    }

    private static CriticalRecoveryResult? ResolveUniqueCriticalRecovery(
        IReadOnlyList<CombatImpactEvent> events,
        long authoritativeTotal
    )
    {
        const int maximumStates = 50_000;
        const int maximumTransitions = 2_000_000;
        long transitions = 0;
        if (authoritativeTotal <= 0)
            return null;

        var states = new Dictionary<long, CriticalRecoveryRange>
        {
            [0] = new CriticalRecoveryRange(0, 0, 0, 0),
        };
        foreach (var item in events)
        {
            var baselineValues = new[] { item.NonCriticalValue, item.AlternateNonCriticalValue }
                .Where(value => value is > 0)
                .Select(value => value!.Value)
                .Distinct()
                .ToArray();
            if (baselineValues.Length == 0)
                return null;

            transitions += (long)states.Count * baselineValues.Length * 2;
            if (transitions > maximumTransitions)
                return null;

            var next = new Dictionary<long, CriticalRecoveryRange>();
            foreach (var (sum, range) in states)
            {
                foreach (var baseline in baselineValues)
                {
                    if (
                        !AddCriticalRecoveryState(
                            next,
                            sum + baseline,
                            range,
                            authoritativeTotal,
                            maximumStates
                        )
                        || !AddCriticalRecoveryState(
                            next,
                            sum + (long)baseline * 2,
                            new CriticalRecoveryRange(
                                range.MinimumCriticalCount + 1,
                                range.MaximumCriticalCount + 1,
                                range.MinimumCriticalValue + (long)baseline * 2,
                                range.MaximumCriticalValue + (long)baseline * 2
                            ),
                            authoritativeTotal,
                            maximumStates
                        )
                    )
                        return null;
                }
            }
            states = next;
        }

        if (
            !states.TryGetValue(authoritativeTotal, out var result)
            || result.MinimumCriticalCount != result.MaximumCriticalCount
            || result.MinimumCriticalCount <= 0
        )
            return null;

        return new CriticalRecoveryResult(
            result.MinimumCriticalCount,
            result.MinimumCriticalValue == result.MaximumCriticalValue
                ? SaturatingInt(result.MinimumCriticalValue)
                : null
        );
    }

    private static bool AddCriticalRecoveryState(
        IDictionary<long, CriticalRecoveryRange> states,
        long total,
        CriticalRecoveryRange candidate,
        long authoritativeTotal,
        int maximumStates
    )
    {
        if (total > authoritativeTotal)
            return true;

        if (states.TryGetValue(total, out var existing))
        {
            states[total] = new CriticalRecoveryRange(
                Math.Min(existing.MinimumCriticalCount, candidate.MinimumCriticalCount),
                Math.Max(existing.MaximumCriticalCount, candidate.MaximumCriticalCount),
                Math.Min(existing.MinimumCriticalValue, candidate.MinimumCriticalValue),
                Math.Max(existing.MaximumCriticalValue, candidate.MaximumCriticalValue)
            );
            return true;
        }

        states[total] = candidate;
        return states.Count <= maximumStates;
    }

    private static string? ResolveActivitySource(
        string? directSourceId,
        string? triggerSourceId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (IsActivityEntity(directSourceId, entities))
            return directSourceId;
        if (IsActivityEntity(triggerSourceId, entities))
            return triggerSourceId;

        return null;
    }

    private static TriggerProvenance ResolveTriggerProvenance(
        ProjectedExecution execution,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var directIsActivity = IsActivityEntity(execution.DirectSourceId, entities);
        var triggerIsActivity = IsActivityEntity(execution.TriggerSourceId, entities);
        if (!directIsActivity && triggerIsActivity)
        {
            return new TriggerProvenance(
                CombatImpactActivitySourceResolution.TriggerFallback,
                CombatImpactTriggerScope.AttributedViaTriggerFallback
            );
        }

        if (directIsActivity && triggerIsActivity)
        {
            return new TriggerProvenance(
                CombatImpactActivitySourceResolution.Direct,
                string.Equals(
                    execution.DirectSourceId,
                    execution.TriggerSourceId,
                    StringComparison.Ordinal
                )
                    ? CombatImpactTriggerScope.AttributedSelf
                    : CombatImpactTriggerScope.AttributedExternal
            );
        }

        if (directIsActivity && !string.IsNullOrWhiteSpace(execution.TriggerSourceId))
        {
            return new TriggerProvenance(
                CombatImpactActivitySourceResolution.Direct,
                CombatImpactTriggerScope.Unattributed
            );
        }

        return new TriggerProvenance(
            CombatImpactActivitySourceResolution.Direct,
            directIsActivity
                ? CombatImpactTriggerScope.NoTriggerEvidence
                : CombatImpactTriggerScope.NotApplicable
        );
    }

    private static bool TryResolveAbilityAttributeType(
        CombatSimEventEffectExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out ECardAttributeType attributeType
    ) =>
        TryResolveEffectAttributeType(
            effect.Source?.Value,
            effect.TriggerSource?.Value,
            effect.EffectId,
            entities,
            static entity => entity.AbilityAttributeTypesByEffectId,
            out attributeType
        );

    private static bool TryResolveAuraAttributeType(
        CombatSimEventEffectAuraExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out ECardAttributeType attributeType
    ) =>
        TryResolveEffectAttributeType(
            effect.Source?.Value,
            effect.TriggerSource?.Value,
            effect.EffectId,
            entities,
            static entity => entity.AuraAttributeTypesByEffectId,
            out attributeType
        );

    private static bool TryResolveEffectAttributeType(
        string? directSourceId,
        string? triggerSourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        Func<CombatImpactEntity, IReadOnlyDictionary<string, ECardAttributeType>?> selectMappings,
        out ECardAttributeType attributeType
    )
    {
        if (
            TryResolveEffectAttributeType(
                directSourceId,
                effectId,
                entities,
                selectMappings,
                out attributeType
            )
        )
            return true;
        if (
            !string.Equals(directSourceId, triggerSourceId, StringComparison.Ordinal)
            && TryResolveEffectAttributeType(
                triggerSourceId,
                effectId,
                entities,
                selectMappings,
                out attributeType
            )
        )
            return true;

        attributeType = default;
        return false;
    }

    private static bool TryResolveEffectAttributeType(
        string? sourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        Func<CombatImpactEntity, IReadOnlyDictionary<string, ECardAttributeType>?> selectMappings,
        out ECardAttributeType attributeType
    )
    {
        if (
            !string.IsNullOrWhiteSpace(sourceId)
            && !string.IsNullOrWhiteSpace(effectId)
            && entities.TryGetValue(sourceId!, out var source)
            && selectMappings(source)?.TryGetValue(effectId!, out attributeType) == true
        )
            return true;

        attributeType = default;
        return false;
    }

    private static bool IsActivityEntity(
        string? entityId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        !string.IsNullOrWhiteSpace(entityId)
        && entities.TryGetValue(entityId!, out var entity)
        && entity.TypeLabel is "Item" or "Skill";

    private static bool IsDisplayableAttributeChange(ResolvedImpactValue resolved) =>
        resolved.NativeAttributeKey is not ("CardModifyAttribute" or "PlayerModifyAttribute")
        && !resolved.NativeAttributeKey.StartsWith("Custom_", StringComparison.Ordinal);

    private static void AddAuraAttributeEvents(
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        ICollection<CombatImpactEvent> events
    )
    {
        foreach (var frame in simulation.Frames)
        {
            var candidates = new List<AuraAttributeCandidate>();
            var playerCandidates = new List<AuraPlayerAttributeCandidate>();
            foreach (var aura in frame.Events.OfType<CombatSimEventEffectAuraExecuted>())
            {
                var sourceId = ResolveActivitySource(
                    aura.Source?.Value,
                    aura.TriggerSource?.Value,
                    entities
                );
                if (sourceId == null)
                    continue;
                var hasExpectedCardAttribute = TryResolveAuraAttributeType(
                    aura,
                    entities,
                    out var expectedCardAttribute
                );
                var isReferenceValuedAura = IsReferenceValuedAuraEffect(aura, entities);

                foreach (var target in aura.AppliedTo.Concat(aura.RemovedFrom))
                {
                    if (target is EffectTargetPlayer playerTarget)
                    {
                        var playerId = PlayerId(playerTarget.Target);
                        var playerUpdate =
                            playerTarget.Target == ECombatantId.Player
                                ? frame.PlayerUpdates
                                : frame.OpponentUpdates;
                        var playerChanges = playerUpdate
                            ?.Attributes.Values.Where(change =>
                                change.Delta != 0
                                && IsDisplayableAuraPlayerAttribute(change.AttributeType)
                            )
                            .ToArray();
                        if (playerChanges?.Length == 1 && entities.ContainsKey(playerId))
                        {
                            playerCandidates.Add(
                                new AuraPlayerAttributeCandidate(
                                    sourceId,
                                    playerId,
                                    playerChanges[0]
                                )
                            );
                        }
                        continue;
                    }

                    if (
                        target is not EffectTargetCard cardTarget
                        || !entities.ContainsKey(cardTarget.Target.Value)
                        || !frame.CardUpdates.TryGetValue(cardTarget.Target, out var cardUpdate)
                    )
                        continue;
                    if (
                        isReferenceValuedAura
                        && string.Equals(
                            sourceId,
                            cardTarget.Target.Value,
                            StringComparison.Ordinal
                        )
                    )
                        continue;

                    CombatSimCardAttributeUpdate cardChange;
                    if (hasExpectedCardAttribute)
                    {
                        if (
                            !IsDisplayableAuraAttribute(expectedCardAttribute)
                            || !cardUpdate.Attributes.TryGetValue(
                                expectedCardAttribute,
                                out var expectedChange
                            )
                            || expectedChange.Delta == 0
                            || HasExplicitModifierClaim(
                                frame,
                                cardTarget.Target,
                                expectedCardAttribute,
                                entities
                            )
                        )
                            continue;
                        cardChange = expectedChange;
                    }
                    else
                    {
                        var cardChanges = cardUpdate
                            .Attributes.Values.Where(change =>
                                change.Delta != 0
                                && IsDisplayableAuraAttribute(change.AttributeType)
                            )
                            .ToArray();
                        if (
                            cardChanges.Length != 1
                            || HasExplicitModifierClaim(frame, cardTarget.Target)
                        )
                            continue;
                        cardChange = cardChanges[0];
                    }

                    candidates.Add(
                        new AuraAttributeCandidate(sourceId, cardTarget.Target.Value, cardChange)
                    );
                }
            }

            foreach (
                var candidate in candidates
                    .GroupBy(candidate => (candidate.TargetId, candidate.Change.AttributeType))
                    .Where(group => group.Count() == 1)
                    .Select(group => group.Single())
            )
            {
                events.Add(
                    new CombatImpactEvent(
                        CombatImpactKind.AttributeChange,
                        candidate.SourceId,
                        candidate.TargetId,
                        candidate.Change.Delta,
                        UnitFor(candidate.Change.AttributeType.ToString()),
                        candidate.Change.AttributeType.ToString(),
                        ValueBasis: CombatImpactValueBasis.NetFrameDelta
                    )
                    {
                        Surface = CombatImpactEventSurface.CardAttribute,
                        OccurrenceBasis = CombatImpactOccurrenceBasis.ReconstructedTransition,
                    }
                );
            }

            foreach (
                var candidate in playerCandidates
                    .GroupBy(candidate => (candidate.TargetId, candidate.Change.AttributeType))
                    .Where(group => group.Count() == 1)
                    .Select(group => group.Single())
            )
            {
                events.Add(
                    new CombatImpactEvent(
                        CombatImpactKind.AttributeChange,
                        candidate.SourceId,
                        candidate.TargetId,
                        candidate.Change.Delta,
                        UnitFor(candidate.Change.AttributeType.ToString()),
                        candidate.Change.AttributeType.ToString(),
                        ValueBasis: CombatImpactValueBasis.NetFrameDelta
                    )
                    {
                        Surface = CombatImpactEventSurface.PlayerAttribute,
                        OccurrenceBasis = CombatImpactOccurrenceBasis.ReconstructedTransition,
                    }
                );
            }
        }
    }

    private static bool IsReferenceValuedAuraEffect(
        CombatSimEventEffectAuraExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        HasReferenceValuedAuraEffect(effect.Source?.Value, effect.EffectId, entities)
        || HasReferenceValuedAuraEffect(effect.TriggerSource?.Value, effect.EffectId, entities);

    private static bool HasReferenceValuedAuraEffect(
        string? sourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        !string.IsNullOrWhiteSpace(sourceId)
        && !string.IsNullOrWhiteSpace(effectId)
        && entities.TryGetValue(sourceId!, out var source)
        && source.ReferenceValuedAuraEffectIds?.Contains(effectId!) == true;

    private static bool HasExplicitModifierClaim(CombatSimFrame frame, InstanceId target) =>
        frame
            .Events.OfType<CombatSimEventEffectExecuted>()
            .Any(item =>
                item.ActionType == EActionCommandType.CardModifyAttribute
                && item.Target is EffectTargetCard cardTarget
                && cardTarget.Target == target
            );

    private static bool HasExplicitModifierClaim(
        CombatSimFrame frame,
        InstanceId target,
        ECardAttributeType attributeType,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        frame
            .Events.OfType<CombatSimEventEffectExecuted>()
            .Any(item =>
                item.ActionType == EActionCommandType.CardModifyAttribute
                && item.Target is EffectTargetCard cardTarget
                && cardTarget.Target == target
                && (
                    !TryResolveAbilityAttributeType(item, entities, out var claimedAttribute)
                    || claimedAttribute == attributeType
                )
            );

    private static bool IsAttributableCardAttribute(ECardAttributeType attribute) =>
        IsDisplayableAuraAttribute(attribute)
        || attribute.ToString().StartsWith("Custom_", StringComparison.Ordinal);

    private static bool IsDisplayableAuraAttribute(ECardAttributeType attribute) =>
        OptionalCombatTempoTypes.IsAmountAttribute(attribute)
        || attribute
            is ECardAttributeType.AmmoMax
                or ECardAttributeType.ReloadAmount
                or ECardAttributeType.ReloadTargets
                or ECardAttributeType.CooldownMax
                or ECardAttributeType.ChargeAmount
                or ECardAttributeType.ChargeTargets
                or ECardAttributeType.HasteAmount
                or ECardAttributeType.HasteTargets
                or ECardAttributeType.SlowAmount
                or ECardAttributeType.SlowTargets
                or ECardAttributeType.FreezeAmount
                or ECardAttributeType.FreezeTargets
                or ECardAttributeType.BurnApplyAmount
                or ECardAttributeType.BurnRemoveAmount
                or ECardAttributeType.PoisonApplyAmount
                or ECardAttributeType.PoisonRemoveAmount
                or ECardAttributeType.Multicast
                or ECardAttributeType.Lifesteal
                or ECardAttributeType.CritChance
                or ECardAttributeType.DamageAmount
                or ECardAttributeType.DamageCrit
                or ECardAttributeType.HealAmount
                or ECardAttributeType.HealCrit
                or ECardAttributeType.JoyApplyAmount
                or ECardAttributeType.JoyRemoveAmount
                or ECardAttributeType.JoyCrit
                or ECardAttributeType.ShieldApplyAmount
                or ECardAttributeType.ShieldRemoveAmount
                or ECardAttributeType.ShieldCrit
                or ECardAttributeType.ForceUseTargets
                or ECardAttributeType.EnchantTargets
                or ECardAttributeType.UpgradeTargets
                or ECardAttributeType.DisableTargets
                or ECardAttributeType.RepairTargets
                or ECardAttributeType.BurnCrit
                or ECardAttributeType.PoisonCrit
                or ECardAttributeType.DestroyTargets
                or ECardAttributeType.RegenApplyAmount
                or ECardAttributeType.RegenRemoveAmount
                or ECardAttributeType.RegenCrit
                or ECardAttributeType.TransformTargets
                or ECardAttributeType.FlatCooldownReduction
                or ECardAttributeType.PercentCooldownReduction
                or ECardAttributeType.EnchantRemoveTargets
                or ECardAttributeType.FlyingTargets
                or ECardAttributeType.PercentChargeReduction
                or ECardAttributeType.PercentHasteReduction
                or ECardAttributeType.PercentSlowReduction
                or ECardAttributeType.PercentFreezeReduction
                or ECardAttributeType.DestroyImmunity
                or ECardAttributeType.RageApplyAmount
                or ECardAttributeType.RageRemoveAmount
                or ECardAttributeType.TempoCost
                or ECardAttributeType.FlatTempoCostReduction
                or ECardAttributeType.PercentTempoCostReduction
                or ECardAttributeType.BuyPrice
                or ECardAttributeType.SellPrice;

    private static bool IsDisplayableAuraPlayerAttribute(EPlayerAttributeType attribute) =>
        attribute
            is EPlayerAttributeType.CritChance
                or EPlayerAttributeType.DamageCrit
                or EPlayerAttributeType.JoyCrit
                or EPlayerAttributeType.HealthMax
                or EPlayerAttributeType.HealthRegen
                or EPlayerAttributeType.HealAmount
                or EPlayerAttributeType.HealCrit
                or EPlayerAttributeType.ShieldCrit
                or EPlayerAttributeType.FlatDamageReduction
                or EPlayerAttributeType.PercentDamageReduction
                or EPlayerAttributeType.Rage
                or EPlayerAttributeType.RageMax
                or EPlayerAttributeType.EnragedDurationMax
                or EPlayerAttributeType.Tempo
                or EPlayerAttributeType.TempoGainCooldownMax
                or EPlayerAttributeType.FlatTempoGainCooldownReduction
                or EPlayerAttributeType.PercentTempoGainCooldownReduction
                or EPlayerAttributeType.Experience
                or EPlayerAttributeType.Gold
                or EPlayerAttributeType.Income
                or EPlayerAttributeType.Prestige
                or EPlayerAttributeType.Level
                or EPlayerAttributeType.RerollCostModifier;

    internal static string PlayerId(ECombatantId combatant) => $"player:{combatant}";

    private static string? ResolveTargetId(IEffectTarget? target) =>
        target switch
        {
            EffectTargetCard card => card.Target.Value,
            EffectTargetPlayer player => PlayerId(player.Target),
            _ => null,
        };

    private static bool IsTransitionUnique(
        CombatSimFrame frame,
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        CombatSimEventEffectExecuted candidate,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (!TryResolveTransitionClaim(candidate.ActionType, out var transition))
            return false;
        var targetId = ResolveTargetId(candidate.Target);
        var claimCount = executed.Count(item =>
            string.Equals(ResolveTargetId(item.Target), targetId, StringComparison.Ordinal)
            && ClaimsTransition(item.ActionType, transition)
        );
        if (
            transition.Domain == ImpactTransitionDomain.PlayerAttribute
            && transition.Attribute == (int)EPlayerAttributeType.Tempo
        )
        {
            claimCount += frame.Events.Count(candidate =>
                CardActionCostSpentEventReader.TryRead(candidate, out var costSpent)
                && costSpent.PlayerAttributeSpent == EPlayerAttributeType.Tempo
                && string.Equals(
                    ResolveCardActionCostTargetId(costSpent, entities),
                    targetId,
                    StringComparison.Ordinal
                )
            );
        }

        return claimCount == 1;
    }

    private static string? ResolveCardActionCostTargetId(
        CardActionCostSpentEvent costSpent,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (
            !entities.TryGetValue(costSpent.ExecutingCard.Value, out var source)
            || !source.CombatantId.HasValue
        )
            return null;

        var targetId = PlayerId(source.CombatantId.Value);
        return entities.ContainsKey(targetId) ? targetId : null;
    }

    private static bool ClaimsTransition(
        EActionCommandType action,
        ImpactTransitionClaim transition
    ) => TryResolveTransitionClaim(action, out var candidate) && candidate == transition;

    private static bool TryResolveTransitionClaim(
        EActionCommandType action,
        out ImpactTransitionClaim transition
    )
    {
        if (OptionalCombatTempoTypes.IsAction(action))
        {
            transition = new ImpactTransitionClaim(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.Tempo
            );
            return true;
        }

        transition = action switch
        {
            EActionCommandType.PlayerDamage => new(
                ImpactTransitionDomain.HealthAdjustment,
                (int)CombatImpactKind.DirectDamage
            ),
            EActionCommandType.PlayerHeal => new(
                ImpactTransitionDomain.HealthAdjustment,
                (int)CombatImpactKind.Healing
            ),
            EActionCommandType.PlayerShieldApply or EActionCommandType.PlayerShieldRemove => new(
                ImpactTransitionDomain.HealthAdjustment,
                (int)CombatImpactKind.Shield
            ),
            EActionCommandType.PlayerBurnApply or EActionCommandType.PlayerBurnRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.Burn
            ),
            EActionCommandType.PlayerPoisonApply or EActionCommandType.PlayerPoisonRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.Poison
            ),
            EActionCommandType.PlayerRegenApply or EActionCommandType.PlayerRegenRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.HealthRegen
            ),
            EActionCommandType.PlayerRageApply or EActionCommandType.PlayerRageRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.Rage
            ),
            EActionCommandType.PlayerMaxHealthIncrease
            or EActionCommandType.PlayerMaxHealthDecrease => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.HealthMax
            ),
            EActionCommandType.CardHaste => new(
                ImpactTransitionDomain.CardAttribute,
                (int)ECardAttributeType.Haste
            ),
            EActionCommandType.CardSlow => new(
                ImpactTransitionDomain.CardAttribute,
                (int)ECardAttributeType.Slow
            ),
            EActionCommandType.CardFreeze => new(
                ImpactTransitionDomain.CardAttribute,
                (int)ECardAttributeType.Freeze
            ),
            EActionCommandType.CardReload => new(
                ImpactTransitionDomain.CardAttribute,
                (int)ECardAttributeType.Ammo
            ),
            _ => default,
        };
        return action
            is EActionCommandType.PlayerDamage
                or EActionCommandType.PlayerHeal
                or EActionCommandType.PlayerShieldApply
                or EActionCommandType.PlayerShieldRemove
                or EActionCommandType.PlayerBurnApply
                or EActionCommandType.PlayerBurnRemove
                or EActionCommandType.PlayerPoisonApply
                or EActionCommandType.PlayerPoisonRemove
                or EActionCommandType.PlayerRegenApply
                or EActionCommandType.PlayerRegenRemove
                or EActionCommandType.PlayerRageApply
                or EActionCommandType.PlayerRageRemove
                or EActionCommandType.PlayerMaxHealthIncrease
                or EActionCommandType.PlayerMaxHealthDecrease
                or EActionCommandType.CardHaste
                or EActionCommandType.CardSlow
                or EActionCommandType.CardFreeze
                or EActionCommandType.CardReload;
    }

    internal static bool HasExplicitDisplayClassification(EActionCommandType action) =>
        TryResolveKind(action, out _) || IsExplicitlyIgnoredAction(action);

    internal static bool TryResolveKind(EActionCommandType action, out CombatImpactKind kind)
    {
        if (OptionalCombatTempoTypes.IsAction(action))
        {
            kind = CombatImpactKind.AttributeChange;
            return true;
        }

        kind = action switch
        {
            EActionCommandType.PlayerDamage => CombatImpactKind.DirectDamage,
            EActionCommandType.PlayerBurnApply => CombatImpactKind.Burn,
            EActionCommandType.PlayerPoisonApply => CombatImpactKind.Poison,
            EActionCommandType.PlayerHeal => CombatImpactKind.Healing,
            EActionCommandType.PlayerShieldApply => CombatImpactKind.Shield,
            EActionCommandType.CardCharge => CombatImpactKind.Charge,
            EActionCommandType.CardHaste => CombatImpactKind.Haste,
            EActionCommandType.CardSlow => CombatImpactKind.Slow,
            EActionCommandType.CardFreeze => CombatImpactKind.Freeze,
            EActionCommandType.FlyingStart or EActionCommandType.FlyingStop =>
                CombatImpactKind.Flying,
            EActionCommandType.CardReload => CombatImpactKind.AttributeChange,
            EActionCommandType.CardForceUse
            or EActionCommandType.CardEnchant
            or EActionCommandType.CardEnchantRemove
            or EActionCommandType.CardTransform
            or EActionCommandType.CardTransformDestroyed
            or EActionCommandType.CardUpgrade
            or EActionCommandType.CardRepair => CombatImpactKind.AttributeChange,
            EActionCommandType.CardModifyAttribute => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerModifyAttribute => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerMaxHealthIncrease => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerMaxHealthDecrease => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerRegenApply => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerRageApply => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerBurnRemove
            or EActionCommandType.PlayerPoisonRemove
            or EActionCommandType.PlayerRegenRemove
            or EActionCommandType.PlayerShieldRemove
            or EActionCommandType.PlayerRageRemove => CombatImpactKind.AttributeChange,
            EActionCommandType.CardDisable or EActionCommandType.CardDestroy =>
                CombatImpactKind.Destroy,
            _ => default,
        };
        return action
            is EActionCommandType.PlayerDamage
                or EActionCommandType.PlayerBurnApply
                or EActionCommandType.PlayerPoisonApply
                or EActionCommandType.PlayerHeal
                or EActionCommandType.PlayerShieldApply
                or EActionCommandType.CardCharge
                or EActionCommandType.CardHaste
                or EActionCommandType.CardSlow
                or EActionCommandType.CardFreeze
                or EActionCommandType.FlyingStart
                or EActionCommandType.FlyingStop
                or EActionCommandType.CardReload
                or EActionCommandType.CardForceUse
                or EActionCommandType.CardEnchant
                or EActionCommandType.CardEnchantRemove
                or EActionCommandType.CardTransform
                or EActionCommandType.CardTransformDestroyed
                or EActionCommandType.CardUpgrade
                or EActionCommandType.CardRepair
                or EActionCommandType.CardModifyAttribute
                or EActionCommandType.PlayerModifyAttribute
                or EActionCommandType.PlayerMaxHealthIncrease
                or EActionCommandType.PlayerMaxHealthDecrease
                or EActionCommandType.PlayerRegenApply
                or EActionCommandType.PlayerRageApply
                or EActionCommandType.PlayerBurnRemove
                or EActionCommandType.PlayerPoisonRemove
                or EActionCommandType.PlayerRegenRemove
                or EActionCommandType.PlayerShieldRemove
                or EActionCommandType.PlayerRageRemove
                or EActionCommandType.CardDisable
                or EActionCommandType.CardDestroy;
    }

    private static bool IsExplicitlyIgnoredAction(EActionCommandType action) =>
        action
            is EActionCommandType.None
                or EActionCommandType.CardAddTags
                or EActionCommandType.CardRemoveTags
                or EActionCommandType.CardBeginSandstorm
                or EActionCommandType.GameDealCards
                or EActionCommandType.GameModifyTime
                or EActionCommandType.GameScheduleEncounter
                or EActionCommandType.GameSpawnCards
                or EActionCommandType.GameStartCombat
                or EActionCommandType.GameUnscheduleEncounter
                or EActionCommandType.PlayerGoldSteal
                or EActionCommandType.PlayerJoyApply
                or EActionCommandType.PlayerJoyRemove
                or EActionCommandType.ExitReplacementSet
                or EActionCommandType.CardAddTagsRandom
                or EActionCommandType.PlayerPortraitNext
                or EActionCommandType.PlayerPortraitReset
                or EActionCommandType.GameReroll;

    private static ResolvedImpactValue ResolveValue(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind,
        bool transitionIsUnique,
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (
            kind == CombatImpactKind.AttributeChange
            && item.ActionType
                is EActionCommandType.CardModifyAttribute
                    or EActionCommandType.PlayerModifyAttribute
        )
            return ResolveConcreteAttributeTransition(frame, item, kind, executed, entities);

        if (TryResolveCategoricalCardAction(frame, item, kind, out var categoricalAction))
            return categoricalAction;

        if (!transitionIsUnique)
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        if (item.Target is EffectTargetPlayer playerTarget)
        {
            var update =
                playerTarget.Target == ECombatantId.Player
                    ? frame.PlayerUpdates
                    : frame.OpponentUpdates;
            if (update == null)
                return ResolvedImpactValue.Empty(kind, item.ActionType);

            if (item.ActionType == EActionCommandType.PlayerShieldRemove)
            {
                return ResolveShieldRemoval(update)
                    ?? ResolvedImpactValue.Empty(kind, item.ActionType);
            }

            if (
                kind
                is CombatImpactKind.DirectDamage
                    or CombatImpactKind.Healing
                    or CombatImpactKind.Shield
            )
            {
                return ResolveHealthAdjustment(update, kind)
                    ?? ResolvedImpactValue.Empty(kind, item.ActionType);
            }

            EPlayerAttributeType? expectedAttribute = OptionalCombatTempoTypes.IsAction(
                item.ActionType
            )
                ? EPlayerAttributeType.Tempo
                : item.ActionType switch
                {
                    EActionCommandType.PlayerBurnApply or EActionCommandType.PlayerBurnRemove =>
                        EPlayerAttributeType.Burn,
                    EActionCommandType.PlayerPoisonApply or EActionCommandType.PlayerPoisonRemove =>
                        EPlayerAttributeType.Poison,
                    EActionCommandType.PlayerRegenApply or EActionCommandType.PlayerRegenRemove =>
                        EPlayerAttributeType.HealthRegen,
                    EActionCommandType.PlayerRageApply or EActionCommandType.PlayerRageRemove =>
                        EPlayerAttributeType.Rage,
                    EActionCommandType.PlayerMaxHealthIncrease => EPlayerAttributeType.HealthMax,
                    EActionCommandType.PlayerMaxHealthDecrease => EPlayerAttributeType.HealthMax,
                    _ => (EPlayerAttributeType?)null,
                };
            if (
                expectedAttribute.HasValue
                && update.Attributes.TryGetValue(expectedAttribute.Value, out var attribute)
                && MatchesExpectedPlayerDelta(item.ActionType, attribute.Delta)
            )
            {
                return new ResolvedImpactValue(
                    IsPlayerRemovalAction(item.ActionType)
                        ? Math.Abs(attribute.Delta)
                        : attribute.Delta,
                    UnitFor(expectedAttribute.Value.ToString()),
                    NativeKeyForAction(item.ActionType, expectedAttribute.Value.ToString()),
                    false,
                    CombatImpactValueBasis.NetFrameDelta
                );
            }

            return ResolvedImpactValue.Empty(kind, item.ActionType);
        }

        if (
            item.Target is not EffectTargetCard cardTarget
            || !frame.CardUpdates.TryGetValue(cardTarget.Target, out var cardUpdate)
        )
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        if (kind == CombatImpactKind.Destroy)
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        if (item.ActionType == EActionCommandType.CardReload)
        {
            if (
                cardUpdate.Attributes.TryGetValue(ECardAttributeType.Ammo, out var ammo)
                && ammo.Delta > 0
            )
            {
                return new ResolvedImpactValue(
                    ammo.Delta,
                    CombatImpactValueUnit.Amount,
                    "ReloadAmount",
                    false,
                    CombatImpactValueBasis.NetFrameDelta
                );
            }

            return ResolvedImpactValue.Empty(kind, item.ActionType);
        }

        return ResolvedImpactValue.Empty(kind, item.ActionType);
    }

    private static bool TryResolveCategoricalCardAction(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind,
        out ResolvedImpactValue resolved
    )
    {
        var nativeKey = item.ActionType switch
        {
            EActionCommandType.CardForceUse => "ForceUseTargets",
            EActionCommandType.CardEnchant => ResolveEnchantActionKey(frame, item),
            EActionCommandType.CardEnchantRemove => "EnchantRemoveTargets",
            EActionCommandType.CardTransform or EActionCommandType.CardTransformDestroyed =>
                "TransformTargets",
            EActionCommandType.CardUpgrade => "UpgradeTargets",
            EActionCommandType.CardRepair => "RepairTargets",
            _ => null,
        };
        if (nativeKey == null)
        {
            resolved = default;
            return false;
        }

        resolved = ResolvedImpactValue.Empty(kind, item.ActionType) with
        {
            NativeAttributeKey = nativeKey,
        };
        return true;
    }

    private static string ResolveEnchantActionKey(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item
    )
    {
        if (item.Target is not EffectTargetCard cardTarget)
            return "EnchantTargets";

        var targetId = cardTarget.Target.Value;
        var matchingExecutions = frame
            .Events.OfType<CombatSimEventEffectExecuted>()
            .Where(candidate =>
                candidate.ActionType == EActionCommandType.CardEnchant
                && string.Equals(
                    ResolveTargetId(candidate.Target),
                    targetId,
                    StringComparison.Ordinal
                )
            )
            .Take(2)
            .Count();
        var enchantments = frame
            .Events.OfType<CombatSimEventCardEnchanted>()
            .Where(candidate =>
                string.Equals(candidate.InstanceId, targetId, StringComparison.Ordinal)
                && !candidate.IsReverted
                && candidate.EnchantmentType.HasValue
            )
            .Select(candidate => candidate.EnchantmentType!.Value)
            .Distinct()
            .Take(2)
            .ToArray();
        return matchingExecutions == 1 && enchantments.Length == 1
            ? $"EnchantTargets:{enchantments[0]}"
            : "EnchantTargets";
    }

    private static ResolvedImpactValue ResolveConcreteAttributeTransition(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind,
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (item.ActionType == EActionCommandType.PlayerModifyAttribute)
        {
            if (item.Target is not EffectTargetPlayer playerTarget)
                return ResolvedImpactValue.Empty(kind, item.ActionType);
            var playerUpdate =
                playerTarget.Target == ECombatantId.Player
                    ? frame.PlayerUpdates
                    : frame.OpponentUpdates;
            var changes = playerUpdate
                ?.Attributes.Values.Where(update =>
                    update.Delta != 0 && IsDisplayableAuraPlayerAttribute(update.AttributeType)
                )
                .ToArray();
            if (changes?.Length != 1)
                return ResolvedImpactValue.Empty(kind, item.ActionType);

            var change = changes[0];
            if (
                IsClaimedByAnotherExecution(
                    executed,
                    item,
                    new ImpactTransitionClaim(
                        ImpactTransitionDomain.PlayerAttribute,
                        (int)change.AttributeType
                    )
                )
            )
                return ResolvedImpactValue.Empty(kind, item.ActionType);

            return new ResolvedImpactValue(
                change.Delta,
                UnitFor(change.AttributeType.ToString()),
                change.AttributeType.ToString(),
                false,
                CombatImpactValueBasis.NetFrameDelta
            )
            {
                Surface = CombatImpactEventSurface.PlayerAttribute,
            };
        }

        if (
            item.Target is not EffectTargetCard cardTarget
            || !frame.CardUpdates.TryGetValue(cardTarget.Target, out var cardUpdate)
        )
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        CombatSimCardAttributeUpdate cardChange;
        if (TryResolveAbilityAttributeType(item, entities, out var expectedAttribute))
        {
            if (
                !IsAttributableCardAttribute(expectedAttribute)
                || !cardUpdate.Attributes.TryGetValue(expectedAttribute, out var expectedChange)
                || expectedChange.Delta == 0
            )
                return ResolvedImpactValue.Empty(kind, item.ActionType);
            cardChange = expectedChange;
        }
        else
        {
            var cardChanges = cardUpdate
                .Attributes.Values.Where(update =>
                    update.Delta != 0 && IsAttributableCardAttribute(update.AttributeType)
                )
                .ToArray();
            if (cardChanges.Length != 1)
                return ResolvedImpactValue.Empty(kind, item.ActionType);
            cardChange = cardChanges[0];
        }
        if (
            IsClaimedByAnotherExecution(
                executed,
                item,
                new ImpactTransitionClaim(
                    ImpactTransitionDomain.CardAttribute,
                    (int)cardChange.AttributeType
                ),
                entities
            )
        )
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        return new ResolvedImpactValue(
            cardChange.Delta,
            UnitFor(cardChange.AttributeType.ToString()),
            cardChange.AttributeType.ToString(),
            false,
            CombatImpactValueBasis.NetFrameDelta
        )
        {
            Surface = CombatImpactEventSurface.CardAttribute,
        };
    }

    private static bool IsClaimedByAnotherExecution(
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        CombatSimEventEffectExecuted item,
        ImpactTransitionClaim transition,
        IReadOnlyDictionary<string, CombatImpactEntity>? entities = null
    )
    {
        var targetId = ResolveTargetId(item.Target);
        return executed.Any(candidate =>
            !ReferenceEquals(candidate, item)
            && string.Equals(ResolveTargetId(candidate.Target), targetId, StringComparison.Ordinal)
            && ClaimsTransition(candidate, transition, entities)
        );
    }

    private static bool ClaimsTransition(
        CombatSimEventEffectExecuted effect,
        ImpactTransitionClaim transition,
        IReadOnlyDictionary<string, CombatImpactEntity>? entities
    )
    {
        if (
            entities != null
            && effect.ActionType == EActionCommandType.CardModifyAttribute
            && transition.Domain == ImpactTransitionDomain.CardAttribute
            && TryResolveAbilityAttributeType(effect, entities, out var attributeType)
        )
            return (int)attributeType == transition.Attribute;

        return ClaimsTransition(effect.ActionType, transition);
    }

    private static ResolvedImpactValue? ResolveHealthAdjustment(
        CombatSimPlayerUpdate update,
        CombatImpactKind kind
    )
    {
        var matches = update
            .HealthAdjustments.Where(adjustment => Matches(kind, adjustment))
            .ToArray();
        if (matches.Length == 0)
            return null;

        long aggregate = 0;
        foreach (var adjustment in matches)
        {
            aggregate +=
                kind == CombatImpactKind.DirectDamage
                    ? Math.Abs((long)adjustment.Amount)
                    : adjustment.Amount;
        }
        var value = SaturatingInt(aggregate);
        if (value <= 0)
            return null;

        return new ResolvedImpactValue(
            value,
            CombatImpactValueUnit.Amount,
            CombatImpactAggregator.NativeKey(kind),
            matches.All(adjustment => adjustment.IsCrit),
            CombatImpactValueBasis.ExactAdjustment
        )
        {
            CriticalCount = matches.Any(adjustment => adjustment.IsCrit) ? 1 : 0,
            CriticalValue = SaturatingInt(
                matches
                    .Where(adjustment => adjustment.IsCrit)
                    .Sum(adjustment =>
                        kind == CombatImpactKind.DirectDamage
                            ? Math.Abs((long)adjustment.Amount)
                            : adjustment.Amount
                    )
            ),
        };
    }

    private static ResolvedImpactValue? ResolveShieldRemoval(CombatSimPlayerUpdate update)
    {
        var matches = update
            .HealthAdjustments.Where(adjustment =>
                adjustment.DamageType == EDamageType.Shield
                && adjustment.AttributeChanged == EPlayerHealthChangeType.Shield
                && adjustment.Amount < 0
            )
            .ToArray();
        if (matches.Length == 0)
            return null;

        var removed = SaturatingInt(matches.Sum(adjustment => Math.Abs((long)adjustment.Amount)));
        return removed > 0
            ? new ResolvedImpactValue(
                removed,
                CombatImpactValueUnit.Amount,
                "ShieldRemoveAmount",
                false,
                CombatImpactValueBasis.ExactAdjustment
            )
            : null;
    }

    private static bool MatchesExpectedPlayerDelta(EActionCommandType action, int delta)
    {
        var isDecrease =
            action == EActionCommandType.PlayerMaxHealthDecrease || IsPlayerRemovalAction(action);
        return isDecrease ? delta < 0 : delta > 0;
    }

    private static bool IsPlayerRemovalAction(EActionCommandType action) =>
        OptionalCombatTempoTypes.IsRemoveAction(action)
        || action
            is EActionCommandType.PlayerBurnRemove
                or EActionCommandType.PlayerPoisonRemove
                or EActionCommandType.PlayerRegenRemove
                or EActionCommandType.PlayerShieldRemove
                or EActionCommandType.PlayerRageRemove;

    private static int SaturatingInt(long value) =>
        value > int.MaxValue ? int.MaxValue
        : value < int.MinValue ? int.MinValue
        : (int)value;

    private static bool Matches(
        CombatImpactKind kind,
        CombatSimPlayerHealthAdjustment adjustment
    ) =>
        kind switch
        {
            CombatImpactKind.DirectDamage => adjustment.DamageType == EDamageType.Damage
                && adjustment.Amount < 0,
            CombatImpactKind.Healing => adjustment.DamageType == EDamageType.Heal
                && adjustment.AttributeChanged == EPlayerHealthChangeType.Health
                && adjustment.Amount > 0,
            CombatImpactKind.Shield => adjustment.DamageType == EDamageType.Shield
                && adjustment.AttributeChanged == EPlayerHealthChangeType.Shield
                && adjustment.Amount > 0,
            _ => false,
        };

    private static string NativeKeyForAction(EActionCommandType action, string fallback)
    {
        if (OptionalCombatTempoTypes.IsApplyAction(action))
            return "TempoApplyAmount";
        if (OptionalCombatTempoTypes.IsRemoveAction(action))
            return "TempoRemoveAmount";

        return action switch
        {
            EActionCommandType.CardReload => "ReloadAmount",
            EActionCommandType.CardForceUse => "ForceUseTargets",
            EActionCommandType.CardEnchant => "EnchantTargets",
            EActionCommandType.CardEnchantRemove => "EnchantRemoveTargets",
            EActionCommandType.CardTransform or EActionCommandType.CardTransformDestroyed =>
                "TransformTargets",
            EActionCommandType.CardUpgrade => "UpgradeTargets",
            EActionCommandType.CardRepair => "RepairTargets",
            EActionCommandType.CardModifyAttribute => "CardModifyAttribute",
            EActionCommandType.PlayerModifyAttribute => "PlayerModifyAttribute",
            EActionCommandType.PlayerBurnApply => CombatImpactAggregator.NativeKey(
                CombatImpactKind.Burn
            ),
            EActionCommandType.PlayerPoisonApply => CombatImpactAggregator.NativeKey(
                CombatImpactKind.Poison
            ),
            EActionCommandType.PlayerBurnRemove => "BurnRemoveAmount",
            EActionCommandType.PlayerPoisonRemove => "PoisonRemoveAmount",
            EActionCommandType.PlayerRegenApply => "RegenApplyAmount",
            EActionCommandType.PlayerRegenRemove => "RegenRemoveAmount",
            EActionCommandType.PlayerRageApply => "RageApplyAmount",
            EActionCommandType.PlayerRageRemove => "RageRemoveAmount",
            EActionCommandType.PlayerShieldRemove => "ShieldRemoveAmount",
            EActionCommandType.PlayerMaxHealthIncrease => "HealthMaxIncrease",
            EActionCommandType.PlayerMaxHealthDecrease => "HealthMaxDecrease",
            _ => fallback,
        };
    }

    private static CombatImpactValueUnit UnitFor(string nativeAttributeKey) =>
        nativeAttributeKey == "Lifesteal"
        || nativeAttributeKey.Contains("Percent", StringComparison.Ordinal)
        || nativeAttributeKey.Contains("CritChance", StringComparison.Ordinal)
            ? CombatImpactValueUnit.PercentagePoints
        : nativeAttributeKey
            is "Cooldown"
                or "CooldownMax"
                or "ChargeAmount"
                or "Haste"
                or "HasteAmount"
                or "Slow"
                or "SlowAmount"
                or "Freeze"
                or "FreezeAmount"
                or "FlatCooldownReduction"
                or "TempoGainCooldownMax"
                or "FlatTempoGainCooldownReduction"
            ? CombatImpactValueUnit.Milliseconds
        : CombatImpactValueUnit.Amount;

    private static void AddAmountMetric(
        IReadOnlyDictionary<ECardStats, int> stats,
        ECardStats stat,
        CombatImpactKind kind,
        string nativeAttributeKey,
        ICollection<CombatImpactAuthoritativeMetric> metrics
    )
    {
        if (!stats.TryGetValue(stat, out var value) || value == 0)
            return;
        metrics.Add(
            new CombatImpactAuthoritativeMetric(
                kind,
                nativeAttributeKey,
                value,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            )
        );
    }

    private static void AddApplicationMetric(
        IReadOnlyDictionary<ECardStats, int> stats,
        ECardStats stat,
        CombatImpactKind kind,
        ICollection<CombatImpactAuthoritativeMetric> metrics
    )
    {
        if (!stats.TryGetValue(stat, out var value) || value == 0)
            return;
        metrics.Add(
            new CombatImpactAuthoritativeMetric(
                kind,
                CombatImpactAggregator.NativeKey(kind),
                value,
                CombatImpactValueUnit.Applications,
                CombatImpactAuthoritativeBasis.ApplicationCount
            )
        );
    }

    private readonly record struct ResolvedImpactValue(
        int? Value,
        CombatImpactValueUnit Unit,
        string NativeAttributeKey,
        bool IsCritical,
        CombatImpactValueBasis Basis
    )
    {
        internal CombatImpactEventSurface Surface { get; init; } =
            CombatImpactEventSurface.AppliedEffect;

        internal int CriticalCount { get; init; }

        internal int? CriticalValue { get; init; }

        internal int? NonCriticalValue { get; init; }

        internal int? AlternateNonCriticalValue { get; init; }

        internal bool HasCriticalAdjustmentCandidate { get; init; }

        internal static ResolvedImpactValue Empty(
            CombatImpactKind kind,
            EActionCommandType action
        ) =>
            new(
                null,
                kind
                    is CombatImpactKind.Charge
                        or CombatImpactKind.Haste
                        or CombatImpactKind.Slow
                        or CombatImpactKind.Freeze
                    ? CombatImpactValueUnit.Milliseconds
                    : CombatImpactValueUnit.Amount,
                NativeKeyForAction(action, CombatImpactAggregator.NativeKey(kind)),
                false,
                CombatImpactValueBasis.None
            );
    }

    private readonly record struct AuraAttributeCandidate(
        string SourceId,
        string TargetId,
        CombatSimCardAttributeUpdate Change
    );

    private readonly record struct AuraPlayerAttributeCandidate(
        string SourceId,
        string TargetId,
        CombatSimPlayerAttributeUpdate Change
    );

    private readonly record struct ProjectedExecution(
        string? SourceId,
        string? TargetId,
        CombatImpactKind? Kind,
        ResolvedImpactValue Resolved,
        string? DirectSourceId,
        string? TriggerSourceId,
        int FrameIndex
    );

    private readonly record struct TriggerProvenance(
        CombatImpactActivitySourceResolution SourceResolution,
        CombatImpactTriggerScope Scope
    );

    private readonly record struct IndexedImpactEvent(int Index, CombatImpactEvent Event);

    private readonly record struct CriticalRecoveryKey(
        string SourceId,
        CombatImpactKind Kind,
        string NativeAttributeKey
    );

    private readonly record struct CriticalRecoveryResult(int CriticalCount, int? CriticalValue);

    private readonly record struct CriticalRecoveryRange(
        int MinimumCriticalCount,
        int MaximumCriticalCount,
        long MinimumCriticalValue,
        long MaximumCriticalValue
    );

    private enum ImpactTransitionDomain
    {
        HealthAdjustment,
        PlayerAttribute,
        CardAttribute,
    }

    private readonly record struct ImpactTransitionClaim(
        ImpactTransitionDomain Domain,
        int Attribute
    );
}
