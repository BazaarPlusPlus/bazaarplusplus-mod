#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayRecord
{
    public string ReplayId { get; set; } = string.Empty;

    public DateTimeOffset SavedAtUtc { get; set; }

    public string? RunId { get; set; }

    public string? CombatKind { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? EncounterId { get; set; }

    public string? PlayerName { get; set; }

    public string? PlayerAccountId { get; set; }

    public string? OpponentName { get; set; }

    public string? OpponentAccountId { get; set; }

    public string? Result { get; set; }

    public string? WinnerCombatantId { get; set; }

    public string? LoserCombatantId { get; set; }

    public List<CombatReplayCardSnapshot> PlayerHandCards { get; set; } = new();

    public List<CombatReplayCardSnapshot> PlayerSkills { get; set; } = new();

    public List<CombatReplayCardSnapshot> OpponentHandCards { get; set; } = new();

    public List<CombatReplayCardSnapshot> OpponentSkills { get; set; } = new();

    public string SpawnMessageBase64 { get; set; } = string.Empty;

    public string CombatMessageBase64 { get; set; } = string.Empty;

    public string DespawnMessageBase64 { get; set; } = string.Empty;
}
