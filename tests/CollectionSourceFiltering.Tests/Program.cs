using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

var ailaId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
var ailaId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
var globalMerchantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
var pygTrainerId = Guid.Parse("44444444-4444-4444-4444-444444444444");
var collisionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

var entries = CollectionSourceCatalog.Build(
    $$"""
    {
      "schemaVersion": 2,
      "entries": [
        {
          "name": "Aila",
          "kind": "Merchant",
          "availableHeroes": ["Vanessa"],
          "description": "Sells Crit items",
          "portraitTemplateId": "{{ailaId1}}",
          "sourceTemplateIds": ["{{ailaId1}}", "{{ailaId2}}"],
          "offerRule": { "heroMode": "SelectedHero", "hiddenTagsAny": ["Crit", "CritReference"] }
        },
        {
          "name": "Aila",
          "kind": "Merchant",
          "availableHeroes": ["Dooley"],
          "description": "Sells Crit items",
          "portraitTemplateId": "{{collisionId}}",
          "sourceTemplateIds": ["{{collisionId}}"],
          "offerRule": { "heroMode": "SelectedHero", "hiddenTagsAny": ["Crit", "CritReference"] }
        },
        {
          "name": "Nufu",
          "kind": "Merchant",
          "availableHeroes": [],
          "description": "Sells common items",
          "portraitTemplateId": "{{globalMerchantId}}",
          "sourceTemplateIds": ["{{globalMerchantId}}"],
          "offerRule": { "heroMode": "SelectedHero" }
        },
        {
          "name": "Professor Riggs",
          "kind": "Trainer",
          "availableHeroes": ["Pygmalien"],
          "description": "Teaches skills",
          "portraitTemplateId": "{{pygTrainerId}}",
          "sourceTemplateIds": ["{{pygTrainerId}}"],
          "offerRule": { "heroMode": "SelectedHero" }
        },
        {
          "name": "Nufu",
          "kind": "Merchant",
          "availableHeroes": [],
          "description": "Second collision entry",
          "portraitTemplateId": "66666666-6666-6666-6666-666666666666",
          "sourceTemplateIds": ["66666666-6666-6666-6666-666666666666"],
          "offerRule": { "heroMode": "AllHeroes" }
        }
      ]
    }
    """
);

AssertEqual(5, entries.Count, "Catalog build should keep valid v2 entries.");
AssertTrue(
    entries.All(entry => !string.IsNullOrWhiteSpace(entry.SourceKey)),
    "Every source entry should receive a stable source key."
);
AssertEqual(
    entries.Count,
    entries.Select(entry => entry.SourceKey).Distinct(StringComparer.Ordinal).Count(),
    "Source keys should be unique across current catalog identities."
);

var vanessaAila = entries.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Aila", StringComparison.Ordinal)
    && entry.AvailableHeroes.Contains(EHero.Vanessa)
);
AssertEqual(
    "merchant:aila:vanessa",
    vanessaAila.SourceKey,
    "Generated source keys should drop source tier and include visible hero identity."
);
AssertValues(
    vanessaAila.SourceTemplateIds.ToArray(),
    new[] { ailaId1, ailaId2 },
    "Source identity must preserve all source template ids for encounter lookup."
);
AssertEqual(
    ailaId1,
    vanessaAila.PortraitTemplateId,
    "Portrait template id should be distinct but drawn from the source id set."
);

var nufuKeys = entries
    .Where(entry => entry.Kind == CollectionSourceKind.Merchant && entry.Name == "Nufu")
    .Select(entry => entry.SourceKey)
    .ToArray();
AssertEqual(2, nufuKeys.Length, "Fixture should exercise a forced source-key collision.");
AssertTrue(
    nufuKeys.All(key => key.StartsWith("merchant:nufu:global:", StringComparison.Ordinal)),
    "Colliding tier-less source keys should receive a template-id fingerprint suffix."
);

AssertThrows<InvalidOperationException>(
    () => CollectionSourceCatalog.Build("""{ "schemaVersion": 1, "entries": [] }"""),
    "Schema v1 source catalogs should fail validation."
);
AssertThrows<InvalidOperationException>(
    () =>
        CollectionSourceCatalog.Build(
            $$"""
            {
              "schemaVersion": 2,
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerRule": { "heroMode": "Bogus" }
                }
              ]
            }
            """
        ),
    "Unknown enum values should fail catalog validation instead of silently defaulting."
);

