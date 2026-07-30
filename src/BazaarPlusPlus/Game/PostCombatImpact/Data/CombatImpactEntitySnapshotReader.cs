#nullable enable
using BazaarGameShared.Domain.Core.Types;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactEntitySnapshotReader
{
    internal static IReadOnlyDictionary<string, CombatImpactEntity> Read()
    {
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal);
        var order = 0;
        foreach (var card in TheBazaar.Data.Entities.Values)
        {
            if (card?.InstanceId.Value is not { Length: > 0 } instanceId)
                continue;

            var owner =
                card.Owner == TheBazaar.Data.Run?.Opponent
                    ? ECombatantId.Opponent
                    : ECombatantId.Player;
            var template = card.Template;
            var name = ResolveTitle(template?.Localization?.Title);
            if (string.IsNullOrWhiteSpace(name))
                name = template?.InternalName;
            if (string.IsNullOrWhiteSpace(name))
                name = card.Type == ECardType.Skill ? "Skill" : "Item";

            entities[instanceId] = new CombatImpactEntity(
                instanceId,
                name!,
                card.Type.ToString(),
                template?.ArtKey,
                null,
                owner,
                order++
            );
        }

        AddPlayer(
            entities,
            ECombatantId.Player,
            TheBazaar.Data.Run?.Player?.Hero ?? EHero.Common,
            TheBazaar.Data.Run?.Player?.Hero.ToString() ?? "Player",
            order++
        );
        AddPlayer(
            entities,
            ECombatantId.Opponent,
            TheBazaar.Data.Run?.Opponent?.Hero ?? EHero.Common,
            TheBazaar.Data.SimPvpOpponent?.Name
                ?? TheBazaar.Data.Run?.Opponent?.Hero.ToString()
                ?? "Opponent",
            order
        );
        return entities;
    }

    private static void AddPlayer(
        Dictionary<string, CombatImpactEntity> entities,
        ECombatantId combatant,
        EHero hero,
        string name,
        int order
    )
    {
        var id = CombatImpactProjector.PlayerId(combatant);
        entities[id] = new CombatImpactEntity(id, name, "Hero", null, hero, combatant, order);
    }

    private static string? ResolveTitle(BazaarGameShared.Domain.Core.TLocalizableText? localizable)
    {
        if (localizable == null)
            return null;
        try
        {
            var localized = TooltipExtensions.GetLocalizedText(localizable);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }
        catch
        {
            // Localization may not be initialized during early replay bootstrap.
        }

        return !string.IsNullOrWhiteSpace(localizable.Text) ? localizable.Text : localizable.Key;
    }
}
