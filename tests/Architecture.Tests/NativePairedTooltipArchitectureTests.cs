#nullable enable
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Guards the seam between the shared paired-tooltip host and the features that consume it.
/// </summary>
public sealed class NativePairedTooltipArchitectureTests
{
    /// <summary>
    /// Ratchet. Before the extraction these types lived inside
    /// <c>Patches/PostCombatImpact/NativePostCombatImpactTooltipView.cs</c>, so this test failed;
    /// it passes only because the native paired-tooltip plumbing now has exactly one home.
    /// </summary>
    [Fact]
    public void Native_paired_tooltip_plumbing_lives_only_in_the_shared_host()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var moduleRoot = Path.Combine(sourceRoot, "GameInterop", "Tooltips");
        var forbidden = new[]
        {
            "CanvasGroupGate",
            "NativeAuxiliaryHostState",
            "PairSide",
            "TryCreateNativeBackground",
            "PrepareNativePresentation",
        };
        var violations = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
        )
        {
            if (file.StartsWith(moduleRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            var source = File.ReadAllText(file);
            foreach (var token in forbidden.Where(source.Contains))
                violations.Add($"{Path.GetRelativePath(sourceRoot, file)}: {token}");
        }

        Assert.True(
            violations.Count == 0,
            "Native paired-tooltip host plumbing must stay in GameInterop/Tooltips instead of "
                + "being re-implemented inside a feature:\n"
                + string.Join("\n", violations)
        );
    }

    /// <summary>
    /// Guardrail, not a ratchet: this already held before the extraction, so passing proves nothing
    /// about the migration. It exists to stop feature vocabulary from leaking into the shared host
    /// later.
    /// </summary>
    [Fact]
    public void Shared_tooltip_adapters_do_not_reference_features_or_patches()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var moduleRoot = Path.Combine(sourceRoot, "GameInterop", "Tooltips");
        var violations = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
        )
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            foreach (var line in File.ReadAllLines(file))
            {
                if (
                    line.StartsWith("using BazaarPlusPlus.Game.", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarPlusPlus.Patches", StringComparison.Ordinal)
                )
                    violations.Add($"{relative}: {line.Trim()}");
            }

            var source = File.ReadAllText(file);
            foreach (
                var token in new[] { "PostCombatImpact", "CombatImpact" }.Where(source.Contains)
            )
                violations.Add($"{relative}: mentions {token}");
        }

        Assert.True(
            violations.Count == 0,
            "GameInterop/Tooltips must stay feature-agnostic — no Game/Patches imports and no "
                + "feature vocabulary:\n"
                + string.Join("\n", violations)
        );
    }

    /// <summary>
    /// The Combat Impact view must consume the shared session rather than owning the native pair
    /// itself.
    /// </summary>
    [Fact]
    public void Combat_impact_tooltip_view_consumes_the_shared_session()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var view = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "Patches",
                "PostCombatImpact",
                "NativePostCombatImpactTooltipView.cs"
            )
        );

        Assert.Contains(
            "using BazaarPlusPlus.GameInterop.Tooltips;",
            view,
            StringComparison.Ordinal
        );
        Assert.Contains("NativePairedTooltipSession", view, StringComparison.Ordinal);
        Assert.Contains("IPairedContentBudget", view, StringComparison.Ordinal);
        Assert.DoesNotContain("_renderGeneration", view, StringComparison.Ordinal);
        Assert.DoesNotContain("StartVisibilityFade", view, StringComparison.Ordinal);
        Assert.DoesNotContain("CleanupCustomContent", view, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pre-existing native card-tooltip content refresher is a different concern and must not
    /// be folded into the paired host.
    /// </summary>
    [Fact]
    public void Card_tooltip_content_refresher_stays_a_separate_adapter()
    {
        var moduleRoot = Path.Combine(MainSourceRoot(RepoRoot()), "GameInterop", "Tooltips");
        var refresher = Path.Combine(moduleRoot, "NativeCardTooltipContentRefresher.cs");

        Assert.True(
            File.Exists(refresher),
            "NativeCardTooltipContentRefresher.cs must remain its own adapter."
        );
        Assert.DoesNotContain(
            "NativePairedTooltip",
            File.ReadAllText(refresher),
            StringComparison.Ordinal
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
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string MainSourceRoot(string repoRoot) =>
        Path.Combine(repoRoot, "src", "BazaarPlusPlus");
}
