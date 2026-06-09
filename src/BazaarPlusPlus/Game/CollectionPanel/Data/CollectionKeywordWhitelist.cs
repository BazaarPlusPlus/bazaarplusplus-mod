#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Player-facing EHiddenTag keyword options, ordered for display. This is intentionally
// conservative until a live cache pass confirms the highest-value skill-tag coverage.
internal static class CollectionKeywordWhitelist
{
    public const int PrimaryCount = 10;

    public static readonly IReadOnlyList<EHiddenTag> Ordered = new[]
    {
        // Primary (visible while collapsed).
        EHiddenTag.Damage,
        EHiddenTag.Heal,
        EHiddenTag.Burn,
        EHiddenTag.Poison,
        EHiddenTag.Freeze,
        EHiddenTag.Shield,
        EHiddenTag.Haste,
        EHiddenTag.Slow,
        EHiddenTag.Crit,
        EHiddenTag.Cooldown,
        // Extended (behind the expand toggle), alphabetical-ish by gameplay surface.
        EHiddenTag.Ammo,
        EHiddenTag.CanCrit,
        EHiddenTag.Charge,
        EHiddenTag.Flying,
        EHiddenTag.Gold,
        EHiddenTag.Income,
        EHiddenTag.Lifesteal,
        EHiddenTag.Multicast,
        EHiddenTag.Rage,
        EHiddenTag.Regen,
        EHiddenTag.Reload,
        EHiddenTag.Tempo,
        EHiddenTag.Value,
    };
}
