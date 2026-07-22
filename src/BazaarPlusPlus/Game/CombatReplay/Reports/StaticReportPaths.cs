#nullable enable
using System.Runtime.InteropServices;
using System.Text;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal sealed class StaticReportPaths
{
    internal StaticReportPaths(string dataRootDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(dataRootDirectoryPath))
            throw new ArgumentException(
                "A report data root is required.",
                nameof(dataRootDirectoryPath)
            );

        DataRootDirectoryPath = TrimEndingSeparators(Path.GetFullPath(dataRootDirectoryPath));
        ReportsDirectoryPath = Path.Combine(DataRootDirectoryPath, "reports");
        ViewerRootDirectoryPath = Path.Combine(DataRootDirectoryPath, "report-viewer");
        AssetRootDirectoryPath = Path.Combine(DataRootDirectoryPath, "report-assets");
        VideoRootDirectoryPath = Path.Combine(DataRootDirectoryPath, "CombatReplayVideos");
    }

    internal string DataRootDirectoryPath { get; }

    internal string ReportsDirectoryPath { get; }

    internal string ViewerRootDirectoryPath { get; }

    internal string AssetRootDirectoryPath { get; }

    internal string VideoRootDirectoryPath { get; }

    internal string GetReportHtmlFilePath(string recordingId) =>
        Path.Combine(ReportsDirectoryPath, ParseRecordingId(recordingId) + ".html");

    internal string GetViewerDirectoryPath(string viewerVersion) =>
        Path.Combine(ViewerRootDirectoryPath, "v" + ParseViewerVersion(viewerVersion));

    internal string GetViewerScriptFilePath(string viewerVersion) =>
        Path.Combine(GetViewerDirectoryPath(viewerVersion), "viewer.js");

    internal string GetViewerStylesheetFilePath(string viewerVersion) =>
        Path.Combine(GetViewerDirectoryPath(viewerVersion), "viewer.css");

    internal static string BuildViewerScriptRelativeUrl(string viewerVersion) =>
        "../report-viewer/v" + ParseViewerVersion(viewerVersion) + "/viewer.js";

    internal static string BuildViewerStylesheetRelativeUrl(string viewerVersion) =>
        "../report-viewer/v" + ParseViewerVersion(viewerVersion) + "/viewer.css";

    internal static string BuildAssetRelativeUrl(string contentSha256)
    {
        var digest = ParseSha256(contentSha256, nameof(contentSha256));
        return "../report-assets/objects/" + digest.Substring(0, 2) + "/" + digest + ".png";
    }

    internal string BuildVideoRelativeUrl(string videoFilePath)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath))
            throw new ArgumentException("A report video path is required.", nameof(videoFilePath));

        var video = Path.GetFullPath(videoFilePath);
        if (!IsBelowRoot(VideoRootDirectoryPath, video))
            throw new InvalidOperationException("The report video escaped CombatReplayVideos.");
        if (!string.Equals(Path.GetExtension(video), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The report video must be an MP4.", nameof(videoFilePath));

        var relative = video
            .Substring(VideoRootDirectoryPath.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries
        );
        if (segments.Length == 0)
            throw new InvalidOperationException("The report video has no relative path.");

        var builder = new StringBuilder("../CombatReplayVideos");
        for (var index = 0; index < segments.Length; index++)
        {
            if (segments[index] == "." || segments[index] == "..")
                throw new InvalidOperationException("The report video path contains traversal.");
            builder.Append('/').Append(Uri.EscapeDataString(segments[index]));
        }

        var url = builder.ToString();
        _ = TypedReportSiblingUrl.Parse(url);
        return url;
    }

    internal static string ParseRecordingId(string value)
    {
        if (!IsLowerHex(value, 32))
            throw new ArgumentException(
                "A recording ID must be 32 lowercase hexadecimal characters.",
                nameof(value)
            );
        return value;
    }

    internal static string ParseViewerVersion(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 8)
            throw new ArgumentException("A Viewer version is required.", nameof(value));
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] < '0' || value[index] > '9')
                throw new ArgumentException(
                    "A Viewer version must contain decimal digits only.",
                    nameof(value)
                );
        }
        if (value.Length > 1 && value[0] == '0')
            throw new ArgumentException(
                "A Viewer version cannot contain leading zeroes.",
                nameof(value)
            );
        return value;
    }

    internal static string ParseSha256(string value, string parameterName)
    {
        if (!IsLowerHex(value, 64))
            throw new ArgumentException(
                "A content SHA-256 must be 64 lowercase hexadecimal characters.",
                parameterName
            );
        return value;
    }

    private static bool IsLowerHex(string? value, int length)
    {
        if (value == null || value.Length != length)
            return false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
                return false;
        }
        return true;
    }

    private static bool IsBelowRoot(string root, string candidatePath)
    {
        var normalizedRoot = TrimEndingSeparators(Path.GetFullPath(root));
        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(prefix, PathComparison);
    }

    private static string TrimEndingSeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        while (
            path.Length > (root?.Length ?? 0)
            && (
                path[path.Length - 1] == Path.DirectorySeparatorChar
                || path[path.Length - 1] == Path.AltDirectorySeparatorChar
            )
        )
        {
            path = path.Substring(0, path.Length - 1);
        }
        return path;
    }

    private static StringComparison PathComparison =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}

