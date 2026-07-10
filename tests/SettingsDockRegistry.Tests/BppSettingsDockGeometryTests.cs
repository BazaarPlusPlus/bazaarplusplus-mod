using System.Runtime.CompilerServices;
using BazaarPlusPlus.Game.Settings;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppSettingsDockGeometryTests
{
    [Fact]
    public void Placement_creates_per_anchor_object_names()
    {
        var mainMenu = BppSettingsDockPlacement.ForButton(
            "MainMenu",
            BppDockButtonIconKind.SettingsDock
        );
        var heroSelect = BppSettingsDockPlacement.ForButton(
            "HeroSelect",
            BppDockButtonIconKind.SettingsDock
        );

        Assert.NotEqual(mainMenu.DockButtonObjectName, heroSelect.DockButtonObjectName);
        Assert.NotEqual(mainMenu.PanelObjectName, heroSelect.PanelObjectName);
        Assert.Contains("MainMenu", mainMenu.DockButtonObjectName);
        Assert.Contains("HeroSelect", heroSelect.PanelObjectName);
        Assert.Equal(BppSettingsDockPanelDirection.UpLeft, mainMenu.PanelDirection);
        Assert.Equal(BppDockButtonIconKind.SettingsDock, mainMenu.ButtonIconKind);
    }

    [Theory]
    [InlineData(1.0f, 1.5f)]
    [InlineData(0.75f, 2.0f)]
    [InlineData(0.00001f, 1.5f)]
    public void CalculatePanelLocalScale_divides_out_clone_scale_with_fallback(
        float cloneLocalScale,
        float expected
    )
    {
        var result = BppSettingsDockGeometry.CalculatePanelLocalScale(
            targetOnScreenScale: 1.5f,
            cloneLocalScale
        );

        Assert.Equal(expected, result, precision: 4);
    }

    [Fact]
    public void DockButtonHoverColors_are_distinct_per_button_kind()
    {
        var settings = BppDockButtonVisuals.ResolveColors(BppDockButtonIconKind.SettingsDock);
        var collection = BppDockButtonVisuals.ResolveColors(BppDockButtonIconKind.CollectionPanel);

        Assert.NotEqual(settings.Highlighted, collection.Highlighted);
        Assert.Equal(Color.white, settings.Normal);
        Assert.Equal(Color.white, collection.Normal);
        Assert.True(settings.FadeDuration > 0f);
        Assert.True(collection.FadeDuration > 0f);
    }

    [Theory]
    [InlineData(Selectable.Transition.ColorTint)]
    [InlineData(Selectable.Transition.SpriteSwap)]
    [InlineData(Selectable.Transition.Animation)]
    public void ResolveButtonState_preserves_native_transition_state(
        Selectable.Transition transition
    )
    {
        var nativeColors = ColorBlock.defaultColorBlock;
        nativeColors.normalColor = new Color(0.15f, 0.25f, 0.35f, 1f);
        nativeColors.highlightedColor = new Color(0.96f, 0.74f, 0.18f, 1f);
        nativeColors.pressedColor = new Color(0.68f, 0.42f, 0.12f, 1f);
        nativeColors.selectedColor = new Color(0.28f, 0.78f, 0.44f, 1f);
        nativeColors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        var nativeSpriteState = new SpriteState();
        var nativeAnimationTriggers = new AnimationTriggers
        {
            normalTrigger = "DockNormal",
            highlightedTrigger = "DockHighlighted",
            pressedTrigger = "DockPressed",
            selectedTrigger = "DockSelected",
            disabledTrigger = "DockDisabled",
        };
        var nativeState = BppDockButtonVisualState.Capture(
            transition,
            nativeColors,
            nativeSpriteState,
            nativeAnimationTriggers
        );

        var resolved = BppDockButtonVisuals.ResolveButtonState(
            BppDockButtonIconKind.SettingsDock,
            nativeState
        );

        Assert.Equal(transition, resolved.Transition);
        Assert.Equal(nativeColors, resolved.Colors);
        Assert.Equal(nativeSpriteState, resolved.SpriteState);
        Assert.Same(nativeAnimationTriggers, resolved.AnimationTriggers);
    }

    [Fact]
    public void Settings_popover_visual_keeps_native_normal_color_baseline()
    {
        var nativeColors = ColorBlock.defaultColorBlock;
        nativeColors.normalColor = new Color(0.15f, 0.25f, 0.35f, 1f);
        nativeColors.highlightedColor = new Color(0.96f, 0.74f, 0.18f, 1f);
        nativeColors.pressedColor = new Color(0.68f, 0.42f, 0.12f, 1f);
        nativeColors.selectedColor = new Color(0.28f, 0.78f, 0.44f, 1f);
        nativeColors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        var nativeState = BppDockButtonVisualState.Capture(
            Selectable.Transition.ColorTint,
            nativeColors,
            new SpriteState(),
            new AnimationTriggers()
        );

        var resolved = nativeState.ResolveNormalColors();

        Assert.Equal(nativeColors, resolved);
    }

    [Fact]
    public void Settings_popover_animation_keeps_native_normal_trigger()
    {
        var nativeTriggers = new AnimationTriggers
        {
            normalTrigger = "DockNormal",
            highlightedTrigger = "DockHighlighted",
            pressedTrigger = "DockPressed",
            selectedTrigger = "DockSelected",
            disabledTrigger = "DockDisabled",
        };
        var nativeState = BppDockButtonVisualState.Capture(
            Selectable.Transition.Animation,
            ColorBlock.defaultColorBlock,
            new SpriteState(),
            nativeTriggers
        );

        var resolved = nativeState.CloneAnimationTriggers();

        Assert.Equal(nativeTriggers.normalTrigger, resolved.normalTrigger);
        Assert.Equal(nativeTriggers.highlightedTrigger, resolved.highlightedTrigger);
        Assert.Equal(nativeTriggers.pressedTrigger, resolved.pressedTrigger);
        Assert.Equal(nativeTriggers.selectedTrigger, resolved.selectedTrigger);
        Assert.Equal(nativeTriggers.disabledTrigger, resolved.disabledTrigger);
    }

    [Fact]
    public void Settings_popover_sprite_keeps_native_normal_base_sprite()
    {
        var normalSprite = (Sprite)RuntimeHelpers.GetUninitializedObject(typeof(Sprite));
        var nativeState = new BppDockButtonVisualState(
            Selectable.Transition.SpriteSwap,
            ColorBlock.defaultColorBlock,
            new SpriteState(),
            new AnimationTriggers(),
            targetGraphic: null,
            normalSprite
        );

        Assert.Same(normalSprite, nativeState.ResolveNormalSprite());
    }

    [Fact]
    public void ShouldSyncForScreenSize_returns_true_when_resolution_changes()
    {
        var changed = BppSettingsDockGeometry.ShouldSyncForScreenSize(1920, 1080, 2560, 1440);
        var same = BppSettingsDockGeometry.ShouldSyncForScreenSize(1920, 1080, 1920, 1080);

        Assert.True(changed);
        Assert.False(same);
    }

    [Fact]
    public void ScreenResizeSyncTracker_requests_sync_for_configured_frames_after_size_changes()
    {
        var tracker = new BppScreenResizeSyncTracker(syncFrameCount: 3);

        Assert.True(tracker.ShouldSync(1920, 1080));
        Assert.True(tracker.ShouldSync(1920, 1080));
        Assert.True(tracker.ShouldSync(1920, 1080));
        Assert.False(tracker.ShouldSync(1920, 1080));

        Assert.True(tracker.ShouldSync(2560, 1440));
        Assert.True(tracker.ShouldSync(2560, 1440));
        Assert.True(tracker.ShouldSync(2560, 1440));
        Assert.False(tracker.ShouldSync(2560, 1440));
    }
}
