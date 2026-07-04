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
      "schemaVersion": 4,
      "groups": [
        "generalist",
        "all-hero",
        "tag-type-specialist",
        "trainer"
      ],
      "entries": [
        {
          "name": "Aila",
          "kind": "Merchant",
          "group": "tag-type-specialist",
          "order": 0,
          "availableHeroes": ["Vanessa"],
          "description": "Sells Crit items",
          "portraitTemplateId": "{{ailaId1}}",
          "sourceTemplateIds": ["{{ailaId1}}", "{{ailaId2}}"],
          "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero", "hiddenTagsAny": ["Crit", "CritReference"] } }]
        },
        {
          "name": "Aila",
          "kind": "Merchant",
          "group": "tag-type-specialist",
          "order": 1,
          "availableHeroes": ["Dooley"],
          "description": "Sells Crit items",
          "portraitTemplateId": "{{collisionId}}",
          "sourceTemplateIds": ["{{collisionId}}"],
          "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero", "hiddenTagsAny": ["Crit", "CritReference"] } }]
        },
        {
          "name": "Nufu",
          "kind": "Merchant",
          "group": "generalist",
          "order": 0,
          "availableHeroes": [],
          "description": "Sells common items",
          "portraitTemplateId": "{{globalMerchantId}}",
          "sourceTemplateIds": ["{{globalMerchantId}}"],
          "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
        },
        {
          "name": "Professor Riggs",
          "kind": "Trainer",
          "group": "trainer",
          "order": 0,
          "availableHeroes": ["Pygmalien"],
          "description": "Teaches skills",
          "portraitTemplateId": "{{pygTrainerId}}",
          "sourceTemplateIds": ["{{pygTrainerId}}"],
          "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
        },
        {
          "name": "Nufu",
          "kind": "Merchant",
          "group": "all-hero",
          "order": 0,
          "availableHeroes": [],
          "description": "Second collision entry",
          "portraitTemplateId": "66666666-6666-6666-6666-666666666666",
          "sourceTemplateIds": ["66666666-6666-6666-6666-666666666666"],
          "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "AllHeroes" } }]
        }
      ]
    }
    """
);

AssertEqual(5, entries.Count, "Catalog build should keep valid v4 entries.");
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
              "schemaVersion": 4,
              "groups": ["generalist"],
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "group": "generalist",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "Bogus" } }]
                }
              ]
            }
            """
        ),
    "Unknown enum values should fail catalog validation instead of silently defaulting."
);
AssertThrows<InvalidOperationException>(
    () =>
        CollectionSourceCatalog.Build(
            $$"""
            {
              "schemaVersion": 4,
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "group": "generalist",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """
        ),
    "Schema v4 source catalogs should require a top-level groups list."
);
AssertThrows<InvalidOperationException>(
    () =>
        CollectionSourceCatalog.Build(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["generalist", "generalist"],
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "group": "generalist",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """
        ),
    "Duplicate group ids should fail catalog validation."
);
AssertThrows<InvalidOperationException>(
    () =>
        CollectionSourceCatalog.Build(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["generalist"],
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "group": "typo",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """
        ),
    "Entry groups should fail validation when they are not declared in groups."
);
AssertThrows<InvalidOperationException>(
    () =>
        CollectionSourceCatalog.Build(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["generalist"],
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "group": "generalist",
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """
        ),
    "Entry order should be required instead of silently defaulting to zero."
);
AssertThrows<InvalidOperationException>(
    () =>
        CollectionSourceCatalog.Build(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["generalist"],
              "entries": [
                {
                  "name": "One",
                  "kind": "Merchant",
                  "group": "generalist",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "One",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                },
                {
                  "name": "Two",
                  "kind": "Trainer",
                  "group": "generalist",
                  "order": 1,
                  "availableHeroes": [],
                  "description": "Two",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """
        ),
    "A source template id should belong to exactly one collection source entry."
);

