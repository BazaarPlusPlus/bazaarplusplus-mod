using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Pins ownership of the shared native paired-tooltip seam. Gate transitions, geometry settling,
/// retry behavior, and teardown are executable behavior in NativePairedTooltipHost.Tests.
/// </summary>
public sealed class NativePairedTooltipArchitectureTests
{
    [Fact]
    public void Paired_tooltip_runtime_has_one_shared_GameInterop_owner()
    {
        var root = RepoRoot();
        var sourceRoot = Path.Combine(root, "src", "BazaarPlusPlus");
        var sharedRoot = Path.Combine(sourceRoot, "GameInterop", "Tooltips");
        Assert.True(File.Exists(Path.Combine(sharedRoot, "NativePairedTooltipHost.cs")));
        Assert.True(File.Exists(Path.Combine(sharedRoot, "NativePairedTooltipContracts.cs")));

        var forbiddenOwners = Directory
            .EnumerateFiles(Path.Combine(sourceRoot, "Game"), "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                Path.GetFileName(file).Contains("NativePairedTooltip", StringComparison.Ordinal)
            )
            .Select(file => Path.GetRelativePath(sourceRoot, file).Replace('\\', '/'))
            .ToArray();
        Assert.True(
            forbiddenOwners.Length == 0,
            "Feature directories must consume the shared host instead of owning native plumbing:\n"
                + string.Join("\n", forbiddenOwners)
        );
    }

    [Fact]
    public void Shared_tooltip_adapters_do_not_import_features_or_patches()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "GameInterop", "Tooltips");
        var violations = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file =>
                File.ReadLines(file)
                    .Select((line, index) => (line, index))
                    .Where(hit =>
                        hit.line.Contains("using BazaarPlusPlus.Game.", StringComparison.Ordinal)
                        || hit.line.Contains(
                            "using BazaarPlusPlus.Patches",
                            StringComparison.Ordinal
                        )
                    )
                    .Select(hit => $"{Path.GetFileName(file)}:{hit.index + 1}: {hit.line.Trim()}")
            )
            .ToArray();
        Assert.True(
            violations.Length == 0,
            "Shared tooltip adapters must remain feature-neutral:\n" + string.Join("\n", violations)
        );
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
