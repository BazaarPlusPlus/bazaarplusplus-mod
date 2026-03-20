using BazaarPlusPlus.Game.CombatLog;
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
    public void NormalizeConfiguredDefaultSpeed_AcceptsConfigOnlySpecialSpeed()
    {
        var result = CombatStatusBar.NormalizeConfiguredDefaultSpeed(1.57f);

        Assert.Equal(1.57f, result);
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
    public void ConfigOnlySpecialSpeed_DisablesUiStepControls()
    {
        CombatStatusBar.SetCombatSpeed(1.57f);

        Assert.False(CombatStatusBar.CanStepCombatSpeed(-1));
        Assert.False(CombatStatusBar.CanStepCombatSpeed(1));
        Assert.Equal(1.57f, CombatStatusBar.StepCombatSpeed(-1));
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
    [InlineData("zh-Hans", "战斗状态栏 | F6 切换")]
    [InlineData("zh-CN", "战斗状态栏 | F6 切换")]
    [InlineData("en", "Combat Status Bar | F6 Toggle")]
    [InlineData("", "Combat Status Bar | F6 Toggle")]
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
    [InlineData("zh-Hans", "始终显示附魔预览")]
    [InlineData("zh-CN", "始终显示附魔预览")]
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
        Assert.Contains("BPP_CombatStatusBarToggle", source, StringComparison.Ordinal);
        Assert.Contains(
            "BppGameplaySettingsCoordinator.EnsureAll",
            source,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void SettingsMenuToggleInstaller_SupportsAnchoringBppRowsBelowOtherBppRows()
    {
        var sourcePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../Patches/Settings/SettingsMenuToggleInstaller.cs"
            )
        );
        Assert.True(
            File.Exists(sourcePath),
            $"Settings menu toggle installer not found at {sourcePath}"
        );
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("PreferredAnchorObjectName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GameplaySettingsCoordinator_UsesStableToggleOrder()
    {
        var sourcePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../Patches/Settings/BppGameplaySettingsCoordinator.cs"
            )
        );
        Assert.True(
            File.Exists(sourcePath),
            $"Gameplay settings coordinator not found at {sourcePath}"
        );
        var source = File.ReadAllText(sourcePath);

        var nameIndex = source.IndexOf(
            "NameOverrideSettingsAwakePatch.EnsureToggleExists",
            StringComparison.Ordinal
        );
        var combatIndex = source.IndexOf(
            "CombatStatusBarSettingsAwakePatch.EnsureToggleExists",
            StringComparison.Ordinal
        );
        var enchantIndex = source.IndexOf(
            "EnchantPreviewSettingsAwakePatch.EnsureToggleExists",
            StringComparison.Ordinal
        );
        var arrangeIndex = source.IndexOf(
            "SettingsMenuToggleInstaller.ArrangeRows",
            StringComparison.Ordinal
        );

        Assert.True(
            nameIndex >= 0
                && enchantIndex > nameIndex
                && combatIndex > enchantIndex
                && arrangeIndex > enchantIndex
        );
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

    [Fact]
    public void CombatLogPlaybackState_MapsProcessedFrameCountToLastProcessedFrameIndex()
    {
        Assert.Equal(-1, CombatLogPlaybackState.GetLastProcessedFrameIndex(0));
        Assert.Equal(0, CombatLogPlaybackState.GetLastProcessedFrameIndex(1));
        Assert.Equal(4, CombatLogPlaybackState.GetLastProcessedFrameIndex(5));
    }

    [Fact]
    public void CombatLogPlaybackState_ReportsNoCurrentFrameBeforePlaybackStarts()
    {
        Assert.False(CombatLogPlaybackState.TryGetCurrentFrameIndex(0, out var frameIndex));
        Assert.Equal(-1, frameIndex);
    }

    [Fact]
    public void CombatLogPlaybackState_MapsVisualStateFromProcessedFrameCount()
    {
        Assert.Equal(
            CombatLogRowVisualState.Current,
            CombatLogPlaybackState.GetVisualState(0, 1, CombatLogPlaybackPass.FirstPlay)
        );
        Assert.Equal(
            CombatLogRowVisualState.Played,
            CombatLogPlaybackState.GetVisualState(4, 10, CombatLogPlaybackPass.FirstPlay)
        );
        Assert.Equal(
            CombatLogRowVisualState.FutureHidden,
            CombatLogPlaybackState.GetVisualState(12, 10, CombatLogPlaybackPass.FirstPlay)
        );
        Assert.Equal(
            CombatLogRowVisualState.FutureDimmed,
            CombatLogPlaybackState.GetVisualState(12, 10, CombatLogPlaybackPass.Replay)
        );
    }
}
