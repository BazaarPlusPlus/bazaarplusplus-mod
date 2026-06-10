using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
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
    Array.Empty<EHero>(),
    "Toggling the only selected hero should clear the hero selection."
);
AssertEqual(
    null,
    heroState.ToSelectionState().SelectedHero,
    "An empty hero selection should round-trip as no selected hero."
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
    "BPP.CollectionPanel.SelectedHero.anonymous",
    CollectionPanelHeroPreference.BuildPrefsKey(null),
    "CollectionPanel hero preference should use an anonymous scope when no account scope is available."
);
AssertEqual(
    "BPP.CollectionPanel.SelectedHero.account%2Fone",
    CollectionPanelHeroPreference.BuildPrefsKey("account/one"),
    "CollectionPanel hero preference key should URI-escape account scopes."
);
AssertEqual(
    "Dooley",
    CollectionPanelHeroPreference.Serialize(EHero.Dooley),
    "CollectionPanel hero preference should serialize enum names."
);
AssertTrue(
    CollectionPanelHeroPreference.TryParse("Dooley", out var parsedDooley)
        && parsedDooley == EHero.Dooley,
    "CollectionPanel hero preference should parse a supported concrete hero."
);
AssertTrue(
    CollectionPanelHeroPreference.TryParse("Common", out var parsedCommon)
        && parsedCommon == EHero.Common,
    "CollectionPanel hero preference should preserve Common as a real panel hero."
);
AssertFalse(
    CollectionPanelHeroPreference.TryParse("NotARealHero", out _),
    "CollectionPanel hero preference should reject unknown hero strings."
);
AssertFalse(
    CollectionPanelHeroPreference.TryParse("", out _),
    "CollectionPanel hero preference should reject empty values."
);
AssertEqual(
    DayTierSchedule.OutOfRunDay,
    new CollectionFilterState().SelectedRunDay,
    "New filter state should start with the day filter selected."
);
var selectionState = new CollectionFilterState();
selectionState.SelectedSourceKey = "trainer:old";
selectionState.ApplySelection(defaultSelection);
AssertValues(
    selectionState.Heroes.ToArray(),
    new[] { EHero.Vanessa },
    "Applying the default selection should select Vanessa in the filter state."
);
AssertEqual(
    "merchant:jay-jay:global",
    selectionState.SelectedSourceKey,
    "Applying the default selection should select Jay Jay in the filter state."
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
    selectionState.SelectedSourceKey,
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
selectionState.SelectedSourceKey = "merchant:stale";
selectionState.ApplySelection(trainerSelection);
AssertEqual(
    ECardType.Skill,
    selectionState.ActiveType,
    "Applying a trainer runtime selection should route the panel to the Skill tab."
);
AssertEqual(
    "trainer:mr-tuskari:pygmalien",
    selectionState.SelectedSourceKey,
    "Applying a trainer runtime selection should store the trainer source key."
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
    selectionState.GetSelectedSourceKey(ECardType.Skill),
    "A single selected source key should not read back as a stale source for the inactive tab."
);

var sourceState = new CollectionFilterState();
sourceState.ToggleSource(ECardType.Item, "merchant:aila");
AssertEqual(
    "merchant:aila",
    sourceState.SelectedSourceKey,
    "Item source selection should store the merchant source key."
);
sourceState.ToggleSource(ECardType.Item, "merchant:helt");
AssertEqual(
    "merchant:helt",
    sourceState.SelectedSourceKey,
    "Selecting another item source should replace the prior merchant source."
);
sourceState.ToggleSource(ECardType.Item, "merchant:helt");
AssertEqual(
    null,
    sourceState.SelectedSourceKey,
    "Selecting the active item source again should clear it."
);
sourceState.ToggleSource(ECardType.Skill, "trainer:juliette");
AssertEqual(
    "trainer:juliette",
    sourceState.SelectedSourceKey,
    "Skill source selection should store the trainer source key."
);
AssertEqual(
    ECardType.Skill,
    sourceState.ActiveType,
    "Toggling a Skill source should set Skill active."
);
sourceState.PackagesOnly = true;
sourceState.ToggleSource(ECardType.Skill, "trainer:scout");
AssertFalse(
    sourceState.PackagesOnly,
    "Skill source selection should clear stale package-only mode."
);
sourceState.ActiveType = ECardType.Item;
sourceState.SelectedSourceKey = "merchant:hidden";
AssertTrue(
    sourceState.PruneSelectedSource(new[] { "merchant:visible" }),
    "Pruning should report a change when a selected source is no longer visible."
);
AssertEqual(
    null,
    sourceState.SelectedSourceKey,
    "Pruning should clear invisible merchant source selection."
);
sourceState.ActiveType = ECardType.Skill;
sourceState.SelectedSourceKey = "trainer:visible";
AssertFalse(
    sourceState.PruneSelectedSource(new[] { "trainer:visible" }),
    "Pruning should report no change when the selected source is still visible."
);
AssertEqual(
    "trainer:visible",
    sourceState.SelectedSourceKey,
    "Pruning should preserve visible trainer source selection."
);

var normal = Card("Normal", ETier.Bronze);
var package = Card("Starter Package", ETier.Silver, isPackage: true);
var bronzePackage = Card(
    "Bronze Package",
    ETier.Bronze,
    isPackage: true,
    heroes: new[] { EHero.Dooley },
    tags: new[] { ECardTag.Tool },
    hiddenTags: new[] { EHiddenTag.Package, EHiddenTag.Shield }
);

var defaultPackageResult = CollectionFilterEngine.Apply(
    new[] { package, normal },
    new CollectionFilterState()
);
AssertSequence(defaultPackageResult, new[] { normal.Id }, "Packages are excluded by default.");

var packagesOnlyFilter = new CollectionFilterState { PackagesOnly = true };
var packagesOnlyResult = CollectionFilterEngine.Apply(
    new[] { package, normal },
    packagesOnlyFilter
);
AssertSequence(
    packagesOnlyResult,
    new[] { package.Id },
    "PackagesOnly shows package cards as an exclusive package view."
);
var packagesOnlyWithSourceResult = CollectionFilterEngine.Apply(
    new[] { package, bronzePackage, normal },
    new CollectionFilterState { PackagesOnly = true },
    new CollectionFilterContext
    {
        OfferedCardIds = new[] { normal.Id },
        ApplyHeroFilter = true,
        SuppressDayGate = true,
    }
);
AssertSequence(
    packagesOnlyWithSourceResult,
    new[] { bronzePackage.Id, package.Id },
    "PackagesOnly ignores source offer pools and context gates."
);
var packagesOnlyWithFacets = new CollectionFilterState { PackagesOnly = true, SelectedRunDay = 1 };
packagesOnlyWithFacets.Heroes.Add(EHero.Vanessa);
packagesOnlyWithFacets.Tiers.Add(ETier.Bronze);
packagesOnlyWithFacets.Sizes.Add(ECardSize.Small);
packagesOnlyWithFacets.Tags.Add(ECardTag.Weapon);
packagesOnlyWithFacets.Keywords.Add(EHiddenTag.Damage);
packagesOnlyWithFacets.TagMatchMode = CollectionFacetMatchMode.All;
packagesOnlyWithFacets.KeywordMatchMode = CollectionFacetMatchMode.All;
AssertSequence(
    CollectionFilterEngine.Apply(new[] { package, bronzePackage, normal }, packagesOnlyWithFacets),
    new[] { bronzePackage.Id, package.Id },
    "PackagesOnly ignores hero, tier, size, tag, keyword, match mode, and day facets."
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

var offerPoolItem = Card("Offer Pool Item", ETier.Bronze);
var offerPoolExcluded = Card("Offer Pool Excluded", ETier.Bronze);

var offerPoolResult = CollectionFilterEngine.Apply(
    new[] { offerPoolExcluded, offerPoolItem, normal },
    new CollectionFilterState(),
    new CollectionFilterContext { OfferedCardIds = new[] { offerPoolItem.Id, Guid.NewGuid() } }
);
AssertSequence(
    offerPoolResult,
    new[] { offerPoolItem.Id },
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
    new[] { potionSkill.Id, weaponSkill.Id },
    "Item tag filters do not narrow the Skill tab."
);
var damageSkill = Card(
    "Damage Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    hiddenTags: new[] { EHiddenTag.Damage }
);
var shieldSkill = Card(
    "Shield Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    hiddenTags: new[] { EHiddenTag.Shield }
);
var damageShieldSkill = Card(
    "Damage Shield Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    hiddenTags: new[] { EHiddenTag.Damage, EHiddenTag.Shield }
);
var skillKeywordFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
skillKeywordFilter.Keywords.Add(EHiddenTag.Damage);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { shieldSkill, damageSkill, normal }, skillKeywordFilter),
    new[] { damageSkill.Id },
    "Skill keyword filters narrow skills by EHiddenTag."
);
skillKeywordFilter.Keywords.Add(EHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { shieldSkill, damageSkill, normal }, skillKeywordFilter),
    new[] { damageSkill.Id, shieldSkill.Id },
    "Multiple selected skill keywords OR together."
);
var allSkillKeywordFilter = new CollectionFilterState
{
    ActiveType = ECardType.Skill,
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allSkillKeywordFilter.Keywords.Add(EHiddenTag.Damage);
allSkillKeywordFilter.Keywords.Add(EHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldSkill, damageSkill, damageShieldSkill, normal },
        allSkillKeywordFilter
    ),
    new[] { damageShieldSkill.Id },
    "All keyword mode requires every selected skill keyword on the same card."
);

