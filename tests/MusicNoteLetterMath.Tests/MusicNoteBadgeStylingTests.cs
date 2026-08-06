using BazaarPlusPlus.Game.MusicNotes;
using Xunit;

namespace MusicNoteLetterMathTests;

public class MusicNoteBadgeStylingTests
{
    [Fact]
    public void BoostedIsHoverImmune()
    {
        foreach (
            var hoverFit in new[]
            {
                MusicNoteHoverFit.None,
                MusicNoteHoverFit.Fits,
                MusicNoteHoverFit.Misses,
            }
        )
        {
            Assert.Equal(
                MusicNoteBadgeVisual.Boosted,
                MusicNoteBadgeStyling.Resolve(notePlaced: true, isBoosted: true, hoverFit)
            );
        }
    }

    [Fact]
    public void NoHoverFilter_BaseStateFollowsPlacement()
    {
        Assert.Equal(
            MusicNoteBadgeVisual.Plate,
            MusicNoteBadgeStyling.Resolve(true, false, MusicNoteHoverFit.None)
        );
        Assert.Equal(
            MusicNoteBadgeVisual.Ghost,
            MusicNoteBadgeStyling.Resolve(false, false, MusicNoteHoverFit.None)
        );
    }

    [Fact]
    public void HoveredCardFits_ChipHighlights()
    {
        Assert.Equal(
            MusicNoteBadgeVisual.PlateHighlighted,
            MusicNoteBadgeStyling.Resolve(true, false, MusicNoteHoverFit.Fits)
        );
        Assert.Equal(
            MusicNoteBadgeVisual.GhostHighlighted,
            MusicNoteBadgeStyling.Resolve(false, false, MusicNoteHoverFit.Fits)
        );
    }

    [Fact]
    public void HoveredCardMisses_ChipDims()
    {
        Assert.Equal(
            MusicNoteBadgeVisual.PlateDimmed,
            MusicNoteBadgeStyling.Resolve(true, false, MusicNoteHoverFit.Misses)
        );
        Assert.Equal(
            MusicNoteBadgeVisual.GhostDimmed,
            MusicNoteBadgeStyling.Resolve(false, false, MusicNoteHoverFit.Misses)
        );
    }

    [Fact]
    public void BoostWithoutPlacedNote_IsIgnored()
    {
        // A boost flag can only come from a placed note's occupant; without placement the
        // resolver must not invent a boosted plate.
        Assert.Equal(
            MusicNoteBadgeVisual.Ghost,
            MusicNoteBadgeStyling.Resolve(false, true, MusicNoteHoverFit.None)
        );
    }
}
