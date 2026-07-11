using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppDockButtonLayoutPlannerTests
{
    private static readonly BppDockButtonBounds Viewport = new(0f, 1200f, 0f, 900f);
    private static readonly BppDockButtonBounds Gear = BppDockButtonBounds.FromCenter(
        1100f,
        100f,
        80f,
        80f
    );

    [Fact]
    public void Visual_footprint_includes_decorations_outside_target_graphic()
    {
        var targetGraphic = BppDockButtonBounds.FromCenter(0f, 0f, 120f, 120f);
        var leftSpike = new BppDockButtonBounds(-84f, -54f, -12f, 12f);

        var footprint = targetGraphic.Union(leftSpike);

        Assert.Equal(-84f, footprint.MinX);
        Assert.Equal(60f, footprint.MaxX);
        Assert.Equal(144f, footprint.Width);
        Assert.Equal(-12f, footprint.CenterX);
    }

    [Theory]
    [InlineData(true, true, 1f, true, false, true)]
    [InlineData(false, true, 1f, true, false, false)]
    [InlineData(true, false, 1f, true, false, false)]
    [InlineData(true, true, 0f, true, false, false)]
    [InlineData(true, true, 1f, false, false, false)]
    [InlineData(true, true, 1f, true, true, false)]
    public void Visual_footprint_filters_non_rendered_panel_and_nested_button_graphics(
        bool isEnabled,
        bool isActiveBelowOwner,
        float authoredAlpha,
        bool belongsToOwner,
        bool isInsideSettingsPanel,
        bool expected
    )
    {
        var result = BppDockButtonVisualFootprint.ShouldIncludeGraphic(
            isEnabled,
            isActiveBelowOwner,
            authoredAlpha,
            belongsToOwner,
            isInsideSettingsPanel
        );

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Resolve_preserves_gap_between_composite_visual_footprints()
    {
        var targetGraphic = BppDockButtonBounds.FromCenter(0f, 0f, 120f, 120f);
        var composite = targetGraphic
            .Union(new BppDockButtonBounds(-72f, -54f, -12f, 12f))
            .Union(new BppDockButtonBounds(54f, 72f, -12f, 12f))
            .Union(new BppDockButtonBounds(-12f, 12f, -72f, -54f))
            .Union(new BppDockButtonBounds(-12f, 12f, 54f, 72f));
        var gear = BppDockButtonBounds.FromCenter(1100f, 100f, composite.Width, composite.Height);

        var result = BppDockButtonLayoutPlanner.Resolve(
            Viewport,
            gear,
            composite.Width,
            composite.Height,
            composite.Width,
            composite.Height,
            gap: 18f,
            blockers: Array.Empty<BppDockButtonObstacle>()
        );

        Assert.Equal(18f, result.CollectionBounds.MinY - gear.MaxY);
        Assert.Equal(18f, gear.MinX - result.SettingsBounds.MaxX);
    }

    [Fact]
    public void Resolve_places_settings_left_of_gear_when_slot_is_clear()
    {
        var result = BppDockButtonLayoutPlanner.Resolve(
            Viewport,
            Gear,
            collectionWidth: 60f,
            collectionHeight: 60f,
            settingsWidth: 40f,
            settingsHeight: 40f,
            gap: 18f,
            blockers: Array.Empty<BppDockButtonObstacle>()
        );

        Assert.True(result.CanApply);
        Assert.Equal(BppDockButtonLayoutSlot.LeftOfGear, result.SettingsSlot);
        Assert.Equal(1100f, result.CollectionBounds.CenterX);
        Assert.Equal(188f, result.CollectionBounds.CenterY);
        Assert.Equal(1022f, result.SettingsBounds.CenterX);
        Assert.Equal(100f, result.SettingsBounds.CenterY);
        Assert.Equal(Gear.MaxY + 18f, result.CollectionBounds.MinY);
        Assert.Equal(Gear.MinX - 18f, result.SettingsBounds.MaxX);
        Assert.True(Viewport.Contains(result.CollectionBounds));
        Assert.True(Viewport.Contains(result.SettingsBounds));
        Assert.False(result.SettingsBounds.Overlaps(result.CollectionBounds));
        Assert.False(result.WasAdjusted);
        Assert.Null(result.BlockerName);
    }

    [Fact]
    public void Resolve_reports_measurement_unavailable_for_zero_sized_visual_footprint()
    {
        var result = BppDockButtonLayoutPlanner.Resolve(
            Viewport,
            Gear,
            collectionWidth: 0f,
            collectionHeight: 60f,
            settingsWidth: 40f,
            settingsHeight: 40f,
            gap: 18f,
            blockers: Array.Empty<BppDockButtonObstacle>()
        );

        Assert.False(result.CanApply);
        Assert.Equal(BppDockButtonLayoutFailureReason.MeasurementUnavailable, result.FailureReason);
    }

    [Fact]
    public void Resolve_places_store_settings_above_book_when_info_blocks_left()
    {
        var infoBounds = BppDockButtonBounds.FromCenter(1022f, 100f, 40f, 40f);

        var result = BppDockButtonLayoutPlanner.Resolve(
            Viewport,
            Gear,
            collectionWidth: 60f,
            collectionHeight: 60f,
            settingsWidth: 40f,
            settingsHeight: 40f,
            gap: 18f,
            blockers: [new BppDockButtonObstacle("InfoButton", infoBounds, isActive: true)]
        );

        Assert.True(result.CanApply);
        Assert.Equal(BppDockButtonLayoutSlot.AboveCollection, result.SettingsSlot);
        Assert.Equal(1100f, result.CollectionBounds.CenterX);
        Assert.Equal(188f, result.CollectionBounds.CenterY);
        Assert.Equal(1100f, result.SettingsBounds.CenterX);
        Assert.Equal(256f, result.SettingsBounds.CenterY);
        Assert.Equal(result.CollectionBounds.MaxY + 18f, result.SettingsBounds.MinY);
        Assert.True(result.WasAdjusted);
        Assert.Equal("InfoButton", result.BlockerName);
        Assert.False(result.SettingsBounds.Overlaps(infoBounds));
        Assert.False(result.SettingsBounds.Overlaps(result.CollectionBounds));
    }

    [Fact]
    public void Resolve_rejects_layout_when_native_button_occupies_collection_slot()
    {
        var collectionBounds = BppDockButtonBounds.FromCenter(1100f, 188f, 60f, 60f);

        var result = BppDockButtonLayoutPlanner.Resolve(
            Viewport,
            Gear,
            collectionWidth: 60f,
            collectionHeight: 60f,
            settingsWidth: 40f,
            settingsHeight: 40f,
            gap: 18f,
            blockers:
            [
                new BppDockButtonObstacle("NativeUpperButton", collectionBounds, isActive: true),
            ]
        );

        Assert.False(result.CanApply);
        Assert.Equal(BppDockButtonLayoutFailureReason.CollectionBlocked, result.FailureReason);
        Assert.Equal("NativeUpperButton", result.BlockerName);
        Assert.True(result.CollectionBounds.Overlaps(collectionBounds));
    }

    [Fact]
    public void Resolve_continues_upward_when_first_stacked_slot_is_occupied()
    {
        var infoBounds = BppDockButtonBounds.FromCenter(1022f, 100f, 40f, 40f);
        var firstStackedBounds = BppDockButtonBounds.FromCenter(1100f, 256f, 40f, 40f);

        var result = BppDockButtonLayoutPlanner.Resolve(
            Viewport,
            Gear,
            collectionWidth: 60f,
            collectionHeight: 60f,
            settingsWidth: 40f,
            settingsHeight: 40f,
            gap: 18f,
            blockers:
            [
                new BppDockButtonObstacle("InfoButton", infoBounds, isActive: true),
                new BppDockButtonObstacle("UpperButton", firstStackedBounds, isActive: true),
            ]
        );

        Assert.True(result.CanApply);
        Assert.Equal(BppDockButtonLayoutSlot.AboveCollection, result.SettingsSlot);
        Assert.Equal(1100f, result.SettingsBounds.CenterX);
        Assert.Equal(314f, result.SettingsBounds.CenterY);
        Assert.False(result.SettingsBounds.Overlaps(firstStackedBounds));
    }

    [Fact]
    public void Resolve_ignores_inactive_blocker_in_gear_left_slot()
    {
        var infoBounds = BppDockButtonBounds.FromCenter(1022f, 100f, 40f, 40f);

        var result = BppDockButtonLayoutPlanner.Resolve(
            Viewport,
            Gear,
            collectionWidth: 60f,
            collectionHeight: 60f,
            settingsWidth: 40f,
            settingsHeight: 40f,
            gap: 18f,
            blockers: [new BppDockButtonObstacle("HiddenInfo", infoBounds, isActive: false)]
        );

        Assert.True(result.CanApply);
        Assert.Equal(BppDockButtonLayoutSlot.LeftOfGear, result.SettingsSlot);
        Assert.False(result.WasAdjusted);
    }

    [Fact]
    public void Resolve_reports_no_layout_instead_of_returning_overlapping_desired_position()
    {
        var shortViewport = new BppDockButtonBounds(0f, 1200f, 0f, 280f);
        var blockers = new List<BppDockButtonObstacle>
        {
            new(
                "InfoButton",
                BppDockButtonBounds.FromCenter(1022f, 100f, 40f, 40f),
                isActive: true
            ),
        };
        for (var index = 0; index < 6; index++)
        {
            blockers.Add(
                new BppDockButtonObstacle(
                    $"UpperButton{index}",
                    BppDockButtonBounds.FromCenter(1100f, 256f + index * 58f, 40f, 40f),
                    isActive: true
                )
            );
        }

        var result = BppDockButtonLayoutPlanner.Resolve(
            shortViewport,
            Gear,
            collectionWidth: 60f,
            collectionHeight: 60f,
            settingsWidth: 40f,
            settingsHeight: 40f,
            gap: 18f,
            blockers
        );

        Assert.False(result.CanApply);
        Assert.False(result.WasAdjusted);
        Assert.Equal(BppDockButtonLayoutFailureReason.NoSettingsSlot, result.FailureReason);
        Assert.Equal("InfoButton", result.BlockerName);
        Assert.True(result.SettingsBounds.Overlaps(blockers[0].Bounds));
    }
}