var currentCatalogPath = Path.Combine(
    "src",
    "BazaarPlusPlus",
    "Data",
    "CollectionSources",
    "collection-sources.json"
);
var currentCatalogJson = File.ReadAllText(currentCatalogPath);
var currentCatalog = CollectionSourceCatalog.Build(currentCatalogJson);
AssertEqual(
    71,
    currentCatalog.Count,
    "Current source catalog should include the 71 known sources after adding Private Pitchfork and Stickybeans."
);
AssertEqual(
    49,
    currentCatalog.Count(entry => entry.Kind == CollectionSourceKind.Merchant),
    "Current source catalog should include the 49 known merchants."
);
AssertEqual(
    22,
    currentCatalog.Count(entry => entry.Kind == CollectionSourceKind.Trainer),
    "Current source catalog should preserve the 22 known trainers."
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
AssertTrue(
    currentCatalog.Any(entry =>
        entry.Kind == CollectionSourceKind.Merchant
        && string.Equals(entry.Name, "Aimbot", StringComparison.Ordinal)
        && entry.AppliesToHero(EHero.Stelle)
    ),
    "Aimbot should be visible for Stelle after the v4 source-catalog migration."
);
var theTester = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "The Tester", StringComparison.Ordinal)
);
AssertValues(
    theTester.AvailableHeroes.ToArray(),
    new[] { EHero.Dooley, EHero.Stelle },
    "The Tester should only be visible for Dooley and Stelle."
);
var testerDooleyTech = CatalogCard(
    Guid.Parse("dddd1111-0000-0000-0000-000000000001"),
    ECardType.Item,
    [EHero.Dooley],
    tags: [ECardTag.Tech]
);
var testerStelleTech = CatalogCard(
    Guid.Parse("dddd1111-0000-0000-0000-000000000002"),
    ECardType.Item,
    [EHero.Stelle],
    tags: [ECardTag.Tech]
);
var testerVanessaTech = CatalogCard(
    Guid.Parse("dddd1111-0000-0000-0000-000000000003"),
    ECardType.Item,
    [EHero.Vanessa],
    tags: [ECardTag.Tech]
);
var testerCommonTech = CatalogCard(
    Guid.Parse("dddd1111-0000-0000-0000-000000000004"),
    ECardType.Item,
    [EHero.Common],
    tags: [ECardTag.Tech]
);
var testerDooleyTool = CatalogCard(
    Guid.Parse("dddd1111-0000-0000-0000-000000000005"),
    ECardType.Item,
    [EHero.Dooley],
    tags: [ECardTag.Tool]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            theTester,
            EHero.Vanessa,
            new[]
            {
                testerDooleyTech,
                testerStelleTech,
                testerVanessaTech,
                testerCommonTech,
                testerDooleyTool,
            }
        )
        .OfferedCardIds,
    new[] { testerDooleyTech.Id, testerStelleTech.Id },
    "The Tester should offer only Dooley/Stelle Tech items, independent of the selected UI hero."
);
var privatePitchfork = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Private Pitchfork", StringComparison.Ordinal)
);
AssertEqual(
    Guid.Parse("15b88e74-024e-40a3-a811-aa5810e68ca2"),
    privatePitchfork.PortraitTemplateId,
    "Private Pitchfork should use the live merchant template id as its portrait id."
);
AssertValues(
    privatePitchfork.SourceTemplateIds.ToArray(),
    new[] { Guid.Parse("15b88e74-024e-40a3-a811-aa5810e68ca2") },
    "Private Pitchfork should index its live merchant template id."
);
var pitchforkNeutralItem = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000001"),
    ECardType.Item,
    [EHero.Common],
    tags: [ECardTag.Tool]
);
var pitchforkNeutralLoot = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000002"),
    ECardType.Item,
    [EHero.Common],
    tags: [ECardTag.Loot]
);
var pitchforkHeroItem = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000003"),
    ECardType.Item,
    [EHero.Vanessa],
    tags: [ECardTag.Tool]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            privatePitchfork,
            EHero.Vanessa,
            new[] { pitchforkNeutralItem, pitchforkNeutralLoot, pitchforkHeroItem }
        )
        .OfferedCardIds,
    new[] { pitchforkNeutralItem.Id },
    "Private Pitchfork should offer neutral non-Loot items only."
);

