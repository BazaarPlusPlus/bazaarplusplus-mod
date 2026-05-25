#nullable enable
using System;
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Models;

public sealed class BazaarDbScreenshotUploadRequest
{
    [JsonProperty("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonProperty("submitted_at_utc")]
    public string SubmittedAtUtc { get; set; } = string.Empty;

    [JsonProperty("player_account_id")]
    public string PlayerAccountId { get; set; } = string.Empty;

    [JsonProperty("screenshot_id")]
    public string ScreenshotId { get; set; } = string.Empty;

    [JsonProperty("run_id")]
    public string? RunId { get; set; }

    [JsonProperty("hero_name")]
    public string? HeroName { get; set; }

    [JsonProperty("final_days")]
    public int? FinalDays { get; set; }

    [JsonProperty("final_victories")]
    public int? FinalVictories { get; set; }

    [JsonProperty("player_name")]
    public string? PlayerName { get; set; }

    [JsonProperty("player_rank")]
    public string? PlayerRank { get; set; }

    [JsonProperty("player_rating")]
    public int? PlayerRating { get; set; }

    [JsonProperty("player_position")]
    public int? PlayerPosition { get; set; }

    [JsonProperty("captured_at_utc")]
    public string CapturedAtUtc { get; set; } = string.Empty;

    [JsonProperty("image_format")]
    public string ImageFormat { get; set; } = "png";

    [JsonIgnore]
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();

    [JsonProperty("image_bytes_base64")]
    public string ImageBytesBase64
    {
        get => Convert.ToBase64String(ImageBytes);
        set =>
            ImageBytes = string.IsNullOrEmpty(value)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(value);
    }
}
