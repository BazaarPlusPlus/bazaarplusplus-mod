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
    bool isPackage = false,
    IReadOnlyCollection<CollectionMerchantKind>? merchants = null
) =>
    new()
    {
        Id = Guid.NewGuid(),
        Type = ECardType.Item,
        Size = ECardSize.Medium,
        StartingTier = tier,
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