var stickybeans = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Stickybeans", StringComparison.Ordinal)
);
AssertEqual(
    Guid.Parse("d0276b47-be8a-4bbc-ab55-9f92b352480a"),
    stickybeans.PortraitTemplateId,
    "Stickybeans should use the live merchant template id as its portrait id."
);
AssertValues(
    stickybeans.AvailableHeroes.ToArray(),
    new[]
    {
        EHero.Vanessa,
        EHero.Dooley,
        EHero.Pygmalien,
        EHero.Karnok,
        EHero.Mak,
        EHero.Stelle,
        EHero.Jules,
    },
    "Stickybeans should be visible for concrete heroes and hidden for Common."
);
var stickybeansCommon = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000001"),
    ECardType.Item,
    [EHero.Common]
);
var stickybeansVanessa = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000002"),
    ECardType.Item,
    [EHero.Vanessa]
);
var stickybeansDooley = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000003"),
    ECardType.Item,
    [EHero.Dooley]
);
var stickybeansSharedOther = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000004"),
    ECardType.Item,
    [EHero.Dooley, EHero.Stelle]
);
var stickybeansSharedSelected = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000005"),
    ECardType.Item,
    [EHero.Vanessa, EHero.Dooley]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            stickybeans,
            EHero.Vanessa,
            new[]
            {
                stickybeansCommon,
                stickybeansVanessa,
                stickybeansDooley,
                stickybeansSharedOther,
                stickybeansSharedSelected,
            }
        )
        .OfferedCardIds,
    new[] { stickybeansDooley.Id, stickybeansSharedOther.Id },
    "Stickybeans should offer non-Common items that do not include the selected UI hero."
);
AssertEqual(
    currentCatalog.Count,
    currentCatalog.Select(entry => entry.SourceKey).Distinct(StringComparer.Ordinal).Count(),
    "Current source catalog keys should be unique."
);
AssertEqual(
    currentCatalog.SelectMany(entry => entry.SourceTemplateIds).Count(),
    currentCatalog.SelectMany(entry => entry.SourceTemplateIds).Distinct().Count(),
    "Current source template ids should be unique across source entries."
);
var eli = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Eli", StringComparison.Ordinal)
);
var potionTaggedCard = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000001"),
    ECardType.Item,
    [EHero.Mak],
    tags: [ECardTag.Potion]
);
var potionReferenceCard = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000002"),
    ECardType.Item,
    [EHero.Mak],
    hiddenTags: [EHiddenTag.PotionReference]
);
var nonPotionCard = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000003"),
    ECardType.Item,
    [EHero.Mak],
    tags: [ECardTag.Tool]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(eli, EHero.Mak, new[] { potionTaggedCard, potionReferenceCard, nonPotionCard })
        .OfferedCardIds,
    new[] { potionTaggedCard.Id, potionReferenceCard.Id },
    "Eli should include both Potion-tagged cards and PotionReference cards."
);
foreach (var source in currentCatalog)
{
    AssertTrue(source.PortraitTemplateId != Guid.Empty, $"{source.SourceKey} needs a portrait id.");
    AssertTrue(
        source.SourceTemplateIds.Contains(source.PortraitTemplateId),
        $"{source.SourceKey} should include portraitTemplateId in sourceTemplateIds."
    );
    AssertTrue(source.SourceTemplateIds.Count > 0, $"{source.SourceKey} needs source ids.");
    var primaryRule = PrimaryRule(source);
    if (primaryRule.HeroMode == CollectionSourceHeroMode.FixedHero)
        AssertTrue(primaryRule.Hero.HasValue, $"{source.SourceKey} needs a fixed hero.");
    if (primaryRule.HeroMode == CollectionSourceHeroMode.NeutralOnly)
        AssertFalse(
            primaryRule.Hero.HasValue,
            $"{source.SourceKey} neutral-only rules should not carry hero."
        );
}

