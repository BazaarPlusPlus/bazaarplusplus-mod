#nullable enable
using System;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayRecord
{
    public string ReplayId { get; set; } = string.Empty;

    public DateTimeOffset SavedAtUtc { get; set; }

    public string? RunId { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? EncounterId { get; set; }

    public string? OpponentName { get; set; }

    public string SpawnMessageBase64 { get; set; } = string.Empty;

    public string CombatMessageBase64 { get; set; } = string.Empty;

    public string DespawnMessageBase64 { get; set; } = string.Empty;
}