var weaponItem = Card("Weapon Item", ETier.Bronze, tags: new[] { ECardTag.Weapon });
var potionItem = Card("Potion Item", ETier.Bronze, tags: new[] { ECardTag.Potion });
var toolItem = Card("Tool Item", ETier.Bronze, tags: new[] { ECardTag.Tool });
var weaponPotionItem = Card(
    "Weapon Potion Item",
    ETier.Bronze,
    tags: new[] { ECardTag.Weapon, ECardTag.Potion }
);
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
var allItemTagFilter = new CollectionFilterState { TagMatchMode = CollectionFacetMatchMode.All };
allItemTagFilter.Tags.Add(ECardTag.Weapon);
allItemTagFilter.Tags.Add(ECardTag.Potion);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { potionItem, toolItem, weaponItem, weaponPotionItem },
        allItemTagFilter
    ),
    new[] { weaponPotionItem.Id },
    "All tag mode requires every selected item tag on the same card."
);

var damageItem = Card(
    "Damage Item",
    ETier.Bronze,
    tags: new[] { ECardTag.Weapon },
    hiddenTags: new[] { EHiddenTag.Damage }
);
var shieldItem = Card(
    "Shield Item",
    ETier.Bronze,
    tags: new[] { ECardTag.Weapon },
    hiddenTags: new[] { EHiddenTag.Shield }
);
var damageShieldItem = Card(
    "Damage Shield Item",
    ETier.Bronze,
    tags: new[] { ECardTag.Weapon },
    hiddenTags: new[] { EHiddenTag.Damage, EHiddenTag.Shield }
);
var damageToolItem = Card(
    "Damage Tool Item",
    ETier.Bronze,
    tags: new[] { ECardTag.Tool },
    hiddenTags: new[] { EHiddenTag.Damage }
);
var damageReferenceItem = Card(
    "Damage Reference Item",
    ETier.Bronze,
    hiddenTags: new[] { EHiddenTag.DamageReference }
);
var itemKeywordFilter = new CollectionFilterState();
itemKeywordFilter.Keywords.Add(EHiddenTag.Damage);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldItem, damageToolItem, damageItem },
        itemKeywordFilter
    ),
    new[] { damageItem.Id, damageToolItem.Id },
    "Item keyword filters narrow items by EHiddenTag."
);
itemKeywordFilter.Keywords.Add(EHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldItem, damageToolItem, damageItem },
        itemKeywordFilter
    ),
    new[] { damageItem.Id, damageToolItem.Id, shieldItem.Id },
    "Multiple selected item keywords OR together."
);
var allItemKeywordFilter = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allItemKeywordFilter.Keywords.Add(EHiddenTag.Damage);
allItemKeywordFilter.Keywords.Add(EHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldItem, damageToolItem, damageItem, damageShieldItem },
        allItemKeywordFilter
    ),
    new[] { damageShieldItem.Id },
    "All keyword mode requires every selected item keyword on the same card."
);
var itemKeywordAndReferenceFilter = new CollectionFilterState();
itemKeywordAndReferenceFilter.Keywords.Add(EHiddenTag.Damage);
itemKeywordAndReferenceFilter.Keywords.Add(EHiddenTag.DamageReference);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { damageReferenceItem, shieldItem, damageItem },
        itemKeywordAndReferenceFilter
    ),
    new[] { damageItem.Id, damageReferenceItem.Id },
    "Keyword and reference selections share the same OR keyword facet."
);
var damageAndReferenceItem = Card(
    "Damage Reference Combo Item",
    ETier.Bronze,
    hiddenTags: new[] { EHiddenTag.Damage, EHiddenTag.DamageReference }
);
var allItemKeywordAndReferenceFilter = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allItemKeywordAndReferenceFilter.Keywords.Add(EHiddenTag.Damage);
allItemKeywordAndReferenceFilter.Keywords.Add(EHiddenTag.DamageReference);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { damageReferenceItem, damageItem, damageAndReferenceItem },
        allItemKeywordAndReferenceFilter
    ),
    new[] { damageAndReferenceItem.Id },
    "All keyword mode also requires curated reference keywords on the same card."
);
var itemTagAndKeywordFilter = new CollectionFilterState();
itemTagAndKeywordFilter.Tags.Add(ECardTag.Weapon);
itemTagAndKeywordFilter.Keywords.Add(EHiddenTag.Damage);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldItem, damageToolItem, damageItem },
        itemTagAndKeywordFilter
    ),
    new[] { damageItem.Id },
    "Item tags and item keywords combine as separate AND facets."
);

var derivedLifestealTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Size = ECardSize.Medium,
    InternalName = "Derived Lifesteal Weapon",
    ArtKey = "Assets/Cards/DerivedLifestealWeapon.png",
    Tags = new HashSet<ECardTag> { ECardTag.Weapon },
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Lifesteal] = 100,
            },
        },
    },
};
var derivedLifestealVm = CollectionCardVm.From(derivedLifestealTemplate);
AssertTrue(
    derivedLifestealVm.HiddenTags.Contains(EHiddenTag.Lifesteal),
    "CollectionCardVm.From should derive Lifesteal from positive item attributes."
);
AssertFalse(
    derivedLifestealTemplate.HiddenTags.Contains(EHiddenTag.Lifesteal),
    "Derived Lifesteal projection should not mutate the source game template HiddenTags."
);
AssertValues(
    CollectionFacetAvailability
        .KeywordsFor(new[] { derivedLifestealVm }, ECardType.Item)
        .Select(tag => tag.ToString())
        .ToArray(),
    new[] { nameof(EHiddenTag.Lifesteal) },
    "Available item keywords should include Lifesteal derived from item attributes."
);
var noLifestealTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Size = ECardSize.Medium,
    InternalName = "No Lifesteal Weapon",
    ArtKey = "Assets/Cards/NoLifestealWeapon.png",
    Tags = new HashSet<ECardTag> { ECardTag.Weapon },
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Lifesteal] = 0,
            },
        },
    },
};
var noLifestealVm = CollectionCardVm.From(noLifestealTemplate);
var lifestealFilter = new CollectionFilterState();
lifestealFilter.Keywords.Add(EHiddenTag.Lifesteal);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { noLifestealVm, derivedLifestealVm }, lifestealFilter),
    new[] { derivedLifestealVm.Id },
    "Lifesteal keyword filtering should include derived Lifesteal VMs and exclude non-positive attributes."
);
var allLifestealFilter = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allLifestealFilter.Keywords.Add(EHiddenTag.Lifesteal);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { noLifestealVm, derivedLifestealVm }, allLifestealFilter),
    new[] { derivedLifestealVm.Id },
    "All keyword mode should still match a VM with derived Lifesteal when Lifesteal is the only selected keyword."
);

