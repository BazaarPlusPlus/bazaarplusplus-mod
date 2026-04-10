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
        var fileName =
            $"{capturedAtLocal:yyyy-MM-dd_HH-mm-ss-fff}_capture_run-{SanitizeRunId(runId)}.png";
        return Path.Combine(dayFolder, fileName);
    }

    public static string BuildRelativePath(
        string? runId,
        DateTimeOffset capturedAtLocal,
        RunScreenshotCaptureSource captureSource,
        string screenshotId,
        string? battleId = null
    )
    {
        var dayFolder = capturedAtLocal.ToString("yyyy-MM-dd");
        var sanitizedRunId = SanitizeRunId(runId);
        var sanitizedBattleId = string.IsNullOrWhiteSpace(battleId)
            ? null
            : SanitizeRunId(battleId);
        var sourceToken = captureSource switch
        {
            RunScreenshotCaptureSource.ManualF9 => "manual",
            RunScreenshotCaptureSource.SettingsDockCameraButton => "manual",
            RunScreenshotCaptureSource.PvpBattleStart => "battle",
            RunScreenshotCaptureSource.EndOfRunAuto => "final",
            _ => "capture",
        };
        var fileName =
            sanitizedBattleId == null
                ? $"{capturedAtLocal:yyyy-MM-dd_HH-mm-ss-fff}_{sourceToken}_run-{sanitizedRunId}.png"
                : $"{capturedAtLocal:yyyy-MM-dd_HH-mm-ss-fff}_{sourceToken}_run-{sanitizedRunId}_battle-{sanitizedBattleId}.png";
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
