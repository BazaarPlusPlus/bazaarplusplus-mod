#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Identity;

internal sealed class PlayerObservationRecord
{
    [JsonProperty("player_account_id")]
    public string PlayerAccountId { get; set; } = string.Empty;

    [JsonProperty("player_username")]
    public string PlayerUsername { get; set; } = string.Empty;

    [JsonProperty("observed_at_utc")]
    public string ObservedAtUtc { get; set; } = string.Empty;

    [JsonProperty("installation_hint")]
    public string? InstallationHint { get; set; }
}
