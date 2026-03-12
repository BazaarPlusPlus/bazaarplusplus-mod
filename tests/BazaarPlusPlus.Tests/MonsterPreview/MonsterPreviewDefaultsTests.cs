using UnityEngine;
using Xunit;

namespace BazaarPlusPlus.Tests.MonsterPreview;

public sealed class MonsterPreviewDefaultsTests
{
    [Fact]
    public void Default_anchor_pose_matches_fixed_preview_position()
    {
        Assert.Equal(new Vector3(4f, 1f, -5f), MonsterPreviewDefaults.DefaultAnchorPose.Position);
        Assert.Equal(Quaternion.identity, MonsterPreviewDefaults.DefaultAnchorPose.Rotation);
    }

    [Fact]
    public void Create_showcase_presentation_returns_expected_defaults()
    {
        var presentation = MonsterPreviewDefaults.CreateShowcasePresentation();

        Assert.Equal(new Vector3(0f, 0.1f, 0f), presentation.LocalOffset);
        Assert.Equal(0.5f, presentation.CardScale.x, 3);
        Assert.Equal(new Vector2(8.25f, 2.75f), presentation.BoardSize);
        Assert.Equal(0.02f, presentation.BoardThickness, 3);
        Assert.Equal(0.04f, presentation.BorderThickness, 3);
        Assert.Equal(0.04f, presentation.BorderHeight, 3);
    }
}
