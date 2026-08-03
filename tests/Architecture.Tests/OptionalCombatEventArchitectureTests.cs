#nullable enable
using Xunit;

namespace Architecture.Tests;

public sealed class OptionalCombatEventArchitectureTests
{
    private static readonly string[] NewerOnlySymbols =
    {
        "CombatSimEventCardActionCostSpent",
        "EActionCommandType.PlayerTempoApply",
        "EActionCommandType.PlayerTempoRemove",
        "ECardAttributeType.TempoApplyAmount",
        "ECardAttributeType.TempoRemoveAmount",
        "ECardStats.TempoAdded",
        "ECardStats.TempoSpent",
    };

    [Fact]
    public void Newer_combat_shapes_stay_behind_runtime_adapters()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");
        var adapterRoot = Path.Combine(sourceRoot, "GameInterop", "CombatSimulation");
        var eventAdapterPath = Path.Combine(adapterRoot, "CardActionCostSpentEventReader.cs");
        var violations = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .SelectMany(path =>
                NewerOnlySymbols
                    .Where(symbol => path.Source.Contains(symbol, StringComparison.Ordinal))
                    .Where(symbol =>
                        symbol != NewerOnlySymbols[0]
                        || !string.Equals(path.Path, eventAdapterPath, StringComparison.Ordinal)
                    )
                    .Select(symbol => $"{Path.GetRelativePath(sourceRoot, path.Path)}: {symbol}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Newer-only combat shapes must not become compile-time Production dependencies:\n"
                + string.Join("\n", violations)
        );

        var eventAdapter = File.ReadAllText(eventAdapterPath);
        Assert.Contains(NewerOnlySymbols[0], eventAdapter, StringComparison.Ordinal);

        var tempoAdapter = File.ReadAllText(
            Path.Combine(adapterRoot, "OptionalCombatTempoTypes.cs")
        );
        Assert.All(
            new[]
            {
                "PlayerTempoApply",
                "PlayerTempoRemove",
                "TempoApplyAmount",
                "TempoRemoveAmount",
                "TempoAdded",
                "TempoSpent",
            },
            symbol => Assert.Contains(symbol, tempoAdapter, StringComparison.Ordinal)
        );

        var projector = File.ReadAllText(
            Path.Combine(sourceRoot, "Game", "PostCombatImpact", "Data", "CombatImpactProjector.cs")
        );
        Assert.Contains(
            "CardActionCostSpentEventReader.TryRead",
            projector,
            StringComparison.Ordinal
        );
        Assert.Contains("OptionalCombatTempoTypes", projector, StringComparison.Ordinal);
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
}
