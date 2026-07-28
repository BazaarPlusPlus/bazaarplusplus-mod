#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;

namespace BazaarPlusPlus.Game.CombatReplay.ReportData;

internal sealed record CombatReportEffectValueAttribution(
    long Value,
    string Unit,
    bool? IsCritical = null
);

/// <summary>
/// Correlates effect executions with the observable value transition emitted in the same frame.
/// A value is returned only when action, target, and transition form a unique match. The raw
/// protocol does not carry evaluated effect amounts, so ambiguous same-frame applications remain
/// deliberately unquantified.
/// </summary>
internal static class CombatReportEffectValueAttributor
{
    internal static IReadOnlyDictionary<int, CombatReportEffectValueAttribution> Resolve(
        CombatSimFrame frame
    )
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));

        var candidates = new Dictionary<
            string,
            List<(int Index, CombatSimEventEffectExecuted Event)>
        >(StringComparer.Ordinal);
        for (var rawIndex = 0; rawIndex < frame.Events.Count; rawIndex++)
        {
            if (frame.Events[rawIndex] is not CombatSimEventEffectExecuted executed)
                continue;
            if (!IsQuantifiable(executed.ActionType))
                continue;

            var targetKey = TargetKey(executed.Target);
            if (targetKey == null)
                continue;
            var key = ((int)executed.ActionType).ToString() + ":" + targetKey;
            if (!candidates.TryGetValue(key, out var group))
            {
                group = new List<(int, CombatSimEventEffectExecuted)>();
                candidates.Add(key, group);
            }
            group.Add((rawIndex, executed));
        }

        var resolved = new Dictionary<int, CombatReportEffectValueAttribution>();
        foreach (var group in candidates.Values)
        {
            // One aggregate state transition cannot be split truthfully between multiple
            // executions, even when they happen to share a source.
            if (group.Count != 1)
                continue;
            var candidate = group[0];
            if (TryResolve(frame, candidate.Event, out var attribution))
                resolved.Add(candidate.Index, attribution);
        }
        return resolved;
    }

    private static bool TryResolve(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted executed,
        out CombatReportEffectValueAttribution attribution
    )
    {
        attribution = null!;
        switch (executed.ActionType)
        {
            case EActionCommandType.PlayerDamage:
                return TryResolveHealthAdjustment(
                    PlayerUpdate(frame, executed.Target),
                    adjustment =>
                        adjustment.DamageType == EDamageType.Damage && adjustment.Amount < 0,
                    absolute: true,
                    out attribution
                );
            case EActionCommandType.PlayerHeal:
                return TryResolveHealthAdjustment(
                    PlayerUpdate(frame, executed.Target),
                    adjustment =>
                        adjustment.DamageType == EDamageType.Heal
                        && adjustment.AttributeChanged == EPlayerHealthChangeType.Health
                        && adjustment.Amount > 0,
                    absolute: false,
                    out attribution
                );
            case EActionCommandType.PlayerShieldApply:
                return TryResolveHealthAdjustment(
                    PlayerUpdate(frame, executed.Target),
                    adjustment =>
                        adjustment.DamageType == EDamageType.Shield
                        && adjustment.AttributeChanged == EPlayerHealthChangeType.Shield
                        && adjustment.Amount > 0,
                    absolute: false,
                    out attribution
                );
            case EActionCommandType.PlayerBurnApply:
                return TryResolvePlayerAttribute(
                    PlayerUpdate(frame, executed.Target),
                    EPlayerAttributeType.Burn,
                    out attribution
                );
            case EActionCommandType.PlayerPoisonApply:
                return TryResolvePlayerAttribute(
                    PlayerUpdate(frame, executed.Target),
                    EPlayerAttributeType.Poison,
                    out attribution
                );
            case EActionCommandType.PlayerRegenApply:
                return TryResolvePlayerAttribute(
                    PlayerUpdate(frame, executed.Target),
                    EPlayerAttributeType.HealthRegen,
                    out attribution
                );
            case EActionCommandType.CardHaste:
                return TryResolveCardAttribute(
                    frame,
                    executed.Target,
                    ECardAttributeType.Haste,
                    out attribution
                );
            case EActionCommandType.CardSlow:
                return TryResolveCardAttribute(
                    frame,
                    executed.Target,
                    ECardAttributeType.Slow,
                    out attribution
                );
            case EActionCommandType.CardFreeze:
                return TryResolveCardAttribute(
                    frame,
                    executed.Target,
                    ECardAttributeType.Freeze,
                    out attribution
                );
            default:
                return false;
        }
    }

    private static bool TryResolveHealthAdjustment(
        CombatSimPlayerUpdate? update,
        Func<CombatSimPlayerHealthAdjustment, bool> predicate,
        bool absolute,
        out CombatReportEffectValueAttribution attribution
    )
    {
        attribution = null!;
        if (update == null)
            return false;

        long sum = 0;
        var matched = false;
        var allCritical = true;
        for (var index = 0; index < update.HealthAdjustments.Count; index++)
        {
            var adjustment = update.HealthAdjustments[index];
            if (!predicate(adjustment))
                continue;
            matched = true;
            allCritical &= adjustment.IsCrit;
            sum = checked(sum + (absolute ? Math.Abs((long)adjustment.Amount) : adjustment.Amount));
        }
        if (!matched || sum <= 0)
            return false;

        attribution = new CombatReportEffectValueAttribution(
            sum,
            "points",
            allCritical ? true : null
        );
        return true;
    }

    private static bool TryResolvePlayerAttribute(
        CombatSimPlayerUpdate? update,
        EPlayerAttributeType attributeType,
        out CombatReportEffectValueAttribution attribution
    )
    {
        attribution = null!;
        if (
            update == null
            || !update.Attributes.TryGetValue(attributeType, out var transition)
            || transition.Delta <= 0
        )
        {
            return false;
        }

        attribution = new CombatReportEffectValueAttribution(transition.Delta, "points");
        return true;
    }

    private static bool TryResolveCardAttribute(
        CombatSimFrame frame,
        IEffectTarget? target,
        ECardAttributeType attributeType,
        out CombatReportEffectValueAttribution attribution
    )
    {
        attribution = null!;
        if (target is not EffectTargetCard card)
            return false;
        if (
            !frame.CardUpdates.TryGetValue(card.Target, out var update)
            || !update.Attributes.TryGetValue(attributeType, out var transition)
            || transition.Delta <= 0
        )
        {
            return false;
        }

        // This is the observable net status increase in the frame. It can be slightly smaller
        // than the configured action amount when the regular 50 ms decay happens concurrently.
        attribution = new CombatReportEffectValueAttribution(transition.Delta, "ms");
        return true;
    }

    private static CombatSimPlayerUpdate? PlayerUpdate(
        CombatSimFrame frame,
        IEffectTarget? target
    ) =>
        target is EffectTargetPlayer player
            ? player.Target switch
            {
                ECombatantId.Player => frame.PlayerUpdates,
                ECombatantId.Opponent => frame.OpponentUpdates,
                _ => null,
            }
            : null;

    private static string? TargetKey(IEffectTarget? target) =>
        target switch
        {
            EffectTargetPlayer player => "player:" + (int)player.Target,
            EffectTargetCard card => "card:" + card.Target.Value,
            _ => null,
        };

    private static bool IsQuantifiable(EActionCommandType action) =>
        action
            is EActionCommandType.PlayerDamage
                or EActionCommandType.PlayerHeal
                or EActionCommandType.PlayerShieldApply
                or EActionCommandType.PlayerBurnApply
                or EActionCommandType.PlayerPoisonApply
                or EActionCommandType.PlayerRegenApply
                or EActionCommandType.CardHaste
                or EActionCommandType.CardSlow
                or EActionCommandType.CardFreeze;
}
