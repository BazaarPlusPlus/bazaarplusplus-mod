#nullable enable
using Xunit;

namespace Architecture.Tests;

public sealed class NativeAssetLoadingArchitectureTests
{
    [Fact]
    public void Cross_build_argument_adaptation_has_one_shared_owner()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var invocationPath = Path.Combine(
            sourceRoot,
            "GameInterop",
            "AssetLoading",
            "NativeAssetLoaderInvocation.cs"
        );
        var globalLoaderPath = Path.Combine(
            sourceRoot,
            "GameInterop",
            "AssetLoading",
            "NativeGlobalAssetLoader.cs"
        );
        var cardPreviewLoaderPath = Path.Combine(
            sourceRoot,
            "GameInterop",
            "CardPreview",
            "NativeCardPreviewAssetLoader.cs"
        );
        var obsoleteCardPreviewHelperPath = Path.Combine(
            sourceRoot,
            "GameInterop",
            "CardPreview",
            "NativeCardPreviewAssetInvocation.cs"
        );

        Assert.True(File.Exists(invocationPath));
        Assert.False(File.Exists(obsoleteCardPreviewHelperPath));
        Assert.Contains(
            "NativeAssetLoaderInvocation.TryBuildArguments",
            File.ReadAllText(globalLoaderPath)
        );
        Assert.Contains(
            "NativeAssetLoaderInvocation.SupportsSignature",
            File.ReadAllText(globalLoaderPath)
        );
        Assert.Contains(
            "NativeAssetLoaderInvocation.TryBuildArguments",
            File.ReadAllText(cardPreviewLoaderPath)
        );
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (
                File.Exists(Path.Combine(current.FullName, "CLAUDE.md"))
                && Directory.Exists(Path.Combine(current.FullName, "src"))
            )
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string MainSourceRoot(string repoRoot) =>
        Path.Combine(repoRoot, "src", "BazaarPlusPlus");
}