AssertEqual(
    CollectionTagWhitelist.Ordered.Count,
    CollectionTagWhitelist.Ordered.Distinct().Count(),
    "Tag whitelist entries must be distinct."
);
AssertValues(
    CollectionTagWhitelist.Ordered.Select(tag => tag.ToString()).ToArray(),
    new[]
    {
        nameof(ECardTag.Weapon),
        nameof(ECardTag.Friend),
        nameof(ECardTag.Aquatic),
        nameof(ECardTag.Tool),
        nameof(ECardTag.Drone),
        nameof(ECardTag.Vehicle),
        nameof(ECardTag.Food),
        nameof(ECardTag.Trap),
        nameof(ECardTag.Toy),
        nameof(ECardTag.Potion),
        nameof(ECardTag.Reagent),
        nameof(ECardTag.Relic),
        nameof(ECardTag.Dragon),
        nameof(ECardTag.Core),
        nameof(ECardTag.Tech),
        nameof(ECardTag.Dinosaur),
        nameof(ECardTag.Ray),
        nameof(ECardTag.Apparel),
        nameof(ECardTag.Merchant),
        nameof(ECardTag.Property),
        nameof(ECardTag.Loot),
    },
    "Tag whitelist should match the curated CollectionPanel tag order."
);
foreach (
    var mechanismTag in new[]
    {
        ECardTag.Unsellable,
        ECardTag.Unstashable,
        ECardTag.Event,
        ECardTag.Combat,
    }
)
    AssertFalse(
        CollectionTagWhitelist.Ordered.Contains(mechanismTag),
        $"Tag whitelist must exclude mechanism tag {mechanismTag}."
    );
