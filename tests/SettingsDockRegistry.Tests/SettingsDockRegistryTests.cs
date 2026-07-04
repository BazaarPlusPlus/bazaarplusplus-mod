using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Screenshots.Upload;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Game.VoiceSubtitles;
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
        private readonly BppChineseLocaleMode _mode;

        public TestLocaleModeProvider(BppChineseLocaleMode mode = BppChineseLocaleMode.Mainland)
        {
            _mode = mode;
        }

        public BppChineseLocaleMode CurrentMode => _mode;
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
    public void ChineseLocaleModeConfig_migrates_legacy_hong_kong_to_taiwan()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-chinese-locale-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            File.WriteAllText(
                configPath,
                """
                [Localization]

                ChineseLocaleMode = HongKong
                """
            );
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();

            config.Initialize(configFile);

            Assert.Equal(BppChineseLocaleMode.Taiwan, config.ChineseLocaleModeConfig!.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void ChineseLocaleModeDockEntry_cycles_between_cn_and_tw_only()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-chinese-locale-dock-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var definition = new ChineseLocaleModeSettingsDockEntry(
                new InMemoryBppEventBus()
            ).Build(config);

            Assert.Equal("CN", definition.ResolveStatus("en"));
            Assert.False(definition.IsActive());

            definition.Activate();

            Assert.Equal(BppChineseLocaleMode.Taiwan, config.ChineseLocaleModeConfig!.Value);
            Assert.Equal("TW", definition.ResolveStatus("en"));
            Assert.True(definition.IsActive());

            definition.Activate();

            Assert.Equal(BppChineseLocaleMode.Mainland, config.ChineseLocaleModeConfig!.Value);
            Assert.Equal("CN", definition.ResolveStatus("en"));
            Assert.False(definition.IsActive());

            config.ChineseLocaleModeConfig.Value = (BppChineseLocaleMode)2;

            Assert.Equal("TW", definition.ResolveStatus("en"));
            Assert.True(definition.IsActive());

            definition.Activate();

            Assert.Equal(BppChineseLocaleMode.Mainland, config.ChineseLocaleModeConfig.Value);
            Assert.Equal("CN", definition.ResolveStatus("en"));
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void EndOfRunScreenshotDockEntry_defaults_on_and_toggles_config()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-end-of-run-screenshot-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var entry = new EndOfRunScreenshotSettingsDockEntry();

            var definition = entry.Build(config);

            Assert.Equal(BppSettingsDockOrder.EndOfRunScreenshot, entry.Order);
            Assert.Equal("EndOfRunScreenshot", definition.Key);
            Assert.Equal("End-of-run Screenshot", definition.ResolveLabel("en"));
            Assert.Equal("终局截图", definition.ResolveLabel("zh-CN"));
            Assert.True(definition.IsActive());
            Assert.Equal("ON", definition.ResolveStatus("en"));

            definition.Activate();

            Assert.False(config.EndOfRunScreenshotEnabledConfig!.Value);
            Assert.False(definition.IsActive());
            Assert.Equal("OFF", definition.ResolveStatus("en"));
            Assert.False(definition.CollapseAfterActivate);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Theory]
    [InlineData("BazaarDbUpload")]
    [InlineData("StreamMode")]
    public void EnablingUploadOrStreamMode_forcesEndOfRunScreenshotOn(string dependencyKey)
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-screenshot-dependency-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            config.EndOfRunScreenshotEnabledConfig!.Value = false;

            var screenshotDefinition = new EndOfRunScreenshotSettingsDockEntry().Build(config);
            var dependencyDefinition =
                dependencyKey == "BazaarDbUpload"
                    ? new BazaarDbSnapshotUploadSettingsDockEntry().Build(config)
                    : new FixedSupporterListSettingsDockEntry().Build(config);

            Assert.False(screenshotDefinition.IsActive());
            Assert.Equal("OFF", screenshotDefinition.ResolveStatus("en"));

            dependencyDefinition.Activate();

            Assert.True(config.EndOfRunScreenshotEnabledConfig.Value);
            Assert.True(screenshotDefinition.IsActive());
            Assert.Equal("ON", screenshotDefinition.ResolveStatus("en"));

            dependencyDefinition.Activate();

            Assert.True(config.EndOfRunScreenshotEnabledConfig.Value);
            Assert.True(screenshotDefinition.IsActive());
            Assert.Equal("ON", screenshotDefinition.ResolveStatus("en"));
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Theory]
    [InlineData("BazaarDbUpload")]
    [InlineData("StreamMode")]
    public void EndOfRunScreenshotDockEntry_doesNotTurnOffWhileUploadOrStreamModeIsOn(
        string dependencyKey
    )
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-screenshot-forced-on-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            config.EndOfRunScreenshotEnabledConfig!.Value = true;

            if (dependencyKey == "BazaarDbUpload")
                config.BazaarDbUploadEnabled!.Value = true;
            else
                config.UseFixedSupporterListConfig!.Value = true;

            var screenshotDefinition = new EndOfRunScreenshotSettingsDockEntry().Build(config);

            Assert.True(screenshotDefinition.IsActive());
            Assert.Equal("ON", screenshotDefinition.ResolveStatus("en"));

            screenshotDefinition.Activate();

            Assert.True(config.EndOfRunScreenshotEnabledConfig.Value);
            Assert.True(screenshotDefinition.IsActive());
            Assert.Equal("ON", screenshotDefinition.ResolveStatus("en"));
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void FixedSupporterListDockEntry_uses_stream_mode_key_and_toggles_config()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-fixed-supporters-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var entry = new FixedSupporterListSettingsDockEntry();

            var definition = entry.Build(config);

            Assert.Equal(BppSettingsDockOrder.FixedSupporterList, entry.Order);
            Assert.Equal("StreamMode", definition.Key);
            Assert.Equal("Stream Mode", definition.ResolveLabel("en"));
            Assert.Equal("直播模式", definition.ResolveLabel("zh-CN"));
            Assert.False(definition.IsActive());
            Assert.Equal("OFF", definition.ResolveStatus("en"));

            definition.Activate();

            Assert.True(config.UseFixedSupporterListConfig!.Value);
            Assert.True(definition.IsActive());
            Assert.Equal("ON", definition.ResolveStatus("en"));
            Assert.False(definition.CollapseAfterActivate);

            configFile.Save();

            var reloadedConfigFile = new ConfigFile(configPath, saveOnInit: false);
            var reloadedConfig = new BppConfig();
            reloadedConfig.Initialize(reloadedConfigFile);

            Assert.True(reloadedConfig.UseFixedSupporterListConfig!.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void VoiceSubtitlesDockEntry_defaults_off_and_toggles_config()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-voice-subtitles-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var entry = new VoiceSubtitlesSettingsDockEntry();

            var definition = entry.Build(config);

            Assert.Equal(BppSettingsDockOrder.VoiceSubtitles, entry.Order);
            Assert.Equal("VoiceSubtitles", definition.Key);
            Assert.Equal("Voice Subtitles", definition.ResolveLabel("en"));
            Assert.Equal("语音字幕", definition.ResolveLabel("zh-CN"));
            Assert.False(config.EnableVoiceSubtitlesConfig!.Value);
            Assert.False(definition.IsActive());
            Assert.Equal("OFF", definition.ResolveStatus("en"));

            definition.Activate();

            Assert.True(config.EnableVoiceSubtitlesConfig.Value);
            Assert.True(definition.IsActive());
            Assert.Equal("ON", definition.ResolveStatus("en"));
            Assert.False(definition.CollapseAfterActivate);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void VoiceSubtitlesDockEntries_register_master_and_setting_rows()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-voice-subtitles-dock-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var registry = new SettingsDockEntryRegistry();

            VoiceSubtitlesSettingsDockEntry.RegisterAll(registry);

            var definitions = registry.MaterializeWithOrder(config);

            Assert.Equal(
                new[]
                {
                    "VoiceSubtitles",
                    "VoiceSubtitlesPosition",
                    "VoiceSubtitlesLanguage",
                    "VoiceSubtitlesEnglishFontScale",
                    "VoiceSubtitlesChineseFontScale",
                },
                definitions.Select(d => d.Definition.Key)
            );
            Assert.Equal(
                new[]
                {
                    BppSettingsDockOrder.VoiceSubtitles,
                    BppSettingsDockOrder.VoiceSubtitlesPosition,
                    BppSettingsDockOrder.VoiceSubtitlesLanguage,
                    BppSettingsDockOrder.VoiceSubtitlesEnglishFontScale,
                    BppSettingsDockOrder.VoiceSubtitlesChineseFontScale,
                },
                definitions.Select(d => d.Order)
            );
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void VoiceSubtitlesDockEntry_defaults_chinese_scale_to_one_and_cycles_to_next_ladder_value()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-voice-subtitles-scale-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var definition = new VoiceSubtitlesChineseFontScaleSettingsDockEntry().Build(config);

            Assert.Equal("Chinese Size", definition.ResolveLabel("en"));
            Assert.Equal("中文字号", definition.ResolveLabel("zh-CN"));

            L.Install(
                new TestLanguageProvider(),
                new TestLocaleModeProvider(BppChineseLocaleMode.Taiwan)
            );
            Assert.Equal("中文字號", definition.ResolveLabel("zh-Hant"));

            Assert.Equal(1.0f, config.VoiceSubtitlesChineseFontScaleConfig!.Value, precision: 2);
            Assert.Equal("1x", definition.ResolveStatus("en"));
            Assert.False(definition.IsActive());

            definition.Activate();

            Assert.Equal("1.25x", definition.ResolveStatus("en"));
            Assert.True(definition.IsActive());
            Assert.Equal(1.25f, config.VoiceSubtitlesChineseFontScaleConfig.Value, precision: 2);
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
            BppSettingsDockOrder.CombatStatusBar,
            new CombatStatusBarSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.ChineseLocaleMode,
            new ChineseLocaleModeSettingsDockEntry(new InMemoryBppEventBus()).Order
        );
        Assert.Equal(
            BppSettingsDockOrder.EndOfRunScreenshot,
            new EndOfRunScreenshotSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.HotkeyTutorial,
            new HotkeyTutorialSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.FixedSupporterList,
            new FixedSupporterListSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.VoiceSubtitles,
            new VoiceSubtitlesSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.VoiceSubtitlesPosition,
            new VoiceSubtitlesPositionSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.VoiceSubtitlesLanguage,
            new VoiceSubtitlesLanguageSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.VoiceSubtitlesEnglishFontScale,
            new VoiceSubtitlesEnglishFontScaleSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.VoiceSubtitlesChineseFontScale,
            new VoiceSubtitlesChineseFontScaleSettingsDockEntry().Order
        );
        Assert.Equal(
            BppSettingsDockOrder.BazaarDbUpload,
            new BazaarDbSnapshotUploadSettingsDockEntry().Order
        );
    }

    [Fact]
    public void SettingsDockCatalog_sorts_stream_mode_immediately_above_hotkey_tutorial()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
        var registry = new SettingsDockEntryRegistry();
        registry.Register(new BazaarDbSnapshotUploadSettingsDockEntry());
        registry.Register(new HotkeyTutorialSettingsDockEntry());
        registry.Register(new FixedSupporterListSettingsDockEntry());
        VoiceSubtitlesSettingsDockEntry.RegisterAll(registry);
        registry.Register(new EndOfRunScreenshotSettingsDockEntry());
        registry.Register(new ChineseLocaleModeSettingsDockEntry(new InMemoryBppEventBus()));

        try
        {
            BppSettingsDockCatalog.Install(new BppConfig(), registry);

            Assert.Equal(
                new[]
                {
                    "ChineseLocaleMode",
                    "StreamMode",
                    "VoiceSubtitles",
                    "VoiceSubtitlesPosition",
                    "VoiceSubtitlesLanguage",
                    "VoiceSubtitlesEnglishFontScale",
                    "VoiceSubtitlesChineseFontScale",
                    "HotkeyTutorial",
                    "EndOfRunScreenshot",
                    "BazaarDbUpload",
                },
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
