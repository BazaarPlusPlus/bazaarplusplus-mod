#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Infra.Messages;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplaySequenceCandidate
{
    public string? RunId { get; set; }

    public List<CombatReplayCardSnapshot> PlayerHandCards { get; set; } = new();

    public NetMessageGameSim? SpawnMessage { get; set; }

    public NetMessageCombatSim? CombatMessage { get; set; }
}