foreach (
    var bazaarDbTag in new[] { ECardTag.Apparel, ECardTag.Merchant, ECardTag.Loot, ECardTag.Weapon }
)
    AssertTrue(
        CollectionTagWhitelist.Ordered.Contains(bazaarDbTag),
        $"Tag whitelist should include BazaarDB type/tag {bazaarDbTag}."
    );
foreach (
    var unusedTypeTag in new[]
    {
        ECardTag.Ingredient,
        ECardTag.Instrument,
        ECardTag.Key,
        ECardTag.Map,
        ECardTag.Sigil,
    }
)
    AssertFalse(
        CollectionTagWhitelist.Ordered.Contains(unusedTypeTag),
        $"Tag whitelist should exclude non-BazaarDB type/tag {unusedTypeTag}."
    );

AssertEqual(
    CollectionKeywordWhitelist.Ordered.Count,
    CollectionKeywordWhitelist.Ordered.Distinct().Count(),
    "Keyword whitelist entries must be distinct."
);
AssertValues(
    CollectionKeywordWhitelist.Ordered.Select(tag => tag.ToString()).ToArray(),
    new[]
    {
        nameof(EHiddenTag.Quest),
        nameof(EHiddenTag.Flying),
        nameof(EHiddenTag.Haste),
        nameof(EHiddenTag.Charge),
        nameof(EHiddenTag.Cooldown),
        nameof(EHiddenTag.Slow),
        nameof(EHiddenTag.Freeze),
        nameof(EHiddenTag.Damage),
        nameof(EHiddenTag.Shield),
        nameof(EHiddenTag.Heal),
        nameof(EHiddenTag.Health),
        nameof(EHiddenTag.Burn),
        nameof(EHiddenTag.Poison),
        nameof(EHiddenTag.Regen),
        nameof(EHiddenTag.Crit),
        nameof(EHiddenTag.Ammo),
        nameof(EHiddenTag.Lifesteal),
        nameof(EHiddenTag.Rage),
        nameof(EHiddenTag.Gold),
        nameof(EHiddenTag.Income),
        nameof(EHiddenTag.Value),
        nameof(EHiddenTag.QuestReference),
        nameof(EHiddenTag.FlyingReference),
        nameof(EHiddenTag.HasteReference),
        nameof(EHiddenTag.CooldownReference),
        nameof(EHiddenTag.SlowReference),
        nameof(EHiddenTag.FreezeReference),
        nameof(EHiddenTag.DamageReference),
        nameof(EHiddenTag.ShieldReference),
        nameof(EHiddenTag.HealReference),
        nameof(EHiddenTag.HealthReference),
        nameof(EHiddenTag.BurnReference),
        nameof(EHiddenTag.PoisonReference),
        nameof(EHiddenTag.RegenReference),
        nameof(EHiddenTag.CritReference),
        nameof(EHiddenTag.AmmoReference),
        nameof(EHiddenTag.RageReference),
        nameof(EHiddenTag.EconomyReference),
    },
    "Keyword whitelist should match the curated CollectionPanel keyword list."
);
foreach (var nonKeyword in new[] { EHiddenTag.Merchant, EHiddenTag.Package, EHiddenTag.Unsellable })
    AssertFalse(
        CollectionKeywordWhitelist.Ordered.Contains(nonKeyword),
        $"Keyword whitelist must exclude non-keyword tag {nonKeyword}."
    );
