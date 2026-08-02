using System.Runtime.CompilerServices;
using Xunit;

namespace Architecture.Tests;

public sealed class V5DataPipelineArchitectureTests
{
    [Fact]
    public void ModApi_has_only_v5_bundle_routes_and_no_multipart_wire()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.ModApi");
        var source = ReadSources(root);
        Assert.Contains("/bundles", source);
        Assert.Contains("/ghost-battles", source);
        Assert.DoesNotContain("mod-api-v4", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/run-bundles", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("multipart", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bazaardb/snapshots", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_has_one_bundle_feed_and_v5_data_root()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var main = ReadSources(Path.Combine(sourceRoot, "BazaarPlusPlus"));
        var storage = ReadSources(Path.Combine(sourceRoot, "BazaarPlusPlus.Storage"));
        Assert.Contains("BazaarPlusPlusV5", storage);
        Assert.Contains("enum UploadFeedKind\n{\n    Bundle,", main);
        Assert.DoesNotContain("RunBundleUploadFeed", main);
        Assert.DoesNotContain("BazaarDbSnapshotUploadFeed", main);
        Assert.DoesNotContain("RunSyncStateStore", main);
        Assert.DoesNotContain("BattleReplaySyncStateStore", main);
        Assert.DoesNotContain("ReplicatedRunLogStore", main);
    }

    [Fact]
    public void Supporter_temp_cache_uses_the_shared_v5_directory_name()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var legacyDirectoryName = "BazaarPlusPlus" + "V4";
        Assert.DoesNotContain(
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories),
            path => File.ReadAllText(path).Contains(legacyDirectoryName, StringComparison.Ordinal)
        );

        var source = File.ReadAllText(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus",
                "Game",
                "Supporters",
                "BPPSupporterCatalog.cs"
            )
        );
        Assert.Contains("PathConstants.DataRootDirectoryName", source);
    }

    private static string ReadSources(string root) =>
        string.Join(
            "\n",
            Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
        );

    private static string RepoRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
