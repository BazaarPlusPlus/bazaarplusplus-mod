using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.ItemEnchantPreview.Preview;

var candidates = ItemEnchantPreviewCandidateSelector.SelectCandidates(
    currentEnchantment: EEnchantmentType.Heavy,
    availableEnchantments:
    [
        EEnchantmentType.Icy,
        EEnchantmentType.Icy,
        EEnchantmentType.Heavy,
        EEnchantmentType.Turbo,
    ],
    allEnchantments: [EEnchantmentType.Heavy, EEnchantmentType.Icy, EEnchantmentType.Turbo]
);

Assert(
    candidates.SequenceEqual([EEnchantmentType.Icy, EEnchantmentType.Turbo]),
    "Candidate selection should dedupe inputs and exclude the current enchantment."
);

Assert(
    ItemEnchantPreviewEligibility.IsEligible(ECardType.Item, EInventorySection.Hand, isInCombat: false),
    "Items in hand should be eligible for enchant previews."
);

Assert(
    !ItemEnchantPreviewEligibility.IsEligible(
        ECardType.Item,
        section: null,
        isInCombat: false
    ),
    "Items outside hand or stash should not be eligible."
);

Assert(
    !ItemEnchantPreviewEligibility.IsEligible(
        ECardType.Item,
        EInventorySection.Hand,
        isInCombat: true
    ),
    "Combat state should suppress enchant previews."
);

var itemCard = new ItemCard
{
    Type = ECardType.Item,
    Section = EInventorySection.Hand,
    Enchantment = EEnchantmentType.Heavy,
    Attributes =
    {
        [ECardAttributeType.DamageAmount] = 10,
        [ECardAttributeType.Cooldown] = 4000,
    },
};

var previewTemplate = new TEnchantment
{
    Attributes =
    {
        [ECardAttributeType.DamageAmount] = 15,
        [ECardAttributeType.FreezeAmount] = 2000,
    },
};

var snapshot = ItemEnchantPreviewSnapshotFactory.Create(
    itemCard,
    EEnchantmentType.Icy,
    previewTemplate
);

Assert(
    snapshot.CurrentEnchantment == EEnchantmentType.Heavy,
    "Snapshot should retain the source card's current enchantment."
);

Assert(
    snapshot.PreviewEnchantment == EEnchantmentType.Icy,
    "Snapshot should track the target preview enchantment."
);

Assert(
    snapshot.PreviewAttributes[ECardAttributeType.DamageAmount] == 15,
    "Preview attributes should override the item's base attributes."
);

Assert(
    snapshot.PreviewAttributes[ECardAttributeType.Cooldown] == 4000,
    "Preview attributes should preserve unchanged item attributes."
);

Assert(
    snapshot.PreviewAttributes[ECardAttributeType.FreezeAmount] == 2000,
    "Preview attributes should include attributes introduced by the preview enchantment."
);

var previewCard = ItemEnchantPreviewCardCloneFactory.Create(itemCard, snapshot);
previewCard.Attributes[ECardAttributeType.DamageAmount] = 42;

Assert(
    previewCard.Enchantment == EEnchantmentType.Icy,
    "Preview clone should expose the target enchantment."
);

Assert(
    itemCard.Enchantment == EEnchantmentType.Heavy,
    "Preview clone should not mutate the source card's enchantment."
);

Assert(
    itemCard.Attributes[ECardAttributeType.DamageAmount] == 10,
    "Preview clone should not share attribute state with the source card."
);

Assert(
    ItemEnchantPreviewFormatting.GetEnchantmentColorHex(EEnchantmentType.Icy) == "3FC8F7",
    "Formatting should expose the configured enchantment color."
);

var segment = ItemEnchantPreviewFormatting.CreateSegment(EEnchantmentType.Icy, "Freeze for 2 seconds");

Assert(
    segment.Text.Contains("Icy") && segment.Text.Contains("Freeze for 2 seconds"),
    "Formatted segments should contain both the enchantment label and rendered body text."
);

var key1 = ItemEnchantPreviewCache.CreateKey(snapshot);

var changedSnapshot = new ItemEnchantPreviewSnapshot
{
    CurrentEnchantment = snapshot.CurrentEnchantment,
    PreviewEnchantment = snapshot.PreviewEnchantment,
    PreviewAttributes = new Dictionary<ECardAttributeType, int>(snapshot.PreviewAttributes)
    {
        [ECardAttributeType.DamageAmount] = 99,
    },
};

var key2 = ItemEnchantPreviewCache.CreateKey(changedSnapshot);

Assert(key1 != key2, "Cache keys must change when preview-relevant attributes change.");

Console.WriteLine("ItemEnchantPreview checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