var currentCatalogPath = Path.Combine("Data", "CollectionSources", "collection-sources.json");
var currentCatalogJson = File.ReadAllText(currentCatalogPath);
var currentCatalog = CollectionSourceCatalog.Build(currentCatalogJson);
AssertEqual(
    70,
    currentCatalog.Count,
    "Current source catalog should preserve the 70 known sources."
);
AssertTrue(
    currentCatalog.Any(entry =>
        string.Equals(
            entry.SourceKey,
            CollectionPanelSelectionState.DefaultMerchantSourceKey,
            StringComparison.Ordinal
        )
    ),
    "Default selected merchant source key should exist in the current source catalog."
);
AssertEqual(
    currentCatalog.Count,
    currentCatalog.Select(entry => entry.SourceKey).Distinct(StringComparer.Ordinal).Count(),
    "Current source catalog keys should be unique."
);
foreach (var source in currentCatalog)
{
    AssertTrue(source.PortraitTemplateId != Guid.Empty, $"{source.SourceKey} needs a portrait id.");
    AssertTrue(
        source.SourceTemplateIds.Contains(source.PortraitTemplateId),
        $"{source.SourceKey} should include portraitTemplateId in sourceTemplateIds."
    );
    AssertTrue(source.SourceTemplateIds.Count > 0, $"{source.SourceKey} needs source ids.");
    if (source.OfferRule.HeroMode == CollectionSourceHeroMode.FixedHero)
        AssertTrue(source.OfferRule.Hero.HasValue, $"{source.SourceKey} needs a fixed hero.");
    if (source.OfferRule.HeroMode == CollectionSourceHeroMode.NeutralOnly)
        AssertFalse(
            source.OfferRule.Hero.HasValue,
            $"{source.SourceKey} neutral-only rules should not carry hero."
        );
}

var openOutsideRun = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: EHero.Dooley,
    currentEncounterTemplateId: vanessaAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries
);
AssertEqual(
    CollectionPanelSelectionState.Default,
    openOutsideRun,
    "Opening outside an in-game run should fall back to VAN + Ande."
);
var dooleyAila = entries.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Aila", StringComparison.Ordinal)
    && entry.AvailableHeroes.Contains(EHero.Dooley)
);
var openOnCurrentMerchant = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: EHero.Dooley,
    currentEncounterTemplateId: dooleyAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(EHero.Dooley, dooleyAila.SourceKey),
    openOnCurrentMerchant,
    "Opening during a run should select the current concrete hero and merchant source."
);
var globalNufu = entries.First(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Nufu", StringComparison.Ordinal)
    && entry.OfferRule.HeroMode == CollectionSourceHeroMode.SelectedHero
);
var openOnChoiceMerchant = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: EHero.Vanessa,
    currentEncounterTemplateId: null,
    choiceSelectionTemplateIds: new[] { pygTrainerId, globalMerchantId },
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(EHero.Vanessa, globalNufu.SourceKey),
    openOnChoiceMerchant,
    "Opening on the choice screen should select the first available merchant source, ignoring trainers."
);
var openWithoutMerchant = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: EHero.Vanessa,
    currentEncounterTemplateId: pygTrainerId,
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(
        EHero.Vanessa,
        CollectionPanelSelectionState.DefaultMerchantSourceKey
    ),
    openWithoutMerchant,
    "Opening during a run without a usable merchant should keep the run hero and fall back to Ande."
);
var openWithoutConcreteHero = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: EHero.Common,
    currentEncounterTemplateId: dooleyAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries
);
AssertEqual(
    CollectionPanelSelectionState.Default,
    openWithoutConcreteHero,
    "Opening during a run without a concrete hero should fall back to VAN + Ande."
);

