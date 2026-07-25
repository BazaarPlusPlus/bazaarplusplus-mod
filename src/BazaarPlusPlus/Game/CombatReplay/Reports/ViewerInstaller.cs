#nullable enable
using BazaarPlusPlus.Infrastructure.Files;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal sealed record ViewerInstallResult(
    ViewerArtifactBundle Artifacts,
    string BundleDirectoryPath,
    ImmutableArtifactCommitResult ScriptPublish,
    ImmutableArtifactCommitResult StylesheetPublish
);

internal sealed class ViewerInstaller
{
    private readonly StaticReportPaths _paths;
    private readonly ViewerArtifactBundle _artifacts;
    private readonly ImmutableArtifactCommitter _committer;

    internal ViewerInstaller(StaticReportPaths paths)
        : this(paths, ViewerArtifactBundle.CreateDefault(), new ImmutableArtifactCommitter()) { }

    internal ViewerInstaller(
        StaticReportPaths paths,
        ViewerArtifactBundle artifacts,
        ImmutableArtifactCommitter committer
    )
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _committer = committer ?? throw new ArgumentNullException(nameof(committer));
    }

    internal ViewerInstallResult EnsureInstalled()
    {
        // A report references this content-addressed generation only after EnsureInstalled
        // returns. A crash between the two writes can leave an unreferenced partial generation,
        // but can never make an existing report combine artifacts from different releases.
        var stylesheet = _committer.CommitBelowRoot(
            _paths.DataRootDirectoryPath,
            _paths.GetViewerStylesheetFilePath(_artifacts.BundleId),
            _artifacts.StylesheetBytes
        );
        var script = _committer.CommitBelowRoot(
            _paths.DataRootDirectoryPath,
            _paths.GetViewerScriptFilePath(_artifacts.BundleId),
            _artifacts.ScriptBytes
        );
        return new ViewerInstallResult(
            _artifacts,
            _paths.GetViewerBundleDirectoryPath(_artifacts.BundleId),
            script,
            stylesheet
        );
    }
}
