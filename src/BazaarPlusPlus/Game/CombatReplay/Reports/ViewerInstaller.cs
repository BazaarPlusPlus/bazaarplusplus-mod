#nullable enable
using BazaarPlusPlus.Infrastructure.Files;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal sealed record ViewerInstallResult(
    ViewerArtifactBundle Artifacts,
    string BundleDirectoryPath,
    ReplaceableArtifactPublishResult ScriptPublish,
    ReplaceableArtifactPublishResult StylesheetPublish
);

internal sealed class ViewerInstaller
{
    private readonly StaticReportPaths _paths;
    private readonly ViewerArtifactBundle _artifacts;
    private readonly ReplaceableArtifactPublisher _publisher;

    internal ViewerInstaller(StaticReportPaths paths)
        : this(paths, ViewerArtifactBundle.CreateDefault(), new ReplaceableArtifactPublisher()) { }

    internal ViewerInstaller(
        StaticReportPaths paths,
        ViewerArtifactBundle artifacts,
        ReplaceableArtifactPublisher publisher
    )
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    internal ViewerInstallResult EnsureInstalled()
    {
        // A report references this content-addressed generation only after EnsureInstalled
        // returns. A crash between the two writes can leave an unreferenced partial generation,
        // but can never make an existing report combine artifacts from different releases.
        var stylesheet = _publisher.PublishBelowRoot(
            _paths.DataRootDirectoryPath,
            _paths.GetViewerStylesheetFilePath(_artifacts.BundleId),
            _artifacts.StylesheetBytes
        );
        var script = _publisher.PublishBelowRoot(
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
