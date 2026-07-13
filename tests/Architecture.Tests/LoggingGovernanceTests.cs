#nullable enable
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Architecture.Tests;

public sealed class LoggingGovernanceTests
{
    private static readonly Regex LegacyBppLogCall = new(
        @"\bBppLog\.(?<member>Debug|Info|Warn|Error)\s*\(",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex LegacyVoiceSubtitlesMember = new(
        @"\bVoiceSubtitlesLog\.(?<member>Info|Debug|Warn|Error|Field|ObjectId|Verbose)\b",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex LegacyAgentLoggerCall = new(
        @"\.(?<member>Info|Warning|Error)\s*\(",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex LegacyStorageLoggerCall = new(
        @"\??\.(?<member>Warn|Error)\s*\(",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex LegacyHostAgentLoggerCall = new(
        @"\b(?:logger|_logger)\.(?<member>Info|Warning|Error)\s*\(",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex LogShapedCall = new(
        @"\.(?<member>Log|LogDebug|LogInfo|LogWarning|LogError|LogFatal|LogMessage)\s*\(",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex RegisteredEventDefinition = new(
        @"\b(?:internal|private|public)\s+static\s+readonly\s+"
            + @"BppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*new\s*\(",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    );

    private static readonly Regex EventDefinitionConstruction = new(
        RegisteredEventDefinition
            + @"|\bnew\s+BppLogEventDefinition\s*\("
            + @"|\bBppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*(?:=|=>)\s*new\s*\("
            + @"|\bBppLog\.(?:DebugEvent|InfoEvent|WarnEvent|ErrorEvent)\s*\(\s*new\s*\("
            + @"|\bBppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*\([^;{}]*\)\s*"
            + @"=>[\s\S]{0,512}?\bnew\s*\("
            + @"|\bBppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*\([^;{}]*\)\s*"
            + @"\{[\s\S]*?\breturn[\s\S]{0,512}?\bnew\s*\("
            + @"|\bFunc\s*<\s*BppLogEventDefinition\s*>\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*"
            + @"(?:\([^)]*\)|[A-Za-z_][A-Za-z0-9_]*)\s*=>[\s\S]{0,512}?\bnew\s*\("
            + @"|\bBppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*"
            + @"=>[\s\S]{0,512}?\bnew\s*\("
            + @"|\bBppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*"
            + @"\{[\s\S]*?\breturn[\s\S]{0,512}?\bnew\s*\(",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    );

    // Transitional expand-contract ratchets. Values are exact line+member fingerprints so a
    // one-for-one replacement cannot hide behind an unchanged per-file count. Each migration
    // removes its converted entries; #60 leaves every map empty.
    private static readonly IReadOnlyDictionary<string, string> ExpectedLegacyCalls =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Game/RunLogging/RunLogStoreLoggerBridge.cs"] = "10:Warn,13:Error",
        };

    private static readonly IReadOnlyDictionary<string, string> ExpectedVoiceSubtitlesMembers =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> ExpectedAgentLoggerCalls =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> ExpectedHostAgentLoggerCalls =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> ExpectedStorageLoggerCalls =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RunLog/Replication/QueuedRunLogStore.cs"] = "91:Warn,147:Error,159:Error",
        };

    private static readonly HashSet<string> ApprovedBepInExAdapters = new(StringComparer.Ordinal)
    {
        "BazaarPlusPlus/Infrastructure/BppLog.cs",
        "BazaarPlusPlus.BazaarAgentHost/BazaarAgentBepInExLogger.cs",
    };

    private static readonly IReadOnlyDictionary<string, string> ExpectedNonAdapterLogShapedCalls =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void Legacy_free_text_BppLog_calls_match_the_shrinking_allowlist()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");
        AssertFingerprintsEqual(
            ExpectedLegacyCalls,
            FingerprintMatches(root, LegacyBppLogCall, relativeTo: root),
            "Legacy BppLog calls changed. Migrations must shrink ExpectedLegacyCalls in the same "
                + "commit; new free-text calls are prohibited."
        );
    }

    [Fact]
    public void VoiceSubtitles_wrapper_surface_matches_the_shrinking_allowlist()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");
        AssertFingerprintsEqual(
            ExpectedVoiceSubtitlesMembers,
            FingerprintMatches(root, LegacyVoiceSubtitlesMember, relativeTo: root),
            "The legacy VoiceSubtitles wrappers and helper members are frozen until #56 removes "
                + "them. New uses are prohibited."
        );
    }

    [Fact]
    public void VoiceSubtitles_free_text_wrappers_and_aliases_are_absent()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");
        Assert.False(
            File.Exists(Path.Combine(root, "Game", "VoiceSubtitles", "VoiceSubtitlesLog.cs"))
        );
        Assert.False(
            File.Exists(
                Path.Combine(root, "GameInterop", "VoiceSubtitles", "VoiceSubtitlesInteropLog.cs")
            )
        );

        foreach (var file in ProductionFiles(root))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("VoiceSubtitlesLog.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("VoiceSubtitlesInteropLog", source, StringComparison.Ordinal);
            Assert.DoesNotContain("using VoiceSubtitlesLog", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CombatReplay_and_Screenshots_free_text_logging_is_absent()
    {
        var gameRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Game");
        var migratedRoots = new[]
        {
            Path.Combine(gameRoot, "CombatReplay"),
            Path.Combine(gameRoot, "Screenshots"),
        };
        var violations = migratedRoots
            .SelectMany(ProductionFiles)
            .SelectMany(file =>
                LegacyBppLogCall
                    .Matches(File.ReadAllText(file))
                    .Select(match => $"{Path.GetRelativePath(gameRoot, file)}:{match.Value}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "CombatReplay/Screenshots migrations must not retain free-text BppLog calls:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Panel_domains_free_text_logging_is_absent()
    {
        var gameRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Game");
        var migratedRoots = new[]
        {
            Path.Combine(gameRoot, "CollectionPanel"),
            Path.Combine(gameRoot, "HistoryPanel"),
            Path.Combine(gameRoot, "LiveBuildPanel"),
            Path.Combine(gameRoot, "OverlayPanels"),
        };
        var violations = migratedRoots
            .SelectMany(ProductionFiles)
            .SelectMany(file =>
                LegacyBppLogCall
                    .Matches(File.ReadAllText(file))
                    .Select(match => $"{Path.GetRelativePath(gameRoot, file)}:{match.Value}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Panel domain migrations must not retain free-text BppLog calls:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Agent_and_Storage_free_text_ports_match_the_shrinking_allowlists()
    {
        var agentRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.BazaarAgent");
        var hostRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.BazaarAgentHost");
        var storageRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.Storage");

        AssertFingerprintsEqual(
            ExpectedAgentLoggerCalls,
            FingerprintMatches(agentRoot, LegacyAgentLoggerCall, relativeTo: agentRoot),
            "The BazaarAgent free-text logger surface is frozen until #54 replaces it."
        );
        AssertFingerprintsEqual(
            ExpectedHostAgentLoggerCalls,
            FingerprintMatches(hostRoot, LegacyHostAgentLoggerCall, relativeTo: hostRoot),
            "The BazaarAgent Host free-text logger surface was removed by #54 and must stay empty."
        );
        AssertFingerprintsEqual(
            ExpectedStorageLoggerCalls,
            FingerprintMatches(storageRoot, LegacyStorageLoggerCall, relativeTo: storageRoot),
            "The Storage free-text logger surface is frozen until #53 replaces it."
        );
    }

    [Fact]
    public void BazaarAgent_logger_port_accepts_only_governed_events()
    {
        var ports = File.ReadAllText(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus.BazaarAgent",
                "Contract",
                "BazaarAgentPorts.cs"
            )
        );

        Assert.Contains("void Emit(BazaarAgentLogEvent logEvent);", ports);
        Assert.DoesNotMatch(
            new Regex(
                @"void\s+(?:Info|Warning|Error)\s*\(\s*string",
                RegexOptions.CultureInvariant
            ),
            ports
        );
    }

    [Fact]
    public void Log_shaped_calls_exist_only_in_approved_adapters_or_the_exact_legacy_allowlist()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var actual = FingerprintMatches(
            sourceRoot,
            LogShapedCall,
            relativeTo: sourceRoot,
            excluded: ApprovedBepInExAdapters
        );

        AssertFingerprintsEqual(
            ExpectedNonAdapterLogShapedCalls,
            actual,
            "Direct BepInEx writes are restricted to approved adapters. The broad .Log scan also "
                + "pins known non-BepInEx calls so generic ManualLogSource receiver names cannot "
                + "escape the boundary."
        );
    }

    [Fact]
    public void BazaarAgentHost_bootstrap_uses_the_structured_adapter_before_bridge_access()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus.BazaarAgentHost",
                "BazaarAgentHostPlugin.cs"
            )
        );
        var adapter = source.IndexOf(
            "new BazaarAgentBepInExLogger(Logger)",
            StringComparison.Ordinal
        );
        var bridge = source.IndexOf("BazaarAgentGameBridge.Current", StringComparison.Ordinal);

        Assert.True(adapter >= 0 && bridge >= 0 && adapter < bridge);
        Assert.Contains("BazaarAgentLogEvents.HostInitializationFailed()", source);
        Assert.Contains("BazaarAgentLogEvents.HostInitialized()", source);
        Assert.DoesNotContain("Logger.Log", source);
    }

    [Fact]
    public void BazaarAgent_dispatcher_returns_typed_failures_without_logging()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus.BazaarAgentHost",
                "BazaarAgentGameActionDispatcher.cs"
            )
        );

        Assert.DoesNotContain("IBazaarAgentLogger", source);
        Assert.DoesNotContain("_logger", source);
        Assert.Contains("BazaarAgentDispatchDiagnostic.DispatcherException", source);
        Assert.Contains(
            "DiagnosticException",
            File.ReadAllText(
                Path.Combine(
                    RepoRoot(),
                    "src",
                    "BazaarPlusPlus.BazaarAgent",
                    "Contract",
                    "BazaarAgentPorts.cs"
                )
            )
        );
    }

    [Fact]
    public void Structured_events_can_only_use_declared_scope_tokens()
    {
        var loggingRoot = Path.Combine(
            RepoRoot(),
            "src",
            "BazaarPlusPlus",
            "Infrastructure",
            "Logging",
            "Core"
        );
        var schema = File.ReadAllText(Path.Combine(loggingRoot, "BppLogSchema.cs"));
        var renderer = File.ReadAllText(Path.Combine(loggingRoot, "BppLogEventRenderer.cs"));
        var facade = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Infrastructure", "BppLog.cs")
        );

        Assert.Contains(
            "private BppLogFeatureScope(string prefixName, string eventIdPrefix)",
            schema
        );
        Assert.Contains("BppLogFeatureScope.IsDeclared(definition.Scope)", renderer);
        Assert.DoesNotMatch(
            new Regex(
                @"(?:DebugEvent|InfoEvent|WarnEvent|ErrorEvent)\s*\([^)]*\bstring\b",
                RegexOptions.Singleline | RegexOptions.CultureInvariant
            ),
            facade
        );

        foreach (var file in ProductionFiles(Path.Combine(RepoRoot(), "src", "BazaarPlusPlus")))
        {
            if (
                Path.GetFullPath(file)
                == Path.GetFullPath(Path.Combine(loggingRoot, "BppLogSchema.cs"))
            )
                continue;
            Assert.DoesNotContain("new BppLogFeatureScope", File.ReadAllText(file));
        }
    }

    [Fact]
    public void Production_helpers_do_not_accept_dynamic_log_scope_parameters()
    {
        var dynamicLogParameter = new Regex(
            @"\bstring\s+log(?:Tag|Component|Scope)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        var violations = ProductionFiles(Path.Combine(RepoRoot(), "src", "BazaarPlusPlus"))
            .Where(file => dynamicLogParameter.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(RepoRoot(), file))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Dynamic log scope parameters are prohibited:\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Plugin_teardown_resets_static_encounter_health_owners()
    {
        var plugin = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Plugin.cs")
        );

        Assert.Contains("TooltipEncounterProbeReader.Reset();", plugin);
        Assert.Contains("BppTooltipSectionRenderPatch.ResetEncounterHealth();", plugin);
    }

    [Fact]
    public void Production_event_definitions_are_discoverable_registered_static_fields()
    {
        var violations = new List<string>();
        foreach (var file in ProductionFiles(Path.Combine(RepoRoot(), "src", "BazaarPlusPlus")))
        {
            var source = File.ReadAllText(file);
            var constructions = EventDefinitionConstruction.Matches(source);
            if (constructions.Count == 0)
                continue;

            var registered = RegisteredEventDefinition.Matches(source);
            if (
                registered.Count != constructions.Count
                || !source.Contains("[BppLogEventSource]", StringComparison.Ordinal)
            )
            {
                var relative = Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');
                violations.Add(
                    relative
                        + ": event definitions must be static readonly fields on a "
                        + "[BppLogEventSource] class"
                );
            }
        }

        Assert.True(
            violations.Count == 0,
            "Every production event definition must be visible to catalog discovery.\n"
                + string.Join("\n", violations)
        );
    }

    [Theory]
    [InlineData("private static BppLogEventDefinition Build() => new(scope, id, fields);")]
    [InlineData("private static BppLogEventDefinition Build() { return new(scope, id, fields); }")]
    [InlineData(
        "private static BppLogEventDefinition Build() => ok ? new(scope, id, fields) : fallback;"
    )]
    [InlineData("private static Func<BppLogEventDefinition> Build = () => new(scope, id, fields);")]
    [InlineData(
        "private static BppLogEventDefinition Build => ok ? new(scope, id, fields) : fallback;"
    )]
    public void Event_definition_construction_scan_recognizes_target_typed_factories(string source)
    {
        Assert.NotEmpty(EventDefinitionConstruction.Matches(source));
        Assert.Empty(RegisteredEventDefinition.Matches(source));
    }

    private static Dictionary<string, string> FingerprintMatches(
        string root,
        Regex pattern,
        string relativeTo,
        IReadOnlySet<string>? excluded = null
    )
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in ProductionFiles(root))
        {
            var relative = Path.GetRelativePath(relativeTo, file).Replace('\\', '/');
            if (excluded?.Contains(relative) == true)
                continue;

            var source = File.ReadAllText(file);
            var matches = pattern.Matches(source);
            if (matches.Count == 0)
                continue;

            var entries = new string[matches.Count];
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                entries[index] =
                    LineNumber(source, match.Index) + ":" + match.Groups["member"].Value;
            }
            fingerprints.Add(relative, string.Join(",", entries));
        }
        return fingerprints;
    }

    private static IEnumerable<string> ProductionFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
            );

    private static int LineNumber(string source, int characterIndex)
    {
        var line = 1;
        for (var index = 0; index < characterIndex; index++)
        {
            if (source[index] == '\n')
                line++;
        }
        return line;
    }

    private static void AssertFingerprintsEqual(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual,
        string message
    )
    {
        var differences = expected
            .Keys.Union(actual.Keys, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Where(path =>
                !expected.TryGetValue(path, out var expectedValue)
                || !actual.TryGetValue(path, out var actualValue)
                || !string.Equals(expectedValue, actualValue, StringComparison.Ordinal)
            )
            .Select(path =>
                path
                + ": expected="
                + (expected.TryGetValue(path, out var expectedValue) ? expectedValue : "<absent>")
                + " actual="
                + (actual.TryGetValue(path, out var actualValue) ? actualValue : "<absent>")
            )
            .ToArray();

        Assert.True(differences.Length == 0, message + "\n" + string.Join("\n", differences));
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var testDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testDir, "..", ".."));
    }
}