var vanessaMerchantRoster = CollectionSourceRoster.Build(
    CollectionSourceCatalog.VisibleEntries(
        currentCatalog,
        CollectionSourceKind.Merchant,
        EHero.Vanessa
    )
);
AssertNondecreasing(
    vanessaMerchantRoster.Select(item => item.Entry.GroupDisplayIndex).ToArray(),
    "Merchant roster should be sorted by declared group display order."
);
AssertValues(
    FirstGroupLayerCounts(vanessaMerchantRoster, 4),
    new[] { 3, 6, 6, 7 },
    "The visible Vanessa merchant roster should preserve the locked 3/6/6/7 top layers."
);
AssertValues(
    vanessaMerchantRoster.Where(item => item.BreakAfter).Select(item => item.Entry.Group).ToArray(),
    new[] { "generalist", "tier-specialist" },
    "Merchant roster should force row breaks only after generalist and tier layers."
);
var vanessaTrainerRoster = CollectionSourceRoster.Build(
    CollectionSourceCatalog.VisibleEntries(
        currentCatalog,
        CollectionSourceKind.Trainer,
        EHero.Vanessa
    )
);
AssertValues(
    vanessaTrainerRoster.Take(6).Select(item => item.Entry.Name).ToArray(),
    new[] { "Old Zane", "Nufu", "Pip", "Argenta", "Orlin", "Adira" },
    "Trainer roster should start with the current hero's trainer, then Nufu and tier trainers."
);
AssertEqual(
    0,
    vanessaTrainerRoster.Count(item => item.BreakAfter),
    "Trainer roster should not force row breaks because its first row self-aligns to six chips."
);

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
    "Opening outside an in-game run should fall back to VAN + Jay Jay."
);
var openOutsideRunWithRememberedHero = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: EHero.Dooley,
    currentEncounterTemplateId: vanessaAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedHero: EHero.Mak
);
AssertEqual(
    new CollectionPanelSelectionState(
        EHero.Mak,
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        CollectionSourceKind.Merchant
    ),
    openOutsideRunWithRememberedHero,
    "Opening outside a run should use the remembered explicit CollectionPanel hero."
);