var noHeroCacheKey = CollectionSourceOfferPoolCacheKey.Build(vanessaAila, selectedHero: null);
AssertTrue(
    noHeroCacheKey.StartsWith(vanessaAila.SourceKey + "|", StringComparison.Ordinal),
    "Source offer cache key should include the stable source key."
);
AssertTrue(
    noHeroCacheKey.Contains(ailaId1.ToString("N")[..12], StringComparison.Ordinal)
        && noHeroCacheKey.Contains(ailaId2.ToString("N")[..12], StringComparison.Ordinal),
    "Source offer cache key should include a fingerprint of all source template ids."
);
AssertTrue(
    noHeroCacheKey.EndsWith("|no-selected-hero", StringComparison.Ordinal),
    "Source offer cache key should distinguish the no-selected-hero state."
);
var heroCacheKey = CollectionSourceOfferPoolCacheKey.Build(vanessaAila, EHero.Vanessa);
AssertFalse(
    string.Equals(noHeroCacheKey, heroCacheKey, StringComparison.Ordinal),
    "Source offer cache key should vary by selected hero."
);

var visibleForVanessa = CollectionSourceCatalog
    .VisibleEntries(entries, CollectionSourceKind.Merchant, EHero.Vanessa)
    .Select(entry => entry.Name)
    .ToArray();
AssertValues(
    visibleForVanessa,
    new[] { "Aila", "Nufu", "Nufu" },
    "Visible merchant sources should include selected hero sources plus global sources."
);
var visibleWithoutHero = CollectionSourceCatalog
    .VisibleEntries(entries, CollectionSourceKind.Merchant, selectedHero: null)
    .Select(entry => entry.Name)
    .ToArray();
AssertValues(
    visibleWithoutHero,
    new[] { "Aila", "Aila", "Nufu", "Nufu" },
    "Without a concrete hero selected, merchant source selector should show all merchants."
);
var visibleTrainers = CollectionSourceCatalog
    .VisibleEntries(entries, CollectionSourceKind.Trainer, EHero.Vanessa)
    .ToArray();
AssertEqual(
    0,
    visibleTrainers.Length,
    "Item and Skill source selectors should stay kind-specific."
);

var selectedHeroRule = BuildSingleEntry(
    "Selected Hero",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "SelectedHero" }"""
);
var selectedHeroCards = new[]
{
    CatalogCard(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
        ECardType.Item,
        [EHero.Vanessa]
    ),
    CatalogCard(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), ECardType.Item, [EHero.Common]),
    CatalogCard(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), ECardType.Item, [EHero.Dooley]),
};
var selectedHeroResult = CollectionSourceOfferPoolResolver.Resolve(
    selectedHeroRule,
    EHero.Vanessa,
    selectedHeroCards
);
AssertEqual(
    CollectionSourceOfferPoolStatus.Ready,
    selectedHeroResult.Status,
    "SelectedHero rules should resolve without runtime fallback."
);
AssertSet(
    selectedHeroResult.OfferedCardIds,
    selectedHeroCards.Take(2).Select(card => card.Id).ToArray(),
    "SelectedHero rules should match the selected hero plus Common cards."
);
var selectedHeroDisabledResult = CollectionSourceOfferPoolResolver.Resolve(
    selectedHeroRule,
    selectedHero: null,
    selectedHeroCards
);
AssertSet(
    selectedHeroDisabledResult.OfferedCardIds,
    selectedHeroCards.Select(card => card.Id).ToArray(),
    "SelectedHero rules should not crop by hero when no concrete hero is selected."
);
var noneSelectedResult = CollectionSourceOfferPoolResolver.Resolve(
    source: null,
    selectedHero: EHero.Vanessa,
    selectedHeroCards
);
AssertEqual(
    CollectionSourceOfferPoolStatus.NoneSelected,
    noneSelectedResult.Status,
    "Resolver should report NoneSelected when no source chip is active."
);

var fixedHeroEntry = BuildSingleEntry(
    "Fixed Jules",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "FixedHero", "hero": "Jules" }"""
);
var fixedHeroCards = new[]
{
    CatalogCard(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), ECardType.Item, [EHero.Jules]),
    CatalogCard(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), ECardType.Item, [EHero.Dooley]),
};
var fixedHeroResult = CollectionSourceOfferPoolResolver.Resolve(
    fixedHeroEntry,
    EHero.Dooley,
    fixedHeroCards
);
AssertSet(
    fixedHeroResult.OfferedCardIds,
    new[] { fixedHeroCards[0].Id },
    "FixedHero rules should use the source hero, not the selected UI hero."
);

