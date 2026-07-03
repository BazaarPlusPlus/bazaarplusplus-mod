#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceLinesDocument
{
    internal const int SupportedSchemaVersion = 1;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonProperty("contentHash")]
    public string? ContentHash { get; set; }

    [JsonProperty("generatedAt")]
    public string? GeneratedAt { get; set; }

    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("lines")]
    public VoiceLineEntry[]? Lines { get; set; }

    internal static VoiceLine[] Parse(string json, string sourceName)
    {
        var document =
            JsonConvert.DeserializeObject<VoiceLinesDocument>(json)
            ?? throw new InvalidOperationException($"Voice line JSON '{sourceName}' is empty.");
        if (document.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidOperationException(
                $"Voice line JSON '{sourceName}' has unsupported schemaVersion {document.SchemaVersion}."
            );

        var entries = document.Lines ?? Array.Empty<VoiceLineEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<VoiceLine>(entries.Length);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var stem = entry.Stem?.Trim();
            if (string.IsNullOrEmpty(stem))
            {
                VoiceSubtitlesLog.Warn(
                    $"Skipping voice line JSON row {i + 1} from {sourceName}: missing stem."
                );
                continue;
            }

            if (!seen.Add(stem))
            {
                VoiceSubtitlesLog.Warn(
                    $"Skipping voice line JSON row {i + 1} from {sourceName}: duplicate stem {stem}."
                );
                continue;
            }

            var english = entry.English ?? string.Empty;
            var chinese = entry.Chinese ?? string.Empty;
            if (string.IsNullOrWhiteSpace(english) && string.IsNullOrWhiteSpace(chinese))
            {
                VoiceSubtitlesLog.Warn(
                    $"Skipping voice line JSON row {i + 1} from {sourceName}: empty subtitle text."
                );
                continue;
            }

            lines.Add(
                new VoiceLine(stem, english, chinese, Math.Max(0f, (float)entry.DurationSeconds))
            );
        }

        var result = lines.ToArray();
        if (document.Count != result.Length)
            throw new InvalidOperationException(
                $"Voice line JSON '{sourceName}' count mismatch: count={document.Count} lines={result.Length}."
            );

        var actualContentHash = ComputeContentHash(result);
        if (
            string.IsNullOrWhiteSpace(document.ContentHash)
            || !string.Equals(
                document.ContentHash,
                actualContentHash,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidOperationException(
                $"Voice line JSON '{sourceName}' contentHash mismatch: "
                    + $"expected={document.ContentHash ?? "<missing>"} actual={actualContentHash}."
            );
        }

        return result;
    }

    internal static string ComputeContentHash(IReadOnlyList<VoiceLine> lines)
    {
        var canonical = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                canonical.Append('\x1e');

            var line = lines[i];
            var centis = (long)Math.Round(line.DurationSeconds * 100.0);
            canonical
                .Append(line.Stem)
                .Append('\x1f')
                .Append(line.English)
                .Append('\x1f')
                .Append(line.Chinese)
                .Append('\x1f')
                .Append(centis.ToString(CultureInfo.InvariantCulture));
        }

        using var sha256 = SHA256.Create();
        var bytes = new UTF8Encoding(false).GetBytes(canonical.ToString());
        var hash = sha256.ComputeHash(bytes);
        var hex = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
            hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));

        return "sha256:" + hex;
    }

    internal sealed class VoiceLineEntry
    {
        [JsonProperty("stem")]
        public string? Stem { get; set; }

        [JsonProperty("english")]
        public string? English { get; set; }

        [JsonProperty("chinese")]
        public string? Chinese { get; set; }

        [JsonProperty("durationSeconds")]
        public double DurationSeconds { get; set; }
    }
}
