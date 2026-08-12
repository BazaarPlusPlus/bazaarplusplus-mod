using System.Xml.Linq;
using Xunit;

namespace Architecture.Tests;

public sealed class TestGovernanceArchitectureTests
{
    [Fact]
    public void Building_projects_is_side_effect_free_unless_deployment_is_explicitly_enabled()
    {
        var root = RepoRoot();
        var directoryProps = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        var mainProject = File.ReadAllText(
            Path.Combine(root, "src", "BazaarPlusPlus", "BazaarPlusPlus.csproj")
        );
        var hostProject = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus.BazaarAgentHost",
                "BazaarPlusPlus.BazaarAgentHost.csproj"
            )
        );
        var script = File.ReadAllText(Path.Combine(root, "run.sh"));

        Assert.Contains(
            "<BppDeployToGame Condition=\"'$(BppDeployToGame)' == ''\">false</BppDeployToGame>",
            directoryProps,
            StringComparison.Ordinal
        );
        Assert.Contains("'$(BppDeployToGame)' == 'true'", mainProject, StringComparison.Ordinal);
        Assert.Contains("'$(GamePath)' != ''", mainProject, StringComparison.Ordinal);
        Assert.Contains("'$(BppDeployToGame)' == 'true'", hostProject, StringComparison.Ordinal);
        Assert.Contains("'$(GamePath)' != ''", hostProject, StringComparison.Ordinal);
        Assert.Contains("-p:BppDeployToGame=true", script, StringComparison.Ordinal);
        Assert.Contains("-p:BppDeployToGame=false", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_compatibility_and_corpus_lanes_are_explicit_and_disjoint()
    {
        var root = RepoRoot();
        var script = File.ReadAllText(Path.Combine(root, "run.sh"));
        var solutionPath = Path.Combine(root, "tests", "BazaarPlusPlus.Tests.slnx");
        var compatibilityPath = Path.Combine(root, "tests", "CompatibilityTests.manifest");

        Assert.True(File.Exists(solutionPath));
        Assert.True(File.Exists(compatibilityPath));
        Assert.Contains(
            "dotnet test tests/BazaarPlusPlus.Tests.slnx",
            script,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("find tests -name", script, StringComparison.Ordinal);
        Assert.Contains("test-compat", script, StringComparison.Ordinal);
        Assert.Contains("test-corpus", script, StringComparison.Ordinal);
        Assert.Contains(
            "tools/PeriodicEffectAttribution.Corpus/PeriodicEffectAttribution.Corpus.csproj",
            script,
            StringComparison.Ordinal
        );
        Assert.False(
            File.Exists(
                Path.Combine(
                    root,
                    "tests",
                    "PeriodicEffectAttribution.Corpus",
                    "PeriodicEffectAttribution.Corpus.csproj"
                )
            )
        );

        var defaultSolution = XDocument.Load(solutionPath).ToString();
        foreach (var entry in CompatibilityEntries(compatibilityPath))
        {
            Assert.DoesNotContain(
                Path.GetFileNameWithoutExtension(entry.Project),
                defaultSolution,
                StringComparison.Ordinal
            );
        }
    }

    [Fact]
    public void Every_default_test_project_is_xunit_discoverable()
    {
        var root = RepoRoot();
        var solution = XDocument.Load(Path.Combine(root, "tests", "BazaarPlusPlus.Tests.slnx"));
        var violations = new List<string>();

        foreach (var projectElement in solution.Descendants("Project"))
        {
            var relative = (string?)projectElement.Attribute("Path");
            if (relative == null)
                continue;
            var projectPath = Path.Combine(root, "tests", relative);
            var project = File.ReadAllText(projectPath);
            if (!project.Contains("Microsoft.NET.Test.Sdk", StringComparison.Ordinal))
                violations.Add(relative);
        }

        Assert.True(
            violations.Count == 0,
            "Every project in the default solution must be directly xUnit-discoverable:\n"
                + string.Join("\n", violations)
        );
        Assert.InRange(solution.Descendants("Project").Count(), 1, 20);
    }

    [Fact]
    public void Executable_scenario_capsules_are_closed_and_owned_by_one_xunit_host()
    {
        var root = RepoRoot();
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var scenarios = props
            .Descendants("BppScenarioRunnerProjects")
            .Single()
            .Value.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .ToHashSet(StringComparer.Ordinal);
        var compatibility = CompatibilityEntries(
                Path.Combine(root, "tests", "CompatibilityTests.manifest")
            )
            .Select(entry => Path.GetFileNameWithoutExtension(entry.Project))
            .ToHashSet(StringComparer.Ordinal);
        var executableProjects = Directory
            .EnumerateFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories)
            .Where(path =>
                File.ReadAllText(path)
                    .Contains("<OutputType>Exe</OutputType>", StringComparison.Ordinal)
            )
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            executableProjects.SetEquals(scenarios.Concat(compatibility)),
            "Every executable test capsule must belong to the default scenario host or the "
                + "compatibility manifest."
        );
        Assert.DoesNotContain(scenarios, scenario => compatibility.Contains(scenario));

        var hostProject = File.ReadAllText(
            Path.Combine(root, "tests", "ScenarioRunner.Tests", "ScenarioRunner.Tests.csproj")
        );
        Assert.Contains("$(BppScenarioRunnerProjects)", hostProject, StringComparison.Ordinal);
        Assert.Contains("ReferenceOutputAssembly=\"false\"", hostProject, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_tests_use_a_checked_in_remote_seed_and_never_refresh_it()
    {
        var root = RepoRoot();
        var script = File.ReadAllText(Path.Combine(root, "run.sh"));
        var seed = Path.Combine(root, "tests", "TestData", "remote-data", "voice-lines.json");

        Assert.True(File.Exists(seed));
        Assert.Contains("ForceRemoteEmbeddedDataRefresh=false", script, StringComparison.Ordinal);
        Assert.Contains("tests/TestData/remote-data", script, StringComparison.Ordinal);
    }

    private static IEnumerable<CompatibilityEntry> CompatibilityEntries(string manifestPath)
    {
        foreach (var line in File.ReadLines(manifestPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;
            var fields = line.Split('|');
            Assert.Equal(3, fields.Length);
            yield return new CompatibilityEntry(fields[0], fields[1], fields[2]);
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record CompatibilityEntry(string Requirement, string Label, string Project);
}