var openOutsideRunWithRememberedCommon = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: EHero.Dooley,
    currentEncounterTemplateId: vanessaAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedHero: EHero.Common
);
AssertEqual(
    new CollectionPanelSelectionState(
        EHero.Common,
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        CollectionSourceKind.Merchant
    ),
    openOutsideRunWithRememberedCommon,
    "Opening outside a run should preserve Common as a remembered hero."
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
    new CollectionPanelSelectionState(
        EHero.Dooley,
        dooleyAila.SourceKey,
        CollectionSourceKind.Merchant
    ),
    openOnCurrentMerchant,
    "Opening during a run should select the current concrete hero and merchant source."
);
var openInRunIgnoresRememberedHero = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: EHero.Dooley,
    currentEncounterTemplateId: dooleyAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedHero: EHero.Mak
);
AssertEqual(
    new CollectionPanelSelectionState(
        EHero.Dooley,
        dooleyAila.SourceKey,
        CollectionSourceKind.Merchant
    ),
    openInRunIgnoresRememberedHero,
    "Opening during a run should keep the current run hero and source over a remembered hero."
);
var pygTrainer = entries.Single(entry =>
    entry.Kind == CollectionSourceKind.Trainer
    && string.Equals(entry.Name, "Professor Riggs", StringComparison.Ordinal)
);
var openOnCurrentTrainer = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: EHero.Pygmalien,
    currentEncounterTemplateId: pygTrainer.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(
        EHero.Pygmalien,
        pygTrainer.SourceKey,
        CollectionSourceKind.Trainer
    ),
    openOnCurrentTrainer,
    "Opening during a run at a trainer should select the trainer source and Skill tab."
);
var globalNufu = entries.First(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Nufu", StringComparison.Ordinal)
    && PrimaryRule(entry).HeroMode == CollectionSourceHeroMode.SelectedHero
);
var openOnChoiceMerchant = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: EHero.Vanessa,
    currentEncounterTemplateId: null,
    choiceSelectionTemplateIds: new[] { globalMerchantId, pygTrainerId },
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(
        EHero.Vanessa,
        globalNufu.SourceKey,
        CollectionSourceKind.Merchant
    ),
    openOnChoiceMerchant,
    "Opening on the choice screen should select the first matching source in SelectionSet order."
);
var openOnChoiceTrainer = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: EHero.Pygmalien,
    currentEncounterTemplateId: null,
    choiceSelectionTemplateIds: new[] { pygTrainerId, globalMerchantId },
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(
        EHero.Pygmalien,
        pygTrainer.SourceKey,
        CollectionSourceKind.Trainer
    ),
    openOnChoiceTrainer,
    "Opening on a trainer choice should select the trainer source when it is the first visible match."
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
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        CollectionSourceKind.Merchant
    ),
    openWithoutMerchant,
    "Opening during a run without a usable source should keep the run hero and fall back to Jay Jay."
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
    "Opening during a run without a concrete hero should fall back to VAN + Jay Jay."
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
    noHeroCacheKey.Contains(vanessaAila.OfferRuleFingerprint, StringComparison.Ordinal),
    "Source offer cache key should include the v4 source rule fingerprint."
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
var commonCacheKey = CollectionSourceOfferPoolCacheKey.Build(vanessaAila, EHero.Common);
AssertFalse(
    string.Equals(noHeroCacheKey, commonCacheKey, StringComparison.Ordinal),
    "Source offer cache key should distinguish Common from no selected hero."
);
AssertFalse(
    string.Equals(heroCacheKey, commonCacheKey, StringComparison.Ordinal),
    "Source offer cache key should distinguish Common from concrete heroes."
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
var visibleForCommon = CollectionSourceCatalog
    .VisibleEntries(entries, CollectionSourceKind.Merchant, EHero.Common)
    .Select(entry => entry.Name)
    .ToArray();
AssertValues(
    visibleForCommon,
    new[] { "Nufu", "Nufu" },
    "Common should behave like a selected hero and show only global merchant sources."
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
    new[] { selectedHeroCards[0].Id },
    "SelectedHero rules should match only the selected concrete hero, not Common cards."
);
var selectedCommonResult = CollectionSourceOfferPoolResolver.Resolve(
    selectedHeroRule,
    EHero.Common,
    selectedHeroCards
);
AssertSet(
    selectedCommonResult.OfferedCardIds,
    new[] { selectedHeroCards[1].Id },
    "SelectedHero rules should match only Common cards when Common is selected."
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
var selectedHeroTrainerRule = BuildSingleEntry(
    "Selected Hero Trainer",
    CollectionSourceKind.Trainer,
    """{ "heroMode": "SelectedHero" }"""
);
var selectedHeroTrainerCards = new[]
{
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000001"),
        ECardType.Skill,
        [EHero.Vanessa]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000002"),
        ECardType.Skill,
        [EHero.Vanessa, EHero.Dooley]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000003"),
        ECardType.Skill,
        [EHero.Common]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000004"),
        ECardType.Skill,
        Array.Empty<EHero>()
    ),
};
var selectedHeroTrainerPool = CollectionSourceOfferPoolResolver.Resolve(
    selectedHeroTrainerRule,
    EHero.Vanessa,
    selectedHeroTrainerCards
);
AssertSet(
    selectedHeroTrainerPool.OfferedCardIds,
    new[] { selectedHeroTrainerCards[0].Id, selectedHeroTrainerCards[1].Id },
    "Source pools may include shared selected-hero skills; final Skill filtering owns exclusivity."
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
var fixedHeroTrainerEntry = BuildSingleEntry(
    "Fixed Vanessa Trainer",
    CollectionSourceKind.Trainer,
    """{ "heroMode": "FixedHero", "hero": "Vanessa" }"""
);
var fixedHeroTrainerCards = new[]
{
    CatalogCard(
        Guid.Parse("bbbbbbbb-1000-0000-0000-000000000001"),
        ECardType.Skill,
        [EHero.Vanessa]
    ),
    CatalogCard(
        Guid.Parse("bbbbbbbb-1000-0000-0000-000000000002"),
        ECardType.Skill,
        [EHero.Vanessa, EHero.Dooley]
    ),
    CatalogCard(
        Guid.Parse("bbbbbbbb-1000-0000-0000-000000000003"),
        ECardType.Skill,
        [EHero.Dooley]
    ),
};
var fixedHeroTrainerResult = CollectionSourceOfferPoolResolver.Resolve(
    fixedHeroTrainerEntry,
    EHero.Dooley,
    fixedHeroTrainerCards
);
AssertSet(
    fixedHeroTrainerResult.OfferedCardIds,
    new[] { fixedHeroTrainerCards[0].Id },
    "FixedHero trainer rules should teach only skills exclusive to the fixed hero."
);

var otherHeroesEntry = BuildSingleEntry(
    "Other Heroes",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "OtherHeroes" }"""
);
var otherHeroCards = new[]
{
    CatalogCard(Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"), ECardType.Item, [EHero.Common]),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000002"),
        ECardType.Item,
        [EHero.Vanessa]
    ),
    CatalogCard(Guid.Parse("eeeeeeee-0000-0000-0000-000000000003"), ECardType.Item, [EHero.Dooley]),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000004"),
        ECardType.Item,
        [EHero.Dooley, EHero.Stelle]
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000005"),
        ECardType.Item,
        [EHero.Vanessa, EHero.Dooley]
    ),
};
var otherHeroesForVanessa = CollectionSourceOfferPoolResolver.Resolve(
    otherHeroesEntry,
    EHero.Vanessa,
    otherHeroCards
);
AssertSet(
    otherHeroesForVanessa.OfferedCardIds,
    new[] { otherHeroCards[2].Id, otherHeroCards[3].Id },
    "OtherHeroes rules should exclude Common cards and cards that include the selected UI hero."
);
var otherHeroesWithoutSelectedHero = CollectionSourceOfferPoolResolver.Resolve(
    otherHeroesEntry,
    selectedHero: null,
    otherHeroCards
);
AssertSet(
    otherHeroesWithoutSelectedHero.OfferedCardIds,
    new[]
    {
        otherHeroCards[1].Id,
        otherHeroCards[2].Id,
        otherHeroCards[3].Id,
        otherHeroCards[4].Id,
    },
    "OtherHeroes rules should include all non-Common hero cards when no UI hero is selected."
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

var segmentedEntry = BuildEntryWithSegments(
    "Segmented",
    CollectionSourceKind.Merchant,
    """
    [
      {
        "key": "normal-tool",
        "kind": "Normal",
        "rule": { "heroMode": "SelectedHero", "tagsAny": ["Tool"] }
      },
      {
        "key": "rare-turbo",
        "kind": "Enchanted",
        "rarityLabel": "Rare",
        "rule": { "heroMode": "SelectedHero", "enchantmentTypesAny": ["Turbo"] }
      }
    ]
    """
);
var segmentedNormal = CatalogCard(
    Guid.Parse("11111111-aaaa-0000-0000-000000000001"),
    ECardType.Item,
    [EHero.Vanessa],
    tags: [ECardTag.Tool]
);
var segmentedEnchanted = CatalogCard(
    Guid.Parse("11111111-aaaa-0000-0000-000000000002"),
    ECardType.Item,
    [EHero.Vanessa],
    enchantments: Enchantments(EEnchantmentType.Turbo)
);
var segmentedBoth = CatalogCard(
    Guid.Parse("11111111-aaaa-0000-0000-000000000003"),
    ECardType.Item,
    [EHero.Vanessa],
    tags: [ECardTag.Tool],
    enchantments: Enchantments(EEnchantmentType.Turbo)
);
var segmentedExcluded = CatalogCard(
    Guid.Parse("11111111-aaaa-0000-0000-000000000004"),
    ECardType.Item,
    [EHero.Dooley],
    tags: [ECardTag.Tool],
    enchantments: Enchantments(EEnchantmentType.Turbo)
);
var segmentedResult = CollectionSourceOfferPoolResolver.Resolve(
    segmentedEntry,
    EHero.Vanessa,
    new[] { segmentedNormal, segmentedEnchanted, segmentedBoth, segmentedExcluded }
);
AssertSet(
    segmentedResult.OfferedCardIds,
    new[] { segmentedNormal.Id, segmentedEnchanted.Id, segmentedBoth.Id },
    "Segments should OR together while each segment keeps its own AND predicates."
);
AssertEqual(
    CollectionSourceOfferSegmentKind.Normal,
    segmentedResult.OfferMatchesByCardId[segmentedNormal.Id][0].SegmentKind,
    "Normal-only matches should retain the normal segment attribution."
);
AssertEqual(
    EEnchantmentType.Turbo,
    segmentedResult.OfferMatchesByCardId[segmentedEnchanted.Id][0].EnchantmentType,
    "Enchanted matches should retain the matched enchantment type."
);
AssertEqual(
    2,
    segmentedResult.OfferMatchesByCardId[segmentedBoth.Id].Count,
    "A card that matches both normal and enchanted segments should keep both match reasons."
);

var hiddenGroupEntry = BuildSingleEntry(
    "Hidden Group",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "AllHeroes", "hiddenTagGroupsAny": ["Crit"] }"""
);
var hiddenGroupCard = CatalogCard(
    Guid.Parse("11111111-bbbb-0000-0000-000000000001"),
    ECardType.Item,
    [EHero.Dooley],
    hiddenTags: [EHiddenTag.CritReference]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(hiddenGroupEntry, EHero.Vanessa, new[] { hiddenGroupCard })
        .OfferedCardIds,
    new[] { hiddenGroupCard.Id },
    "hiddenTagGroupsAny should expand base/reference hidden tags during catalog parsing."
);

var enchantmentFacetEntry = BuildEntryWithSegments(
    "Enchantment Facets",
    CollectionSourceKind.Merchant,
    """
    [
      {
        "key": "rare-tagged",
        "kind": "Enchanted",
        "rarityLabel": "Rare",
        "rule": {
          "heroMode": "SelectedHero",
          "enchantmentTagsAny": ["Weapon"],
          "enchantmentHiddenTagsAny": ["Haste"]
        }
      }
    ]
    """
);
var enchantmentFacetMatch = CatalogCard(
    Guid.Parse("11111111-cccc-0000-0000-000000000001"),
    ECardType.Item,
    [EHero.Vanessa],
    enchantments: EnchantmentWithFacets(
        EEnchantmentType.Turbo,
        new[] { ECardTag.Weapon },
        new[] { EHiddenTag.Haste }
    )
);
var enchantmentFacetMiss = CatalogCard(
    Guid.Parse("11111111-cccc-0000-0000-000000000002"),
    ECardType.Item,
    [EHero.Vanessa],
    enchantments: EnchantmentWithFacets(
        EEnchantmentType.Turbo,
        new[] { ECardTag.Weapon },
        new[] { EHiddenTag.Slow }
    )
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            enchantmentFacetEntry,
            EHero.Vanessa,
            new[] { enchantmentFacetMatch, enchantmentFacetMiss }
        )
        .OfferedCardIds,
    new[] { enchantmentFacetMatch.Id },
    "enchantmentTagsAny and enchantmentHiddenTagsAny should match the same TEnchantment facets."
);

