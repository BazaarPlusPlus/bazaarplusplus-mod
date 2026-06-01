#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace Architecture.Tests;

// Ratchet guard for the intended layering: types under Core/ are the pure abstraction floor and
// must not depend on the Game features, the GameInterop bridge, or the game DLLs. Because every
// layer compiles into one assembly, the C# compiler does not enforce this; this test does.
//
// A small allowlist records the deliberate, known exceptions. When a Core -> GameInterop leak is
// fixed, remove its entry; when a new dependency appears, this fails until it is justified (added
// here) or removed. Game.* and game-DLL (BazaarGameShared) references are never allowed.
public class CoreLayeringTests
{
    private static readonly HashSet<string> AllowedGameInteropFiles = new(StringComparer.Ordinal)
    {
        // The service aggregate re-exports GameInterop.IRunContext to features. Splitting Core into
        // its own assembly (or moving IRunContext's abstraction down) would let us drop these.
        "Core/Runtime/IBppServices.cs",
        "Core/Runtime/BppRuntimeServices.cs",
    };

    [Fact]
    public void Core_does_not_depend_on_Game_GameInterop_or_game_assemblies()
    {
        var repoRoot = RepoRoot();
        var coreDir = Path.Combine(repoRoot, "Core");
        Assert.True(Directory.Exists(coreDir), $"Could not locate Core directory at '{coreDir}'.");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("using ", StringComparison.Ordinal))
                    continue;

                // The game DLL and sibling Game features are never permitted in Core.
                if (
                    line.StartsWith("using BazaarGameShared", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarPlusPlus.Game.", StringComparison.Ordinal)
                )
                {
                    violations.Add($"{relative}: {line}");
                    continue;
                }

                // GameInterop is permitted only for the explicitly allowlisted files.
                if (
                    line.StartsWith("using BazaarPlusPlus.GameInterop", StringComparison.Ordinal)
                    && !AllowedGameInteropFiles.Contains(relative)
                )
                {
                    violations.Add($"{relative}: {line}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Core must stay free of Game/GameInterop/game-DLL dependencies. Either remove the "
                + "import or, for a deliberate GameInterop dependency, add the file to "
                + "AllowedGameInteropFiles with justification. Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void CollectionPanel_does_not_depend_on_HistoryPanel_preview_internals()
    {
        var repoRoot = RepoRoot();
        var collectionPanelDir = Path.Combine(repoRoot, "Game", "CollectionPanel");
        Assert.True(
            Directory.Exists(collectionPanelDir),
            $"Could not locate CollectionPanel directory at '{collectionPanelDir}'."
        );

        var violations = new List<string>();
        foreach (
            var file in Directory.EnumerateFiles(
                collectionPanelDir,
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (
                    line.StartsWith(
                        "using BazaarPlusPlus.Game.HistoryPanel.Preview",
                        StringComparison.Ordinal
                    )
                )
                {
                    violations.Add($"{relative}: {line}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "CollectionPanel must use shared GameInterop card-preview adapters instead of "
                + "depending on HistoryPanel preview internals. Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void GameInterop_does_not_depend_on_Game_feature_namespaces()
    {
        var repoRoot = RepoRoot();
        var gameInteropDir = Path.Combine(repoRoot, "GameInterop");
        Assert.True(
            Directory.Exists(gameInteropDir),
            $"Could not locate GameInterop directory at '{gameInteropDir}'."
        );

        var violations = new List<string>();
        foreach (
            var file in Directory.EnumerateFiles(
                gameInteropDir,
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("using BazaarPlusPlus.Game.", StringComparison.Ordinal))
                    violations.Add($"{relative}: {line}");
                if (line.StartsWith("using BazaarPlusPlus.AutoBazaar", StringComparison.Ordinal))
                    violations.Add($"{relative}: {line}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "GameInterop must stay reusable and must not import Game feature namespaces. "
                + "Pass primitive ids/DTOs across the boundary instead. Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void AutoBazaar_core_does_not_depend_on_host_or_game_runtime_namespaces()
    {
        var repoRoot = RepoRoot();
        var autoBazaarDir = Path.Combine(repoRoot, "AutoBazaar");
        Assert.True(
            Directory.Exists(autoBazaarDir),
            $"Could not locate AutoBazaar directory at '{autoBazaarDir}'."
        );

        var violations = new List<string>();
        foreach (
            var file in Directory.EnumerateFiles(autoBazaarDir, "*.cs", SearchOption.AllDirectories)
        )
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("using ", StringComparison.Ordinal))
                    continue;

                if (
                    line.StartsWith("using UnityEngine", StringComparison.Ordinal)
                    || line.StartsWith("using BepInEx", StringComparison.Ordinal)
                    || line.StartsWith("using HarmonyLib", StringComparison.Ordinal)
                    || line.StartsWith("using TheBazaar", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarGameClient", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarGameShared", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarPlusPlus.Game.", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarPlusPlus.GameInterop", StringComparison.Ordinal)
                )
                {
                    violations.Add($"{relative}: {line}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "AutoBazaar core must stay independent from Unity, BepInEx, Harmony, game DLLs, "
                + "Game features, and GameInterop. Put those dependencies in Game/AutoBazaarHost. "
                + "Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void AutoBazaar_tests_reference_project_instead_of_source_linking_game_files()
    {
        var repoRoot = RepoRoot();
        var testProject = Path.Combine(
            repoRoot,
            "tests",
            "AutoBazaar.Tests",
            "AutoBazaar.Tests.csproj"
        );
        Assert.True(File.Exists(testProject), $"Could not locate test project at '{testProject}'.");

        var text = File.ReadAllText(testProject);
        Assert.Contains("BazaarPlusPlus.AutoBazaar.csproj", text);
        Assert.DoesNotContain("Game\\AutoBazaar", text);
        Assert.DoesNotContain("Game/AutoBazaar", text);
        Assert.DoesNotContain("<Compile Include=\"..\\..\\Game", text);
    }

    [Fact]
    public void Main_project_references_AutoBazaar_assembly_without_implicit_source_compile()
    {
        var repoRoot = RepoRoot();
        var mainProject = Path.Combine(repoRoot, "BazaarPlusPlus.csproj");
        Assert.True(File.Exists(mainProject), $"Could not locate main project at '{mainProject}'.");

        var text = File.ReadAllText(mainProject);
        Assert.Contains("ProjectReference Include=\"BazaarPlusPlus.AutoBazaar.csproj\"", text);
        Assert.Contains("Compile Remove=\"AutoBazaar/**\"", text);
        Assert.DoesNotContain("Compile Include=\"AutoBazaar/", text);
        Assert.DoesNotContain("Compile Include=\"AutoBazaar\\", text);
    }

    // The compile-time path of this source file anchors the repo root without loading any
    // game-coupled assembly at runtime: <repo>/tests/Architecture.Tests/CoreLayeringTests.cs.
    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var testDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testDir, "..", ".."));
    }
}
