using Xunit;

namespace Architecture.Tests;

public sealed class OutboundNetworkArchitectureTests
{
    [Fact]
    public void Build_seed_target_delegates_transport_and_contains_no_inline_csharp()
    {
        var root = RepoRoot();
        var targets = File.ReadAllText(
            Path.Combine(root, "src", "BazaarPlusPlus", "RemoteEmbeddedData.targets")
        );
        var fetcher = File.ReadAllText(
            Path.Combine(root, "build", "RemoteEmbeddedDataFetcher", "RemoteEmbeddedDataFetch.cs")
        );
        var script = File.ReadAllText(Path.Combine(root, "run.sh"));

        Assert.Contains("RemoteEmbeddedDataFetcherProject", targets);
        Assert.Contains("<Exec", targets);
        Assert.DoesNotContain("RoslynCodeTaskFactory", targets);
        Assert.DoesNotContain("<![CDATA[", targets);
        Assert.Contains("HttpClient", fetcher);
        Assert.Contains("PromoteSeedSet", fetcher);
        Assert.Contains("run_seed_gates", script);
        Assert.Contains("FullyQualifiedName~Embedded_seed", script);
        Assert.Contains("promote \"$staging_directory\"", script);
        Assert.Contains("for arg in \"$@\"", script);
        Assert.DoesNotContain("local args=(\"$@\")", script);
    }

    [Fact]
    public void Main_menu_controller_delegates_release_protocol_and_request_lifecycle()
    {
        var root = RepoRoot();
        var controller = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus",
                "Game",
                "Lobby",
                "MainMenuVersionCheckController.cs"
            )
        );
        var adapter = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus",
                "Infrastructure",
                "ReleaseManifest",
                "ReleaseManifestClient.cs"
            )
        );
        var lifecycle = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus",
                "Game",
                "Lobby",
                "ReleaseManifestCheckLifecycle.cs"
            )
        );

        Assert.Contains("new ReleaseManifestClient", controller);
        Assert.Contains("_lifecycle.Begin(httpClient)", controller);
        Assert.Contains("_lifecycle.RunAsync(", controller);
        Assert.DoesNotContain("private HttpClient", controller, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetAsync(", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonConvert", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("_generation", controller, StringComparison.Ordinal);
        Assert.Contains(".GetAsync(", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("UnityEngine", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("BepInEx", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("UnityEngine", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("BepInEx", lifecycle, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
