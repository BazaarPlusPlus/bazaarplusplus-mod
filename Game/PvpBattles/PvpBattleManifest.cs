#nullable enable
using System;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpBattleManifest
{
    public string BattleId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    [JsonProperty("recorded_at_utc")]
    public DateTimeOffset SavedAtUtc { get; set; }

    public string? CombatKind { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? EncounterId { get; set; }

    public PvpBattleParticipants Participants { get; set; } = new();

    public PvpBattleOutcome Outcome { get; set; } = new();

    public PvpBattleSnapshots Snapshots { get; set; } = new();
}
