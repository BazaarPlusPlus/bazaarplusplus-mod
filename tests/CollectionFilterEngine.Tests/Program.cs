using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

var heroState = new CollectionFilterState();
heroState.ToggleHero(EHero.Vanessa);
AssertValues(
    heroState.Heroes.ToArray(),
    new[] { EHero.Vanessa },
    "Selecting a concrete hero should add that hero."
);
heroState.ToggleHero(EHero.Dooley);
AssertValues(
    heroState.Heroes.ToArray(),
    new[] { EHero.Dooley },
    "Selecting a second concrete hero should replace the first."
);
heroState.ToggleHero(EHero.Common);
AssertValues(
    heroState.Heroes.ToArray(),
    new[] { EHero.Common },
    "Selecting Common should replace the selected concrete hero."
);
AssertEqual(
    EHero.Common,
    heroState.ToSelectionState().SelectedHero,
    "Common should round-trip as the selected hero, not as no selected hero."
);
heroState.ToggleHero(EHero.Common);
AssertValues(
    heroState.Heroes.ToArray(),
    new[] { EHero.Common },
    "Toggling the only selected hero should keep that hero selected."
);
heroState.ToggleHero(EHero.Dooley);
AssertValues(
    heroState.Heroes.ToArray(),
    new[] { EHero.Dooley },
    "Selecting a concrete hero after Common should still select only that hero."
);
var ambiguousHeroState = new CollectionFilterState();
ambiguousHeroState.Heroes.Add(EHero.Common);
ambiguousHeroState.Heroes.Add(EHero.Vanessa);
AssertEqual(
    null,
    ambiguousHeroState.SelectedHero,
    "Ambiguous multi-hero state should not pick a HashSet-dependent selected hero."
);

var defaultSelection = CollectionPanelSelectionState.Default;
AssertEqual(
    EHero.Vanessa,
    defaultSelection.SelectedHero,
    "Default panel selection should start on VAN/Vanessa."
);
AssertEqual(
    "merchant:jay-jay:global",
    defaultSelection.SelectedSourceKey,
    "Default panel selection should start on Jay Jay."
);
AssertEqual(
    CollectionSourceKind.Merchant,
    defaultSelection.SelectedSourceKind,
    "Default panel selection should target the Item merchant source rail."
);
AssertEqual(
    DayTierSchedule.OutOfRunDay,
    new CollectionFilterState().SelectedRunDay,
    "New filter state should start with the day filter selected."
);
var selectionState = new CollectionFilterState();
selectionState.Merchants.Add(CollectionMerchantKind.Burn);
selectionState.SelectedTrainerSourceKey = "trainer:old";
selectionState.ApplySelection(defaultSelection);
AssertValues(
    selectionState.Heroes.ToArray(),
    new[] { EHero.Vanessa },
    "Applying the default selection should select Vanessa in the filter state."
);
AssertEqual(
    "merchant:jay-jay:global",
    selectionState.SelectedMerchantSourceKey,
    "Applying the default selection should select Jay Jay in the filter state."
);
AssertFalse(
    selectionState.Merchants.Contains(CollectionMerchantKind.Burn),
    "Applying a panel selection should clear stale merchant-kind filters."
);
AssertEqual(
    null,
    selectionState.SelectedTrainerSourceKey,
    "Applying a merchant selection should clear stale trainer source selection."
);
AssertEqual(
    defaultSelection,
    selectionState.ToSelectionState(),
    "Filter state should round-trip the selected hero and merchant through the selection interface."
);
var runtimeSelection = new CollectionPanelSelectionState(
    EHero.Dooley,
    "merchant:jules:diamond:dooley+karnok+mak+pygmalien+stelle+vanessa",
    CollectionSourceKind.Merchant
);
selectionState.ApplySelection(runtimeSelection);
AssertValues(
    selectionState.Heroes.ToArray(),
    new[] { EHero.Dooley },
    "Runtime selection should replace the previous selected hero."
);
AssertEqual(
    "merchant:jules:diamond:dooley+karnok+mak+pygmalien+stelle+vanessa",
    selectionState.SelectedMerchantSourceKey,
    "Runtime selection should replace the previous selected merchant."
);
AssertEqual(
    runtimeSelection,
    selectionState.ToSelectionState(),
    "Runtime selection should be readable back from the filter state."
);
var trainerSelection = new CollectionPanelSelectionState(
    EHero.Pygmalien,
    "trainer:mr-tuskari:pygmalien",
    CollectionSourceKind.Trainer
);
selectionState.SelectedMerchantSourceKey = "merchant:stale";
selectionState.ApplySelection(trainerSelection);
AssertEqual(
    ECardType.Skill,
    selectionState.ActiveType,
    "Applying a trainer runtime selection should route the panel to the Skill tab."
);
AssertEqual(
    "trainer:mr-tuskari:pygmalien",
    selectionState.SelectedTrainerSourceKey,
    "Applying a trainer runtime selection should store the trainer source key."
);
AssertEqual(
    null,
    selectionState.SelectedMerchantSourceKey,
    "Applying a trainer runtime selection should clear stale merchant source selection."
);
AssertEqual(
    trainerSelection,
    selectionState.ToSelectionState(),
    "Trainer source selection should round-trip through the selection interface."
);
selectionState.ApplySelection(runtimeSelection);
AssertEqual(
    ECardType.Item,
    selectionState.ActiveType,
    "Applying a merchant runtime selection should route the panel back to the Item tab."
);
AssertEqual(
    null,
    selectionState.SelectedTrainerSourceKey,
    "Applying a merchant runtime selection should clear stale trainer source selection."
);

