#nullable enable
using BazaarPlusPlus.Infrastructure.Files;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal sealed record ViewerInstallResult(
    ViewerReleaseDefinition Release,
    ImmutableArtifactCommitResult ScriptCommit,
    ImmutableArtifactCommitResult StylesheetCommit
);

internal sealed class ViewerInstaller
{
    private readonly StaticReportPaths _paths;
    private readonly ViewerReleaseRegistry _registry;
    private readonly ImmutableArtifactCommitter _committer;

    internal ViewerInstaller(StaticReportPaths paths)
        : this(paths, ViewerReleaseRegistry.CreateDefault(), new ImmutableArtifactCommitter()) { }

    internal ViewerInstaller(
        StaticReportPaths paths,
        ViewerReleaseRegistry registry,
        ImmutableArtifactCommitter committer
    )
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _committer = committer ?? throw new ArgumentNullException(nameof(committer));
    }

    internal ViewerInstallResult EnsureInstalled(string version)
    {
        var release = _registry.GetRequired(version);
        var script = _committer.CommitBelowRoot(
            _paths.DataRootDirectoryPath,
            _paths.GetViewerScriptFilePath(release.Version),
            release.ScriptBytes
        );
        var stylesheet = _committer.CommitBelowRoot(
            _paths.DataRootDirectoryPath,
            _paths.GetViewerStylesheetFilePath(release.Version),
            release.StylesheetBytes
        );
        return new ViewerInstallResult(release, script, stylesheet);
    }
}
