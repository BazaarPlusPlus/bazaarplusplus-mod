#nullable enable
using BazaarPlusPlus.Infrastructure.Files;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

/// <summary>
/// Installs or verifies the one shared, content-addressed Viewer generation before a report is
/// opened or existing report rows are accepted during startup recovery.
/// </summary>
internal static class CombatReplayReportViewerGate
{
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
            var reportRoot = PhysicalPathPolicy.TrimEndingSeparators(
                Path.GetFullPath(reportRootDirectoryPath)
            );
            var dataRoot = Path.GetDirectoryName(reportRoot);
            if (string.IsNullOrWhiteSpace(dataRoot))
                return false;

            var paths = new StaticReportPaths(dataRoot);
            if (
                !string.Equals(
                    paths.ReportsDirectoryPath,
                    reportRoot,
                    PhysicalPathPolicy.PathComparison
                )
            )
                return false;

            ReportPhysicalFile.RequireBelowRoot(reportRoot, reportFilePath, "Battle report");
            if (
                !StaticReportPaths.TryValidateViewerReport(
                    reportFilePath,
                    out var bundleId,
                    out var scriptSha256,
                    out var stylesheetSha256,
                    out reason
                )
            )
            {
                return false;
            }

            var currentArtifacts = ViewerArtifactBundle.CreateDefault();
            if (string.Equals(bundleId, currentArtifacts.BundleId, StringComparison.Ordinal))
                _ = new ViewerInstaller(paths).EnsureInstalled();

            if (
                !TryValidateReferencedViewer(
                    paths,
                    bundleId,
                    scriptSha256,
                    stylesheetSha256,
                    out reason
                )
            )
                return false;

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

    private static bool TryValidateReferencedViewer(
        StaticReportPaths paths,
        string bundleId,
        string scriptSha256,
        string stylesheetSha256,
        out string reason
    )
    {
        try
        {
            if (
                !string.Equals(
                    ViewerArtifactBundle.ComputeBundleId(
                        ViewerArtifactBundle.CurrentReportSchemaVersion,
                        scriptSha256,
                        stylesheetSha256
                    ),
                    bundleId,
                    StringComparison.Ordinal
                )
            )
            {
                reason =
                    "Battle report Viewer hashes do not match the referenced immutable bundle.";
                return false;
            }

            var scriptPath = paths.GetViewerScriptFilePath(bundleId);
            var stylesheetPath = paths.GetViewerStylesheetFilePath(bundleId);
            ReportPhysicalFile.RequireBelowRoot(
                paths.ViewerRootDirectoryPath,
                scriptPath,
                "Battle report Viewer script"
            );
            ReportPhysicalFile.RequireBelowRoot(
                paths.ViewerRootDirectoryPath,
                stylesheetPath,
                "Battle report Viewer stylesheet"
            );
            if (
                !string.Equals(
                    StaticReportIntegrity.Sha256File(scriptPath),
                    scriptSha256,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    StaticReportIntegrity.Sha256File(stylesheetPath),
                    stylesheetSha256,
                    StringComparison.Ordinal
                )
            )
            {
                reason =
                    "Battle report Viewer files do not match the report's immutable references.";
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
}
