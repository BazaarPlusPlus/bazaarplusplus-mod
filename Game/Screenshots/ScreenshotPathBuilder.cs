#nullable enable
using System;
using System.IO;
using System.Text;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class ScreenshotPathBuilder
{
    public static string BuildRelativePath(string? runId, DateTimeOffset capturedAtLocal)
    {
        var dayFolder = capturedAtLocal.ToString("yyyy-MM-dd");
        var fileName = $"{SanitizeRunId(runId)}-{capturedAtLocal:HHmmssfff}.png";
        return Path.Combine(dayFolder, fileName);
    }

    private static string SanitizeRunId(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return "anonymous";

        var builder = new StringBuilder(runId.Length);
        foreach (var character in runId)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (character is '-' or '_')
                builder.Append(character);
        }

        return builder.Length == 0 ? "anonymous" : builder.ToString();
    }
}
