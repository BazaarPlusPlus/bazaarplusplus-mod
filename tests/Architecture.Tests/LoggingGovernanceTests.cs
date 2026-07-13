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
            ["BazaarAgentReplayRecorderWiring.cs"] = "101:Info",
            ["Core/Events/InMemoryBppEventBus.cs"] = "69:Error",
            ["Core/Runtime/BppFeatureRegistry.cs"] = "26:Error,45:Error",
            ["Game/CombatStatusBar/CombatStatusBar.Config.cs"] = "22:Info",
            ["Game/EventPreview/EventPreviewPlanController.cs"] =
                "64:Warn,110:Info,147:Error,154:Info,165:Error",
            ["Game/Input/BppHotkeyService.cs"] = "85:Warn,97:Warn,304:Info",
            ["Game/ItemEnchantPreview/ItemEnchantPreviewFormatting.cs"] = "93:Debug",
            ["Game/ItemEnchantPreview/Preview/ItemEnchantPreviewRenderer.cs"] =
                "72:Debug,91:Debug,158:Debug",
            ["Game/Lobby/MainMenuVersionCheckController.cs"] =
                "80:Warn,101:Warn,113:Warn,122:Info,130:Warn,137:Warn",
            ["Game/Lobby/RandomHeroPool/RandomHeroPoolNativeController.cs"] = "64:Warn,222:Warn",
            ["Game/Lobby/RandomPoolPrefsHelpers.cs"] = "31:Warn,64:Warn",
            ["Game/PvpBattles/PvpBattleSnapshotCollector.cs"] =
                "226:Warn,242:Warn,324:Warn,343:Warn,364:Warn,408:Warn",
            ["Game/RunLogging/RunLogStoreLoggerBridge.cs"] = "10:Warn,13:Error",
            ["Game/Settings/BppDockButtonSpriteProvider.cs"] = "41:Warn,62:Warn",
            ["Game/Settings/BppNativeSettingsButtonClone.cs"] = "72:Debug",
            ["Game/Settings/BppNativeSettingsSectionController.cs"] =
                "95:Warn,109:Warn,116:Warn,170:Info,194:Error,445:Warn,460:Warn,467:Info,477:Info,484:Warn,535:Info",
            ["Game/Supporters/BPPSupporterCatalog.cs"] =
                "118:Info,125:Warn,163:Info,170:Warn,186:Warn",
            ["Game/Tooltips/CardTooltipDataFactory.cs"] = "135:Warn",
            ["Game/Tooltips/TooltipModifierRefreshController.cs"] = "59:Error,163:Debug",
            ["Game/Tooltips/TooltipPreviewTargetResolver.cs"] =
                "50:Debug,60:Debug,70:Debug,79:Debug,87:Debug",
            ["GameInterop/BppClientCacheBridge.cs"] = "51:Debug,153:Debug",
            ["GameInterop/CardPreview/NativeCardPreviewAssetLoader.cs"] =
                "35:Warn,60:Warn,71:Warn,86:Warn",
            ["GameInterop/CardPreview/NativeCardPreviewFactory.cs"] = "72:Warn,146:Debug,153:Warn",
            ["GameInterop/CardPreview/NativeCardPreviewHoverRelay.cs"] = "80:Debug,88:Debug",
            ["GameInterop/CardPreview/NativeCardPreviewReflection.cs"] =
                "131:Debug,139:Debug,146:Debug",
            ["GameInterop/CardPreview/NativeCardPreviewRuntime.cs"] =
                "52:Warn,68:Warn,101:Warn,109:Warn",
            ["GameInterop/Encounter/EncounterStateProbe.cs"] = "32:Error,174:Error",
            ["GameInterop/Encounter/InteractionFilterProbe.cs"] = "34:Info,53:Error",
            ["GameInterop/Encounter/PedestalEligibilityProbe.cs"] = "41:Info,46:Info,69:Info",
            ["GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs"] =
                "48:Warn,57:Warn,70:Warn,82:Warn",
            ["GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs"] =
                "46:Warn,57:Warn,66:Debug,74:Warn",
            ["GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs"] = "526:Warn,549:Warn",
            ["GameInterop/LiveCards/LiveCardSnapshotReader.cs"] = "40:Warn,87:Warn",
            ["GameInterop/Localization/ChineseTranslationCatalog.cs"] = "105:Info,135:Warn",
            ["GameInterop/Localization/NativeChineseFontFallback.cs"] =
                "81:Warn,141:Warn,192:Warn,202:Info,218:Warn",
            ["GameInterop/TagTypography/KeywordIconSpriteProvider.cs"] = "62:Warn",
            ["GameInterop/TagTypography/NativeTagTypography.cs"] = "255:Warn",
            ["Infrastructure/FileBackedPayloadStore.cs"] = "118:Warn,124:Warn",
            ["Infrastructure/Fonts/BppTmpFont.cs"] = "89:Info,152:Debug,184:Warn",
            ["Infrastructure/Fonts/BppUiFont.cs"] = "67:Info,76:Warn,83:Info",
            ["Patches/CollectionPanel/CollectionItemLoadArtPatch.cs"] = "140:Warn",
            ["Patches/CollectionPanel/CollectionTierTooltipPatch.cs"] =
                "29:Error,53:Error,87:Error",
            ["Patches/Lobby/MainMenuVersionLabelPatches.cs"] = "31:Warn",
            ["Patches/Lobby/RandomHeroPoolPatches.cs"] =
                "29:Warn,106:Warn,123:Warn,142:Warn,168:Warn",
            ["Patches/Lobby/RandomHeroSkinPoolPatches.cs"] =
                "33:Warn,46:Warn,62:Warn,80:Warn,97:Warn,123:Warn,149:Warn,173:Warn,198:Warn",
            ["Patches/NameOverride/NameOverridePatches.cs"] =
                "43:Debug,76:Debug,112:Debug,128:Debug",
            ["Patches/Settings/BppKeybindSettingsPatch.cs"] = "59:Warn,213:Error,271:Error",
            ["Patches/Settings/BppNativeSettingsSectionPatch.cs"] = "23:Error",
            ["Patches/Settings/BppSettingsDockPatch.cs"] = "37:Error,82:Error",
            ["Patches/Settings/OptionsDialogLanguageRefreshPatch.cs"] = "25:Error",
            ["Patches/Settings/SettingsMenuToggleInstaller.cs"] = "37:Debug,62:Info",
            ["Patches/Tooltips/AggregateItemMissingTypesTooltipPatch.cs"] = "47:Error",
            ["Patches/Tooltips/BilingualItemNamePatch.cs"] = "58:Error",
            ["Patches/Tooltips/BppTooltipSections.cs"] = "142:Info,183:Info",
            ["Patches/Tooltips/EncounterEventTooltipPatch.cs"] = "70:Error,144:Warn",
            ["Patches/Tooltips/HeroLevelRewardsTooltipPatch.cs"] =
                "48:Debug,80:Debug,92:Debug,104:Error",
            ["Patches/Tooltips/ItemEnchantPreviewPatch.cs"] = "87:Error",
            ["Plugin.cs"] =
                "47:Info,58:Info,60:Warn,66:Info,83:Info,85:Info,87:Info,91:Error,149:Error,211:Warn,221:Info,226:Info,246:Error,251:Warn,255:Info",
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