var sourceState = new CollectionFilterState();
sourceState.ToggleSource(ECardType.Item, "merchant:aila");
AssertEqual(
    "merchant:aila",
    sourceState.SelectedMerchantSourceKey,
    "Item source selection should store the merchant source key."
);
sourceState.ToggleSource(ECardType.Item, "merchant:helt");
AssertEqual(
    "merchant:helt",
    sourceState.SelectedMerchantSourceKey,
    "Selecting another item source should replace the prior merchant source."
);
sourceState.ToggleSource(ECardType.Item, "merchant:helt");
AssertEqual(
    null,
    sourceState.SelectedMerchantSourceKey,
    "Selecting the active item source again should clear it."
);
sourceState.ToggleSource(ECardType.Skill, "trainer:juliette");
AssertEqual(
    "trainer:juliette",
    sourceState.SelectedTrainerSourceKey,
    "Skill source selection should store the trainer source key."
);
sourceState.SelectedMerchantSourceKey = "merchant:hidden";
sourceState.SelectedTrainerSourceKey = "trainer:visible";
AssertTrue(
    sourceState.PruneSelectedSources(new[] { "merchant:visible" }, new[] { "trainer:visible" }),
    "Pruning should report a change when a selected source is no longer visible."
);
AssertEqual(
    null,
    sourceState.SelectedMerchantSourceKey,
    "Pruning should clear invisible merchant source selection."
);
AssertEqual(
    "trainer:visible",
    sourceState.SelectedTrainerSourceKey,
    "Pruning should preserve visible trainer source selection."
);

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

var bronzeLarge = Card("A Bronze Large", ETier.Bronze, size: ECardSize.Large);
var bronzeSmall = Card("B Bronze Small", ETier.Bronze, size: ECardSize.Small);
var silverLarge = Card("C Silver Large", ETier.Silver, size: ECardSize.Large);
var silverSmall = Card("D Silver Small", ETier.Silver, size: ECardSize.Small);
var tierSizeResult = CollectionFilterEngine.Apply(
    new[] { silverSmall, bronzeLarge, silverLarge, bronzeSmall },
    new CollectionFilterState()
);
AssertSequence(
    tierSizeResult,
    new[] { bronzeSmall.Id, bronzeLarge.Id, silverSmall.Id, silverLarge.Id },
    "Visible cards sort by tier first, then by size within each tier."
);

