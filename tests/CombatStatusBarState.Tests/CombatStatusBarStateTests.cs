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
    }

    public void Dispose()
    {
        CombatStatusBar.ResetStateForTests();
        BazaarPlusPlus.Core.Runtime.BppRuntimeHost.TestContext.IsInGameRun = false;
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
        CombatStatusBar.SetCombatSpeed(0.33f);

        var result = CombatStatusBar.FormatCombatSpeedLabel();

        Assert.Equal("0.33x", result);
    }

    [Fact]
    public void NormalizeConfiguredDefaultSpeed_AcceptsNewSupportedStep()
    {
        var result = CombatStatusBar.NormalizeConfiguredDefaultSpeed(0.33f);

        Assert.Equal(0.33f, result);
    }

    [Fact]
    public void NormalizeConfiguredDefaultSpeed_ClampsLegacyConfiguredSpeedAboveOneToOne()
    {
        var result = CombatStatusBar.NormalizeConfiguredDefaultSpeed(1.57f);

        Assert.Equal(1f, result);
    }

    [Fact]
    public void NormalizeConfiguredDefaultSpeed_RejectsRemovedStep()
    {
        var result = CombatStatusBar.NormalizeConfiguredDefaultSpeed(1.5f);

        Assert.Equal(1f, result);
    }

    [Fact]
    public void CombatSpeedSteps_ExposeExpectedOfflineSpeedPresets()
    {
        Assert.Equal(new[] { 0.25f, 0.33f, 0.5f, 1f }, CombatStatusBar.CombatSpeedSteps.ToArray());
    }

    [Fact]
    public void SetCombatSpeed_RejectsUnsupportedValueAboveOne()
    {
        CombatStatusBar.SetCombatSpeed(0.5f);

        var result = CombatStatusBar.SetCombatSpeed(1.57f);

        Assert.Equal(0.5f, result);
        Assert.Equal("0.50x", CombatStatusBar.FormatCombatSpeedLabel());
    }

    [Fact]
    public void ShouldOverrideCombatSpeed_RequestAtNormalSpeed_DuringCombat()
    {
        CombatStatusBar.BeginCombatPlayback();

        var result = CombatStatusBar.ShouldOverrideCombatSpeed(1f);

        Assert.True(result);
    }

    [Fact]
    public void ShouldOverrideCombatSpeed_DoesNotOverrideFastForwardFirstFightSpeed()
    {
        CombatStatusBar.BeginCombatPlayback();

        var result = CombatStatusBar.ShouldOverrideCombatSpeed(2f);

        Assert.False(result);
    }

    [Fact]
    public void ShouldOverrideCombatSpeed_DoesNotOverrideOutsideCombat()
    {
        var result = CombatStatusBar.ShouldOverrideCombatSpeed(1f);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRenderForState_RequiresFeatureEnabledAndInGameRun()
    {
        BazaarPlusPlus.Core.Runtime.BppRuntimeHost.TestContext.IsInGameRun = false;
        Assert.False(CombatStatusBar.ShouldRenderForState(enabled: true));

        BazaarPlusPlus.Core.Runtime.BppRuntimeHost.TestContext.IsInGameRun = true;
        Assert.True(CombatStatusBar.ShouldRenderForState(enabled: true));
        Assert.False(CombatStatusBar.ShouldRenderForState(enabled: false));
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

    [Fact]
    public void DockDefinition_ToggleRow_ReportsStatusAndFlipsStateOnActivate()
    {
        var enabled = false;
        var bridge = new SettingsMenuToggleBridge(() => enabled, value => enabled = value);
        var definition = new BppSettingsDockDefinition("CombatStatusBar", _ => "Combat", bridge);

        Assert.False(definition.IsActive());
        Assert.Equal("OFF", definition.ResolveStatus("en"));

        definition.Activate();

        Assert.True(enabled);
        Assert.True(definition.IsActive());
        Assert.Equal("ON", definition.ResolveStatus("en"));
        Assert.False(definition.CollapseAfterActivate);
    }

    [Fact]
    public void DockDefinition_ActionRow_InvokesAction_AndReportsDynamicStatus()
    {
        var open = false;
        var activationCount = 0;
        var definition = new BppSettingsDockDefinition(
            "GameHistory",
            _ => "Game History",
            _ => open ? "OPEN" : "VIEW",
            () => open,
            () =>
            {
                activationCount++;
                open = true;
            },
            collapseAfterActivate: true
        );

        Assert.False(definition.IsActive());
        Assert.Equal("VIEW", definition.ResolveStatus("en"));

        definition.Activate();

        Assert.Equal(1, activationCount);
        Assert.True(definition.IsActive());
        Assert.Equal("OPEN", definition.ResolveStatus("en"));
        Assert.True(definition.CollapseAfterActivate);
    }

    [Theory]
    [InlineData("zh-Hans", "\u6218\u6597\u72b6\u6001\u680f")]
    [InlineData("zh-CN", "\u6218\u6597\u72b6\u6001\u680f")]
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
    [InlineData("zh-Hans", "\u533f\u540d\u6a21\u5f0f")]
    [InlineData("zh-CN", "\u533f\u540d\u6a21\u5f0f")]
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
    [InlineData("zh-Hans", "\u59cb\u7ec8\u663e\u793a\u9644\u9b54\u9884\u89c8")]
    [InlineData("zh-CN", "\u59cb\u7ec8\u663e\u793a\u9644\u9b54\u9884\u89c8")]
    [InlineData("en", "Always Show Enchant Preview")]
    [InlineData("", "Always Show Enchant Preview")]
    public void EnchantPreviewSettingsMenuLabel_UsesChineseOnlyForSimplifiedChinese(
        string languageCode,
        string expected
    )
    {
        var result = EnchantPreviewSettingsMenuLabel.Resolve(languageCode);

        Assert.Equal(expected, result);
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
