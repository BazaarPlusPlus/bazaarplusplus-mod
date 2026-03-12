using UnityEngine;
using Xunit;

namespace BazaarPlusPlus.Tests.MonsterPreview;

public sealed class AnchorStrategyTests
{
    [Fact]
    public void FixedAnchorStrategy_returns_the_configured_pose()
    {
        var strategy = new FixedAnchorStrategy(new BoardPose
        {
            Position = new Vector3(1f, 2f, 3f),
            Rotation = Quaternion.Euler(0f, 45f, 0f),
        });

        var resolved = strategy.TryResolve(out var pose);

        Assert.True(resolved);
        Assert.NotNull(pose);
        Assert.Equal(new Vector3(1f, 2f, 3f), pose!.Position);
        Assert.Equal(Quaternion.Euler(0f, 45f, 0f), pose.Rotation);
    }

    [Fact]
    public void AdjustableAnchorStrategy_applies_world_offset_and_rotation_adjustment()
    {
        var baseStrategy = new FixedAnchorStrategy(new BoardPose
        {
            Position = new Vector3(4f, 5f, 6f),
            Rotation = Quaternion.Euler(0f, 10f, 0f),
        });
        var strategy = new AdjustableAnchorStrategy(
            baseStrategy,
            new AnchorAdjustment
            {
                WorldOffset = new Vector3(1f, -2f, 3f),
                RotationOffsetEuler = new Vector3(0f, 15f, 0f),
            }
        );

        var resolved = strategy.TryResolve(out var pose);

        Assert.True(resolved);
        Assert.NotNull(pose);
        Assert.Equal(new Vector3(5f, 3f, 9f), pose!.Position);
        Assert.Equal(Quaternion.Euler(0f, 25f, 0f), pose.Rotation);
    }
}
