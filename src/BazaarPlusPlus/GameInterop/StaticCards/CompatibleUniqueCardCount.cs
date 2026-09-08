#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Values.ReferenceValues;

namespace BazaarPlusPlus.GameInterop.StaticCards;

// Keep this unregistered: native discriminator discovery must not collide with future clients.
internal sealed record CompatibleUniqueCardCount : TReferenceValueWithTargetCard
{
    protected override float? GetValueFromTargets(IEnumerable<ICard> targets) =>
        targets.Select(card => card.TemplateId).Distinct().Count();
}
