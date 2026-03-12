#pragma warning disable CS0436
using System.Collections.Generic;

namespace BazaarPlusPlus;

internal static class PreviewBoardRequestFactory
{
    public static PreviewBoardRequest CreateFixed(
        IReadOnlyList<PreviewCardSpec> itemCards,
        IReadOnlyList<PreviewCardSpec> skillCards,
        BoardPose pose,
        string title = "",
        IReadOnlyDictionary<string, string> metadata = null,
        PreviewBoardPresentation presentation = null,
        PreviewBoardDebugOptions debug = null
    )
    {
        var dataSource = new InMemoryPreviewDataSource();
        dataSource.SetCards(itemCards, skillCards);
        dataSource.SetMetadata(title, metadata);

        return new PreviewBoardRequest
        {
            DataSource = dataSource,
            AnchorStrategy = new FixedAnchorStrategy(pose),
            Presentation = presentation ?? CreateShowcasePresentation(),
            Debug = debug ?? new PreviewBoardDebugOptions(),
        };
    }

    public static PreviewBoardPresentation CreateShowcasePresentation()
    {
        return new PreviewBoardPresentation
        {
            Visible = true,
            LocalOffset = new UnityEngine.Vector3(0f, 0.1f, 0f),
            CardSpacing = new UnityEngine.Vector3(1.1f, 0f, 0f),
            CardScale = UnityEngine.Vector3.one * 0.5f,
            BoardSize = new UnityEngine.Vector2(8.25f, 2.75f),
            BoardThickness = 0.02f,
            BorderThickness = 0.04f,
            BorderHeight = 0.04f,
        };
    }
}
