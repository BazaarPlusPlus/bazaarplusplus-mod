using BazaarPlusPlus.Game.Settings;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppSettingsDockGeometryTests
{
    [Fact]
    public void LeftOfSettingButton_creates_per_anchor_object_names()
    {
        var mainMenu = BppSettingsDockPlacement.LeftOfSettingButton(
            "MainMenu",
            BppDockButtonIconKind.SettingsDock
        );
        var heroSelect = BppSettingsDockPlacement.LeftOfSettingButton(
            "HeroSelect",
            BppDockButtonIconKind.SettingsDock
        );

        Assert.NotEqual(mainMenu.DockButtonObjectName, heroSelect.DockButtonObjectName);
        Assert.NotEqual(mainMenu.PanelObjectName, heroSelect.PanelObjectName);
        Assert.Contains("MainMenu", mainMenu.DockButtonObjectName);
        Assert.Contains("HeroSelect", heroSelect.PanelObjectName);
        Assert.Equal(BppSettingsDockSide.LeftOfAnchor, mainMenu.Side);
        Assert.Equal(BppSettingsDockPanelDirection.UpLeft, mainMenu.PanelDirection);
        Assert.Equal(BppDockButtonIconKind.SettingsDock, mainMenu.ButtonIconKind);
    }

    [Fact]
    public void CalculateDockButtonLocalPosition_places_clone_left_of_anchor_with_gap()
    {
        var placement = BppSettingsDockPlacement.LeftOfSettingButton(
            "MainMenu",
            BppDockButtonIconKind.SettingsDock
        );

        var result = BppSettingsDockGeometry.CalculateDockButtonLocalPosition(
            anchorCenterLocalX: 100f,
            anchorCenterLocalY: 40f,
            anchorLeftLocalX: 60f,
            anchorRightLocalX: 140f,
            anchorTopLocalY: 70f,
            anchorBottomLocalY: 10f,
            currentLocalZ: 7f,
            placement: placement
        );

        Assert.Equal(2f, result.X);
        Assert.Equal(40f, result.Y);
        Assert.Equal(7f, result.Z);
    }

    [Fact]
    public void CalculateDockButtonLocalPosition_places_clone_world_left_when_local_axis_is_flipped()
    {
        var placement = BppSettingsDockPlacement.LeftOfSettingButton(
            "HeroSelect",
            BppDockButtonIconKind.SettingsDock
        );

        var result = BppSettingsDockGeometry.CalculateDockButtonLocalPosition(
            anchorCenterLocalX: 100f,
            anchorCenterLocalY: 40f,
            anchorLeftLocalX: 140f,
            anchorRightLocalX: 60f,
            anchorTopLocalY: 70f,
            anchorBottomLocalY: 10f,
            currentLocalZ: 7f,
            placement: placement
        );

        Assert.Equal(198f, result.X);
        Assert.Equal(40f, result.Y);
        Assert.Equal(7f, result.Z);
    }

    [Fact]
    public void CalculateDockButtonLocalPosition_places_clone_above_anchor_with_gap()
    {
        var placement = BppSettingsDockPlacement.AboveSettingButton(
            "CollectionPanel",
            BppDockButtonIconKind.CollectionPanel
        );

        var result = BppSettingsDockGeometry.CalculateDockButtonLocalPosition(
            anchorCenterLocalX: 100f,
            anchorCenterLocalY: 40f,
            anchorLeftLocalX: 60f,
            anchorRightLocalX: 140f,
            anchorTopLocalY: 70f,
            anchorBottomLocalY: 10f,
            currentLocalZ: 7f,
            placement
        );

        Assert.Equal(100f, result.X);
        Assert.Equal(118f, result.Y);
        Assert.Equal(7f, result.Z);
        Assert.Equal(BppDockButtonIconKind.CollectionPanel, placement.ButtonIconKind);
    }

    [Fact]
    public void CalculateDockButtonLocalPosition_places_clone_multiple_steps_above_anchor()
    {
        var placement = BppSettingsDockPlacement.AboveSettingButton(
            "FightMenu",
            BppDockButtonIconKind.SettingsDock,
            siblingStepCount: 2
        );

        var result = BppSettingsDockGeometry.CalculateDockButtonLocalPosition(
            anchorCenterLocalX: 100f,
            anchorCenterLocalY: 40f,
            anchorLeftLocalX: 60f,
            anchorRightLocalX: 140f,
            anchorTopLocalY: 70f,
            anchorBottomLocalY: 10f,
            currentLocalZ: 7f,
            placement
        );

        Assert.Equal(100f, result.X);
        Assert.Equal(196f, result.Y);
        Assert.Equal(7f, result.Z);
        Assert.Equal(2, placement.SiblingStepCount);
    }

    [Fact]
    public void ResolveForScene_uses_chest_opening_override_when_configured()
    {
        var placement = BppSettingsDockPlacement
            .LeftOfSettingButton("MainMenu", BppDockButtonIconKind.SettingsDock)
            .WithChestOpeningPlacement(
                BppSettingsDockSide.AboveAnchor,
                BppSettingsDockPanelDirection.UpLeft,
                siblingStepCount: 2
            );

        var defaultPlacement = placement.ResolveForScene(BppSettingsDockSceneKind.Default);
        var chestPlacement = placement.ResolveForScene(BppSettingsDockSceneKind.ChestOpening);

        Assert.Equal(BppSettingsDockSide.LeftOfAnchor, defaultPlacement.Side);
        Assert.Equal(1, defaultPlacement.SiblingStepCount);
        Assert.Equal(BppSettingsDockSide.AboveAnchor, chestPlacement.Side);
        Assert.Equal(2, chestPlacement.SiblingStepCount);
        Assert.Equal(BppDockButtonIconKind.SettingsDock, chestPlacement.ButtonIconKind);
    }

    [Theory]
    [InlineData("ChestOpening", null, true)]
    [InlineData("Chest Scene", "Chest Scene", true)]
    [InlineData("CollectionWheel", "Chest Scene", false)]
    public void ResolveSceneKind_detects_chest_opening_scene(
        string activeSceneName,
        string? catalogChestSceneName,
        bool expectChestOpening
    )
    {
        var result = BppSettingsDockSceneContext.ResolveSceneKind(
            activeSceneName,
            catalogChestSceneName
        );

        var expected = expectChestOpening
            ? BppSettingsDockSceneKind.ChestOpening
            : BppSettingsDockSceneKind.Default;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateDockButtonLocalPosition_places_clone_world_above_when_local_axis_is_flipped()
    {
        var placement = BppSettingsDockPlacement.AboveSettingButton(
            "CollectionPanel",
            BppDockButtonIconKind.CollectionPanel
        );

        var result = BppSettingsDockGeometry.CalculateDockButtonLocalPosition(
            anchorCenterLocalX: 100f,
            anchorCenterLocalY: 40f,
            anchorLeftLocalX: 60f,
            anchorRightLocalX: 140f,
            anchorTopLocalY: 10f,
            anchorBottomLocalY: 70f,
            currentLocalZ: 7f,
            placement
        );

        Assert.Equal(100f, result.X);
        Assert.Equal(-38f, result.Y);
        Assert.Equal(7f, result.Z);
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

    [Fact]
    public void ResolveButtonState_prefers_native_hover_transition()
    {
        var nativeColors = ColorBlock.defaultColorBlock;
        nativeColors.highlightedColor = new Color(0.96f, 0.74f, 0.18f, 1f);
        var nativeState = BppDockButtonVisualState.Capture(
            Selectable.Transition.SpriteSwap,
            nativeColors,
            new SpriteState(),
            new AnimationTriggers()
        );

        var resolved = BppDockButtonVisuals.ResolveButtonState(
            BppDockButtonIconKind.SettingsDock,
            nativeState
        );

        Assert.Equal(Selectable.Transition.SpriteSwap, resolved.Transition);
        Assert.Equal(nativeColors.highlightedColor, resolved.Colors.highlightedColor);
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
