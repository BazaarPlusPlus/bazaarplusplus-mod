using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppDockButtonAvoidanceTests
{
    private static readonly BppDockButtonBounds ParentBounds = new(-200f, 200f, -200f, 200f);

    [Fact]
    public void Resolve_skips_occupied_first_candidate_and_uses_second_candidate()
    {
        var desired = Position(-100f, 0f);
        var firstCandidate = Position(0f, 80f);
        var secondCandidate = Position(0f, 150f);
        var blockers = new[]
        {
            ActiveBlocker("left-native", desired),
            ActiveBlocker("first-stack-native", firstCandidate),
        };

        var result = BppDockButtonAvoidanceSolver.Resolve(
            ParentBounds,
            dockWidth: 40f,
            dockHeight: 40f,
            desired,
            new[] { firstCandidate, secondCandidate },
            blockers
        );

        Assert.True(result.CanApply);
        Assert.True(result.WasAdjusted);
        Assert.Equal("left-native", result.BlockerName);
        Assert.Equal(secondCandidate.X, result.Position.X);
        Assert.Equal(secondCandidate.Y, result.Position.Y);
    }

    [Fact]
    public void Resolve_rejects_out_of_bounds_candidate()
    {
        var desired = Position(-100f, 0f);
        var outOfBounds = Position(0f, 190f);
        var valid = Position(0f, 150f);

        var result = BppDockButtonAvoidanceSolver.Resolve(
            ParentBounds,
            dockWidth: 40f,
            dockHeight: 40f,
            desired,
            new[] { outOfBounds, valid },
            new[] { ActiveBlocker("left-native", desired) }
        );

        Assert.True(result.WasAdjusted);
        Assert.Equal(valid.Y, result.Position.Y);
        Assert.NotEqual(outOfBounds.Y, result.Position.Y);
    }

    [Fact]
    public void Resolve_ignores_inactive_blocker()
    {
        var desired = Position(-100f, 0f);
        var inactive = new BppDockButtonObstacle(
            "inactive-native",
            BppDockButtonBounds.FromCenter(desired.X, desired.Y, 40f, 40f),
            isActive: false
        );

        var result = BppDockButtonAvoidanceSolver.Resolve(
            ParentBounds,
            dockWidth: 40f,
            dockHeight: 40f,
            desired,
            new[] { Position(0f, 80f) },
            new[] { inactive }
        );

        Assert.True(result.CanApply);
        Assert.False(result.WasAdjusted);
        Assert.Equal(desired.X, result.Position.X);
        Assert.Equal(desired.Y, result.Position.Y);
    }

    [Fact]
    public void Stacked_candidate_uses_scene_resolved_placement()
    {
        var original = BppSettingsDockPlacement
            .LeftOfSettingButton("MainMenu", BppDockButtonIconKind.SettingsDock)
            .WithRightDockStackedPlacement(
                BppSettingsDockSide.AboveAnchor,
                BppSettingsDockPanelDirection.UpLeft,
                siblingStepCount: 2
            );
        var resolved = original.ResolveForScene(BppSettingsDockSceneKind.RightDockStacked);

        var candidate = BppDockButtonAvoidance.CalculateStackedCandidatePosition(
            anchorCenterLocalX: 100f,
            anchorCenterLocalY: 40f,
            anchorTopLocalY: 70f,
            anchorBottomLocalY: 10f,
            currentLocalZ: 7f,
            finalPlacement: resolved,
            candidateOffset: 0
        );

        Assert.Equal(BppSettingsDockSide.AboveAnchor, resolved.Side);
        Assert.Equal(100f, candidate.X);
        Assert.Equal(274f, candidate.Y);
        Assert.Equal(7f, candidate.Z);
    }

    [Fact]
    public void Delayed_probe_rechecks_after_blocker_appears()
    {
        var tracker = new BppDockLayoutSyncTracker(immediateSyncFrameCount: 1);
        var desired = Position(-100f, 0f);
        var fallback = Position(0f, 80f);

        Assert.True(
            tracker.ShouldSync(
                sceneHandle: 10,
                BppSettingsDockSceneKind.RightDockStacked,
                realtimeSeconds: 0f
            )
        );
        var beforeBlocker = BppDockButtonAvoidanceSolver.Resolve(
            ParentBounds,
            40f,
            40f,
            desired,
            new[] { fallback },
            Array.Empty<BppDockButtonObstacle>()
        );
        Assert.False(beforeBlocker.WasAdjusted);

        Assert.False(
            tracker.ShouldSync(
                sceneHandle: 10,
                BppSettingsDockSceneKind.RightDockStacked,
                realtimeSeconds: 0.1f
            )
        );
        Assert.True(
            tracker.ShouldSync(
                sceneHandle: 10,
                BppSettingsDockSceneKind.RightDockStacked,
                realtimeSeconds: 0.25f
            )
        );
        var afterBlocker = BppDockButtonAvoidanceSolver.Resolve(
            ParentBounds,
            40f,
            40f,
            desired,
            new[] { fallback },
            new[] { ActiveBlocker("late-native", desired) }
        );

        Assert.True(afterBlocker.WasAdjusted);
        Assert.Equal(fallback.Y, afterBlocker.Position.Y);
    }

    [Fact]
    public void Scene_handle_change_restarts_sync_even_when_scene_kind_is_unchanged()
    {
        var tracker = new BppDockLayoutSyncTracker(immediateSyncFrameCount: 1);

        Assert.True(tracker.ShouldSync(1, BppSettingsDockSceneKind.Default, realtimeSeconds: 0f));
        Assert.False(
            tracker.ShouldSync(1, BppSettingsDockSceneKind.Default, realtimeSeconds: 0.1f)
        );
        Assert.True(tracker.ShouldSync(2, BppSettingsDockSceneKind.Default, realtimeSeconds: 0.1f));
    }

    [Fact]
    public void Steady_state_probe_continues_after_delayed_layout_window()
    {
        var tracker = new BppDockLayoutSyncTracker(immediateSyncFrameCount: 1);
        var desired = Position(-100f, 0f);
        var fallback = Position(0f, 80f);

        Assert.True(tracker.ShouldSync(10, BppSettingsDockSceneKind.RightDockStacked, 0f));
        Assert.True(tracker.ShouldSync(10, BppSettingsDockSceneKind.RightDockStacked, 8f));
        Assert.False(tracker.ShouldSync(10, BppSettingsDockSceneKind.RightDockStacked, 9f));
        Assert.True(tracker.ShouldSync(10, BppSettingsDockSceneKind.RightDockStacked, 10f));
        var afterLateBlocker = BppDockButtonAvoidanceSolver.Resolve(
            ParentBounds,
            40f,
            40f,
            desired,
            new[] { fallback },
            new[] { ActiveBlocker("ten-second-native", desired) }
        );
        Assert.True(afterLateBlocker.WasAdjusted);
        Assert.Equal(fallback.Y, afterLateBlocker.Position.Y);
        Assert.False(tracker.ShouldSync(10, BppSettingsDockSceneKind.RightDockStacked, 11f));
        Assert.True(tracker.ShouldSync(10, BppSettingsDockSceneKind.RightDockStacked, 12f));
    }

    // The main-menu left slot is valid even though it extends outside the native button container.
    [Fact]
    public void Resolve_reports_desired_and_no_apply_when_desired_and_candidates_are_out_of_bounds()
    {
        var smallParent = new BppDockButtonBounds(-100f, 100f, -100f, 100f);
        var desired = Position(-378f, 0f); // left of the anchor, well outside the small container
        var stackedAbove = Position(0f, 388f); // every stacked candidate is also out of bounds

        var result = BppDockButtonAvoidanceSolver.Resolve(
            smallParent,
            dockWidth: 170f,
            dockHeight: 170f,
            desired,
            new[] { stackedAbove },
            Array.Empty<BppDockButtonObstacle>()
        );

        Assert.False(result.CanApply);
        Assert.False(result.WasAdjusted);
        Assert.Null(result.BlockerName);
        Assert.Equal(desired.X, result.Position.X);
        Assert.Equal(desired.Y, result.Position.Y);
    }

    private static BppSettingsDockLocalPosition Position(float x, float y) => new(x, y, 0f);

    private static BppDockButtonObstacle ActiveBlocker(
        string name,
        BppSettingsDockLocalPosition position
    ) =>
        new(name, BppDockButtonBounds.FromCenter(position.X, position.Y, 40f, 40f), isActive: true);
}