var allHeroEntry = BuildSingleEntry(
    "All Crit",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "AllHeroes", "hiddenTagsAny": ["Crit"] }"""
);
var allHeroCards = new[]
{
    CatalogCard(
        Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
        ECardType.Item,
        [EHero.Vanessa],
        hiddenTags: [EHiddenTag.Crit]
    ),
    CatalogCard(
        Guid.Parse("cccccccc-0000-0000-0000-000000000002"),
        ECardType.Item,
        [EHero.Dooley],
        hiddenTags: [EHiddenTag.Crit]
    ),
    CatalogCard(Guid.Parse("cccccccc-0000-0000-0000-000000000003"), ECardType.Item, [EHero.Dooley]),
};
var allHeroResult = CollectionSourceOfferPoolResolver.Resolve(
    allHeroEntry,
    EHero.Vanessa,
    allHeroCards
);
AssertSet(
    allHeroResult.OfferedCardIds,
    allHeroCards.Take(2).Select(card => card.Id).ToArray(),
    "AllHeroes rules should ignore the selected UI hero and still apply other predicates."
);

var neutralEntry = BuildSingleEntry(
    "Neutral Bronze",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "NeutralOnly", "startingTier": { "mode": "AtMost", "tier": "Bronze" } }"""
);
var neutralCards = new[]
{
    CatalogCard(
        Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
        ECardType.Item,
        [EHero.Common],
        tier: ETier.Bronze
    ),
    CatalogCard(
        Guid.Parse("dddddddd-0000-0000-0000-000000000002"),
        ECardType.Item,
        [EHero.Common],
        tier: ETier.Silver
    ),
    CatalogCard(
        Guid.Parse("dddddddd-0000-0000-0000-000000000003"),
        ECardType.Item,
        [EHero.Vanessa],
        tier: ETier.Bronze
    ),
};
var neutralResult = CollectionSourceOfferPoolResolver.Resolve(
    neutralEntry,
    EHero.Vanessa,
    neutralCards
);
AssertSet(
    neutralResult.OfferedCardIds,
    new[] { neutralCards[0].Id },
    "NeutralOnly rules should require Common cards and still enforce starting tier."
);

// NeutralOnly ignores the selected UI hero entirely: it returns Common/neutral cards
// regardless of which concrete hero (or none) is selected. This is an INTENTIONAL,
// documented divergence from the pre-migration description switch, which ANDed the UI
// hero filter and returned an empty pool when a concrete hero was selected (the sole
// NeutralOnly source, Curio). See the design doc "NeutralOnly migration note (Curio)".
var neutralHeroEntry = BuildSingleEntry(
    "Neutral Hero Invariance",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "NeutralOnly" }"""
);
var neutralCommonCard = CatalogCard(
    Guid.Parse("d0000000-0000-0000-0000-000000000001"),
    ECardType.Item,
    [EHero.Common]
);
var neutralVanessaCard = CatalogCard(
    Guid.Parse("d0000000-0000-0000-0000-000000000002"),
    ECardType.Item,
    [EHero.Vanessa]
);
var neutralHeroCards = new[] { neutralCommonCard, neutralVanessaCard };
var neutralWithVanessa = CollectionSourceOfferPoolResolver
    .Resolve(neutralHeroEntry, EHero.Vanessa, neutralHeroCards)
    .OfferedCardIds;
AssertSet(
    neutralWithVanessa,
    new[] { neutralCommonCard.Id },
    "NeutralOnly must return only Common cards and exclude the selected concrete hero's card "
        + "(intentional divergence from the old description switch, which returned empty)."
);
var neutralWithNoHero = CollectionSourceOfferPoolResolver
    .Resolve(neutralHeroEntry, selectedHero: null, neutralHeroCards)
    .OfferedCardIds;
AssertSet(
    neutralWithNoHero,
    neutralWithVanessa.ToArray(),
    "NeutralOnly output must be invariant to the selected UI hero (no concrete hero vs Vanessa)."
);
var neutralWithOtherHero = CollectionSourceOfferPoolResolver
    .Resolve(neutralHeroEntry, EHero.Dooley, neutralHeroCards)
    .OfferedCardIds;
AssertSet(
    neutralWithOtherHero,
    neutralWithVanessa.ToArray(),
    "NeutralOnly output must be invariant to the selected UI hero (Dooley vs Vanessa)."
);

var exactTierEntry = BuildSingleEntry(
    "Exact Gold",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "AllHeroes", "startingTier": { "mode": "Exact", "tier": "Gold" } }"""
);
var exactTierCards = new[]
{
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
        ECardType.Item,
        [EHero.Common],
        tier: ETier.Silver
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000002"),
        ECardType.Item,
        [EHero.Common],
        tier: ETier.Gold
    ),
};
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(exactTierEntry, EHero.Vanessa, exactTierCards)
        .OfferedCardIds,
    new[] { exactTierCards[1].Id },
    "Exact starting-tier rules should not behave like AtMost."
);

