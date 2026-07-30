#nullable enable
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
        var events = new List<CombatImpactEvent>();
        var triggerCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var frame in simulation.Frames)
        {
            foreach (var triggered in frame.Events.OfType<CombatSimEventEffectTriggered>())
            {
                var sourceId = triggered.Source?.Value;
                if (!string.IsNullOrWhiteSpace(sourceId))
                    triggerCounts[sourceId!] = triggerCounts.GetValueOrDefault(sourceId!) + 1;
            }

            var executed = frame.Events.OfType<CombatSimEventEffectExecuted>().ToArray();
            foreach (var item in executed)
            {
                if (
                    item.Source?.Value is not { Length: > 0 } sourceId
                    || ResolveTargetId(item.Target) is not { Length: > 0 } targetId
                    || !TryResolveKind(item.ActionType, out var kind)
                )
                    continue;

                var matchingActionCount = executed.Count(candidate =>
                    candidate.ActionType == item.ActionType
                    && ResolveTargetId(candidate.Target) == targetId
                );
                var resolved = ResolveValue(frame, item, kind, matchingActionCount == 1);
                if (
                    kind == CombatImpactKind.AttributeChange
                    && !IsDisplayableAttributeChange(resolved)
                )
                    continue;
                events.Add(
                    new CombatImpactEvent(
                        kind,
                        sourceId,
                        targetId,
                        resolved.Value,
                        resolved.Unit,
                        resolved.NativeAttributeKey
                    )
                );

                if (resolved.IsCritical)
                {
                    events.Add(
                        new CombatImpactEvent(
                            CombatImpactKind.Critical,
                            sourceId,
                            targetId,
                            resolved.Value,
                            resolved.Unit,
                            CombatImpactAggregator.NativeKey(CombatImpactKind.Critical)
                        )
                    );
                }
            }
        }

        var useCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var authoritative = new Dictionary<string, IReadOnlyDictionary<CombatImpactKind, int>>(
            StringComparer.Ordinal
        );
        foreach (var (sourceId, stats) in simulation.CardStats)
        {
            if (stats.TryGetValue(ECardStats.UseCount, out var uses))
                useCounts[sourceId] = uses;

            var totals = new Dictionary<CombatImpactKind, int>();
            AddTotal(stats, ECardStats.DamageDone, CombatImpactKind.DirectDamage, totals);
            AddTotal(stats, ECardStats.BurnAdded, CombatImpactKind.Burn, totals);
            AddTotal(stats, ECardStats.PoisonAdded, CombatImpactKind.Poison, totals);
            AddTotal(stats, ECardStats.HealAdded, CombatImpactKind.Healing, totals);
            AddTotal(stats, ECardStats.ShieldAdded, CombatImpactKind.Shield, totals);
            if (totals.Count > 0)
                authoritative[sourceId] = totals;
        }

        return CombatImpactAggregator.Aggregate(
            new CombatImpactProjectionInput(
                entities,
                events,
                useCounts,
                triggerCounts,
                authoritative
            )
        );
    }

    private static bool IsDisplayableAttributeChange(ResolvedImpactValue resolved) =>
        resolved.Value.HasValue
        && !resolved.NativeAttributeKey.StartsWith("Custom_", StringComparison.Ordinal);

    internal static string PlayerId(ECombatantId combatant) => $"player:{combatant}";

    private static string? ResolveTargetId(IEffectTarget? target) =>
        target switch
        {
            EffectTargetCard card => card.Target.Value,
            EffectTargetPlayer player => PlayerId(player.Target),
            _ => null,
        };

    private static bool TryResolveKind(EActionCommandType action, out CombatImpactKind kind)
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
            EActionCommandType.CardReload => CombatImpactKind.AttributeChange,
            EActionCommandType.CardModifyAttribute => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerModifyAttribute => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerMaxHealthIncrease => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerMaxHealthDecrease => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerRegenApply => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerRageApply => CombatImpactKind.AttributeChange,
            EActionCommandType.CardDestroy => CombatImpactKind.Destroy,
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
                or EActionCommandType.CardReload
                or EActionCommandType.CardModifyAttribute
                or EActionCommandType.PlayerModifyAttribute
                or EActionCommandType.PlayerMaxHealthIncrease
                or EActionCommandType.PlayerMaxHealthDecrease
                or EActionCommandType.PlayerRegenApply
                or EActionCommandType.PlayerRageApply
                or EActionCommandType.CardDestroy;
    }

    private static ResolvedImpactValue ResolveValue(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind,
        bool actionTargetIsUnique
    )
    {
        if (!actionTargetIsUnique)
            return ResolvedImpactValue.Empty(kind);

        if (item.Target is EffectTargetPlayer playerTarget)
        {
            var update =
                playerTarget.Target == ECombatantId.Player
                    ? frame.PlayerUpdates
                    : frame.OpponentUpdates;
            if (update == null)
                return ResolvedImpactValue.Empty(kind);

            if (
                kind
                is CombatImpactKind.DirectDamage
                    or CombatImpactKind.Healing
                    or CombatImpactKind.Shield
            )
            {
                var healthMatches = update
                    .HealthAdjustments.Where(adjustment => Matches(kind, adjustment))
                    .ToArray();
                return healthMatches.Length == 1
                    ? new ResolvedImpactValue(
                        Math.Abs(healthMatches[0].Amount),
                        CombatImpactValueUnit.Amount,
                        CombatImpactAggregator.NativeKey(kind),
                        healthMatches[0].IsCrit
                    )
                    : ResolvedImpactValue.Empty(kind);
            }

            var expectedAttribute = kind switch
            {
                CombatImpactKind.Burn => EPlayerAttributeType.Burn,
                CombatImpactKind.Poison => EPlayerAttributeType.Poison,
                _ => (EPlayerAttributeType?)null,
            };
            if (
                expectedAttribute.HasValue
                && update.Attributes.TryGetValue(expectedAttribute.Value, out var attribute)
                && attribute.Delta != 0
            )
            {
                return new ResolvedImpactValue(
                    Math.Abs(attribute.Delta),
                    CombatImpactValueUnit.Amount,
                    CombatImpactAggregator.NativeKey(kind),
                    false
                );
            }

            if (kind == CombatImpactKind.AttributeChange)
            {
                var attributes = update
                    .Attributes.Values.Where(value => value.Delta != 0)
                    .ToArray();
                if (attributes.Length == 1)
                {
                    var changedAttribute = attributes[0];
                    return new ResolvedImpactValue(
                        changedAttribute.Delta,
                        UnitFor(changedAttribute.AttributeType.ToString()),
                        changedAttribute.AttributeType.ToString(),
                        false
                    );
                }
            }

            return ResolvedImpactValue.Empty(kind);
        }

        if (
            item.Target is not EffectTargetCard cardTarget
            || !frame.CardUpdates.TryGetValue(cardTarget.Target, out var cardUpdate)
        )
            return ResolvedImpactValue.Empty(kind);

        if (kind == CombatImpactKind.Destroy)
            return ResolvedImpactValue.Empty(kind);

        if (kind == CombatImpactKind.AttributeChange)
        {
            var attributes = cardUpdate
                .Attributes.Values.Where(value => value.Delta != 0)
                .ToArray();
            if (attributes.Length != 1)
                return ResolvedImpactValue.Empty(kind);
            var attribute = attributes[0];
            return new ResolvedImpactValue(
                attribute.Delta,
                UnitFor(attribute.AttributeType.ToString()),
                attribute.AttributeType.ToString(),
                false
            );
        }

        var expected = kind switch
        {
            CombatImpactKind.Charge => ECardAttributeType.Cooldown,
            CombatImpactKind.Haste => ECardAttributeType.Haste,
            CombatImpactKind.Slow => ECardAttributeType.Slow,
            CombatImpactKind.Freeze => ECardAttributeType.Freeze,
            _ => (ECardAttributeType?)null,
        };
        if (
            expected.HasValue
            && cardUpdate.Attributes.TryGetValue(expected.Value, out var updateValue)
            && updateValue.Delta != 0
        )
        {
            return new ResolvedImpactValue(
                Math.Abs(updateValue.Delta),
                CombatImpactValueUnit.Milliseconds,
                CombatImpactAggregator.NativeKey(kind),
                false
            );
        }

        return ResolvedImpactValue.Empty(kind);
    }

    private static bool Matches(
        CombatImpactKind kind,
        CombatSimPlayerHealthAdjustment adjustment
    ) =>
        kind switch
        {
            CombatImpactKind.DirectDamage => adjustment.DamageType == EDamageType.Damage
                && adjustment.Amount < 0,
            CombatImpactKind.Healing => adjustment.DamageType
                is EDamageType.Heal
                    or EDamageType.Regen
                && adjustment.Amount > 0,
            CombatImpactKind.Shield => adjustment.AttributeChanged == EPlayerHealthChangeType.Shield
                && adjustment.Amount > 0,
            _ => false,
        };

    private static CombatImpactValueUnit UnitFor(string nativeAttributeKey) =>
        nativeAttributeKey.Contains("Percent", StringComparison.Ordinal)
        || nativeAttributeKey.Contains("CritChance", StringComparison.Ordinal)
            ? CombatImpactValueUnit.PercentagePoints
        : nativeAttributeKey is "Cooldown" or "CooldownMax" or "Haste" or "Slow" or "Freeze"
            ? CombatImpactValueUnit.Milliseconds
        : CombatImpactValueUnit.Amount;

    private static void AddTotal(
        IReadOnlyDictionary<ECardStats, int> stats,
        ECardStats stat,
        CombatImpactKind kind,
        Dictionary<CombatImpactKind, int> totals
    )
    {
        if (stats.TryGetValue(stat, out var value) && value != 0)
            totals[kind] = value;
    }

    private readonly record struct ResolvedImpactValue(
        int? Value,
        CombatImpactValueUnit Unit,
        string NativeAttributeKey,
        bool IsCritical
    )
    {
        internal static ResolvedImpactValue Empty(CombatImpactKind kind) =>
            new(
                null,
                kind
                    is CombatImpactKind.Charge
                        or CombatImpactKind.Haste
                        or CombatImpactKind.Slow
                        or CombatImpactKind.Freeze
                    ? CombatImpactValueUnit.Milliseconds
                    : CombatImpactValueUnit.Amount,
                CombatImpactAggregator.NativeKey(kind),
                false
            );
    }
}