foreach (
    var referenceKeyword in new[]
    {
        EHiddenTag.ChilledReference,
        EHiddenTag.HeatedReference,
        EHiddenTag.JoyReference,
        EHiddenTag.PotionReference,
        EHiddenTag.TechReference,
        EHiddenTag.TempoReference,
    }
)
    AssertFalse(
        CollectionKeywordWhitelist.Ordered.Contains(referenceKeyword),
        $"Keyword whitelist should exclude non-curated reference tag {referenceKeyword}."
    );
foreach (
    var referenceKeyword in new[]
    {
        EHiddenTag.DamageReference,
        EHiddenTag.HealReference,
        EHiddenTag.AmmoReference,
        EHiddenTag.RageReference,
        EHiddenTag.EconomyReference,
    }
)
    AssertTrue(
        CollectionKeywordWhitelist.Ordered.Contains(referenceKeyword),
        $"Keyword whitelist should include curated reference tag {referenceKeyword}."
    );
foreach (
    var bazaarDbKeyword in new[]
    {
        EHiddenTag.Ammo,
        EHiddenTag.Crit,
        EHiddenTag.Health,
        EHiddenTag.Quest,
        EHiddenTag.Value,
    }
)
    AssertTrue(
        CollectionKeywordWhitelist.Ordered.Contains(bazaarDbKeyword),
        $"Keyword whitelist should include BazaarDB keyword {bazaarDbKeyword}."
    );
