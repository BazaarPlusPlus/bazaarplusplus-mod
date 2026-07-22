#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BazaarPlusPlus.GameInterop.Files;

internal enum SystemReportOpenPlatform
{
    MacOS,
    Windows,
    Linux,
}

internal readonly struct CombatReportRecordingId : IEquatable<CombatReportRecordingId>
{
    private const int CanonicalLength = 32;
    private readonly string? _value;

    private CombatReportRecordingId(string value)
    {
        _value = value;
    }

    internal string Value => _value ?? string.Empty;

    internal bool IsCanonical => _value != null;

    internal static bool TryParse(string? value, out CombatReportRecordingId recordingId)
    {
        recordingId = default;
        if (value == null || value.Length != CanonicalLength)
            return false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
            {
                return false;
            }
        }

        recordingId = new CombatReportRecordingId(value);
        return true;
    }

    public bool Equals(CombatReportRecordingId other) =>
        string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is CombatReportRecordingId other && Equals(other);

    public override int GetHashCode() =>
        _value == null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    public override string ToString() => Value;
}

internal sealed class ResolvedSystemReport
{
    internal ResolvedSystemReport(
        string reportRootDirectoryPath,
        CombatReportRecordingId recordingId,
        string fullPath
    )
    {
        ReportRootDirectoryPath = reportRootDirectoryPath;
        RecordingId = recordingId;
        FullPath = fullPath;
    }

    internal string ReportRootDirectoryPath { get; }

    internal CombatReportRecordingId RecordingId { get; }

    internal string FullPath { get; }
}

internal static class SystemReportOpener
{
    internal static bool TryOpen(
        string reportRootDirectoryPath,
        CombatReportRecordingId recordingId,
        out string reason
    )
    {
        if (!TryResolve(reportRootDirectoryPath, recordingId, out var resolvedReport, out reason))
        {
            return false;
        }

        return TryOpen(resolvedReport!, out reason);
    }

    internal static bool TryOpen(ResolvedSystemReport resolvedReport, out string reason)
    {
        if (resolvedReport == null)
        {
            reason = "Battle report is unavailable.";
            return false;
        }

        if (
            !TryResolve(
                resolvedReport.ReportRootDirectoryPath,
                resolvedReport.RecordingId,
                out var currentReport,
                out reason
            ) || !string.Equals(currentReport!.FullPath, resolvedReport.FullPath, PathComparison)
        )
        {
            reason = "Battle report is no longer a valid physical file.";
            return false;
        }

        try
        {
            Process.Start(BuildStartInfo(DetectPlatform(), currentReport.FullPath));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    internal static bool TryResolve(
        string reportRootDirectoryPath,
        CombatReportRecordingId recordingId,
        out ResolvedSystemReport? resolvedReport,
        out string reason
    )
    {
        resolvedReport = null;
        reason = "Battle report is unavailable.";
        if (string.IsNullOrWhiteSpace(reportRootDirectoryPath) || !recordingId.IsCanonical)
        {
            return false;
        }

        try
        {
            var reportRoot = TrimEndingSeparators(Path.GetFullPath(reportRootDirectoryPath));
            if (!IsPhysicalDirectory(reportRoot))
            {
                reason = "Battle report root is unavailable or is not a physical directory.";
                return false;
            }

            var expectedFileName = recordingId.Value + ".html";
            var candidate = Path.GetFullPath(Path.Combine(reportRoot, expectedFileName));
            var candidateParent = TrimEndingSeparators(
                Path.GetDirectoryName(candidate) ?? string.Empty
            );
            if (
                !string.Equals(candidateParent, reportRoot, PathComparison)
                || !string.Equals(
                    Path.GetFileName(candidate),
                    expectedFileName,
                    StringComparison.Ordinal
                )
                || !IsPhysicalReportFile(candidate)
            )
            {
                reason = "Battle report no longer exists as a physical HTML file.";
                return false;
            }

            resolvedReport = new ResolvedSystemReport(reportRoot, recordingId, candidate);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    internal static ProcessStartInfo BuildStartInfo(
        SystemReportOpenPlatform platform,
        string fullPath
    )
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathRooted(fullPath))
            throw new ArgumentException("A full report path is required.", nameof(fullPath));

        if (platform == SystemReportOpenPlatform.Windows)
        {
            return new ProcessStartInfo { FileName = fullPath, UseShellExecute = true };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = platform == SystemReportOpenPlatform.MacOS ? "/usr/bin/open" : "xdg-open",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(fullPath);
        return startInfo;
    }

    private static bool IsPhysicalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.Directory) != 0
            && (attributes & FileAttributes.ReparsePoint) == 0;
    }

    private static bool IsPhysicalReportFile(string path)
    {
        if (!File.Exists(path))
            return false;

        var attributes = File.GetAttributes(path);
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0
            && new FileInfo(path).Length > 0;
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

    private static SystemReportOpenPlatform DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return SystemReportOpenPlatform.MacOS;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return SystemReportOpenPlatform.Windows;
        return SystemReportOpenPlatform.Linux;
    }
}
