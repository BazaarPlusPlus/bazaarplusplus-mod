#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Values;

namespace BazaarPlusPlus.Game.Tooltips;

internal sealed class UpgradePreviewValueProjection
{
    private UpgradePreviewValueProjection(
        ItemCard card,
        ItemCard sourceCard,
        ITCard template,
        ValueContext valueContext
    )
    {
        Card = card;
        SourceCard = sourceCard;
        Template = template;
        ValueContext = valueContext;
    }

    internal ItemCard Card { get; }

    internal ItemCard SourceCard { get; }

    internal ITCard Template { get; }

    internal ValueContext ValueContext { get; }

    internal static bool TryCreate(
        ItemCard source,
        ITCard template,
        ValueContext sourceValueContext,
        out UpgradePreviewValueProjection projection
    )
    {
        projection = null!;
        if (source == null || template is not TCardItem itemTemplate)
            return false;

        var nextTier = source.GetNextTier();
        if (
            nextTier == source.Tier
            || !itemTemplate.TryGetTierTemplate(nextTier, out var next)
            || next == null
        )
            return false;

        var attributes = new Dictionary<ECardAttributeType, int>(source.Attributes);
        foreach (var attribute in source.Attributes.Keys.Concat(next.Attributes.Keys).Distinct())
        {
            var currentBase = itemTemplate.GetAttributeBaseValueAtTier(attribute, source.Tier);
            var nextBase = itemTemplate.GetAttributeBaseValueAtTier(attribute, nextTier);
            if (!nextBase.HasValue)
                continue;

            if (source.Attributes.TryGetValue(attribute, out var currentValue))
            {
                attributes[attribute] =
                    nextBase.Value + (currentBase.HasValue ? currentValue - currentBase.Value : 0);
            }
            else
            {
                attributes[attribute] = nextBase.Value;
            }
        }

        var projectedCard = new ItemCard
        {
            InstanceId = source.InstanceId,
            TemplateId = source.TemplateId,
            Attributes = attributes,
            Heroes = new HashSet<EHero>(source.Heroes),
            HiddenTags = new HashSet<EHiddenTag>(source.HiddenTags),
            Size = source.Size,
            Tags = new HashSet<ECardTag>(source.Tags),
            Tier = nextTier,
            Type = source.Type,
            Owner = source.Owner,
            LeftSocketId = source.LeftSocketId,
            Section = source.Section,
            State = source.State,
            Template = template,
            Enchantment = source.Enchantment,
        };

        projection = new UpgradePreviewValueProjection(
            projectedCard,
            source,
            template,
            new ValueContext(sourceValueContext.Run, projectedCard, sourceValueContext.EventContext)
        );
        return true;
    }

    internal bool TryResolve(ITooltipComponent? component, out float value)
    {
        value = default;
        if (component is not ITooltipToken token)
            return false;

        var context = new TooltipContext(Card, Template, ValueContext);
        ITooltipToken? projectedToken = token switch
        {
            TooltipComponentAttribute attribute when attribute.ReferencedAttribute.HasValue =>
                TooltipComponentAttribute.Create(
                    context,
                    attribute.ReferencedAttribute.Value.ToString(),
                    attribute.StartWordIndex
                ),
            TooltipComponentAbility ability => TooltipComponentAbility.Create(
                context,
                ability.EffectId,
                ability.Accessor,
                ability.StartWordIndex
            ),
            TooltipComponentAura aura => TooltipComponentAura.Create(
                context,
                aura.EffectId,
                aura.Accessor,
                aura.StartWordIndex
            ),
            _ => null,
        };

        var resolved = projectedToken?.Resolve();
        if (!resolved.HasValue)
            return false;

        value = resolved.Value;
        return true;
    }

    internal bool TryResolveEffectiveCooldowns(out float currentSeconds, out float upgradedSeconds)
    {
        currentSeconds = default;
        upgradedSeconds = default;
        if (
            !SourceCard.Attributes.TryGetValue(ECardAttributeType.CooldownMax, out var currentMs)
            || !Card.Attributes.TryGetValue(ECardAttributeType.CooldownMax, out var upgradedMs)
            || currentMs <= 0
            || upgradedMs <= 0
        )
            return false;

        currentSeconds = ApplyCooldownReduction(SourceCard, currentMs) / 1000f;
        upgradedSeconds = ApplyCooldownReduction(Card, upgradedMs) / 1000f;
        return true;
    }

    private static float ApplyCooldownReduction(ItemCard card, int cooldownMs)
    {
        var reduction = card.Attributes.GetValueOrDefault(
            ECardAttributeType.PercentCooldownReduction
        );
        var multiplier = Math.Max(0, 100 - reduction) / 100f;
        return cooldownMs * multiplier;
    }
}
