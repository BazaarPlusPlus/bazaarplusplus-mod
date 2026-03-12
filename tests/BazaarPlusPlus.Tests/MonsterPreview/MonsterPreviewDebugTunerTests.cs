using UnityEngine;
using Xunit;

namespace BazaarPlusPlus.Tests.MonsterPreview;

public sealed class MonsterPreviewDebugTunerTests
{
    [Fact]
    public void Adjustments_update_anchor_rotation_and_layout_values()
    {
        var anchor = new FixedAnchorStrategy();
        var presentation = new PreviewBoardPresentation();
        var tuner = new MonsterPreviewDebugTuner(anchor, presentation);

        tuner.MoveAnchor(new Vector3(1f, 2f, 3f));
        tuner.RotateAnchorY(15f);
        tuner.AdjustBoardWidth(-10f);
        tuner.AdjustBoardHeight(1.5f);
        tuner.AdjustSpacingX(-10f);
        tuner.AdjustCardScale(-10f);
        tuner.AdjustBoardThickness(-10f);
        tuner.AdjustBorderThickness(-10f);
        tuner.AdjustBorderHeight(-10f);

        Assert.Equal(new Vector3(1f, 2f, 3f), anchor.Position);
        AssertQuaternionEquivalent(Quaternion.Euler(0f, 15f, 0f), anchor.Rotation);
        Assert.Equal(1f, presentation.BoardSize.x, 3);
        Assert.Equal(4.25f, presentation.BoardSize.y, 3);
        Assert.Equal(0.2f, presentation.CardSpacing.x, 3);
        Assert.Equal(0.1f, presentation.CardScale.x, 3);
        Assert.Equal(0.01f, presentation.BoardThickness, 3);
        Assert.Equal(0.01f, presentation.BorderThickness, 3);
        Assert.Equal(0.01f, presentation.BorderHeight, 3);
    }

    [Fact]
    public void ResetAnchor_restores_seed_pose()
    {
        var anchor = new FixedAnchorStrategy();
        var presentation = new PreviewBoardPresentation();
        var tuner = new MonsterPreviewDebugTuner(anchor, presentation);
        var seed = new BoardPose
        {
            Position = new Vector3(5f, 6f, 7f),
            Rotation = Quaternion.Euler(0f, 45f, 0f),
        };

        tuner.MoveAnchor(new Vector3(1f, 0f, 0f));
        tuner.ResetAnchor(seed);

        Assert.Equal(seed.Position, anchor.Position);
        AssertQuaternionEquivalent(seed.Rotation, anchor.Rotation);
    }

    private static void AssertQuaternionEquivalent(Quaternion expected, Quaternion actual)
    {
        Assert.Equal(expected.x, actual.x, 3);
        Assert.Equal(expected.y, actual.y, 3);
        Assert.Equal(expected.z, actual.z, 3);
        Assert.Equal(expected.w, actual.w, 3);
    }
}
