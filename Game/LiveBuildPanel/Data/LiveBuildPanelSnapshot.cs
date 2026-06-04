#nullable enable

using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

internal sealed class LiveBuildPanelSnapshot
{
    public EHero? Hero { get; init; }

    public BppItemBoard FinalBuild { get; init; } = BppItemBoard.Empty;

    public BppItemBoard Shop { get; init; } = BppItemBoard.Empty;

    public BppItemBoard Board { get; init; } = BppItemBoard.Empty;

    public BppItemBoard Stash { get; init; } = BppItemBoard.Empty;

    public IReadOnlyCollection<Guid> CandidateTemplateIds { get; init; } = Array.Empty<Guid>();

    public string RecommendationStatus { get; init; } = string.Empty;

    public int RecommendationIndex { get; init; }

    public int RecommendationCount { get; init; }

    public IReadOnlyList<BPPSupporterSample> Supporters { get; init; } =
        Array.Empty<BPPSupporterSample>();

    public LiveItemBoardRowVm[] Rows =>
        [
            new LiveItemBoardRowVm(
                FinalBuild,
                LiveBuildPanelText.FinalBuildRow(),
                LiveBuildPanelText.NoRecommendation()
            ),
            new LiveItemBoardRowVm(
                Shop,
                LiveBuildPanelText.ShopRow(),
                LiveBuildPanelText.EmptyShop()
            ),
            new LiveItemBoardRowVm(
                Board,
                LiveBuildPanelText.BoardRow(),
                LiveBuildPanelText.EmptyBoard()
            ),
            new LiveItemBoardRowVm(
                Stash,
                LiveBuildPanelText.StashRow(),
                LiveBuildPanelText.EmptyStash()
            ),
        ];
}