var sizePriorityFilter = new CollectionFilterState { SortPriority = CollectionSortPriority.Size };
var sizePriorityResult = CollectionFilterEngine.Apply(
    new[] { silverSmall, bronzeLarge, silverLarge, bronzeSmall },
    sizePriorityFilter
);
AssertSequence(
    sizePriorityResult,
    new[] { bronzeSmall.Id, silverSmall.Id, bronzeLarge.Id, silverLarge.Id },
    "Size sort priority sorts by size first, then by tier within each size."
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

var offerPoolResult = CollectionFilterEngine.Apply(
    new[] { healMerchant, burnMerchant, normal },
    new CollectionFilterState(),
    new CollectionFilterContext { OfferedCardIds = new[] { burnMerchant.Id, Guid.NewGuid() } }
);
AssertSequence(
    offerPoolResult,
    new[] { burnMerchant.Id },
    "Resolved offer pool should AND with the normal visible card filters."
);

var vanessaBronze = Card("Vanessa Bronze", ETier.Bronze, heroes: new[] { EHero.Vanessa });
var dooleyBronze = Card("Dooley Bronze", ETier.Bronze, heroes: new[] { EHero.Dooley });
var vanessaSilver = Card("Vanessa Silver", ETier.Silver, heroes: new[] { EHero.Vanessa });
var sourceAndHeroFilter = new CollectionFilterState();
sourceAndHeroFilter.Heroes.Add(EHero.Vanessa);
sourceAndHeroFilter.Tiers.Add(ETier.Bronze);
var sourceAndHeroResult = CollectionFilterEngine.Apply(
    new[] { dooleyBronze, vanessaSilver, vanessaBronze },
    sourceAndHeroFilter,
    new CollectionFilterContext
    {
        OfferedCardIds = new[] { vanessaBronze.Id, vanessaSilver.Id, dooleyBronze.Id },
    }
);
AssertSequence(
    sourceAndHeroResult,
    new[] { vanessaBronze.Id },
    "Resolved offer pool should preserve AND semantics with hero and tier filters."
);

var sourceOwnedHeroFilter = new CollectionFilterState();
sourceOwnedHeroFilter.Heroes.Add(EHero.Vanessa);
var sourceOwnedHeroResult = CollectionFilterEngine.Apply(
    new[] { dooleyBronze, vanessaBronze },
    sourceOwnedHeroFilter,
    new CollectionFilterContext
    {
        OfferedCardIds = new[] { dooleyBronze.Id },
        ApplyHeroFilter = false,
    }
);
AssertSequence(
    sourceOwnedHeroResult,
    new[] { dooleyBronze.Id },
    "Selected source pools should not be cropped by the run-hero selector a second time."
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

var weaponItem = Card("Weapon Item", ETier.Bronze, tags: new[] { ECardTag.Weapon });
var potionItem = Card("Potion Item", ETier.Bronze, tags: new[] { ECardTag.Potion });
var toolItem = Card("Tool Item", ETier.Bronze, tags: new[] { ECardTag.Tool });
var itemTagFilter = new CollectionFilterState();
itemTagFilter.Tags.Add(ECardTag.Weapon);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, toolItem, weaponItem }, itemTagFilter),
    new[] { weaponItem.Id },
    "A single selected tag narrows items to cards carrying that tag."
);
itemTagFilter.Tags.Add(ECardTag.Potion);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, toolItem, weaponItem }, itemTagFilter),
    new[] { potionItem.Id, weaponItem.Id },
    "Multiple selected tags OR together, matching the other facet rows."
);

AssertEqual(
    CollectionTagWhitelist.Ordered.Count,
    CollectionTagWhitelist.Ordered.Distinct().Count(),
    "Tag whitelist entries must be distinct."
);
AssertTrue(
    CollectionTagWhitelist.PrimaryCount <= CollectionTagWhitelist.Ordered.Count,
    "Tag whitelist primary slice must fit inside the option list."
);
foreach (
    var mechanismTag in new[]
    {
        ECardTag.Unsellable,
        ECardTag.Unstashable,
        ECardTag.Merchant,
        ECardTag.Event,
        ECardTag.Combat,
        ECardTag.Loot,
    }
)
    AssertFalse(
        CollectionTagWhitelist.Ordered.Contains(mechanismTag),
        $"Tag whitelist must exclude mechanism tag {mechanismTag}."
    );

