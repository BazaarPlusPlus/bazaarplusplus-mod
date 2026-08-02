#nullable enable
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactProjector
{
    internal static CombatImpactReport Project(
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var executions = ProjectExecutions(simulation, entities);
        var events = new List<CombatImpactEvent>();

        foreach (var execution in executions)
        {
            var fact = execution.Fact;
            if (
                fact.AttributedSourceId == null
                || fact.TargetId == null
                || !execution.Kind.HasValue
            )
                continue;

            var kind = execution.Kind.Value;
            var resolved = execution.Resolved;
            if (kind == CombatImpactKind.AttributeChange && !IsDisplayableAttributeChange(resolved))
                continue;

            events.Add(
                new CombatImpactEvent(
                    kind,
                    fact.AttributedSourceId,
                    fact.TargetId,
                    fact.Value.HasValue ? SaturatingInt(fact.Value.Value) : null,
                    fact.Unit ?? resolved.Unit,
                    resolved.NativeAttributeKey,
                    resolved.IsCritical,
                    fact.ValueBasis
                )
                {
                    Surface = resolved.Surface,
                    CriticalCount = resolved.CriticalCount,
                    CriticalValue = resolved.CriticalValue,
                    NonCriticalValue = resolved.NonCriticalValue,
                }
            );
        }

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
            if (metrics.Count > 0)
                authoritative[sourceId] = metrics;
        }

        RecoverDroppedAppliedEffectCriticals(events, authoritative);

        return CombatImpactAggregator.Aggregate(
            new CombatImpactProjectionInput(entities, events, useCounts, authoritative)
        );
    }

    internal static IReadOnlyList<CombatImpactFact> ProjectFacts(
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) => ProjectExecutions(simulation, entities).Select(execution => execution.Fact).ToArray();

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
            for (var frameEventIndex = 0; frameEventIndex < frame.Events.Count; frameEventIndex++)
            {
                if (frame.Events[frameEventIndex] is not CombatSimEventEffectExecuted item)
                    continue;

                var directSourceId = item.Source?.Value;
                var triggerSourceId = item.TriggerSource?.Value;
                var attributedSourceId = ResolveActivitySource(
                    directSourceId,
                    triggerSourceId,
                    entities,
                    out var sourceAttribution
                );
                var target = ResolveTarget(item.Target);
                var hasKind = TryResolveKind(item.ActionType, out var kind);
                var resolved = hasKind
                    ? ResolveValue(frame, item, kind, IsTransitionUnique(executed, item), executed)
                    : default;
                if (
                    attributedSourceId != null
                    && TryResolveNonCriticalValue(
                        item.ActionType,
                        attributedSourceId,
                        cardAttributes,
                        out var nonCriticalValue
                    )
                )
                    resolved = resolved with { NonCriticalValue = nonCriticalValue };
                var unknownReason = CombatImpactUnknownReason.None;
                if (attributedSourceId == null)
                    unknownReason |= CombatImpactUnknownReason.UnattributedSource;
                if (target.Id == null || !entities.ContainsKey(target.Id))
                    unknownReason |= CombatImpactUnknownReason.UnresolvedTarget;
                if (!resolved.Value.HasValue)
                    unknownReason |= CombatImpactUnknownReason.ValueNotQuantified;

                projections.Add(
                    new ProjectedExecution(
                        new CombatImpactFact(
                            frameIndex,
                            frameEventIndex,
                            item.ActionType,
                            directSourceId,
                            triggerSourceId,
                            attributedSourceId,
                            sourceAttribution,
                            target.Kind,
                            target.Id,
                            resolved.Value,
                            resolved.Value.HasValue ? resolved.Unit : null,
                            resolved.Basis,
                            unknownReason
                        ),
                        hasKind ? kind : null,
                        resolved
                    )
                );
            }
            ReconcileCardAttributeTimeline(frame, cardAttributes, usePreviousValue: false);
        }
        return projections;
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

    private static bool TryResolveNonCriticalValue(
        EActionCommandType action,
        string sourceId,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        out int value
    )
    {
        var attribute = action switch
        {
            EActionCommandType.PlayerBurnApply => ECardAttributeType.BurnApplyAmount,
            EActionCommandType.PlayerPoisonApply => ECardAttributeType.PoisonApplyAmount,
            EActionCommandType.PlayerRegenApply => ECardAttributeType.RegenApplyAmount,
            _ => (ECardAttributeType?)null,
        };
        if (
            attribute.HasValue
            && cardAttributes.TryGetValue(sourceId, out var attributes)
            && attributes.TryGetValue(attribute.Value, out value)
            && value > 0
        )
            return true;

        value = 0;
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

            var baselines = indexedEvents
                .Select(item => item.Event.NonCriticalValue!.Value)
                .ToArray();
            var baselineTotal = baselines.Sum(value => (long)value);
            var criticalExtra = (long)metric.Value - baselineTotal;
            if (criticalExtra <= 0 || criticalExtra > baselineTotal)
                continue;

            var criticalCount = ResolveUniqueCriticalCount(baselines, criticalExtra);
            if (criticalCount is not > 0)
                continue;

            var first = indexedEvents[0];
            events[first.Index] = first.Event with
            {
                CriticalCount = criticalCount.Value,
                CriticalValue = SaturatingInt(criticalExtra * 2),
            };
        }
    }

    private static bool IsRecoverableAppliedEffect(CombatImpactEvent item) =>
        item.Surface == CombatImpactEventSurface.AppliedEffect
        && (
            item.Kind == CombatImpactKind.Burn
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

    private static int? ResolveUniqueCriticalCount(
        IReadOnlyList<int> nonCriticalValues,
        long criticalExtra
    )
    {
        const int maximumStates = 50_000;
        const int maximumTransitions = 2_000_000;
        var transitions = 0;
        var states = new Dictionary<long, CriticalCountRange>
        {
            [0] = new CriticalCountRange(0, 0),
        };
        foreach (var value in nonCriticalValues)
        {
            var snapshot = states.ToArray();
            transitions += snapshot.Length;
            if (transitions > maximumTransitions)
                return null;

            foreach (var (sum, countRange) in snapshot)
            {
                var nextSum = sum + value;
                if (nextSum > criticalExtra)
                    continue;

                var candidate = new CriticalCountRange(
                    countRange.Minimum + 1,
                    countRange.Maximum + 1
                );
                if (states.TryGetValue(nextSum, out var existing))
                {
                    states[nextSum] = new CriticalCountRange(
                        Math.Min(existing.Minimum, candidate.Minimum),
                        Math.Max(existing.Maximum, candidate.Maximum)
                    );
                }
                else
                {
                    states[nextSum] = candidate;
                    if (states.Count > maximumStates)
                        return null;
                }
            }
        }

        return states.TryGetValue(criticalExtra, out var result) && result.Minimum == result.Maximum
            ? result.Minimum
            : null;
    }

    private static string? ResolveActivitySource(
        string? directSourceId,
        string? triggerSourceId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out CombatImpactSourceAttribution attribution
    )
    {
        if (IsActivityEntity(directSourceId, entities))
        {
            attribution = CombatImpactSourceAttribution.Direct;
            return directSourceId;
        }
        if (IsActivityEntity(triggerSourceId, entities))
        {
            attribution = CombatImpactSourceAttribution.Trigger;
            return triggerSourceId;
        }

        attribution = CombatImpactSourceAttribution.Unattributed;
        return null;
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
                    entities,
                    out _
                );
                if (sourceId == null)
                    continue;

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

                    var cardChanges = cardUpdate
                        .Attributes.Values.Where(change =>
                            change.Delta != 0 && IsDisplayableAuraAttribute(change.AttributeType)
                        )
                        .ToArray();
                    if (
                        cardChanges.Length != 1
                        || HasExplicitModifierClaim(frame, cardTarget.Target)
                    )
                        continue;

                    candidates.Add(
                        new AuraAttributeCandidate(
                            sourceId,
                            cardTarget.Target.Value,
                            cardChanges[0]
                        )
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
                    }
                );
            }
        }
    }

    private static bool HasExplicitModifierClaim(CombatSimFrame frame, InstanceId target) =>
        frame
            .Events.OfType<CombatSimEventEffectExecuted>()
            .Any(item =>
                item.ActionType == EActionCommandType.CardModifyAttribute
                && item.Target is EffectTargetCard cardTarget
                && cardTarget.Target == target
            );

    private static bool IsDisplayableAuraAttribute(ECardAttributeType attribute) =>
        attribute
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

    private static string? ResolveTargetId(IEffectTarget? target) => ResolveTarget(target).Id;

    private static bool IsTransitionUnique(
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        CombatSimEventEffectExecuted candidate
    )
    {
        if (!TryResolveTransitionClaim(candidate.ActionType, out var transition))
            return false;
        var targetId = ResolveTargetId(candidate.Target);
        return executed.Count(item =>
                string.Equals(ResolveTargetId(item.Target), targetId, StringComparison.Ordinal)
                && ClaimsTransition(item.ActionType, transition)
            ) == 1;
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

    private static ResolvedTarget ResolveTarget(IEffectTarget? target) =>
        target switch
        {
            EffectTargetCard card => new(CombatImpactTargetKind.Card, card.Target.Value),
            EffectTargetPlayer player => new(
                CombatImpactTargetKind.Player,
                PlayerId(player.Target)
            ),
            _ => new(CombatImpactTargetKind.Unknown, null),
        };

    internal static bool HasExplicitDisplayClassification(EActionCommandType action) =>
        TryResolveKind(action, out _) || IsExplicitlyIgnoredAction(action);

    internal static bool TryResolveKind(EActionCommandType action, out CombatImpactKind kind)
    {
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
            EActionCommandType.CardModifyAttribute => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerModifyAttribute => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerMaxHealthIncrease => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerMaxHealthDecrease => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerRegenApply => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerRageApply => CombatImpactKind.AttributeChange,
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
                or EActionCommandType.CardModifyAttribute
                or EActionCommandType.PlayerModifyAttribute
                or EActionCommandType.PlayerMaxHealthIncrease
                or EActionCommandType.PlayerMaxHealthDecrease
                or EActionCommandType.PlayerRegenApply
                or EActionCommandType.PlayerRageApply
                or EActionCommandType.CardDisable
                or EActionCommandType.CardDestroy;
    }

    private static bool IsExplicitlyIgnoredAction(EActionCommandType action) =>
        action
            is EActionCommandType.None
                or EActionCommandType.CardAddTags
                or EActionCommandType.CardRemoveTags
                or EActionCommandType.CardBeginSandstorm
                or EActionCommandType.CardForceUse
                or EActionCommandType.CardEnchant
                or EActionCommandType.CardTransform
                or EActionCommandType.CardUpgrade
                or EActionCommandType.GameDealCards
                or EActionCommandType.GameModifyTime
                or EActionCommandType.GameScheduleEncounter
                or EActionCommandType.GameSpawnCards
                or EActionCommandType.GameStartCombat
                or EActionCommandType.GameUnscheduleEncounter
                or EActionCommandType.PlayerBurnRemove
                or EActionCommandType.PlayerGoldSteal
                or EActionCommandType.PlayerJoyApply
                or EActionCommandType.PlayerJoyRemove
                or EActionCommandType.PlayerPoisonRemove
                or EActionCommandType.PlayerRegenRemove
                or EActionCommandType.PlayerShieldRemove
                or EActionCommandType.ExitReplacementSet
                or EActionCommandType.CardEnchantRemove
                or EActionCommandType.CardRepair
                or EActionCommandType.CardTransformDestroyed
                or EActionCommandType.CardAddTagsRandom
                or EActionCommandType.PlayerPortraitNext
                or EActionCommandType.PlayerPortraitReset
                or EActionCommandType.PlayerRageRemove
                or EActionCommandType.GameReroll;

    private static ResolvedImpactValue ResolveValue(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind,
        bool transitionIsUnique,
        IReadOnlyList<CombatSimEventEffectExecuted> executed
    )
    {
        if (
            kind == CombatImpactKind.AttributeChange
            && item.ActionType
                is EActionCommandType.CardModifyAttribute
                    or EActionCommandType.PlayerModifyAttribute
        )
            return ResolveConcreteAttributeTransition(frame, item, kind, executed);

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

            var expectedAttribute = item.ActionType switch
            {
                EActionCommandType.PlayerBurnApply => EPlayerAttributeType.Burn,
                EActionCommandType.PlayerPoisonApply => EPlayerAttributeType.Poison,
                EActionCommandType.PlayerRegenApply => EPlayerAttributeType.HealthRegen,
                EActionCommandType.PlayerRageApply => EPlayerAttributeType.Rage,
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
                    attribute.Delta,
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

        var expected = kind switch
        {
            CombatImpactKind.Haste => ECardAttributeType.Haste,
            CombatImpactKind.Slow => ECardAttributeType.Slow,
            CombatImpactKind.Freeze => ECardAttributeType.Freeze,
            _ => (ECardAttributeType?)null,
        };
        if (
            expected.HasValue
            && cardUpdate.Attributes.TryGetValue(expected.Value, out var updateValue)
            && updateValue.Delta > 0
        )
        {
            return new ResolvedImpactValue(
                Math.Abs(updateValue.Delta),
                CombatImpactValueUnit.Milliseconds,
                CombatImpactAggregator.NativeKey(kind),
                false,
                CombatImpactValueBasis.NetFrameDelta
            );
        }

        return ResolvedImpactValue.Empty(kind, item.ActionType);
    }

    private static ResolvedImpactValue ResolveConcreteAttributeTransition(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind,
        IReadOnlyList<CombatSimEventEffectExecuted> executed
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

        var cardChanges = cardUpdate
            .Attributes.Values.Where(update =>
                update.Delta != 0
                && (
                    IsDisplayableAuraAttribute(update.AttributeType)
                    || update
                        .AttributeType.ToString()
                        .StartsWith("Custom_", StringComparison.Ordinal)
                )
            )
            .ToArray();
        if (cardChanges.Length != 1)
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        var cardChange = cardChanges[0];
        if (
            IsClaimedByAnotherExecution(
                executed,
                item,
                new ImpactTransitionClaim(
                    ImpactTransitionDomain.CardAttribute,
                    (int)cardChange.AttributeType
                )
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
        ImpactTransitionClaim transition
    )
    {
        var targetId = ResolveTargetId(item.Target);
        return executed.Any(candidate =>
            !ReferenceEquals(candidate, item)
            && string.Equals(ResolveTargetId(candidate.Target), targetId, StringComparison.Ordinal)
            && ClaimsTransition(candidate.ActionType, transition)
        );
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
            CriticalCount = matches.Count(adjustment => adjustment.IsCrit),
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

    private static bool MatchesExpectedPlayerDelta(EActionCommandType action, int delta) =>
        action == EActionCommandType.PlayerMaxHealthDecrease ? delta < 0 : delta > 0;

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

    private static string NativeKeyForAction(EActionCommandType action, string fallback) =>
        action switch
        {
            EActionCommandType.CardReload => "ReloadAmount",
            EActionCommandType.CardModifyAttribute => "CardModifyAttribute",
            EActionCommandType.PlayerModifyAttribute => "PlayerModifyAttribute",
            EActionCommandType.PlayerBurnApply => CombatImpactAggregator.NativeKey(
                CombatImpactKind.Burn
            ),
            EActionCommandType.PlayerPoisonApply => CombatImpactAggregator.NativeKey(
                CombatImpactKind.Poison
            ),
            EActionCommandType.PlayerRegenApply => "RegenApplyAmount",
            EActionCommandType.PlayerRageApply => "RageApplyAmount",
            EActionCommandType.PlayerMaxHealthIncrease => "HealthMaxIncrease",
            EActionCommandType.PlayerMaxHealthDecrease => "HealthMaxDecrease",
            _ => fallback,
        };

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

    private readonly record struct ResolvedTarget(CombatImpactTargetKind Kind, string? Id);

    private readonly record struct ProjectedExecution(
        CombatImpactFact Fact,
        CombatImpactKind? Kind,
        ResolvedImpactValue Resolved
    );

    private readonly record struct IndexedImpactEvent(int Index, CombatImpactEvent Event);

    private readonly record struct CriticalRecoveryKey(
        string SourceId,
        CombatImpactKind Kind,
        string NativeAttributeKey
    );

    private readonly record struct CriticalCountRange(int Minimum, int Maximum);

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
