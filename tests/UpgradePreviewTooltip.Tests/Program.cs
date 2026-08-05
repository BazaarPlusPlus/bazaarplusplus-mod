using BazaarGameClient.Domain.Models.Cards;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarPlusPlus.Game.Tooltips;

var bongosValueReference = new TReferenceValueCardAttribute
{
    AttributeType = ECardAttributeType.Custom_1,
    Target = new TTargetCardSelf(),
    Modifier = new TValueModifier
    {
        ModifyMode = EValueModifierMode.Multiply,
        Value = new TFixedValue { Value = 0.2f },
    },
};
var fieryAbility = new TCardAbility
{
    Id = "fiery",
    Action = new TActionCardModifyAttribute
    {
        AttributeType = ECardAttributeType.BurnApplyAmount,
        Target = new TTargetCardSelf(),
        Value = bongosValueReference,
    },
};
var template = new TCardItem
{
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Enchantments = new Dictionary<EEnchantmentType, TEnchantment>
    {
        [EEnchantmentType.Fiery] = new TEnchantment
        {
            Abilities = new Dictionary<string, TCardAbility> { [fieryAbility.Id] = fieryAbility },
        },
    },
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_1] = 5,
            },
        },
        [ETier.Silver] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_1] = 10,
            },
        },
        [ETier.Gold] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_1] = 15,
            },
        },
    },
};
var card = new ItemCard
{
    Template = template,
    Type = ECardType.Item,
    Tier = ETier.Silver,
    Enchantment = EEnchantmentType.Fiery,
    Attributes = new Dictionary<ECardAttributeType, int> { [ECardAttributeType.Custom_1] = 10 },
};
var currentContext = new TooltipContext(card, template, new ValueContext(null!, card));
var currentToken = TooltipComponentAbility.Create(currentContext, fieryAbility.Id, null, 0);

AssertNotNull(currentToken, "Current Fiery token should be created.");
AssertEqual(2f, currentToken!.Resolve()!.Value, "Current derived Burn value should be 2.");
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        card,
        template,
        currentContext.ValueContext,
        out var projection
    ),
    "Upgrade projection should be created."
);
AssertEqual(
    15,
    projection.Card.Attributes[ECardAttributeType.Custom_1],
    "Projected Bongos value should use the Gold base."
);
AssertTrue(
    projection.TryResolve(currentToken, out var upgradedBurn),
    "Projected Fiery token should resolve."
);
AssertEqual(3f, upgradedBurn, "Derived Burn should be recomputed as 20% of 15, not 2 + 5.");

card.Attributes[ECardAttributeType.Custom_1] = 12;
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        card,
        template,
        currentContext.ValueContext,
        out var buffedProjection
    ),
    "Buffed upgrade projection should be created."
);
AssertEqual(
    17,
    buffedProjection.Card.Attributes[ECardAttributeType.Custom_1],
    "Runtime buffs should be preserved over the next-tier base."
);

var cooldownTemplate = new TCardItem
{
    Type = ECardType.Item,
    StartingTier = ETier.Silver,
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Silver] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.CooldownMax] = 7000,
            },
        },
        [ETier.Gold] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.CooldownMax] = 6000,
            },
        },
    },
};
var cooldownCard = new ItemCard
{
    Template = cooldownTemplate,
    Type = ECardType.Item,
    Tier = ETier.Silver,
    Attributes = new Dictionary<ECardAttributeType, int>
    {
        [ECardAttributeType.CooldownMax] = 7000,
        [ECardAttributeType.PercentCooldownReduction] = 50,
    },
};
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        cooldownCard,
        cooldownTemplate,
        new ValueContext(null!, cooldownCard),
        out var cooldownProjection
    ),
    "Cooldown upgrade projection should be created."
);
AssertTrue(
    cooldownProjection.TryResolveEffectiveCooldowns(
        out var currentCooldown,
        out var upgradedCooldown
    ),
    "Effective cooldowns should resolve."
);
AssertEqual(3.5f, currentCooldown, "50% reduction should halve the current cooldown.");
AssertEqual(3f, upgradedCooldown, "50% reduction should halve the upgraded cooldown.");

Console.WriteLine("Upgrade preview tooltip projection tests passed.");

static void AssertNotNull(object? value, string message)
{
    if (value == null)
        throw new InvalidOperationException(message);
}

static void AssertTrue(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

static void AssertEqual<T>(T expected, T actual, string message)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException($"{message} Expected={expected}, Actual={actual}");
}
