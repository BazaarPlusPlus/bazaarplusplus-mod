#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.PvpBattles;

public sealed class PvpBattleCardSetCapture
{
    public IList<CombatReplayCardSnapshot> Items { get; set; } =
        new List<CombatReplayCardSnapshot>();

    public PvpBattleCaptureStatus Status { get; set; }

    public PvpBattleCaptureSource Source { get; set; }
}
