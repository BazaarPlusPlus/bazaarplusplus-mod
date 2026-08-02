#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.Localization;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactEntitySnapshotReader
{
    internal static string ResolveDisplayName(Card card, string nativeTooltipTitle)
    {
        var name = ResolveTitle(card.Template?.Localization?.Title);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        if (!string.IsNullOrWhiteSpace(card.Template?.InternalName))
            return card.Template.InternalName;

        return CombatImpactEntityName.RemoveNativeEnchantmentPrefix(nativeTooltipTitle);
    }

    internal static IReadOnlyDictionary<string, CombatImpactEntity> Read()
    {
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal);
        var order = 0;
        var cards = TheBazaar
            .Data.Entities.Values.Where(card => card?.InstanceId.Value is { Length: > 0 })
            .OrderBy(card => CombatImpactEntityOwnerResolver.Resolve(card!.Owner?.CombatantId))
            .ThenBy(card => card!.InstanceId.Value, StringComparer.Ordinal);
        foreach (var card in cards)
        {
            if (card?.InstanceId.Value is not { Length: > 0 } instanceId)
                continue;

            var owner = CombatImpactEntityOwnerResolver.Resolve(card.Owner?.CombatantId);
            var template = card.Template;
            var name = ResolveTitle(template?.Localization?.Title);
            if (string.IsNullOrWhiteSpace(name))
                name = template?.InternalName;
            if (string.IsNullOrWhiteSpace(name))
                name = card.Type == ECardType.Skill ? "Skill" : "Item";

            var item = card as ItemCard;
            entities[instanceId] = new CombatImpactEntity(
                instanceId,
                name!,
                card.Type.ToString(),
                template?.ArtKey,
                null,
                owner,
                order++,
                card.TemplateId,
                card.Tier,
                item == null ? 1 : CardSizeSpan.Resolve(item.Size),
                item?.Enchantment,
                card.Attributes == null
                    ? null
                    : new Dictionary<ECardAttributeType, int>(card.Attributes)
            );
        }

        var playerHero = NormalizeHero(TheBazaar.Data.Run?.Player?.Hero);
        AddPlayer(entities, ECombatantId.Player, playerHero, T("己方", "You"), order++);
        var opponentHero = NormalizeHero(TheBazaar.Data.Run?.Opponent?.Hero);
        var opponentName = TheBazaar.Data.SimPvpOpponent?.Name;
        if (string.IsNullOrWhiteSpace(opponentName))
            opponentName = opponentHero?.ToString() ?? T("对手", "Opponent");
        AddPlayer(entities, ECombatantId.Opponent, opponentHero, opponentName, order);
        return entities;
    }

    private static void AddPlayer(
        Dictionary<string, CombatImpactEntity> entities,
        ECombatantId combatant,
        EHero? hero,
        string name,
        int order
    )
    {
        var id = CombatImpactProjector.PlayerId(combatant);
        entities[id] = new CombatImpactEntity(id, name, "Hero", null, hero, combatant, order);
    }

    private static EHero? NormalizeHero(EHero? hero) =>
        hero.HasValue && hero.Value != EHero.Common ? hero : null;

    private static string T(string chinese, string english) =>
        L.CurrentLanguageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? chinese
            : english;

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
