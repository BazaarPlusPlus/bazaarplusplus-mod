#nullable enable
using System;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal static class RunUploadErrorFormatter
{
    public static string Truncate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "empty_response";

        return value.Length <= 256 ? value : value[..256];
    }

    public static bool IndicatesMissingClient(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("client not found", StringComparison.OrdinalIgnoreCase)
            || value.Contains("unknown client", StringComparison.OrdinalIgnoreCase)
            || value.Contains("invalid client", StringComparison.OrdinalIgnoreCase)
            || value.Contains("unregistered client", StringComparison.OrdinalIgnoreCase);
    }
}
