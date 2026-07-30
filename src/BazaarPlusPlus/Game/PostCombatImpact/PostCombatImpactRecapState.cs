#nullable enable

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal enum PostCombatImpactRecapTransition
{
    None,
    Show,
    Hide,
}

internal sealed class PostCombatImpactRecapState
{
    private bool _awaitingOpen;
    private bool _wasOpen;

    internal void RecapStarted()
    {
        _awaitingOpen = true;
    }

    internal PostCombatImpactRecapTransition RecapEnded()
    {
        _awaitingOpen = false;
        _wasOpen = false;
        return PostCombatImpactRecapTransition.Hide;
    }

    internal PostCombatImpactRecapTransition Observe(bool isOpen, bool isMoving)
    {
        var transition = PostCombatImpactRecapTransition.None;
        if (_awaitingOpen && isOpen && !isMoving)
        {
            _awaitingOpen = false;
            transition = PostCombatImpactRecapTransition.Show;
        }
        else if (_wasOpen && !isOpen)
        {
            transition = PostCombatImpactRecapTransition.Hide;
        }

        _wasOpen = isOpen;
        return transition;
    }
}