var specialistEnchantments = new Dictionary<string, EEnchantmentType>
{
    ["Knightshade"] = EEnchantmentType.Toxic,
    ["Hef"] = EEnchantmentType.Fiery,
    ["Chronos"] = EEnchantmentType.Turbo,
    ["Freiya"] = EEnchantmentType.Icy,
    ["Cobweb"] = EEnchantmentType.Heavy,
};
foreach (var pair in specialistEnchantments)
{
    var specialist = currentCatalog.Single(entry =>
        entry.Kind == CollectionSourceKind.Merchant
        && string.Equals(entry.Name, pair.Key, StringComparison.Ordinal)
    );
    AssertEqual(
        2,
        specialist.OfferSegments.Count,
        $"{pair.Key} should expose normal and rare enchanted source segments."
    );
    var enchantedSegment = specialist.OfferSegments.Single(segment =>
        segment.Kind == CollectionSourceOfferSegmentKind.Enchanted
    );
    AssertValues(
        enchantedSegment.Rule.EnchantmentTypesAny.ToArray(),
        new[] { pair.Value },
        $"{pair.Key} rare segment should use the expected enchantment type."
    );

    var candidate = CatalogCard(
        GuidFromIndex(pair.Key, 99),
        ECardType.Item,
        [EHero.Vanessa],
        enchantments: Enchantments(pair.Value)
    );
    var specialistResult = CollectionSourceOfferPoolResolver.Resolve(
        specialist,
        EHero.Vanessa,
        new[] { candidate }
    );
    AssertSet(
        specialistResult.OfferedCardIds,
        new[] { candidate.Id },
        $"{pair.Key} rare segment should match by TCardItem.Enchantments keys without base hidden tags."
    );
    AssertEqual(
        pair.Value,
        specialistResult.OfferMatchesByCardId[candidate.Id][0].EnchantmentType,
        $"{pair.Key} rare match should carry the matched enchantment type."
    );
}

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
    return BuildEntryWithSegments(
        name,
        kind,
        $$"""[{ "key": "normal", "kind": "Normal", "rule": {{offerRuleJson}} }]"""
    );
}

