#nullable enable
using System.Runtime.InteropServices;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

/// <summary>
/// Repairs the one shared, content-addressed Viewer generation before a report is opened or
/// existing report rows are accepted during startup recovery.
/// </summary>
internal static class CombatReplayReportViewerGate
{
    internal static bool TryEnsureInstalledForReportRoot(
        string reportRootDirectoryPath,
        out string reason
    )
    {
        reason = "Battle report Viewer is unavailable.";
        if (string.IsNullOrWhiteSpace(reportRootDirectoryPath))
            return false;

        try
        {
            var reportRoot = TrimEndingSeparators(Path.GetFullPath(reportRootDirectoryPath));
            var dataRoot = Path.GetDirectoryName(reportRoot);
            if (string.IsNullOrWhiteSpace(dataRoot))
                return false;

            var paths = new StaticReportPaths(dataRoot);
            if (!string.Equals(paths.ReportsDirectoryPath, reportRoot, PathComparison))
                return false;

            _ = new ViewerInstaller(paths).EnsureInstalled();
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    internal static bool TryEnsureInstalledForReport(
        string reportRootDirectoryPath,
        string reportFilePath,
        out string reason
    )
    {
        reason = "Battle report Viewer is unavailable.";
        if (
            string.IsNullOrWhiteSpace(reportRootDirectoryPath)
            || string.IsNullOrWhiteSpace(reportFilePath)
        )
            return false;

        try
        {
            var reportRoot = TrimEndingSeparators(Path.GetFullPath(reportRootDirectoryPath));
            var dataRoot = Path.GetDirectoryName(reportRoot);
            if (string.IsNullOrWhiteSpace(dataRoot))
                return false;

            var paths = new StaticReportPaths(dataRoot);
            if (!string.Equals(paths.ReportsDirectoryPath, reportRoot, PathComparison))
                return false;

            ReportPhysicalFile.RequireBelowRoot(reportRoot, reportFilePath, "Battle report");
            var install = new ViewerInstaller(paths).EnsureInstalled();
            if (
                !StaticReportPaths.IsCurrentViewerReport(
                    reportFilePath,
                    install.Artifacts.BundleId,
                    out reason
                )
            )
            {
                return false;
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    internal static ViewerInstallResult EnsureInstalledForDataRoot(string dataRootDirectoryPath)
    {
        var paths = new StaticReportPaths(dataRootDirectoryPath);
        return new ViewerInstaller(paths).EnsureInstalled();
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
