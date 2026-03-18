#nullable enable
using BazaarGameShared.Infra.Messages;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplaySequenceCandidate
{
    public string? RunId { get; set; }

    public NetMessageGameSim? SpawnMessage { get; set; }

    public NetMessageCombatSim? CombatMessage { get; set; }
}
