#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Infra.Messages;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplaySequenceCandidate
{
    public string? RunId { get; set; }

    public string? OpponentName { get; set; }

    public string? OpponentAccountId { get; set; }

    public bool PlayerHandCardsCapturedFromOpening { get; set; }

    public List<CombatReplayCardSnapshot> PlayerHandCards { get; set; } = new();

    public bool PlayerSkillsCapturedFromOpening { get; set; }

    public List<CombatReplayCardSnapshot> PlayerSkills { get; set; } = new();

    public bool OpponentHandCardsCapturedFromOpening { get; set; }

    public List<CombatReplayCardSnapshot> OpponentHandCards { get; set; } = new();

    public bool OpponentSkillsCapturedFromOpening { get; set; }

    public List<CombatReplayCardSnapshot> OpponentSkills { get; set; } = new();

    public NetMessageGameSim? SpawnMessage { get; set; }

    public NetMessageCombatSim? CombatMessage { get; set; }
}
