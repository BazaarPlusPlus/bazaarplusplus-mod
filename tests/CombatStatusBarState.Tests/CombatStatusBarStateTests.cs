using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class CombatStatusBarStateTests : IDisposable
{
    public CombatStatusBarStateTests()
    {
        CombatStatusBar.ResetStateForTests();
        CombatStatusBar.ClearPersistedOverlayVisibilityForTests();
    }

    public void Dispose()
    {
        CombatStatusBar.ResetStateForTests();
        CombatStatusBar.ClearPersistedOverlayVisibilityForTests();
    }

    [Fact]
    public void OverlayVisibility_DefaultsToVisible()
    {
        Assert.True(CombatStatusBar.IsOverlayVisible);
    }

    [Fact]
    public void ToggleOverlayVisibility_FlipsVisibilityAndPersists()
    {
        var result = CombatStatusBar.ToggleOverlayVisibility();

        Assert.False(result);
        Assert.False(CombatStatusBar.IsOverlayVisible);
        Assert.False(CombatStatusBar.GetPersistedOverlayVisibilityForTests());
    }

    [Fact]
    public void SetOverlayVisibility_PersistsRequestedValue()
    {
        var result = CombatStatusBar.SetOverlayVisibility(false);

        Assert.False(result);
        Assert.False(CombatStatusBar.IsOverlayVisible);
        Assert.False(CombatStatusBar.GetPersistedOverlayVisibilityForTests());
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsStandbyPlaceholderOutsideCombat()
    {
        Assert.Equal("LastCombat", CombatStatusBar.GetDisplayedTimeLabel());

        var result = CombatStatusBar.GetDisplayedTimeText();

        Assert.Equal("-:--:--", result);
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsLogicalCombatTimeDuringCombat()
    {
        CombatStatusBar.BeginCombatPlayback();
        for (var i = 0; i < 25; i++)
            CombatStatusBar.AdvanceCombatFrame();

        Assert.Equal("Time", CombatStatusBar.GetDisplayedTimeLabel());

        var result = CombatStatusBar.GetDisplayedTimeText();

        Assert.Equal("0:01:25", result);
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsLastCombatTimeAfterCombatEnds()
    {
        CombatStatusBar.BeginCombatPlayback();
        for (var i = 0; i < 25; i++)
            CombatStatusBar.AdvanceCombatFrame();

        CombatStatusBar.EndCombatPlayback();

        Assert.Equal("LastCombat", CombatStatusBar.GetDisplayedTimeLabel());
        Assert.Equal("0:01:25", CombatStatusBar.GetDisplayedTimeText());
    }

    [Fact]
    public void GetDisplayedFrameText_ReflectsStandbyAndProcessedFrames()
    {
        Assert.Equal("Standby", CombatStatusBar.GetDisplayedFrameText());

        CombatStatusBar.BeginCombatPlayback();
        CombatStatusBar.AdvanceCombatFrame();
        CombatStatusBar.AdvanceCombatFrame();

        Assert.Equal("2", CombatStatusBar.GetDisplayedFrameText());
    }

    [Fact]
    public void FormatCombatSpeedLabel_UsesTwoDecimalPlaces()
    {
        CombatStatusBar.SetCombatSpeed(1.5f);

        var result = CombatStatusBar.FormatCombatSpeedLabel();

        Assert.Equal("1.50x", result);
    }

    [Fact]
    public void NormalizeConfiguredDefaultSpeed_AcceptsNewSupportedStep()
    {
        var result = CombatStatusBar.NormalizeConfiguredDefaultSpeed(1.5f);

        Assert.Equal(1.5f, result);
    }

    [Fact]
    public void NormalizeConfiguredDefaultSpeed_RejectsRemovedStep()
    {
        var result = CombatStatusBar.NormalizeConfiguredDefaultSpeed(4f);

        Assert.Equal(1f, result);
    }

    [Fact]
    public void SettingsMenuBridge_ReadsInitialValueAndWritesBackChanges()
    {
        var enabled = false;
        var bridge = new CombatStatusBarSettingsMenuBridge(() => enabled, value => enabled = value);

        Assert.False(bridge.GetInitialValue());

        bridge.ApplyValue(true);

        Assert.True(enabled);
    }

    [Fact]
    public void SharedSettingsMenuBridge_ReadsInitialValue_WritesBackChanges_AndInvokesOnChanged()
    {
        var enabled = false;
        bool? changedValue = null;
        var bridge = new SettingsMenuToggleBridge(
            () => enabled,
            value => enabled = value,
            value => changedValue = value
        );

        Assert.False(bridge.GetInitialValue());

        bridge.ApplyValue(true);

        Assert.True(enabled);
        Assert.True(changedValue);
    }

    [Theory]
    [InlineData("zh-Hans", "战斗状态栏")]
    [InlineData("zh-CN", "战斗状态栏")]
    [InlineData("en", "Combat Status Bar")]
    [InlineData("", "Combat Status Bar")]
    public void SettingsMenuLabel_UsesChineseOnlyForSimplifiedChinese(
        string languageCode,
        string expected
    )
    {
        var result = CombatStatusBarSettingsMenuLabel.Resolve(languageCode);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void NameOverrideSettingsMenuBridge_ReadsInitialValue_WritesBackChanges_AndRequestsRefreshOnEveryChange()
    {
        var enabled = false;
        var refreshCount = 0;
        var bridge = new NameOverrideSettingsMenuBridge(
            () => enabled,
            value => enabled = value,
            () => refreshCount++
        );

        Assert.False(bridge.GetInitialValue());

        bridge.ApplyValue(true);
        bridge.ApplyValue(false);

        Assert.False(enabled);
        Assert.Equal(2, refreshCount);
    }

    [Theory]
    [InlineData("zh-Hans", "匿名模式")]
    [InlineData("zh-CN", "匿名模式")]
    [InlineData("en", "Anonymous Mode")]
    [InlineData("", "Anonymous Mode")]
    public void NameOverrideSettingsMenuLabel_UsesChineseOnlyForSimplifiedChinese(
        string languageCode,
        string expected
    )
    {
        var result = NameOverrideSettingsMenuLabel.Resolve(languageCode);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("zh-Hans", "附魔预览始终显示")]
    [InlineData("zh-CN", "附魔预览始终显示")]
    [InlineData("en", "Enchant Preview Always Show")]
    [InlineData("", "Enchant Preview Always Show")]
    public void EnchantPreviewSettingsMenuLabel_UsesChineseOnlyForSimplifiedChinese(
        string languageCode,
        string expected
    )
    {
        var result = EnchantPreviewSettingsMenuLabel.Resolve(languageCode);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void EnchantPreviewSettingsPatch_Exists_And_BindsAlwaysShowConfig()
    {
        var sourcePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../Patches/Tooltips/EnchantPreviewSettingsPatch.cs"
            )
        );
        Assert.True(
            File.Exists(sourcePath),
            $"Enchant preview settings patch not found at {sourcePath}"
        );
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("EnchantPreviewAlwaysShowConfig", source, StringComparison.Ordinal);
        Assert.Contains("BPP_EnchantPreviewToggle", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0f, true, 0.10f, 0.50f)]
    [InlineData(0.90f, true, 0.10f, 1.00f)]
    [InlineData(0.10f, false, 0.10f, 0.00f)]
    public void AdvanceVisualBlend_ApproachesTargetWithoutOvershooting(
        float current,
        bool active,
        float deltaTime,
        float expected
    )
    {
        var result = CombatStatusBar.AdvanceVisualBlend(current, active, deltaTime);

        Assert.Equal(expected, result, precision: 3);
    }
}
