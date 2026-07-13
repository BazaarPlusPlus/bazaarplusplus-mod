using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.ItemEnchantPreview.Preview;
using BazaarPlusPlus.Patches.Tooltips;
using TheBazaar.Tooltips;

var candidates = ItemEnchantPreviewCandidateSelector.SelectCandidates(
    currentEnchantment: EEnchantmentType.Heavy,
    allEnchantments: [EEnchantmentType.Heavy, EEnchantmentType.Icy, EEnchantmentType.Turbo]
);

Assert(
    candidates.SequenceEqual([EEnchantmentType.Icy, EEnchantmentType.Turbo]),
    "Candidate selection should dedupe inputs and exclude the current enchantment."
);

var restrictedToOffered = ItemEnchantPreviewCandidateSelector.SelectCandidates(
    currentEnchantment: EEnchantmentType.Heavy,
    allEnchantments: [EEnchantmentType.Heavy, EEnchantmentType.Icy, EEnchantmentType.Turbo],
    restrictToEnchantmentNames: ["icy"]
);

Assert(
    restrictedToOffered.SequenceEqual([EEnchantmentType.Icy]),
    "A pedestal restriction should keep only the offered enchant type(s), matched case-insensitively."
);

var emptyRestriction = ItemEnchantPreviewCandidateSelector.SelectCandidates(
    currentEnchantment: EEnchantmentType.Heavy,
    allEnchantments: [EEnchantmentType.Heavy, EEnchantmentType.Icy, EEnchantmentType.Turbo],
    restrictToEnchantmentNames: []
);

Assert(
    emptyRestriction.SequenceEqual([EEnchantmentType.Icy, EEnchantmentType.Turbo]),
    "An empty restriction (manual Ctrl / Always / empty random pool) should keep the full candidate list."
);

Assert(
    ItemEnchantPreviewEligibility.IsEligible(
        ECardType.Item,
        EInventorySection.Hand,
        isInCombat: false
    ),
    "Items in hand should be eligible for enchant previews."
);

Assert(
    !ItemEnchantPreviewEligibility.IsEligible(ECardType.Item, section: null, isInCombat: false),
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

var opponentBoardItem = new ItemCard { Type = ECardType.Item, Section = null };

Assert(
    ItemEnchantPreviewEligibility.IsEligible(opponentBoardItem, isInCombat: false),
    "Opponent-board items without inventory ownership should still be eligible for enchant previews."
);

var itemCard = new ItemCard
{
    Type = ECardType.Item,
    Section = EInventorySection.Hand,
    Enchantment = EEnchantmentType.Heavy,
    Attributes = { [ECardAttributeType.DamageAmount] = 10, [ECardAttributeType.Cooldown] = 4000 },
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

Assert(
    typeof(ItemEnchantPreviewFormatting).Assembly.GetType(
        "BazaarPlusPlus.Patches.Tooltips.CardTooltipDataPassivePatch"
    ) == null,
    "Enchant preview must not append into the game's native passive-tooltip string."
);

Assert(
    BppTooltipSectionRenderPatch.HasNativeContent(passiveText: "", questGroupCount: 1),
    "Quest rows should count as native content and keep the divider above enchant previews."
);
Assert(
    !BppTooltipSectionRenderPatch.HasNativeContent(passiveText: "", questGroupCount: 0),
    "An otherwise empty native block should not add a divider above enchant previews."
);

Assert(
    ItemEnchantPreviewTooltipLayerPolicy.ElevatedSortingOrder(150) == 151,
    "Enchant preview elevation should sit one step above the tooltip clone's own sorting order, not an absolute layer."
);

var segment = ItemEnchantPreviewFormatting.CreateSegment(
    EEnchantmentType.Icy,
    "Freeze for 2 seconds"
);

Assert(
    segment.Text.Contains("Icy") && segment.Text.Contains("Freeze for 2 seconds"),
    "Formatted segments should contain both the enchantment label and rendered body text."
);
Assert(
    !segment.Text.Contains("·") && !segment.Text.Contains("\u00A0"),
    "Formatted enchantment previews should not reserve bullet or indentation space."
);

var cjkSegment = ItemEnchantPreviewFormatting.CreateSegment(
    EEnchantmentType.Icy,
    "<line-height=1.6em>冰冷时获得护盾</line-height>"
);

Assert(
    cjkSegment.Text.Contains("<line-height=1.9em>")
        && !cjkSegment.Text.Contains("<line-height=1.6em>")
        && !cjkSegment.Text.Contains("<line-height=2.1em>"),
    "CJK enchant preview wrapped lines should use the shared line height."
);

var englishLineHeightSegment = ItemEnchantPreviewFormatting.CreateSegment(
    EEnchantmentType.Icy,
    "<line-height=1.6em>Freeze for 2 seconds</line-height>"
);

Assert(
    englishLineHeightSegment.Text.Contains("<line-height=1.9em>")
        && !englishLineHeightSegment.Text.Contains("<line-height=1.6em>")
        && !englishLineHeightSegment.Text.Contains("<line-height=2.1em>"),
    "English enchant preview wrapped lines should use the shared line height."
);

var germanLineHeightSegment = ItemEnchantPreviewFormatting.CreateSegment(
    EEnchantmentType.Icy,
    "<line-height=1.6em>Für 2 Sekunden einfrieren</line-height>"
);

Assert(
    germanLineHeightSegment.Text.Contains("<line-height=1.9em>")
        && !germanLineHeightSegment.Text.Contains("<line-height=1.6em>")
        && !germanLineHeightSegment.Text.Contains("<line-height=2.1em>"),
    "Other enchant preview languages should use the shared line height."
);

var sectionText = ItemEnchantPreviewFormatting.BuildSectionText(
    new[] { segment, new TooltipSegment("<size=55%>second\r\n\r\nthird</size>", null, null, -1) }
);
Assert(
    sectionText
        == $"{segment.Text}<size=55%><line-height=2.1em>\n</line-height></size><size=55%>second\n\nthird</size>"
        && !sectionText.EndsWith("\n", StringComparison.Ordinal),
    "Section text should separate entries without flattening an entry's paragraphs or list lines."
);

var cjkSectionText = ItemEnchantPreviewFormatting.BuildSectionText(
    new[] { cjkSegment, cjkSegment }
);
Assert(
    cjkSectionText.Contains("<size=55%><line-height=2.1em>\n</line-height></size>")
        && !cjkSectionText.Contains("<line-height=2.3em>"),
    "CJK enchant preview entries should use the shared entry break."
);

var germanSectionText = ItemEnchantPreviewFormatting.BuildSectionText(
    new[] { germanLineHeightSegment, germanLineHeightSegment }
);
Assert(
    germanSectionText.Contains("<size=55%><line-height=2.1em>\n</line-height></size>")
        && !germanSectionText.Contains("<line-height=2.3em>"),
    "Other enchant preview languages should use the shared entry break."
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
