#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.ReportData;

internal sealed record ReportStatusIconSemantic(string StableKey, string NativeAttributeKey);

/// <summary>
/// Maps only combat-report event shapes whose native icon styling is explicitly established by
/// the game's TooltipComponentExtensions/Data keyword-legend path. Unknown actions fail closed:
/// the report keeps its textual semantic and does not invent an icon attribution.
/// </summary>
internal static class ReportStatusIconSemanticResolver
{
    private static readonly ReportStatusIconSemantic Burn = new("status.burn", "BurnApplyAmount");
    private static readonly ReportStatusIconSemantic Damage = new("status.damage", "DamageAmount");
    private static readonly ReportStatusIconSemantic Heal = new("status.heal", "HealAmount");
    private static readonly ReportStatusIconSemantic Poison = new(
        "status.poison",
        "PoisonApplyAmount"
    );
    private static readonly ReportStatusIconSemantic Regen = new(
        "status.regen",
        "RegenApplyAmount"
    );
    private static readonly ReportStatusIconSemantic Shield = new(
        "status.shield",
        "ShieldApplyAmount"
    );
    private static readonly ReportStatusIconSemantic Charge = new("status.charge", "ChargeAmount");
    private static readonly ReportStatusIconSemantic Haste = new("status.haste", "HasteAmount");
    private static readonly ReportStatusIconSemantic Slow = new("status.slow", "SlowAmount");
    private static readonly ReportStatusIconSemantic Freeze = new("status.freeze", "FreezeAmount");
    private static readonly ReportStatusIconSemantic Ammo = new("status.ammo", "Ammo");
    private static readonly ReportStatusIconSemantic CooldownReduction = new(
        "status.cooldownReduction",
        "PercentCooldownReduction"
    );
    private static readonly ReportStatusIconSemantic CritChance = new(
        "status.critChance",
        "CritChance"
    );
    private static readonly ReportStatusIconSemantic Destroy = new(
        "status.destroy",
        "DisableTargets"
    );
    private static readonly ReportStatusIconSemantic Multicast = new(
        "status.multicast",
        "Multicast"
    );

    private static readonly IReadOnlyDictionary<string, ReportStatusIconSemantic> ByStableKey =
        new Dictionary<string, ReportStatusIconSemantic>(StringComparer.Ordinal)
        {
            [Burn.StableKey] = Burn,
            [Damage.StableKey] = Damage,
            [Heal.StableKey] = Heal,
            [Poison.StableKey] = Poison,
            [Regen.StableKey] = Regen,
            [Shield.StableKey] = Shield,
            [Charge.StableKey] = Charge,
            [Haste.StableKey] = Haste,
            [Slow.StableKey] = Slow,
            [Freeze.StableKey] = Freeze,
            [Ammo.StableKey] = Ammo,
            [CooldownReduction.StableKey] = CooldownReduction,
            [CritChance.StableKey] = CritChance,
            [Destroy.StableKey] = Destroy,
            [Multicast.StableKey] = Multicast,
        };

    internal static bool TryResolve(
        CombatReportEventV1? reportEvent,
        out ReportStatusIconSemantic semantic
    )
    {
        semantic = null!;
        if (reportEvent == null)
            return false;

        if (
            string.Equals(reportEvent.Kind, "player-attribute", StringComparison.Ordinal)
            && string.Equals(reportEvent.Action, "Health", StringComparison.Ordinal)
            && reportEvent.Value is long healthDelta
            && healthDelta != 0
        )
        {
            semantic = healthDelta > 0 ? Heal : Damage;
            return true;
        }

        return TryResolve(reportEvent.Kind, reportEvent.Action, out semantic);
    }

    internal static bool TryResolve(
        string? kind,
        string? action,
        out ReportStatusIconSemantic semantic
    )
    {
        semantic = null!;
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(action))
            return false;

        if (string.Equals(kind, "player-attribute", StringComparison.Ordinal))
        {
            semantic = action switch
            {
                "Burn" => Burn,
                "Poison" => Poison,
                "HealthRegen" => Regen,
                "Shield" => Shield,
                _ => null!,
            };
            return semantic != null;
        }

        if (string.Equals(kind, "card-attribute", StringComparison.Ordinal))
        {
            semantic = action switch
            {
                "Ammo" => Ammo,
                "CritChance" => CritChance,
                "DamageAmount" => Damage,
                "Multicast" => Multicast,
                "PercentCooldownReduction" => CooldownReduction,
                "ChargeAmount" => Charge,
                "Haste" or "HasteAmount" => Haste,
                "Slow" or "SlowAmount" => Slow,
                "Freeze" or "FreezeAmount" => Freeze,
                _ => null!,
            };
            return semantic != null;
        }

        if (string.Equals(kind, "effect-executed", StringComparison.Ordinal))
        {
            semantic = action switch
            {
                "CardDisable" or "CardDestroy" => Destroy,
                "CardCharge" => Charge,
                "CardHaste" => Haste,
                "CardSlow" => Slow,
                "CardFreeze" => Freeze,
                "PlayerDamage" => Damage,
                "PlayerHeal" => Heal,
                "PlayerBurnApply" or "PlayerBurnRemove" => Burn,
                "PlayerPoisonApply" or "PlayerPoisonRemove" => Poison,
                "PlayerRegenApply" or "PlayerRegenRemove" => Regen,
                "PlayerShieldApply" or "PlayerShieldRemove" => Shield,
                _ => null!,
            };
            return semantic != null;
        }

        if (string.Equals(kind, "health", StringComparison.Ordinal))
        {
            semantic = action switch
            {
                "Health:Damage" or "Shield:Damage" => Damage,
                "Health:Burn" or "Shield:Burn" => Burn,
                "Health:Heal" => Heal,
                "Health:Regen" => Regen,
                "Shield:Shield" => Shield,
                _ => null!,
            };
            return semantic != null;
        }

        return false;
    }

    internal static bool TryGetByStableKey(string? stableKey, out ReportStatusIconSemantic semantic)
    {
        semantic = null!;
        return !string.IsNullOrWhiteSpace(stableKey)
            && ByStableKey.TryGetValue(stableKey, out semantic!);
    }
}