var complexEntry = BuildSingleEntry(
    "Complex",
    CollectionSourceKind.Merchant,
    """
    {
      "heroMode": "AllHeroes",
      "sizesAny": ["Small"],
      "tagsAny": ["Tool"],
      "tagsNone": ["Weapon"],
      "hiddenTagsAny": ["Burn"],
      "enchantableOnly": true
    }
    """
);
var complexCards = new[]
{
    CatalogCard(
        Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
        ECardType.Item,
        [EHero.Common],
        tags: [ECardTag.Tool],
        hiddenTags: [EHiddenTag.Burn],
        size: ECardSize.Small,
        isEnchantable: true
    ),
    CatalogCard(
        Guid.Parse("ffffffff-0000-0000-0000-000000000002"),
        ECardType.Item,
        [EHero.Common],
        tags: [ECardTag.Tool, ECardTag.Weapon],
        hiddenTags: [EHiddenTag.Burn],
        size: ECardSize.Small,
        isEnchantable: true
    ),
    CatalogCard(
        Guid.Parse("ffffffff-0000-0000-0000-000000000003"),
        ECardType.Item,
        [EHero.Common],
        tags: [ECardTag.Tool],
        hiddenTags: [EHiddenTag.Burn],
        size: ECardSize.Medium,
        isEnchantable: true
    ),
};
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(complexEntry, EHero.Vanessa, complexCards)
        .OfferedCardIds,
    new[] { complexCards[0].Id },
    "Structured rules should AND size, tag include/exclude, hidden tag, and enchantable predicates."
);

foreach (var sourceEntry in currentCatalog)
{
    var selectedHero = RepresentativeSelectedHero(sourceEntry);
    var match = MatchingCard(sourceEntry, selectedHero, GuidFromIndex(sourceEntry.SourceKey, 1));
    var nonMatch = MatchingCard(
        sourceEntry,
        selectedHero,
        GuidFromIndex(sourceEntry.SourceKey, 2),
        sourceEntry.Kind == CollectionSourceKind.Merchant ? ECardType.Skill : ECardType.Item
    );
    var probePool = CollectionSourceOfferPoolResolver.Resolve(
        sourceEntry,
        selectedHero,
        new[] { match, nonMatch }
    );
    AssertEqual(
        CollectionSourceOfferPoolStatus.Ready,
        probePool.Status,
        $"Current source catalog entry should resolve as Ready: {sourceEntry.SourceKey}."
    );
    AssertTrue(
        probePool.OfferedCardIds.Contains(match.Id),
        $"Representative match should be included for {sourceEntry.SourceKey}."
    );
    AssertFalse(
        probePool.OfferedCardIds.Contains(nonMatch.Id),
        $"Representative non-match should be excluded for {sourceEntry.SourceKey}."
    );
}

Console.WriteLine("Collection source filtering checks passed.");

static CollectionSourceEntry BuildSingleEntry(
    string name,
    CollectionSourceKind kind,
    string offerRuleJson
)
{
    var id = GuidFromIndex(name, 7);
    return CollectionSourceCatalog
        .Build(
            $$"""
            {
              "schemaVersion": 2,
              "entries": [
                {
                  "name": "{{name}}",
                  "kind": "{{kind}}",
                  "availableHeroes": [],
                  "description": "{{name}} fixture",
                  "portraitTemplateId": "{{id}}",
                  "sourceTemplateIds": ["{{id}}"],
                  "offerRule": {{offerRuleJson}}
                }
              ]
            }
            """
        )
        .Single();
}

