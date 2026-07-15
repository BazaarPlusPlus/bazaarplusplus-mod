#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace BazaarPlusPlus.Game.SteamTimeline;

internal static class SteamTimelinePhaseId
{
    internal static string? FromRunId(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return null;

        using var sha256 = SHA256.Create();
        var digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(runId.Trim()));
        var builder = new StringBuilder(4 + 32);
        builder.Append("bpp-");
        for (var index = 0; index < 16; index++)
            builder.Append(digest[index].ToString("x2"));
        return builder.ToString();
    }
}
