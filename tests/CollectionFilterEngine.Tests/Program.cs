using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

var normal = Card("Normal", ETier.Bronze);
var package = Card("Starter Package", ETier.Silver, isPackage: true);

var defaultPackageResult = CollectionFilterEngine.Apply(
    new[] { package, normal },
    new CollectionFilterState()
);
AssertSequence(defaultPackageResult, new[] { normal.Id }, "Packages are excluded by default.");

var includePackageFilter = new CollectionFilterState { IncludePackages = true };
var includePackageResult = CollectionFilterEngine.Apply(
    new[] { package, normal },
    includePackageFilter
);
AssertSequence(
    includePackageResult,
    new[] { normal.Id, package.Id },
    "IncludePackages restores package cards to the visible set."
);

var burnMerchant = Card(
    "Burn Merchant Item",
    ETier.Bronze,
    merchants: new[] { CollectionMerchantKind.Burn }
);
var healMerchant = Card(
    "Heal Merchant Item",
    ETier.Bronze,
    merchants: new[] { CollectionMerchantKind.Heal }
);
var merchantFilter = new CollectionFilterState();
merchantFilter.Merchants.Add(CollectionMerchantKind.Burn);
var merchantResult = CollectionFilterEngine.Apply(
    new[] { healMerchant, burnMerchant, normal },
    merchantFilter
);
AssertSequence(
    merchantResult,
    new[] { burnMerchant.Id },
    "Merchant filters match cards classified for at least one selected merchant."
);

var burnMerchantSkill = Card(
    "Burn Merchant Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    merchants: new[] { CollectionMerchantKind.Burn }
);
var healMerchantSkill = Card(
    "Heal Merchant Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    merchants: new[] { CollectionMerchantKind.Heal }
);
var skillMerchantFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
skillMerchantFilter.Merchants.Add(CollectionMerchantKind.Burn);
var skillMerchantResult = CollectionFilterEngine.Apply(
    new[] { healMerchantSkill, burnMerchantSkill, normal },
    skillMerchantFilter
);
AssertSequence(
    skillMerchantResult,
    new[] { burnMerchantSkill.Id },
    "Merchant filters also narrow the Skill tab."
);

var weaponSkill = Card(
    "Weapon Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    tags: new[] { ECardTag.Weapon }
);
var potionSkill = Card(
    "Potion Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    tags: new[] { ECardTag.Potion }
);
var skillTagFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
skillTagFilter.Tags.Add(ECardTag.Weapon);
var skillTagResult = CollectionFilterEngine.Apply(
    new[] { potionSkill, weaponSkill, normal },
    skillTagFilter
);
AssertSequence(
    skillTagResult,
    new[] { weaponSkill.Id },
    "Tag filters are available for future skill filtering rules."
);

AssertFalse(
    CollectionCardClassifier.IsCatalogCard(
        ECardType.Item,
        "Assets/Cards/Debug.png",
        "[DEBUG] Item"
    ),
    "Debug-marked templates do not enter the catalog even when they have art."
);
AssertFalse(
    CollectionCardClassifier.IsCatalogCard(
        ECardType.Item,
        "Assets/Cards/Template.png",
        "[TEMPLATE] Item"
    ),
    "Template-marked entries do not enter the catalog even when they have art."
);
AssertTrue(
    CollectionCardClassifier.IsCatalogCard(ECardType.Skill, "Assets/Cards/Skill.png", "Real Skill"),
    "Normal Item/Skill cards with art are catalog cards."
);
AssertFalse(
    CollectionCardClassifier.IsCatalogCard(
        ECardType.Skill,
        "Icon_Skill_Placeholder.png",
        "Aggressive Mutations"
    ),
    "Placeholder skill art is not a catalog-ready skill."
);
AssertFalse(
    CollectionCardClassifier.IsCatalogCard(ECardType.Skill, "Placeholder", "[SKILL TEMPLATE]"),
    "Skill template placeholders do not enter the catalog."
);
AssertFalse(
    CollectionCardClassifier.IsCatalogCard(
        ECardType.Item,
        "Assets/Cards/LegacyItem.mat",
        "Legacy Material Item"
    ),
    "Legacy material art keys do not enter the catalog."
);
AssertFalse(
    CollectionCardClassifier.IsCatalogCard(
        ECardType.Item,
        "Assets/Cards/Template.png",
        "[SMALL ITEM TEMPLATE]"
    ),
    "Bracketed item template names do not enter the catalog."
);
AssertTrue(
    CollectionCardClassifier.IsPackageName("Vanessa Starter Package"),
    "Package detection is centralized for future rule hardening."
);
AssertValues(
    CollectionCardClassifier
        .ResolveMerchants(
            Array.Empty<ECardTag>(),
            new[] { EHiddenTag.BurnMerchant, EHiddenTag.Merchant }
        )
        .ToArray(),
    new[] { CollectionMerchantKind.Burn, CollectionMerchantKind.General },
    "Merchant hidden tags map to stable collection merchant kinds."
);

Console.WriteLine("CollectionFilterEngine checks passed.");

static CollectionCardVm Card(
    string name,
    ETier tier,
    ECardType type = ECardType.Item,
    bool isPackage = false,
    IReadOnlyCollection<ECardTag>? tags = null,
    IReadOnlyCollection<CollectionMerchantKind>? merchants = null
) =>
    new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Size = ECardSize.Medium,
        StartingTier = tier,
        Tags = tags ?? Array.Empty<ECardTag>(),
        DisplayName = name,
        InternalName = name,
        IsPackage = isPackage,
        Merchants = merchants ?? Array.Empty<CollectionMerchantKind>(),
    };

static void AssertSequence(
    IReadOnlyList<CollectionCardVm> actual,
    IReadOnlyList<Guid> expected,
    string message
)
{
    var actualIds = new Guid[actual.Count];
    for (var i = 0; i < actual.Count; i++)
        actualIds[i] = actual[i].Id;
    AssertValues(actualIds, expected, message);
}

static void AssertValues<T>(IReadOnlyList<T> actual, IReadOnlyList<T> expected, string message)
{
    if (actual.Count != expected.Count)
        throw new InvalidOperationException(
            $"{message} Expected {expected.Count} values, got {actual.Count}."
        );
    for (var i = 0; i < actual.Count; i++)
    {
        if (!EqualityComparer<T>.Default.Equals(actual[i], expected[i]))
            throw new InvalidOperationException(
                $"{message} At {i}: expected {expected[i]}, got {actual[i]}."
            );
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
