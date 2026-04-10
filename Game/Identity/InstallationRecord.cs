#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Identity;

internal sealed class InstallationRecord
{
    [JsonProperty("installation_id")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonProperty("player_account_id")]
    public string PlayerAccountId { get; set; } = string.Empty;

    [JsonProperty("api_base_url")]
    public string ApiBaseUrl { get; set; } = string.Empty;

    [JsonProperty("public_key")]
    public InstallationPublicKeyRecord PublicKey { get; set; } = new();

    [JsonProperty("status")]
    public string Status { get; set; } = "active";

    [JsonProperty("created_at_utc")]
    public string CreatedAtUtc { get; set; } = string.Empty;
}

internal sealed class InstallationPublicKeyRecord
{
    [JsonProperty("modulus_b64")]
    public string ModulusBase64 { get; set; } = string.Empty;

    [JsonProperty("exponent_b64")]
    public string ExponentBase64 { get; set; } = string.Empty;
}