foreach (
    var nonBazaarDbKeyword in new[]
    {
        EHiddenTag.AbsorbDestroy,
        EHiddenTag.AbsorbFreeze,
        EHiddenTag.AbsorbSlow,
        EHiddenTag.CanCrit,
        EHiddenTag.Experience,
        EHiddenTag.Level,
        EHiddenTag.Multicast,
        EHiddenTag.Reload,
        EHiddenTag.Tempo,
        EHiddenTag.Ticket,
    }
)
    AssertFalse(
        CollectionKeywordWhitelist.Ordered.Contains(nonBazaarDbKeyword),
        $"Keyword whitelist should exclude non-BazaarDB keyword {nonBazaarDbKeyword}."
    );

var availableFacetCards = new[]
{
    Card(
        "Damage Weapon",
        ETier.Bronze,
        tags: new[] { ECardTag.Weapon, ECardTag.Ingredient },
        hiddenTags: new[] { EHiddenTag.Damage, EHiddenTag.DamageReference }
    ),
    Card(
        "Package Shield",
        ETier.Bronze,
        isPackage: true,
        tags: new[] { ECardTag.Tool },
        hiddenTags: new[] { EHiddenTag.Package, EHiddenTag.Shield }
    ),
    Card(
        "Skill Quest",
        ETier.Bronze,
        type: ECardType.Skill,
        hiddenTags: new[] { EHiddenTag.Quest }
    ),
};
AssertValues(
    CollectionFacetAvailability
        .TagsFor(availableFacetCards, ECardType.Item)
        .Select(tag => tag.ToString())
        .ToArray(),
    new[] { nameof(ECardTag.Weapon) },
    "Available item tags should include only non-package catalog tags that are in the BazaarDB-facing whitelist."
);
AssertValues(
    CollectionFacetAvailability
        .KeywordsFor(availableFacetCards, ECardType.Item)
        .Select(tag => tag.ToString())
        .ToArray(),
    new[] { nameof(EHiddenTag.Damage), nameof(EHiddenTag.DamageReference) },
    "Available item keywords should include non-package catalog keywords and curated references from the same facet."
);
AssertValues(
    CollectionFacetAvailability
        .KeywordsFor(availableFacetCards, ECardType.Skill)
        .Select(tag => tag.ToString())
        .ToArray(),
    new[] { nameof(EHiddenTag.Quest) },
    "Available skill keywords should be computed independently from item keywords."
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
var emptyHeroSkillFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
var emptyHeroSkillResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    emptyHeroSkillFilter
);
AssertSequence(
    emptyHeroSkillResult,
    new[]
    {
        commonSharedSkill.Id,
        commonSkill.Id,
        missingHeroSkill.Id,
        sharedHeroSkill.Id,
        vanessaExclusiveSkill.Id,
    },
    "Empty hero selection should show every skill card without applying hero scope."
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
var hiddenTagPackageTemplate = new TCardItem
{
    Type = ECardType.Item,
    ArtKey = "Assets/Cards/Bundle.png",
    InternalName = "Vanessa Starter Bundle",
    HiddenTags = new HashSet<EHiddenTag> { EHiddenTag.Package },
};
AssertTrue(
    CollectionCardClassifier.Classify(hiddenTagPackageTemplate).IsPackage,
    "HiddenTag.Package marks package templates for the exclusive package view."
);
var packageNameWithoutTagTemplate = new TCardItem
{
    Type = ECardType.Item,
    ArtKey = "Assets/Cards/Package.png",
    InternalName = "Vanessa Starter Package",
};
AssertFalse(
    CollectionCardClassifier.Classify(packageNameWithoutTagTemplate).IsPackage,
    "Package-like names no longer mark packages without HiddenTag.Package."
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
    IReadOnlyCollection<EHiddenTag>? hiddenTags = null,
    IReadOnlyCollection<EHero>? heroes = null
) =>
    new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Size = size,
        StartingTier = tier,
        Tags = tags ?? Array.Empty<ECardTag>(),
        HiddenTags = hiddenTags ?? Array.Empty<EHiddenTag>(),
        DisplayName = name,
        InternalName = name,
        IsPackage = isPackage,
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
