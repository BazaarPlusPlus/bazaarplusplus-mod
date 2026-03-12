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
            Presentation = presentation ?? MonsterPreviewDefaults.CreateShowcasePresentation(),
            Debug = debug ?? new PreviewBoardDebugOptions(),
        };
    }
}