var vanessaExclusiveSkill = Card(
    "Vanessa Exclusive Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Vanessa }
);
var sharedHeroSkill = Card(
    "Shared Hero Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Vanessa, EHero.Dooley }
);
var commonSkill = Card(
    "Common Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Common }
);
var commonSharedSkill = Card(
    "Common Shared Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Common, EHero.Vanessa }
);
var missingHeroSkill = Card(
    "Missing Hero Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: Array.Empty<EHero>()
);
var exclusiveSkillFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
exclusiveSkillFilter.Heroes.Add(EHero.Vanessa);
var exclusiveSkillResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    exclusiveSkillFilter
);
AssertSequence(
    exclusiveSkillResult,
    new[] { sharedHeroSkill.Id, vanessaExclusiveSkill.Id },
    "Skill hero filtering shows hero-exclusive plus general-shared (multi-hero, non-Common) skills; Common-scoped multi-hero skills stay hidden."
);

var exclusiveSkillSourceResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    exclusiveSkillFilter,
    new CollectionFilterContext
    {
        OfferedCardIds = new[]
        {
            sharedHeroSkill.Id,
            commonSkill.Id,
            commonSharedSkill.Id,
            missingHeroSkill.Id,
            vanessaExclusiveSkill.Id,
        },
        ApplyHeroFilter = true,
    }
);
AssertSequence(
    exclusiveSkillSourceResult,
    new[] { sharedHeroSkill.Id, vanessaExclusiveSkill.Id },
    "Trainer/source pools AND with the widened skill hero scope, so shared skills surface in trainer pools too."
);

var commonSkillFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
commonSkillFilter.Heroes.Add(EHero.Common);
var commonSkillResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    commonSkillFilter
);
AssertSequence(
    commonSkillResult,
    new[] { commonSkill.Id },
    "Common skill filtering should only show skills explicitly scoped to Common/global — the general-shared bucket is empty by definition for Common."
);

AssertTrue(
    CollectionHeroScope.MatchesSkillHeroScope(new[] { EHero.Vanessa, EHero.Mak }, EHero.Vanessa),
    "A {Vanessa, Mak} shared skill matches the Vanessa scope."
);
AssertFalse(
    CollectionHeroScope.MatchesSkillHeroScope(new[] { EHero.Vanessa, EHero.Mak }, EHero.Dooley),
    "A {Vanessa, Mak} shared skill does not match an uninvolved hero."
);
AssertFalse(
    CollectionHeroScope.MatchesSkillHeroScope(new[] { EHero.Common, EHero.Vanessa }, EHero.Vanessa),
    "Common-scoped multi-hero skills are not general-shared."
);

