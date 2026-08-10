using BazaarPlusPlus.Game.MusicNotes;
using Xunit;

namespace MusicNoteLetterMathTests;

public sealed class MusicNoteOverlayVisibilityPolicyTests
{
    [Fact]
    public void Active_overlay_is_visible_on_an_available_board() =>
        Assert.True(
            MusicNoteOverlayVisibilityPolicy.ShouldShow(
                activationActive: true,
                hasPlayer: true,
                isInCombat: false,
                isReplay: false,
                isRecapOpen: false,
                isNewDayTransitionActive: false
            )
        );

    [Theory]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, true, true, false, false, false)]
    [InlineData(true, true, false, true, false, false)]
    [InlineData(true, true, false, false, true, false)]
    [InlineData(true, true, false, false, false, true)]
    public void Overlay_is_hidden_when_activation_or_board_context_is_unavailable(
        bool activationActive,
        bool hasPlayer,
        bool isInCombat,
        bool isReplay,
        bool isRecapOpen,
        bool isNewDayTransitionActive
    ) =>
        Assert.False(
            MusicNoteOverlayVisibilityPolicy.ShouldShow(
                activationActive,
                hasPlayer,
                isInCombat,
                isReplay,
                isRecapOpen,
                isNewDayTransitionActive
            )
        );

    [Fact]
    public void New_day_transition_suspends_but_does_not_reset_toggle_visibility()
    {
        Assert.False(
            MusicNoteOverlayVisibilityPolicy.ShouldShow(
                activationActive: true,
                hasPlayer: true,
                isInCombat: false,
                isReplay: false,
                isRecapOpen: false,
                isNewDayTransitionActive: true
            )
        );
        Assert.True(
            MusicNoteOverlayVisibilityPolicy.ShouldShow(
                activationActive: true,
                hasPlayer: true,
                isInCombat: false,
                isReplay: false,
                isRecapOpen: false,
                isNewDayTransitionActive: false
            )
        );
    }
}