static CollectionSourceEntry BuildEntryWithSegments(
    string name,
    CollectionSourceKind kind,
    string offerSegmentsJson
)
{
    var id = GuidFromIndex(name, 7);
    return CollectionSourceCatalog
        .Build(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["fixture"],
              "entries": [
                {
                  "name": "{{name}}",
                  "kind": "{{kind}}",
                  "group": "fixture",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "{{name}} fixture",
                  "portraitTemplateId": "{{id}}",
                  "sourceTemplateIds": ["{{id}}"],
                  "offerSegments": {{offerSegmentsJson}}
                }
              ]
            }
            """
        )
        .Single();
}

static EHero? RepresentativeSelectedHero(CollectionSourceEntry entry)
{
    if (PrimaryRule(entry).HeroMode != CollectionSourceHeroMode.SelectedHero)
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
    var rule = PrimaryRule(entry);
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
        Enchantments = rule.EnchantableOnly
            ? Enchantments(EEnchantmentType.Toxic)
            : new Dictionary<EEnchantmentType, CollectionCardEnchantmentFacets>(),
    };
}

static CollectionSourceOfferRule PrimaryRule(CollectionSourceEntry entry) =>
    entry.OfferSegments[0].Rule;

static CollectionCardVm CatalogCard(
    Guid id,
    ECardType type,
    IEnumerable<EHero> heroes,
    IEnumerable<ECardTag>? tags = null,
    IEnumerable<EHiddenTag>? hiddenTags = null,
    ECardSize size = ECardSize.Medium,
    ETier tier = ETier.Bronze,
    bool isEnchantable = false,
    IReadOnlyDictionary<EEnchantmentType, CollectionCardEnchantmentFacets>? enchantments = null
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
        IsEnchantable = isEnchantable || (enchantments != null && enchantments.Count > 0),
        Enchantments =
            enchantments
            ?? (
                isEnchantable
                    ? Enchantments(EEnchantmentType.Toxic)
                    : new Dictionary<EEnchantmentType, CollectionCardEnchantmentFacets>()
            ),
    };

static IReadOnlyDictionary<EEnchantmentType, CollectionCardEnchantmentFacets> Enchantments(
    params EEnchantmentType[] types
)
{
    var result = new Dictionary<EEnchantmentType, CollectionCardEnchantmentFacets>();
    foreach (var type in types)
        result[type] = new CollectionCardEnchantmentFacets(
            type,
            Array.Empty<ECardTag>(),
            Array.Empty<EHiddenTag>()
        );
    return result;
}

static IReadOnlyDictionary<EEnchantmentType, CollectionCardEnchantmentFacets> EnchantmentWithFacets(
    EEnchantmentType type,
    IReadOnlyCollection<ECardTag> tags,
    IReadOnlyCollection<EHiddenTag> hiddenTags
) =>
    new Dictionary<EEnchantmentType, CollectionCardEnchantmentFacets>
    {
        [type] = new(type, tags, hiddenTags),
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

static int[] FirstGroupLayerCounts(IReadOnlyList<CollectionSourceRosterItem> roster, int layerCount)
{
    var result = new List<int>(layerCount);
    string? currentGroup = null;
    var count = 0;
    foreach (var item in roster)
    {
        if (currentGroup == null)
        {
            currentGroup = item.Entry.Group;
            count = 1;
            continue;
        }

        if (string.Equals(currentGroup, item.Entry.Group, StringComparison.Ordinal))
        {
            count++;
            continue;
        }

        result.Add(count);
        if (result.Count == layerCount)
            break;
        currentGroup = item.Entry.Group;
        count = 1;
    }

    if (result.Count < layerCount && currentGroup != null)
        result.Add(count);

    return result.ToArray();
}

static void AssertNondecreasing(IReadOnlyList<int> actual, string message)
{
    for (var i = 1; i < actual.Count; i++)
    {
        if (actual[i] < actual[i - 1])
            throw new InvalidOperationException(
                $"{message} Value at {i} ({actual[i]}) is less than value at {i - 1} ({actual[i - 1]})."
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
