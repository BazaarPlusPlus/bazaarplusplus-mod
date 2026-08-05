#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay;

internal static class CurrentReplayRecordingTooltipPadding
{
    internal static (int Top, int Bottom) Balance(int top, int bottom)
    {
        var total = Math.Max(0, top) + Math.Max(0, bottom);
        var balancedTop = total / 2;
        return (balancedTop, total - balancedTop);
    }
}
