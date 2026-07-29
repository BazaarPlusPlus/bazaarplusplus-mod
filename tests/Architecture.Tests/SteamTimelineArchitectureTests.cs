#nullable enable
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BazaarPlusPlus.Game.SteamTimeline;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace Architecture.Tests;

public sealed class SteamTimelineArchitectureTests
{
    [Fact]
    public void Steamworks_dependency_is_confined_to_the_capability_adapter()
    {
        var root = MainSourceRoot();
        var references = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("using Steamworks;", StringComparison.Ordinal)
                    || source.Contains("global::Steamworks.", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToArray();

        Assert.Equal(["GameInterop/SteamTimeline/SteamworksTimelineAdapter.cs"], references);
    }

    [Fact]
    public void Timeline_integration_never_owns_the_global_Steam_callback_lifecycle()
    {
        var timelineRoots = new[]
        {
            Path.Combine(MainSourceRoot(), "Game", "SteamTimeline"),
            Path.Combine(MainSourceRoot(), "GameInterop", "SteamTimeline"),
        };
        var source = string.Join(
            "\n",
            timelineRoots
                .SelectMany(path => Directory.EnumerateFiles(path, "*.cs"))
                .Select(File.ReadAllText)
        );

        Assert.DoesNotContain("SteamAPI.Init(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamAPI.Shutdown(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamAPI.RunCallbacks(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Steamworks_managed_dll_remains_a_host_owned_non_copying_reference()
    {
        var project = XDocument.Load(Path.Combine(MainSourceRoot(), "BazaarPlusPlus.csproj"));
        var reference = project
            .Descendants("Reference")
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute("Include"),
                    "com.rlabrecque.steamworks.net",
                    StringComparison.Ordinal
                )
            );

        Assert.Equal("false", (string?)reference.Element("Private"));
        Assert.Equal(
            "$(ManagedPath)\\com.rlabrecque.steamworks.net.dll",
            (string?)reference.Element("HintPath")
        );
    }

    [Fact]
    public void Live_battle_patches_publish_only_for_native_Pvp_playback()
    {
        var source = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(),
                "Patches",
                "SteamTimeline",
                "SteamTimelinePlaybackPatches.cs"
            )
        );

        Assert.Equal(2, Count(source, "AppState.CurrentState is not PVPCombatState"));
        Assert.DoesNotContain("ReplayState", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PvE", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Battle_marking_reads_only_the_terminal_result_not_combat_frames()
    {
        var source = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(),
                "GameInterop",
                "SteamTimeline",
                "SteamTimelineGameProbe.cs"
            )
        );

        Assert.Contains(".Winner", source, StringComparison.Ordinal);
        Assert.Contains(".Loser", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Frames", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CombatSimEvent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_uses_only_the_verified_built_in_Steam_icon_set()
    {
        var source = string.Join(
            "\n",
            Directory
                .EnumerateFiles(Path.Combine(MainSourceRoot(), "Game", "SteamTimeline"), "*.cs")
                .Select(File.ReadAllText)
        );
        var icons = Regex
            .Matches(source, "\\\"(?<icon>steam_[a-z]+)\\\"")
            .Select(match => match.Groups["icon"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "steam_attack",
                "steam_caution",
                "steam_death",
                "steam_crown",
                "steam_ribbon",
                "steam_starburst",
                "steam_trophy",
            },
            icons
        );
        Assert.DoesNotContain("Addressables", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_log_events_have_a_closed_schema()
    {
        var definitions = new[]
        {
            SteamTimelineLogEvents.Degraded,
            SteamTimelineLogEvents.LifecycleChanged,
        };
        var actual = definitions.ToDictionary(
            definition => definition.EventId,
            definition =>
                string.Join(
                    "|",
                    definition.Fields.Select(field =>
                        $"{field.Name}:{field.Privacy}:{field.Cardinality}:{field.Correlation}"
                    )
                ),
            StringComparer.Ordinal
        );

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["steam_timeline.runtime.degraded"] =
                    "operation:Public:Low:None|reason_code:Public:Low:None",
                ["steam_timeline.lifecycle.changed"] =
                    "event_kind:Public:Low:None|outcome:Public:Low:None|level:Public:High:None",
            },
            actual
        );
        Assert.All(
            definitions,
            definition => Assert.Same(BppLogFeatureScope.SteamTimeline, definition.Scope)
        );
        var validation = BppLogEventCatalog.FromDefinitions(definitions).Validate();
        Assert.True(validation.IsValid, string.Join("; ", validation.Violations));
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string MainSourceRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "src", "BazaarPlusPlus")
        );
}
