#pragma warning disable CS0436
using UnityEngine;

namespace BazaarPlusPlus;

internal static class MonsterPreviewDefaults
{
    public static readonly BoardPose DefaultAnchorPose = new BoardPose
    {
        Position = new Vector3(4f, 1f, -5f),
        Rotation = Quaternion.identity,
    };

    public static PreviewBoardPresentation CreateDebugPresentation()
    {
        return CreateShowcasePresentation();
    }

    public static PreviewBoardPresentation CreateShowcasePresentation()
    {
        return new PreviewBoardPresentation
        {
            Visible = true,
            LocalOffset = new Vector3(0f, 0.1f, 0f),
            CardSpacing = new Vector3(1.1f, 0f, 0f),
            CardScale = Vector3.one * 0.5f,
            BoardSize = new Vector2(8.25f, 2.75f),
            BoardThickness = 0.02f,
            BorderThickness = 0.04f,
            BorderHeight = 0.04f,
        };
    }
}
