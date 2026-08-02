#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactEntityOwnerResolver
{
    internal static ECombatantId Resolve(ECombatantId? owner) =>
        owner == ECombatantId.Opponent ? ECombatantId.Opponent : ECombatantId.Player;
}
