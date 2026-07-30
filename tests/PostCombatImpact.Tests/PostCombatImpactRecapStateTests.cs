using BazaarPlusPlus.Game.PostCombatImpact;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class PostCombatImpactRecapStateTests
{
    [Fact]
    public void Waits_for_native_recap_to_open_before_showing()
    {
        var state = new PostCombatImpactRecapState();
        state.RecapStarted();

        Assert.Equal(
            PostCombatImpactRecapTransition.None,
            state.Observe(isOpen: false, isMoving: false)
        );
        Assert.Equal(
            PostCombatImpactRecapTransition.Show,
            state.Observe(isOpen: true, isMoving: false)
        );
        Assert.Equal(
            PostCombatImpactRecapTransition.None,
            state.Observe(isOpen: true, isMoving: false)
        );
    }

    [Fact]
    public void Waits_for_native_recap_animation_to_finish_before_showing()
    {
        var state = new PostCombatImpactRecapState();
        state.RecapStarted();

        Assert.Equal(
            PostCombatImpactRecapTransition.None,
            state.Observe(isOpen: true, isMoving: true)
        );
        Assert.Equal(
            PostCombatImpactRecapTransition.Show,
            state.Observe(isOpen: true, isMoving: false)
        );
    }

    [Fact]
    public void Hides_on_native_state_edge_even_before_recap_ended_event()
    {
        var state = new PostCombatImpactRecapState();
        state.RecapStarted();
        state.Observe(isOpen: true, isMoving: false);

        Assert.Equal(
            PostCombatImpactRecapTransition.Hide,
            state.Observe(isOpen: false, isMoving: true)
        );
    }

    [Fact]
    public void Recap_ended_always_hides_and_cancels_pending_open()
    {
        var state = new PostCombatImpactRecapState();
        state.RecapStarted();

        Assert.Equal(PostCombatImpactRecapTransition.Hide, state.RecapEnded());
        Assert.Equal(
            PostCombatImpactRecapTransition.None,
            state.Observe(isOpen: false, isMoving: false)
        );
    }
}