AssertFalse(
    CollectionCardClassifier
        .Classify(ECardType.Item, "Assets/Cards/Debug.png", "[DEBUG] Item")
        .IsCatalogCard,
    "Debug-marked templates do not enter the catalog even when they have art."
);
AssertEqual(
    CollectionCardEligibilityReason.DebugTemplate,
    CollectionCardClassifier
        .Classify(ECardType.Item, "Assets/Cards/Debug.png", "[DEBUG] Item")
        .EligibilityReason,
    "Debug-marked templates should report a reasoned rejection."
);
AssertFalse(
    CollectionCardClassifier
        .Classify(ECardType.Item, "Assets/Cards/Template.png", "[TEMPLATE] Item")
        .IsCatalogCard,
    "Template-marked entries do not enter the catalog even when they have art."
);
AssertEqual(
    CollectionCardEligibilityReason.TemplateInternalName,
    CollectionCardClassifier
        .Classify(ECardType.Item, "Assets/Cards/Template.png", "[TEMPLATE] Item")
        .EligibilityReason,
    "Template-marked entries should report a reasoned rejection."
);
AssertTrue(
    CollectionCardClassifier
        .Classify(ECardType.Skill, "Assets/Cards/Skill.png", "Real Skill")
        .IsCatalogCard,
    "Normal Item/Skill cards with art are catalog cards."
);
AssertEqual(
    CollectionCardEligibilityReason.Accepted,
    CollectionCardClassifier
        .Classify(ECardType.Skill, "Assets/Cards/Skill.png", "Real Skill")
        .EligibilityReason,
    "Accepted catalog cards should report the accepted reason."
);
AssertFalse(
    CollectionCardClassifier
        .Classify(ECardType.Skill, "Icon_Skill_Placeholder.png", "Aggressive Mutations")
        .IsCatalogCard,
    "Placeholder skill art is not a catalog-ready skill."
);
AssertEqual(
    CollectionCardEligibilityReason.PlaceholderArtKey,
    CollectionCardClassifier
        .Classify(ECardType.Skill, "Icon_Skill_Placeholder.png", "Aggressive Mutations")
        .EligibilityReason,
    "Placeholder art should report a reasoned rejection."
);
AssertFalse(
    CollectionCardClassifier
        .Classify(ECardType.Skill, "Placeholder", "[SKILL TEMPLATE]")
        .IsCatalogCard,
    "Skill template placeholders do not enter the catalog."
);
AssertFalse(
    CollectionCardClassifier
        .Classify(ECardType.Item, "Assets/Cards/LegacyItem.mat", "Legacy Material Item")
        .IsCatalogCard,
    "Legacy material art keys do not enter the catalog."
);
AssertEqual(
    CollectionCardEligibilityReason.MaterialArtKey,
    CollectionCardClassifier
        .Classify(ECardType.Item, "Assets/Cards/LegacyItem.mat", "Legacy Material Item")
        .EligibilityReason,
    "Material art keys should report a reasoned rejection."
);
AssertFalse(
    CollectionCardClassifier
        .Classify(ECardType.Item, "Assets/Cards/Template.png", "[SMALL ITEM TEMPLATE]")
        .IsCatalogCard,
    "Bracketed item template names do not enter the catalog."
);
AssertEqual(
    CollectionCardEligibilityReason.InvalidArtKey,
    CollectionCardClassifier.Classify(ECardType.Item, "Invalid", "Real Item").EligibilityReason,
    "The literal Invalid art key should report a reasoned rejection."
);
AssertEqual(
    CollectionCardEligibilityReason.MissingArtKey,
    CollectionCardClassifier.Classify(ECardType.Item, "", "Real Item").EligibilityReason,
    "A missing art key should report a reasoned rejection."
);
AssertEqual(
    CollectionCardEligibilityReason.UnsupportedType,
    CollectionCardClassifier
        .Classify((ECardType)999, "Assets/Cards/Event.png", "Event")
        .EligibilityReason,
    "Unsupported card types should report a reasoned rejection."
);
AssertTrue(
    CollectionCardClassifier
        .Classify(ECardType.Item, "Assets/Cards/Package.png", "Vanessa Starter Package")
        .IsPackage,
    "Packages should stay in the catalog classification result and be hidden by filters."
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

// --- Day filter: DayTierSchedule maps a run day to a StartingTier ceiling. ---
AssertEqual(ETier.Bronze, DayTierSchedule.CeilingTier(1), "Day 1 ceiling is Bronze.");
AssertEqual(ETier.Silver, DayTierSchedule.CeilingTier(2), "Day 2 raises the ceiling to Silver.");
AssertEqual(ETier.Silver, DayTierSchedule.CeilingTier(5), "Day 5 is still Silver-capped.");
AssertEqual(ETier.Gold, DayTierSchedule.CeilingTier(6), "Day 6 raises the ceiling to Gold.");
AssertEqual(ETier.Gold, DayTierSchedule.CeilingTier(7), "Day 7 is still Gold-capped.");
AssertEqual(ETier.Diamond, DayTierSchedule.CeilingTier(8), "Day 8 raises the ceiling to Diamond.");
AssertEqual(
    ETier.Diamond,
    DayTierSchedule.CeilingTier(12),
    "Days beyond the table stay Diamond-capped."
);
AssertEqual(
    ETier.Diamond,
    DayTierSchedule.CeilingTier(DayTierSchedule.OutOfRunDay),
    "OutOfRunDay sits in the Diamond band, so the out-of-run filter narrows nothing."
);

// --- Day filter: SelectedRunDay keeps StartingTier <= ceiling(day); ANDs with other dimensions. ---
var dayBronze = Card("Day Bronze", ETier.Bronze);
var daySilver = Card("Day Silver", ETier.Silver);
var dayGold = Card("Day Gold", ETier.Gold);
var dayDiamond = Card("Day Diamond", ETier.Diamond);
var dayLegendary = Card("Day Legendary", ETier.Legendary);
var dayPool = new[] { dayBronze, daySilver, dayGold, dayDiamond, dayLegendary };

AssertSequence(
    CollectionFilterEngine.Apply(dayPool, new CollectionFilterState { SelectedRunDay = 1 }),
    new[] { dayBronze.Id },
    "Day 1 keeps only Bronze-start cards."
);
AssertSequence(
    CollectionFilterEngine.Apply(dayPool, new CollectionFilterState { SelectedRunDay = 2 }),
    new[] { dayBronze.Id, daySilver.Id },
    "Day 2 keeps Bronze and Silver, excludes Gold/Diamond."
);
AssertSequence(
    CollectionFilterEngine.Apply(dayPool, new CollectionFilterState { SelectedRunDay = 6 }),
    new[] { dayBronze.Id, daySilver.Id, dayGold.Id },
    "Day 6 unlocks Gold."
);
AssertSequence(
    CollectionFilterEngine.Apply(dayPool, new CollectionFilterState { SelectedRunDay = 8 }),
    new[] { dayBronze.Id, daySilver.Id, dayGold.Id, dayDiamond.Id, dayLegendary.Id },
    "Day 8 unlocks Diamond and shows Legendary-start cards alongside Diamond."
);
AssertSequence(
    CollectionFilterEngine.Apply(dayPool, new CollectionFilterState { SelectedRunDay = null }),
    new[] { dayBronze.Id, daySilver.Id, dayGold.Id, dayDiamond.Id, dayLegendary.Id },
    "Null SelectedRunDay disables day filtering."
);

var dayAndTierFilter = new CollectionFilterState { SelectedRunDay = 6 };
dayAndTierFilter.Tiers.Add(ETier.Diamond);
AssertSequence(
    CollectionFilterEngine.Apply(dayPool, dayAndTierFilter),
    Array.Empty<Guid>(),
    "Manual Tier=Diamond ANDs with Day 6 (ceiling Gold) to nothing — independent dimensions, neither rewrites the other."
);

AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        new CollectionFilterState { SelectedRunDay = 8 },
        new CollectionFilterContext { OfferedCardIds = new[] { dayGold.Id, dayLegendary.Id } }
    ),
    new[] { dayGold.Id, dayLegendary.Id },
    "Day predicate ANDs with the resolved offer pool."
);

