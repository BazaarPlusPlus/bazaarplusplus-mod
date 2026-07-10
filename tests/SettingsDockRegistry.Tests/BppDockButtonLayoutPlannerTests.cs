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
        Assert.Equal("InfoButton", result.BlockerName);
        Assert.True(result.SettingsBounds.Overlaps(blockers[0].Bounds));
    }
}
