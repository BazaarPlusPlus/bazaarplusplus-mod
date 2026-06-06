using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.BuildRecommendations;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure.UiTokens;

TestOverlaySortingLayersKeepNativeCardsBetweenPanelAndForeground();
TestCandidateToggleUsesTemplateId();
TestCandidatePruneKeepsSelectableRowsOnly();
TestRowVmTogglePolicyComesFromBoardType();
TestSlotChromeGeometryMatchesTenSlotContract();
TestBuildRatingTierMapping();

Console.WriteLine("LiveBuildPanel checks passed.");

static void TestBuildRatingTierMapping()
{
    Assert(BuildRatingTier.FromRating(null) == "all", "null rating -> all");
    Assert(BuildRatingTier.FromRating(500) == "low", "<=500 -> low");
    Assert(BuildRatingTier.FromRating(501) == "mid", "501 -> mid");
    Assert(BuildRatingTier.FromRating(899) == "mid", "899 -> mid");
    Assert(BuildRatingTier.FromRating(900) == "high", "900 -> high");
}

static void TestOverlaySortingLayersKeepNativeCardsBetweenPanelAndForeground()
{
    Assert(
        BppOverlaySorting.PanelUiToolkit < BppOverlaySorting.NativeCardPreview,
        "Native cards must render above the main UI Toolkit panel."
    );
    Assert(
        BppOverlaySorting.NativeCardPreview < BppOverlaySorting.PanelForeground,
        "Foreground markers must render above native cards."
    );
    Assert(
        BppOverlaySorting.MainOverlayPanelBand == BppOverlaySorting.NativeCardPreview,
        "Panel mutex band should remain a separate semantic constant even when it shares the card overlay value."
    );
}

static void TestCandidateToggleUsesTemplateId()
{
    var state = new LiveBuildCandidateState();
    var templateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    Assert(state.Toggle(templateId), "First toggle should change state.");
    Assert(state.Contains(templateId), "Template id should be selected after first toggle.");
    Assert(state.Toggle(templateId), "Second toggle should change state.");
    Assert(!state.Contains(templateId), "Template id should be removed after second toggle.");
}

static void TestCandidatePruneKeepsSelectableRowsOnly()
{
    var state = new LiveBuildCandidateState();
    var selectableTemplate = Guid.Parse("22222222-2222-2222-2222-222222222222");
    var referenceTemplate = Guid.Parse("33333333-3333-3333-3333-333333333333");
    state.Toggle(selectableTemplate);
    state.Toggle(referenceTemplate);

    state.PruneToSelectableRows([
        Board(BppItemBoardId.LiveBoard, BppItemBoardType.SelectableContainer, selectableTemplate),
        Board(BppItemBoardId.FinalBuild, BppItemBoardType.Reference, referenceTemplate),
    ]);

    Assert(state.Contains(selectableTemplate), "Selectable row candidate should survive prune.");
    Assert(!state.Contains(referenceTemplate), "Reference row candidate should be pruned.");
}

static void TestRowVmTogglePolicyComesFromBoardType()
{
    Assert(
        new LiveItemBoardRowVm(
            Board(BppItemBoardId.FinalBuild, BppItemBoardType.Reference, Guid.NewGuid()),
            "final",
            "empty"
        ).CanToggleCandidates == false,
        "Final build row should not expose candidate hit targets."
    );
    Assert(
        new LiveItemBoardRowVm(
            Board(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop, Guid.NewGuid()),
            "shop",
            "empty"
        ).CanToggleCandidates,
        "Shop row should expose candidate hit targets."
    );
}

static void TestSlotChromeGeometryMatchesTenSlotContract()
{
    var hitPercent = ItemBoardSlotGridGeometry.ResolveOccupiedRect(100f, 100f, 3, 2, 0f, 0f);
    var markerPixels = ItemBoardSlotGridGeometry.ResolveOccupiedRect(1000f, 180f, 3, 2, 0f, 4f);

    Assert(hitPercent.X == 30f, "Hit target should start at the socket's 10-slot percent.");
    Assert(hitPercent.Width == 20f, "Hit target should span the card's display slots.");
    Assert(markerPixels.X == 300f, "Marker should use the same left socket in pixels.");
    Assert(markerPixels.Width == 200f, "Marker should use the same occupied span in pixels.");
    Assert(markerPixels.Y == 4f, "Marker should apply only its vertical chrome inset.");
    Assert(markerPixels.Height == 172f, "Marker height should preserve the vertical chrome inset.");
}

static BppItemBoard Board(BppItemBoardId id, BppItemBoardType type, Guid templateId) =>
    new(id, type, [new BppItemBoardCard { TemplateId = templateId, Size = ECardSize.Small }]);

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