internal enum TypedReportSiblingUrlKind
{
    Asset,
    Video,
}

internal sealed record TypedReportSiblingUrl(TypedReportSiblingUrlKind Kind, string Value)
{
    internal static TypedReportSiblingUrl Parse(string value)
    {
        if (TryParse(value, out var parsed))
            return parsed!;
        throw new ArgumentException(
            "The report URL is not an allowed sibling resource URL.",
            nameof(value)
        );
    }

    internal static bool TryParse(string? value, out TypedReportSiblingUrl? parsed)
    {
        parsed = null;
        if (
            string.IsNullOrEmpty(value)
            || value.Length > 2048
            || value.IndexOf('?') >= 0
            || value.IndexOf('#') >= 0
            || value.IndexOf('\\') >= 0
            || value.IndexOf(':') >= 0
        )
        {
            return false;
        }

        var segments = value.Split('/');
        if (segments.Length < 3 || segments[0] != "..")
            return false;
        for (var index = 1; index < segments.Length; index++)
        {
            if (!TryValidateEscapedSegment(segments[index]))
                return false;
        }

        if (
            segments.Length == 5
            && segments[1] == "report-assets"
            && segments[2] == "objects"
            && IsLowerHex(segments[3], 2)
            && segments[4].EndsWith(".png", StringComparison.Ordinal)
        )
        {
            var digest = segments[4].Substring(0, segments[4].Length - 4);
            if (IsLowerHex(digest, 64) && digest.StartsWith(segments[3], StringComparison.Ordinal))
            {
                parsed = new TypedReportSiblingUrl(TypedReportSiblingUrlKind.Asset, value);
                return true;
            }
            return false;
        }

        if (segments[1] == "CombatReplayVideos" && segments.Length >= 3)
        {
            string finalSegment;
            try
            {
                finalSegment = Uri.UnescapeDataString(segments[segments.Length - 1]);
            }
            catch
            {
                return false;
            }
            if (finalSegment.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                parsed = new TypedReportSiblingUrl(TypedReportSiblingUrlKind.Video, value);
                return true;
            }
        }

        return false;
    }

    private static bool TryValidateEscapedSegment(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return false;

        string decoded;
        string decodedTwice;
        try
        {
            decoded = Uri.UnescapeDataString(raw);
            decodedTwice = Uri.UnescapeDataString(decoded);
        }
        catch
        {
            return false;
        }

        if (!string.Equals(Uri.EscapeDataString(decoded), raw, StringComparison.Ordinal))
            return false;
        return IsSafeDecodedSegment(decoded) && IsSafeDecodedSegment(decodedTwice);
    }

    private static bool IsSafeDecodedSegment(string value)
    {
        if (
            string.IsNullOrEmpty(value)
            || value == "."
            || value == ".."
            || value.IndexOf('/') >= 0
            || value.IndexOf('\\') >= 0
            || value.IndexOf(':') >= 0
            || value.IndexOf('?') >= 0
            || value.IndexOf('#') >= 0
            || value.IndexOf('%') >= 0
            || value.IndexOf('\0') >= 0
        )
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] <= '\u001f')
                return false;
        }

        return true;
    }

    private static bool IsLowerHex(string? value, int length)
    {
        if (value == null || value.Length != length)
            return false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
                return false;
        }
        return true;
    }
}
