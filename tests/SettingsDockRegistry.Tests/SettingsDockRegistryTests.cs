using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Game.CardArtReplacement;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.Screenshots.Upload;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;
using BepInEx.Configuration;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class SettingsDockRegistryTests
{
    private sealed class FakeEntry : ISettingsDockEntry
    {
        public string Key { get; }
        public int Order { get; init; }
        public int BuildCount { get; private set; }

        public FakeEntry(string key)
        {
            Key = key;
        }

        public BppSettingsDockDefinition Build(IBppConfig config)
        {
            BuildCount++;
            return new BppSettingsDockDefinition(
                Key,
                resolveLabel: _ => $"Label-{Key}",
                resolveStatus: _ => "ON",
                isActive: () => true,
                activate: () => { },
                collapseAfterActivate: false
            );
        }
    }

    private sealed class TestLanguageProvider : ILanguageProvider
    {
        public string CurrentLanguageCode => "en";
    }

    private sealed class TestLocaleModeProvider : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }

    [Fact]
    public void MaterializeAll_returns_one_definition_per_registered_entry()
    {
        var registry = new SettingsDockEntryRegistry();
        var a = new FakeEntry("A");
        var b = new FakeEntry("B");

        registry.Register(a);
        registry.Register(b);

        var defs = registry.MaterializeAll(config: null!);

        Assert.Equal(2, defs.Count);
        Assert.Contains(defs, d => d.Key == "A");
        Assert.Contains(defs, d => d.Key == "B");
        Assert.Equal(1, a.BuildCount);
        Assert.Equal(1, b.BuildCount);
    }

    [Fact]
    public void Register_null_throws()
    {
        var registry = new SettingsDockEntryRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void MaterializeWithOrder_returns_entries_paired_with_their_Order()
    {
        var registry = new SettingsDockEntryRegistry();
        registry.Register(new FakeEntry("A") { Order = 5 });
        registry.Register(new FakeEntry("B") { Order = 1 });

        var pairs = registry.MaterializeWithOrder(config: null!);

        Assert.Equal(2, pairs.Count);
        // Materialization preserves registration order; sorting is the caller's job.
        Assert.Equal(5, pairs[0].Order);
        Assert.Equal("A", pairs[0].Definition.Key);
        Assert.Equal(1, pairs[1].Order);
        Assert.Equal("B", pairs[1].Definition.Key);
    }

    [Fact]
    public void PackageCardArtReplacementDockEntry_uses_named_order_and_toggles_config()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-package-art-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var entry = new PackageCardArtReplacementSettingsDockEntry();

            var definition = entry.Build(config);

            Assert.Equal(BppSettingsDockOrder.PackageCardArtReplacement, entry.Order);
            Assert.Equal("PackageCardArtReplacement", definition.Key);
            Assert.Equal("Package Swap", definition.ResolveLabel("en"));
            Assert.Equal("掉包快递", definition.ResolveLabel("zh-CN"));
            Assert.False(definition.IsActive());
            Assert.Equal("OFF", definition.ResolveStatus("en"));

            definition.Activate();

            Assert.True(config.EnablePackageCardArtReplacementConfig!.Value);
            Assert.True(definition.IsActive());
            Assert.Equal("ON", definition.ResolveStatus("en"));
            Assert.False(definition.CollapseAfterActivate);

            configFile.Save();

            var reloadedConfigFile = new ConfigFile(configPath, saveOnInit: false);
            var reloadedConfig = new BppConfig();
            reloadedConfig.Initialize(reloadedConfigFile);

            Assert.True(PackageCardArtReplacementPolicy.IsEnabled(reloadedConfig));
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Theory]
    [InlineData("zh-CN", "https://bazaarplusplus.com/tutorial")]
    [InlineData("zh-Hant", "https://bazaarplusplus.com/tutorial")]
    [InlineData("en", "https://bazaarplusplus.com/tutorial?lang=en")]
    [InlineData("de-DE", "https://bazaarplusplus.com/tutorial?lang=en")]
    [InlineData("", "https://bazaarplusplus.com/tutorial?lang=en")]
    public void HotkeyTutorialLinks_resolves_tutorial_url_by_language(
        string languageCode,
        string expected
    )
    {
        var result = HotkeyTutorialLinks.ResolveTutorialUrl(languageCode);

        Assert.Equal(expected, result);
        Assert.DoesNotContain("lang=en?lang=en", result);
    }

    [Fact]
    public void HotkeyTutorialDockEntry_builds_action_definition()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
        var entry = new HotkeyTutorialSettingsDockEntry();

        var definition = entry.Build(config: null!);

        Assert.Equal(BppSettingsDockOrder.HotkeyTutorial, entry.Order);
        Assert.Equal("HotkeyTutorial", definition.Key);
        Assert.Equal("快捷键教程", definition.ResolveLabel("zh-CN"));
        Assert.Equal("Hotkey Tutorial", definition.ResolveLabel("en"));
        Assert.Equal("打开", definition.ResolveStatus("zh-CN"));
        Assert.Equal("OPEN", definition.ResolveStatus("en"));
        Assert.True(definition.IsActive());
        Assert.True(definition.CollapseAfterActivate);
    }

    [Fact]
    public void SettingsDockEntries_use_named_order_constants()
    {
        Assert.Equal(BppSettingsDockOrder.GameHistory, new HistoryPanelSettingsDockEntry().Order);
        Assert.Equal(BppSettingsDockOrder.NameOverride, new NameOverrideSettingsDockEntry().Order);
        Assert.Equal(
            BppSettingsDockOrder.LegendaryPosition,
            new LegendaryPositionSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.EnchantPreview,
            new ItemEnchantPreviewSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.PackageCardArtReplacement,
            new PackageCardArtReplacementSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.CombatStatusBar,
            new CombatStatusBarSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.ChineseLocaleMode,
            new ChineseLocaleModeSettingsDockEntry(new InMemoryBppEventBus()).Order
        );
        Assert.Equal(
            BppSettingsDockOrder.HotkeyTutorial,
            new HotkeyTutorialSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.BazaarDbUpload,
            new BazaarDbSnapshotUploadSettingsDockEntry().Order
        );
    }

    [Fact]
    public void SettingsDockCatalog_sorts_tutorial_between_chinese_locale_and_bazaar_db()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
        var registry = new SettingsDockEntryRegistry();
        registry.Register(new BazaarDbSnapshotUploadSettingsDockEntry());
        registry.Register(new HotkeyTutorialSettingsDockEntry());
        registry.Register(new ChineseLocaleModeSettingsDockEntry(new InMemoryBppEventBus()));

        try
        {
            BppSettingsDockCatalog.Install(new BppConfig(), registry);

            Assert.Equal(
                new[] { "ChineseLocaleMode", "HotkeyTutorial", "BazaarDbUpload" },
                BppSettingsDockCatalog.Definitions.Select(d => d.Key)
            );
        }
        finally
        {
            BppSettingsDockCatalog.Reset();
        }
    }

    [Theory]
    [InlineData(0, "zh-CN", "按键显示")]
    [InlineData(1, "zh-CN", "智能切换")]
    [InlineData(2, "zh-CN", "常驻显示")]
    [InlineData(0, "en", "OFF")]
    [InlineData(1, "en", "AUTO")]
    [InlineData(2, "en", "ON")]
    public void ResolvePreviewVisibilityModeStatus_returns_localized_dock_status(
        int modeValue,
        string languageCode,
        string expected
    )
    {
        var mode = (PreviewVisibilityMode)modeValue;
        var result = BppSettingsDockCatalog.ResolvePreviewVisibilityModeStatus(mode, languageCode);

        Assert.Equal(expected, result);
    }
}
