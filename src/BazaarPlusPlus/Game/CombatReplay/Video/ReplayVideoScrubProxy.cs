#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class ReplayVideoScrubProxy
{
    private const string ProxySuffix = ".scrub.mp4";
    private const long MinimumUsableFileSizeBytes = 1024;

    internal static string BuildFilePath(string finalVideoFilePath)
    {
        if (string.IsNullOrWhiteSpace(finalVideoFilePath))
            throw new ArgumentException(
                "The final video path is required.",
                nameof(finalVideoFilePath)
            );

        var fullPath = Path.GetFullPath(finalVideoFilePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "The final video must be an MP4.",
                nameof(finalVideoFilePath)
            );

        var stem = Path.GetFileNameWithoutExtension(fullPath);
        if (stem.EndsWith(".scrub", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "A scrub proxy cannot be derived from another scrub proxy.",
                nameof(finalVideoFilePath)
            );

        return Path.Combine(
            Path.GetDirectoryName(fullPath)
                ?? throw new ArgumentException(
                    "The final video directory is required.",
                    nameof(finalVideoFilePath)
                ),
            stem + ProxySuffix
        );
    }

    internal static bool TryGetExistingFilePath(string finalVideoFilePath, out string proxyFilePath)
    {
        proxyFilePath = string.Empty;
        try
        {
            var candidate = BuildFilePath(finalVideoFilePath);
            if (!IsUsable(candidate))
                return false;
            proxyFilePath = candidate;
            return true;
        }
        catch (Exception ex)
            when (ex
                    is ArgumentException
                        or IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
            )
        {
            return false;
        }
    }

    internal static bool IsUsable(string filePath)
    {
        try
        {
            return File.Exists(filePath)
                && new FileInfo(filePath).Length >= MinimumUsableFileSizeBytes;
        }
        catch (Exception ex)
            when (ex
                    is ArgumentException
                        or IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
            )
        {
            return false;
        }
    }
}
