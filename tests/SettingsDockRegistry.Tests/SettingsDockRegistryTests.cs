using System.Reflection;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Screenshots.Upload;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Storage.Paths;
using BepInEx.Configuration;
using BepInEx.Logging;
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
        private readonly string _languageCode;

        public TestLanguageProvider(string languageCode = "en")
        {
            _languageCode = languageCode;
        }

        public string CurrentLanguageCode => _languageCode;
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

    private sealed class ContractTestServices : IBppServices
    {
        public ContractTestServices(IBppConfig config)
        {
            Config = config;
        }

        public IBppConfig Config { get; }
        public IBppEventBus EventBus => null!;
        public IPathProvider Paths => null!;
        public IRunContext RunContext => null!;
        public IGameStateProbe GameStateProbe => null!;
        public IEncounterStateProbe EncounterState => null!;
        public IRunSnapshotProbe RunSnapshot => null!;
        public IGameBuildInfo GameBuild => null!;
        public ManualLogSource Logger => null!;
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
    public void CyclingEntry_wraps_from_last_ladder_value_to_first()
    {
        var value = 2;
        var entry = CreateIntegerCyclingEntry(() => value, next => value = next);
        var definition = entry.Build(new BppConfig());

        definition.Activate();

        Assert.Equal(1, value);
    }

    [Fact]
    public void CyclingEntry_falls_back_to_first_ladder_value_when_current_is_unknown()
    {
        var value = 99;
        var entry = CreateIntegerCyclingEntry(() => value, next => value = next);
        var definition = entry.Build(new BppConfig());

        definition.Activate();

        Assert.Equal(1, value);
    }

    [Fact]
    public void CyclingEntry_prefers_nextOverride_over_ladder_lookup()
    {
        var value = 1;
        var entry = new CyclingSettingsDockEntry<int>(
            order: 0,
            key: "Test",
            resolveLabel: _ => "Test",
            ladder: new[] { 1, 2 },
            read: _ => value,
            write: (_, next) => value = next,
            highlightWhen: _ => false,
            resolveStatus: (current, _) => current.ToString(),
            nextOverride: _ => 42
        );
        var definition = entry.Build(new BppConfig());

        definition.Activate();

        Assert.Equal(42, value);
    }

    [Fact]
    public void CyclingEntry_invokes_onChanged_after_write_with_the_new_value()
    {
        var value = 1;
        var observed = new List<(string Phase, int Value)>();
        var entry = new CyclingSettingsDockEntry<int>(
            order: 0,
            key: "Test",
            resolveLabel: _ => "Test",
            ladder: new[] { 1, 2 },
            read: _ => value,
            write: (_, next) =>
            {
                value = next;
                observed.Add(("write", value));
            },
            highlightWhen: _ => false,
            resolveStatus: (current, _) => current.ToString(),
            onChanged: next => observed.Add(("changed", next))
        );
        var definition = entry.Build(new BppConfig());

        definition.Activate();

        Assert.Equal(new[] { ("write", 2), ("changed", 2) }, observed);
    }

    [Fact]
    public void CyclingEntry_status_and_highlight_read_the_current_value()
    {
        var value = 1;
        var entry = CreateIntegerCyclingEntry(() => value, next => value = next);
        var definition = entry.Build(new BppConfig());

        Assert.Equal("1", definition.ResolveStatus("en"));
        Assert.False(definition.IsActive());

        value = 2;

        Assert.Equal("2", definition.ResolveStatus("en"));
        Assert.True(definition.IsActive());
    }

    [Fact]
    public void CyclingEntry_Toggle_supplies_boolean_ladder_highlight_and_status()
    {
        var enabled = false;
        var entry = CyclingSettingsDockEntry<bool>.Toggle(
            order: 0,
            key: "Test",
            resolveLabel: _ => "Test",
            read: _ => enabled,
            write: (_, next) => enabled = next
        );
        var definition = entry.Build(new BppConfig());

        Assert.False(definition.IsActive());
        Assert.Equal("OFF", definition.ResolveStatus("zh-CN"));

        definition.Activate();

        Assert.True(enabled);
        Assert.True(definition.IsActive());
        Assert.Equal("ON", definition.ResolveStatus("en"));
    }

    private static CyclingSettingsDockEntry<int> CreateIntegerCyclingEntry(
        Func<int> read,
        Action<int> write
    ) =>
        new(
            order: 0,
            key: "Test",
            resolveLabel: _ => "Test",
            ladder: new[] { 1, 2 },
            read: _ => read(),
            write: (_, next) => write(next),
            highlightWhen: current => current != 1,
            resolveStatus: (current, _) => current.ToString()
        );

    [Fact]
    public void NameOverrideDockEntry_requests_refresh_after_every_activation()
    {
        var refreshCount = 0;
        var definition = NameOverrideSettingsDockEntry
            .Create(() => refreshCount++)
            .Build(new BppConfig());

        definition.Activate();
        definition.Activate();

        Assert.Equal(2, refreshCount);
    }

    [Fact]
    public void EventPreviewDockEntry_uses_true_when_config_is_unavailable()
    {
        var definition = EventPreviewSettingsDockEntry.Create().Build(new BppConfig());

        Assert.True(definition.IsActive());
        Assert.Equal("ON", definition.ResolveStatus("en"));
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
            var eventBus = new InMemoryBppEventBus();
            var changedCount = 0;
            using var subscription = eventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
                changedCount++
            );
            var definition = ChineseLocaleModeSettingsDockEntry.Create(eventBus).Build(config);

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
            Assert.Equal(3, changedCount);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void UiFontDockEntry_defaults_to_lxgw_wenkai_and_is_inactive()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-ui-font-dock-default-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var entry = UiFontSettingsDockEntry.Create();

            var definition = entry.Build(config);

            Assert.Equal(BppSettingsDockOrder.UiFont, entry.Order);
            Assert.Equal("UiFont", definition.Key);
            Assert.Equal(BppConfig.DefaultUiFontKind, config.UiFontKindConfig!.Value);
            Assert.Equal("UI Font", definition.ResolveLabel("en"));
            Assert.Equal("界面字体", definition.ResolveLabel("zh-CN"));
            Assert.Equal("KAI", definition.ResolveStatus("en"));
            Assert.Equal("楷体", definition.ResolveStatus("zh-CN"));
            Assert.False(definition.IsActive());
            Assert.False(definition.CollapseAfterActivate);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void UiFontDockEntry_cycles_between_lxgw_wenkai_and_sans_serif()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-ui-font-dock-cycle-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var definition = UiFontSettingsDockEntry.Create().Build(config);

            definition.Activate();

            Assert.Equal(BppUiFontKind.SansSerif, config.UiFontKindConfig!.Value);
            Assert.Equal("SANS", definition.ResolveStatus("en"));
            Assert.Equal("黑体", definition.ResolveStatus("zh-CN"));
            Assert.True(definition.IsActive());

            definition.Activate();

            Assert.Equal(BppUiFontKind.LxgwWenKai, config.UiFontKindConfig.Value);
            Assert.Equal("KAI", definition.ResolveStatus("en"));
            Assert.Equal("楷体", definition.ResolveStatus("zh-CN"));
            Assert.False(definition.IsActive());
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void UiFontDockEntry_treats_unknown_value_as_default_then_cycles_to_sans_serif()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-ui-font-dock-invalid-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var definition = UiFontSettingsDockEntry.Create().Build(config);

            config.UiFontKindConfig!.Value = (BppUiFontKind)99;

            Assert.Equal("KAI", definition.ResolveStatus("en"));
            Assert.False(definition.IsActive());

            definition.Activate();

            Assert.Equal(BppUiFontKind.SansSerif, config.UiFontKindConfig.Value);
            Assert.Equal("SANS", definition.ResolveStatus("en"));
            Assert.True(definition.IsActive());
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void EnchantPreviewDockEntry_cycles_unknown_mode_to_auto()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-enchant-preview-invalid-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            config.EnchantPreviewModeConfig!.Value = (PreviewVisibilityMode)99;
            var definition = ItemEnchantPreviewSettingsDockEntry.Create().Build(config);

            definition.Activate();

            Assert.Equal(
                PreviewVisibilityMode.AutoOnPedestalChoice,
                config.EnchantPreviewModeConfig.Value
            );
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
                    ? BazaarDbSnapshotUploadSettingsDockEntry.Create().Build(config)
                    : FixedSupporterListSettingsDockEntry.Create().Build(config);

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
            var entry = FixedSupporterListSettingsDockEntry.Create();

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
    public void VoiceSubtitlesDockEntry_cycles_subtitle_mode_through_off_both_chinese_english()
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
            var entry = VoiceSubtitlesSettingsDockEntry.Create();

            var definition = entry.Build(config);

            Assert.Equal(BppSettingsDockOrder.VoiceSubtitles, entry.Order);
            Assert.Equal("VoiceSubtitles", definition.Key);
            Assert.Equal("Subtitle Mode", definition.ResolveLabel("en"));
            Assert.Equal("字幕模式", definition.ResolveLabel("zh-CN"));
            Assert.False(config.EnableVoiceSubtitlesConfig!.Value);
            Assert.Equal(SubtitleLanguageMode.Both, config.VoiceSubtitlesLanguageModeConfig!.Value);
            Assert.False(definition.IsActive());
            Assert.Equal("OFF", definition.ResolveStatus("en"));
            Assert.Equal("关闭", definition.ResolveStatus("zh-CN"));

            definition.Activate();

            Assert.True(config.EnableVoiceSubtitlesConfig.Value);
            Assert.Equal(SubtitleLanguageMode.Both, config.VoiceSubtitlesLanguageModeConfig.Value);
            Assert.True(definition.IsActive());
            Assert.Equal("BOTH", definition.ResolveStatus("en"));
            Assert.Equal("双语", definition.ResolveStatus("zh-CN"));

            definition.Activate();

            Assert.True(config.EnableVoiceSubtitlesConfig.Value);
            Assert.Equal(
                SubtitleLanguageMode.ChineseOnly,
                config.VoiceSubtitlesLanguageModeConfig.Value
            );
            Assert.True(definition.IsActive());
            Assert.Equal("ZH", definition.ResolveStatus("en"));
            Assert.Equal("中文", definition.ResolveStatus("zh-CN"));

            definition.Activate();

            Assert.True(config.EnableVoiceSubtitlesConfig.Value);
            Assert.Equal(
                SubtitleLanguageMode.EnglishOnly,
                config.VoiceSubtitlesLanguageModeConfig.Value
            );
            Assert.True(definition.IsActive());
            Assert.Equal("EN", definition.ResolveStatus("en"));
            Assert.Equal("英文", definition.ResolveStatus("zh-CN"));

            definition.Activate();

            Assert.False(config.EnableVoiceSubtitlesConfig.Value);
            Assert.Equal(SubtitleLanguageMode.Both, config.VoiceSubtitlesLanguageModeConfig.Value);
            Assert.False(definition.IsActive());
            Assert.Equal("OFF", definition.ResolveStatus("en"));
            Assert.Equal("关闭", definition.ResolveStatus("zh-CN"));
            Assert.False(definition.CollapseAfterActivate);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void VoiceSubtitlesPositionDockEntry_defaults_top_center_and_cycles_to_top_left()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-voice-subtitles-position-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var definition = VoiceSubtitlesPositionSettingsDockEntry.Create().Build(config);

            Assert.Equal(SubtitlePosition.TopCenter, config.VoiceSubtitlesPositionConfig!.Value);
            Assert.Equal("Top Center", definition.ResolveStatus("en"));
            Assert.Equal("顶部居中", definition.ResolveStatus("zh-CN"));
            Assert.False(definition.IsActive());

            definition.Activate();

            Assert.Equal(SubtitlePosition.TopLeft, config.VoiceSubtitlesPositionConfig.Value);
            Assert.Equal("Top Left", definition.ResolveStatus("en"));
            Assert.True(definition.IsActive());
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
            var definition = VoiceSubtitlesChineseFontScaleSettingsDockEntry.Create().Build(config);

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

    [Fact]
    public void VoiceSubtitlesFontScaleDockEntry_uses_first_strictly_greater_ladder_value()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-voice-subtitles-scale-between-steps-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            config.VoiceSubtitlesEnglishFontScaleConfig!.Value = 1.3f;
            var definition = VoiceSubtitlesEnglishFontScaleSettingsDockEntry.Create().Build(config);

            definition.Activate();

            Assert.Equal(1.5f, config.VoiceSubtitlesEnglishFontScaleConfig.Value, precision: 2);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void HistoryPanelDockEntry_uses_game_history_order()
    {
        Assert.Equal(BppSettingsDockOrder.GameHistory, new HistoryPanelSettingsDockEntry().Order);
    }

    [Theory]
    [InlineData(
        "NameOverride",
        BppSettingsDockOrder.NameOverride,
        "Anonymous Mode",
        "匿名模式",
        "OFF>ON>OFF",
        "false>true>false"
    )]
    [InlineData(
        "LegendaryPositionDisplay",
        BppSettingsDockOrder.LegendaryPosition,
        "Legendary Position",
        "传奇名次",
        "DEF>BLANK>999999>P|R>DEF",
        "false>true>true>true>false"
    )]
    [InlineData(
        "EnchantPreview",
        BppSettingsDockOrder.EnchantPreview,
        "Enchant Preview",
        "附魔预览",
        "ON>OFF>AUTO>ON",
        "true>false>true>true"
    )]
    [InlineData(
        "EventPreview",
        BppSettingsDockOrder.EventPreview,
        "Event Preview",
        "事件预览",
        "ON>OFF>ON",
        "true>false>true"
    )]
    [InlineData(
        "CombatStatusBar",
        BppSettingsDockOrder.CombatStatusBar,
        "Combat Status Bar",
        "战斗状态",
        "OFF>ON>OFF",
        "false>true>false"
    )]
    [InlineData(
        "ChineseLocaleMode",
        BppSettingsDockOrder.ChineseLocaleMode,
        "Chinese Locale",
        "中文模式",
        "CN>TW>CN",
        "false>true>false"
    )]
    [InlineData(
        "UiFont",
        BppSettingsDockOrder.UiFont,
        "UI Font",
        "界面字体",
        "KAI>SANS>KAI",
        "false>true>false"
    )]
    [InlineData(
        "StreamMode",
        BppSettingsDockOrder.FixedSupporterList,
        "Stream Mode",
        "直播模式",
        "OFF>ON>OFF",
        "false>true>false"
    )]
    [InlineData(
        "VoiceSubtitles",
        BppSettingsDockOrder.VoiceSubtitles,
        "Subtitle Mode",
        "字幕模式",
        "OFF>BOTH>ZH>EN>OFF",
        "false>true>true>true>false"
    )]
    [InlineData(
        "VoiceSubtitlesPosition",
        BppSettingsDockOrder.VoiceSubtitlesPosition,
        "Subtitle Position",
        "字幕位置",
        "Top Center>Top Left>Top Right>Top Center",
        "false>true>true>false"
    )]
    [InlineData(
        "VoiceSubtitlesEnglishFontScale",
        BppSettingsDockOrder.VoiceSubtitlesEnglishFontScale,
        "English Size",
        "英文字号",
        "1x>1.25x>1.5x>1.75x>2x>2.25x>2.5x>1x",
        "false>true>true>true>true>true>true>false"
    )]
    [InlineData(
        "VoiceSubtitlesChineseFontScale",
        BppSettingsDockOrder.VoiceSubtitlesChineseFontScale,
        "Chinese Size",
        "中文字号",
        "1x>1.25x>1.5x>1.75x>2x>2.25x>2.5x>1x",
        "false>true>true>true>true>true>true>false"
    )]
    [InlineData(
        "BazaarDbUpload",
        BppSettingsDockOrder.BazaarDbUpload,
        "BazaarDB upload",
        "BazaarDB 数据共建",
        "OFF>ON>OFF",
        "false>true>false"
    )]
    public void CyclingSettingsDockEntries_preserve_contracts(
        string key,
        int expectedOrder,
        string expectedEnglishLabel,
        string expectedChineseLabel,
        string expectedStatusSequence,
        string expectedHighlightSequence
    )
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-cycling-dock-contract-{key}-{Guid.NewGuid():N}.cfg"
        );
        var servicesField = typeof(CombatStatusBar).GetField(
            "_services",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        var previousServices = servicesField.GetValue(null);

        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            servicesField.SetValue(null, new ContractTestServices(config));

            var entry = CreateCyclingEntry(key);
            var definition = entry.Build(config);
            var expectedStatuses = expectedStatusSequence.Split('>');
            var expectedHighlights = expectedHighlightSequence
                .Split('>')
                .Select(bool.Parse)
                .ToArray();
            var actualStatuses = new List<string>();
            var actualHighlights = new List<bool>();

            for (var i = 0; i < expectedStatuses.Length; i++)
            {
                actualStatuses.Add(definition.ResolveStatus("en"));
                actualHighlights.Add(definition.IsActive());
                if (i + 1 < expectedStatuses.Length)
                    definition.Activate();
            }

            Assert.Equal(expectedOrder, entry.Order);
            Assert.Equal(key, definition.Key);
            L.Install(new TestLanguageProvider("en"), new TestLocaleModeProvider());
            Assert.Equal(expectedEnglishLabel, definition.ResolveLabel("en"));
            L.Install(new TestLanguageProvider("zh-CN"), new TestLocaleModeProvider());
            Assert.Equal(expectedChineseLabel, definition.ResolveLabel("zh-CN"));
            Assert.Equal(expectedStatuses, actualStatuses);
            Assert.Equal(expectedHighlights, actualHighlights);
            Assert.False(definition.CollapseAfterActivate);
        }
        finally
        {
            servicesField.SetValue(null, previousServices);
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    private static ISettingsDockEntry CreateCyclingEntry(string key) =>
        key switch
        {
            "NameOverride" => NameOverrideSettingsDockEntry.Create(() => { }),
            "LegendaryPositionDisplay" => LegendaryPositionSettingsDockEntry.Create(() => { }),
            "EnchantPreview" => ItemEnchantPreviewSettingsDockEntry.Create(),
            "EventPreview" => EventPreviewSettingsDockEntry.Create(),
            "CombatStatusBar" => CombatStatusBarSettingsDockEntry.Create(),
            "ChineseLocaleMode" => ChineseLocaleModeSettingsDockEntry.Create(
                new InMemoryBppEventBus()
            ),
            "UiFont" => UiFontSettingsDockEntry.Create(),
            "StreamMode" => FixedSupporterListSettingsDockEntry.Create(),
            "VoiceSubtitles" => VoiceSubtitlesSettingsDockEntry.Create(),
            "VoiceSubtitlesPosition" => VoiceSubtitlesPositionSettingsDockEntry.Create(),
            "VoiceSubtitlesEnglishFontScale" =>
                VoiceSubtitlesEnglishFontScaleSettingsDockEntry.Create(),
            "VoiceSubtitlesChineseFontScale" =>
                VoiceSubtitlesChineseFontScaleSettingsDockEntry.Create(),
            "BazaarDbUpload" => BazaarDbSnapshotUploadSettingsDockEntry.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

    [Fact]
    public void SettingsDockCatalog_sorts_ui_font_adjacent_to_chinese_locale()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
        var registry = new SettingsDockEntryRegistry();
        registry.Register(BazaarDbSnapshotUploadSettingsDockEntry.Create());
        registry.Register(FixedSupporterListSettingsDockEntry.Create());
        VoiceSubtitlesSettingsDockEntry.RegisterAll(registry);
        registry.Register(new EndOfRunScreenshotSettingsDockEntry());
        registry.Register(UiFontSettingsDockEntry.Create());
        registry.Register(ChineseLocaleModeSettingsDockEntry.Create(new InMemoryBppEventBus()));

        try
        {
            BppSettingsDockCatalog.Install(new BppConfig(), registry);

            Assert.Equal(
                new[]
                {
                    "ChineseLocaleMode",
                    "UiFont",
                    "StreamMode",
                    "VoiceSubtitles",
                    "VoiceSubtitlesPosition",
                    "VoiceSubtitlesEnglishFontScale",
                    "VoiceSubtitlesChineseFontScale",
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
