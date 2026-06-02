using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppSettingsDockGeometryTests
{
    [Fact]
    public void LeftOfSettingButton_creates_per_anchor_object_names()
    {
        var mainMenu = BppSettingsDockPlacement.LeftOfSettingButton("MainMenu");
        var heroSelect = BppSettingsDockPlacement.LeftOfSettingButton("HeroSelect");

        Assert.NotEqual(mainMenu.DockButtonObjectName, heroSelect.DockButtonObjectName);
        Assert.NotEqual(mainMenu.PanelObjectName, heroSelect.PanelObjectName);
        Assert.Contains("MainMenu", mainMenu.DockButtonObjectName);
        Assert.Contains("HeroSelect", heroSelect.PanelObjectName);
        Assert.Equal(BppSettingsDockSide.LeftOfAnchor, mainMenu.Side);
        Assert.Equal(BppSettingsDockPanelDirection.UpLeft, mainMenu.PanelDirection);
    }

    [Fact]
    public void CalculateDockButtonLocalPosition_places_clone_left_of_anchor_with_gap()
    {
        var placement = BppSettingsDockPlacement.LeftOfSettingButton("MainMenu");

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
        var placement = BppSettingsDockPlacement.LeftOfSettingButton("HeroSelect");

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
        var placement = BppSettingsDockPlacement.AboveSettingButton("CollectionPanel");

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
    }

    [Fact]
    public void CalculateDockButtonLocalPosition_places_clone_world_above_when_local_axis_is_flipped()
    {
        var placement = BppSettingsDockPlacement.AboveSettingButton("CollectionPanel");

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
}
