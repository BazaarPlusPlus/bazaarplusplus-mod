using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.ItemEnchantPreview.Preview;

var labelSourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Game/ItemEnchantPreview/EnchantPreview.SettingsMenuLabel.cs"
    )
);
Assert(
    File.Exists(labelSourcePath),
    $"Enchant preview label source not found at {labelSourcePath}"
);
var labelSource = File.ReadAllText(labelSourcePath);
Assert(
    labelSource.Contains("Enchant Preview Always Show", StringComparison.Ordinal),
    "Enchant preview settings label should expose the English label."
);
Assert(
    labelSource.Contains("附魔预览始终显示", StringComparison.Ordinal),
    "Enchant preview settings label should expose the Simplified Chinese label."
);

var settingsPatchSourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Patches/Tooltips/EnchantPreviewSettingsPatch.cs"
    )
);
Assert(
    File.Exists(settingsPatchSourcePath),
    $"Enchant preview settings patch not found at {settingsPatchSourcePath}"
);
var settingsPatchSource = File.ReadAllText(settingsPatchSourcePath);
Assert(
    settingsPatchSource.Contains("EnchantPreviewAlwaysShowConfig", StringComparison.Ordinal),
    "Enchant preview settings patch should bind the AlwaysShow config entry."
);
Assert(
    settingsPatchSource.Contains("BPP_EnchantPreviewToggle", StringComparison.Ordinal),
    "Enchant preview settings patch should install a dedicated gameplay toggle."
);

var combatSettingsPatchSourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Patches/Combat/CombatStatusBarSettingsPatch.cs"
    )
);
var combatSettingsPatchSource = File.ReadAllText(combatSettingsPatchSourcePath);
Assert(
    combatSettingsPatchSource.Contains(
        "BppGameplaySettingsCoordinator.EnsureAll(__instance);",
        StringComparison.Ordinal
    ),
    "Gameplay settings refresh should install the enchant preview toggle alongside the other Bazaar++ toggles."
);

var hotkeyServiceSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Game/Input/BppHotkeyService.cs")
);
Assert(
    File.Exists(hotkeyServiceSourcePath),
    $"BPP hotkey service source not found at {hotkeyServiceSourcePath}"
);
var hotkeyServiceSource = File.ReadAllText(hotkeyServiceSourcePath);
Assert(
    hotkeyServiceSource.Contains("EnchantPreview", StringComparison.Ordinal)
        && hotkeyServiceSource.Contains("UpgradePreview", StringComparison.Ordinal),
    "BPP hotkey service should define dedicated enchant and upgrade preview actions."
);

var keybindSettingsPatchSourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Patches/Settings/BppKeybindSettingsPatch.cs"
    )
);
Assert(
    File.Exists(keybindSettingsPatchSourcePath),
    $"BPP keybind settings patch source not found at {keybindSettingsPatchSourcePath}"
);
var keybindSettingsPatchSource = File.ReadAllText(keybindSettingsPatchSourcePath);
Assert(
    keybindSettingsPatchSource.Contains("Show Enchant Preview", StringComparison.Ordinal)
        && keybindSettingsPatchSource.Contains("Show Upgrade Preview", StringComparison.Ordinal),
    "BPP keybind settings patch should inject dedicated settings rows for enchant and upgrade previews."
);
var enchantIndex = keybindSettingsPatchSource.IndexOf(
    "BPP_Keybind_EnchantPreview",
    StringComparison.Ordinal
);
var upgradeIndex = keybindSettingsPatchSource.IndexOf(
    "BPP_Keybind_UpgradePreview",
    StringComparison.Ordinal
);
Assert(
    enchantIndex >= 0 && upgradeIndex > enchantIndex,
    "BPP keybind settings patch should keep enchant preview before upgrade preview in the fixed row order."
);
Assert(
    keybindSettingsPatchSource.Contains("ArrangeRows(", StringComparison.Ordinal),
    "BPP keybind settings patch should explicitly reorder keybind rows after ensuring they exist."
);
var keybindLabelResolverSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Game/Input/BppKeybindLabelResolver.cs")
);
var keybindLabelResolverSource = File.ReadAllText(keybindLabelResolverSourcePath);
Assert(
    keybindLabelResolverSource.Contains("zh-Hans", StringComparison.Ordinal)
        || keybindLabelResolverSource.Contains(
            "SimplifiedChineseLanguage.Matches",
            StringComparison.Ordinal
        ),
    "BPP keybind label resolver should recognize zh-Hans as Simplified Chinese, directly or through the shared helper."
);

var tooltipRefreshSourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Game/Tooltips/TooltipModifierRefreshController.cs"
    )
);
var tooltipRefreshSource = File.ReadAllText(tooltipRefreshSourcePath);
Assert(
    tooltipRefreshSource.Contains("BppHotkeyService", StringComparison.Ordinal),
    "Tooltip refresh flow should query BPP hotkey service instead of hard-coded modifiers."
);

var upgradePatchSourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Patches/Tooltips/UpgradePreviewTooltipPatch.cs"
    )
);
var upgradePatchSource = File.ReadAllText(upgradePatchSourcePath);
Assert(
    upgradePatchSource.Contains("BppHotkeyService", StringComparison.Ordinal),
    "Upgrade preview tooltip patch should query BPP hotkey service instead of hard-coded Shift."
);

var enchantPatchSourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Patches/Tooltips/ItemEnchantPreviewPatch.cs"
    )
);
var enchantPatchSource = File.ReadAllText(enchantPatchSourcePath);
Assert(
    enchantPatchSource.Contains("BppHotkeyService", StringComparison.Ordinal),
    "Enchant preview tooltip patch should query BPP hotkey service instead of hard-coded Ctrl."
);

var nativeKeybindLabelPatchSourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Patches/Settings/NativeKeybindLabelPatch.cs"
    )
);
Assert(
    File.Exists(nativeKeybindLabelPatchSourcePath),
    $"Native keybind label patch source not found at {nativeKeybindLabelPatchSourcePath}"
);
var nativeKeybindLabelPatchSource = File.ReadAllText(nativeKeybindLabelPatchSourcePath);
Assert(
    nativeKeybindLabelPatchSource.Contains("Show Monster Preview", StringComparison.Ordinal),
    "Native keybind label patch should expose the English monster preview label."
);
Assert(
    nativeKeybindLabelPatchSource.Contains("展示野怪预览", StringComparison.Ordinal),
    "Native keybind label patch should expose the Simplified Chinese monster preview label."
);
Assert(
    nativeKeybindLabelPatchSource.Contains("Lock", StringComparison.Ordinal),
    "Native keybind label patch should target the native Lock keybind action."
);
Assert(
    nativeKeybindLabelPatchSource.Contains("zh-Hans", StringComparison.Ordinal)
        || nativeKeybindLabelPatchSource.Contains(
            "SimplifiedChineseLanguage.Matches",
            StringComparison.Ordinal
        ),
    "Native keybind label patch should recognize zh-Hans as Simplified Chinese, directly or through the shared helper."
);

var candidates = ItemEnchantPreviewCandidateSelector.SelectCandidates(
    currentEnchantment: EEnchantmentType.Heavy,
    allEnchantments: [EEnchantmentType.Heavy, EEnchantmentType.Icy, EEnchantmentType.Turbo]
);

Assert(
    candidates.SequenceEqual([EEnchantmentType.Icy, EEnchantmentType.Turbo]),
    "Candidate selection should dedupe inputs and exclude the current enchantment."
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

var segment = ItemEnchantPreviewFormatting.CreateSegment(
    EEnchantmentType.Icy,
    "Freeze for 2 seconds"
);

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
