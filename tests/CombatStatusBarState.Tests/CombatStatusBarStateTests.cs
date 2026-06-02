using System.Reflection;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.HistoryPanel;
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
        BazaarPlusPlus.Core.Runtime.TestServices.Instance.RunContext.IsInGameRun = false;
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

        Assert.Equal("0:01:20", result);
    }

    [Fact]
    public void GetDisplayedTimeText_ReturnsLastCombatTimeAfterCombatEnds()
    {
        CombatStatusBar.BeginCombatPlayback();
        for (var i = 0; i < 25; i++)
            CombatStatusBar.AdvanceCombatFrame();

        CombatStatusBar.EndCombatPlayback();

        Assert.Equal("LastCombat", CombatStatusBar.GetDisplayedTimeLabel());
        Assert.Equal("0:01:20", CombatStatusBar.GetDisplayedTimeText());
    }

    [Fact]
    public void FrameDisplayText_IsNotPartOfCombatStatusBarState()
    {
        var frameTextMethod = typeof(CombatStatusBar).GetMethod(
            "GetDisplayedFrameText",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.Null(frameTextMethod);
    }

    [Fact]
    public void DisplayedTimeText_StillUsesLogicalFrameIndex()
    {
        CombatStatusBar.BeginCombatPlayback();

        CombatStatusBar.AdvanceCombatFrame();
        Assert.Equal("0:00:00", CombatStatusBar.GetDisplayedTimeText());

        CombatStatusBar.AdvanceCombatFrame();
        Assert.Equal("0:00:05", CombatStatusBar.GetDisplayedTimeText());
    }

    [Fact]
    public void ButtonVisuals_KeepPauseBrightWhenDisabled()
    {
        var palette = new CombatStatusBarButtonPalette(
            Normal: new CombatStatusBarRgba(0.7f, 0.6f, 0.2f, 1f),
            Pressed: new CombatStatusBarRgba(0.9f, 0.7f, 0.3f, 1f),
            Unavailable: new CombatStatusBarRgba(0.2f, 0.2f, 0.2f, 0.45f),
            Text: new CombatStatusBarRgba(1f, 0.95f, 0.85f, 1f)
        );

        var result = CombatStatusBar.ResolveButtonVisuals(
            CombatStatusBarButtonKind.Pause,
            interactable: false,
            palette
        );

        Assert.False(result.Interactable);
        Assert.Equal(palette.Normal, result.Disabled);
        Assert.Equal(palette.Normal, result.Background);
        Assert.Equal(palette.Text, result.Text);
    }

    [Fact]
    public void ButtonVisuals_DimSpeedWhenUnavailable()
    {
        var palette = new CombatStatusBarButtonPalette(
            Normal: new CombatStatusBarRgba(0.7f, 0.6f, 0.2f, 1f),
            Pressed: new CombatStatusBarRgba(0.9f, 0.7f, 0.3f, 1f),
            Unavailable: new CombatStatusBarRgba(0.35f, 0.28f, 0.16f, 0.55f),
            Text: new CombatStatusBarRgba(1f, 0.95f, 0.85f, 1f)
        );

        var result = CombatStatusBar.ResolveButtonVisuals(
            CombatStatusBarButtonKind.Speed,
            interactable: false,
            palette
        );

        Assert.False(result.Interactable);
        Assert.Equal(palette.Unavailable, result.Disabled);
        Assert.Equal(palette.Unavailable, result.Background);
        Assert.Equal(new CombatStatusBarRgba(1f, 0.95f, 0.85f, 0.45f), result.Text);
    }

    [Fact]
    public void CombatSpeed_UsesRequestedDiscreteSteps()
    {
        Assert.Equal(1f, CombatStatusBar.CombatSpeedMultiplier, 3);
        Assert.Equal(1f, CombatStatusBar.NormalizeConfiguredDefaultSpeed(1f), 3);
        Assert.Equal(1f, CombatStatusBar.NormalizeConfiguredDefaultSpeed(1.25f), 3);

        CombatStatusBar.SetCombatSpeed(0.67f);
        Assert.Equal(0.67f, CombatStatusBar.CombatSpeedMultiplier, 3);

        CombatStatusBar.StepCombatSpeed(-1);
        Assert.Equal(0.5f, CombatStatusBar.CombatSpeedMultiplier, 3);

        CombatStatusBar.StepCombatSpeed(1);
        CombatStatusBar.StepCombatSpeed(1);
        Assert.Equal(1f, CombatStatusBar.CombatSpeedMultiplier, 3);
    }

    [Fact]
    public void CombatSpeed_CanStepWithinBounds_AndOnlyOverridesDuringPlayback()
    {
        Assert.False(CombatStatusBar.ShouldOverrideCombatSpeed(1f));

        CombatStatusBar.SetCombatSpeed(0.5f);
        Assert.False(CombatStatusBar.CanStepCombatSpeed(-1));
        Assert.True(CombatStatusBar.CanStepCombatSpeed(1));

        CombatStatusBar.BeginCombatPlayback();

        Assert.True(CombatStatusBar.ShouldOverrideCombatSpeed(1f));
        Assert.False(CombatStatusBar.ShouldOverrideCombatSpeed(1.2f));

        CombatStatusBar.SetCombatSpeed(1f);
        Assert.True(CombatStatusBar.CanStepCombatSpeed(-1));
        Assert.False(CombatStatusBar.CanStepCombatSpeed(1));
    }

    [Fact]
    public void ShouldRenderForState_RequiresFeatureEnabledAndInGameRun()
    {
        BazaarPlusPlus.Core.Runtime.TestServices.Instance.RunContext.IsInGameRun = false;
        Assert.False(CombatStatusBar.ShouldRenderForState(enabled: true));

        BazaarPlusPlus.Core.Runtime.TestServices.Instance.RunContext.IsInGameRun = true;
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
    [InlineData("zh-Hans", "附魔预览")]
    [InlineData("zh-CN", "附魔预览")]
    [InlineData("en", "Enchant Preview")]
    [InlineData("", "Enchant Preview")]
    public void EnchantPreviewSettingsMenuLabel_UsesChineseOnlyForSimplifiedChinese(
        string languageCode,
        string expected
    )
    {
        var result = EnchantPreviewSettingsMenuLabel.Resolve(languageCode);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void HistoryPanelAccessPolicy_RequiresLobbyState(bool isInGameRun, bool expected)
    {
        var result = HistoryPanelAccessPolicy.CanOpen(isInGameRun);

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