// --- Day filter: fixed-tier sources (offer rule pins a starting tier) ignore the day gate. ---
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        new CollectionFilterState { SelectedRunDay = 1 },
        new CollectionFilterContext
        {
            OfferedCardIds = new[] { dayGold.Id, dayDiamond.Id },
            SuppressDayGate = true,
        }
    ),
    new[] { dayGold.Id, dayDiamond.Id },
    "A fixed-tier source's pool is exempt from the day ceiling (Luxe on Day 1 still deals Diamond)."
);
var suppressedDayManualTier = new CollectionFilterState { SelectedRunDay = 1 };
suppressedDayManualTier.Tiers.Add(ETier.Gold);
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        suppressedDayManualTier,
        new CollectionFilterContext
        {
            OfferedCardIds = new[] { dayGold.Id, dayDiamond.Id },
            SuppressDayGate = true,
        }
    ),
    new[] { dayGold.Id },
    "Suppressing the day gate leaves the manual Tier row in force."
);

Console.WriteLine("CollectionFilterEngine checks passed.");

static CollectionCardVm Card(
    string name,
    ETier tier,
    ECardType type = ECardType.Item,
    ECardSize size = ECardSize.Medium,
    bool isPackage = false,
    IReadOnlyCollection<ECardTag>? tags = null,
    IReadOnlyCollection<CollectionMerchantKind>? merchants = null,
    IReadOnlyCollection<EHero>? heroes = null
) =>
    new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Size = size,
        StartingTier = tier,
        Tags = tags ?? Array.Empty<ECardTag>(),
        DisplayName = name,
        InternalName = name,
        IsPackage = isPackage,
        Merchants = merchants ?? Array.Empty<CollectionMerchantKind>(),
        Heroes = heroes ?? Array.Empty<EHero>(),
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
