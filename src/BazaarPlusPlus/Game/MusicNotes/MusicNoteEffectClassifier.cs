#nullable enable
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;

namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>
/// Extracts the card attribute a note socket effect grants, so the badge can reuse the game's
/// own keyword icon for it. Auras are checked first — the shape the game itself classifies
/// socket effects by (SocketEffectController.GetEffectType) — then a bounded map of well-known
/// ability action types. Effects outside both shapes resolve to null and render letter-only.
/// </summary>
internal static class MusicNoteEffectClassifier
{
    internal static ECardAttributeType? TryGetAttribute(TCardSocketEffect? template)
    {
        if (template == null)
            return null;

        try
        {
            if (template.Auras != null)
            {
                foreach (var aura in template.Auras.Values)
                {
                    if (aura?.Action is TAuraActionCardModifyAttribute modifyAttribute)
                        return modifyAttribute.AttributeType;
                }
            }

            if (template.Abilities != null)
            {
                foreach (var ability in template.Abilities.Values)
                {
                    if (FromAbilityAction(ability?.Action) is ECardAttributeType attribute)
                        return attribute;
                }
            }
        }
        catch
        {
            // Template graphs are data-driven; treat any surprise shape as "no icon".
        }

        return null;
    }

    private static ECardAttributeType? FromAbilityAction(object? action) =>
        action switch
        {
            TActionCardModifyAttribute modify => modify.AttributeType,
            TActionCardHaste => ECardAttributeType.Haste,
            TActionCardSlow => ECardAttributeType.Slow,
            TActionCardFreeze => ECardAttributeType.Freeze,
            TActionPlayerBurnApply => ECardAttributeType.BurnApplyAmount,
            TActionPlayerPoisonApply => ECardAttributeType.PoisonApplyAmount,
            TActionPlayerShieldApply => ECardAttributeType.ShieldApplyAmount,
            TActionPlayerDamage => ECardAttributeType.DamageAmount,
            TActionPlayerHeal => ECardAttributeType.HealAmount,
            _ => null,
        };
}