static EHero? RepresentativeSelectedHero(CollectionSourceEntry entry)
{
    if (entry.OfferRule.HeroMode != CollectionSourceHeroMode.SelectedHero)
        return EHero.Vanessa;
    if (entry.AvailableHeroes.Count > 0)
        return entry.AvailableHeroes[0];
    return EHero.Vanessa;
}

static CollectionCardVm MatchingCard(
    CollectionSourceEntry entry,
    EHero? selectedHero,
    Guid id,
    ECardType? cardType = null
)
{
    var rule = entry.OfferRule;
    var tags = rule.TagsAny.Count > 0 ? new[] { rule.TagsAny[0] } : Array.Empty<ECardTag>();
    if (rule.TagsNone.Contains(ECardTag.Tool) && tags.Length == 0)
        tags = new[] { ECardTag.Food };
    else if (tags.Length == 0)
        tags = new[] { ECardTag.Tool };

    return new CollectionCardVm
    {
        Id = id,
        Type =
            cardType
            ?? (entry.Kind == CollectionSourceKind.Merchant ? ECardType.Item : ECardType.Skill),
        Size = rule.SizesAny.Count > 0 ? rule.SizesAny[0] : ECardSize.Medium,
        StartingTier = rule.StartingTier?.Tier ?? ETier.Bronze,
        Heroes = rule.HeroMode switch
        {
            CollectionSourceHeroMode.SelectedHero => selectedHero.HasValue
                ? new[] { selectedHero.Value }
                : new[] { EHero.Dooley },
            CollectionSourceHeroMode.FixedHero => new[] { rule.Hero!.Value },
            CollectionSourceHeroMode.NeutralOnly => new[] { EHero.Common },
            _ => new[] { EHero.Dooley },
        },
        Tags = tags,
        HiddenTags =
            rule.HiddenTagsAny.Count > 0
                ? new[] { rule.HiddenTagsAny[0] }
                : Array.Empty<EHiddenTag>(),
        DisplayName = id.ToString("N"),
        InternalName = id.ToString("N"),
        ArtKey = id.ToString("N"),
        IsEnchantable = rule.EnchantableOnly,
    };
}

static CollectionCardVm CatalogCard(
    Guid id,
    ECardType type,
    IEnumerable<EHero> heroes,
    IEnumerable<ECardTag>? tags = null,
    IEnumerable<EHiddenTag>? hiddenTags = null,
    ECardSize size = ECardSize.Medium,
    ETier tier = ETier.Bronze,
    bool isEnchantable = false
) =>
    new()
    {
        Id = id,
        Type = type,
        Size = size,
        StartingTier = tier,
        Heroes = heroes.ToArray(),
        Tags = tags?.ToArray() ?? Array.Empty<ECardTag>(),
        HiddenTags = hiddenTags?.ToArray() ?? Array.Empty<EHiddenTag>(),
        DisplayName = id.ToString("N"),
        InternalName = id.ToString("N"),
        ArtKey = id.ToString("N"),
        IsEnchantable = isEnchantable,
    };

static Guid GuidFromIndex(string seed, int index)
{
    var bytes = System.Text.Encoding.UTF8.GetBytes(seed + "|" + index);
    var buffer = new byte[16];
    for (var i = 0; i < bytes.Length; i++)
        buffer[i % buffer.Length] = (byte)(buffer[i % buffer.Length] ^ bytes[i]);
    return new Guid(buffer);
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

static void AssertSet<T>(
    IReadOnlyCollection<T> actual,
    IReadOnlyCollection<T> expected,
    string message
)
{
    var actualSet = new HashSet<T>(actual);
    var expectedSet = new HashSet<T>(expected);
    if (!actualSet.SetEquals(expectedSet))
        throw new InvalidOperationException(
            $"{message} Expected [{string.Join(", ", expectedSet)}], got [{string.Join(", ", actualSet)}]."
        );
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
