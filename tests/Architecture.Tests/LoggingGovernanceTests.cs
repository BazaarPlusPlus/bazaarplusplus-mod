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
            ["BazaarAgentReplayRecorderWiring.cs"] = "94:Info",
            ["Core/Events/InMemoryBppEventBus.cs"] = "69:Error",
            ["Core/Runtime/BppFeatureRegistry.cs"] = "26:Error,45:Error",
            ["Game/CollectionPanel/CollectionPanel.cs"] =
                "174:Warn,180:Info,200:Debug,236:Warn,256:Warn,269:Warn,282:Warn,303:Info,550:Warn,797:Error",
            ["Game/CollectionPanel/CollectionPanelDockButtonController.cs"] =
                "85:Warn,129:Debug,131:Warn",
            ["Game/CollectionPanel/CollectionPanelHeroPreferenceStore.cs"] =
                "32:Warn,42:Warn,69:Warn",
            ["Game/CollectionPanel/CollectionPanelLoadDiagnostics.cs"] = "30:Info",
            ["Game/CollectionPanel/CollectionPanelMount.cs"] = "39:Warn,50:Info",
            ["Game/CollectionPanel/Data/CollectionCatalog.cs"] =
                "45:Info,88:Debug,98:Warn,125:Info,135:Info",
            ["Game/CollectionPanel/Grid/CollectionCardArtCache.cs"] =
                "68:Warn,78:Warn,144:Debug,191:Debug",
            ["Game/CollectionPanel/Grid/CollectionCardFactory.cs"] = "81:Warn,130:Debug",
            ["Game/CollectionPanel/Grid/CollectionCardHoverRelay.cs"] = "74:Debug,82:Debug",
            ["Game/CollectionPanel/Grid/CollectionCardMaterialCache.cs"] = "82:Debug,106:Debug",
            ["Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs"] =
                "399:Warn,826:Debug,861:Debug,1258:Debug,1317:Debug",
            ["Game/CollectionPanel/Sources/CollectionSourceCatalog.cs"] =
                "79:Info,87:Error,486:Warn",
            ["Game/CombatReplay/Audio/CoreAudioProcessTapCaptureTap.cs"] =
                "67:Warn,83:Info,88:Warn,129:Warn,183:Warn",
            ["Game/CombatReplay/Audio/ReplayAudioCaptureFactory.cs"] = "76:Warn",
            ["Game/CombatReplay/Audio/ReplayAudioTapStopper.cs"] = "49:Warn,54:Info",
            ["Game/CombatReplay/Audio/WasapiLoopbackCaptureTap.cs"] =
                "97:Warn,111:Warn,120:Warn,130:Warn,147:Warn,156:Warn,168:Warn,182:Info,190:Warn,223:Warn,237:Warn,314:Warn",
            ["Game/CombatReplay/Bootstrap/ReplayBootstrap.cs"] =
                "25:Info,55:Info,74:Info,118:Info,125:Warn,147:Error",
            ["Game/CombatReplay/Bootstrap/SnapshotRehydrator.cs"] =
                "31:Warn,49:Warn,67:Warn,86:Warn,106:Info",
            ["Game/CombatReplay/Bootstrap/SocketBehaviorBridge.cs"] = "44:Warn,69:Warn",
            ["Game/CombatReplay/CombatReplayPersistenceQueue.cs"] = "92:Warn,131:Warn",
            ["Game/CombatReplay/CombatReplayRuntime.cs"] =
                "228:Error,245:Warn,287:Warn,387:Info,396:Error,441:Info,449:Error,496:Info,501:Error",
            ["Game/CombatReplay/PlaybackUi/OpponentPortraitController.cs"] =
                "44:Warn,56:Warn,86:Warn,101:Warn,128:Warn,188:Warn",
            ["Game/CombatReplay/PlaybackUi/PlayerAttributeRepairer.cs"] =
                "67:Warn,99:Warn,130:Warn",
            ["Game/CombatReplay/ReplayPersistenceOrchestrator.cs"] =
                "64:Error,73:Info,110:Warn,119:Warn",
            ["Game/CombatReplay/ReplayPlaybackPublisher.cs"] = "38:Info,68:Error,104:Error",
            ["Game/CombatReplay/Video/CombatReplayVideoRecorder.cs"] =
                "45:Warn,77:Info,101:Debug,105:Debug,114:Info,121:Warn,145:Info,151:Warn,157:Warn,176:Error,235:Info,242:Error,319:Info,357:Warn,395:Warn,446:Warn,540:Warn,592:Debug,631:Warn,691:Debug,705:Warn,725:Debug",
            ["Game/CombatReplay/Video/FfmpegLocator.cs"] =
                "27:Info,34:Info,112:Debug,150:Warn,161:Debug",
            ["Game/CombatReplay/Video/FfmpegRawVideoEncoder.cs"] =
                "128:Info,171:Warn,187:Warn,200:Warn,275:Debug,311:Error,338:Warn,348:Debug",
            ["Game/CombatReplay/Video/ReplayVideoAudioMuxer.cs"] =
                "99:Error,115:Debug,307:Info,363:Debug,382:Warn,389:Info,400:Warn,415:Warn,577:Warn,587:Debug,665:Debug,729:Debug,762:Info,769:Debug",
            ["Game/CombatReplay/Video/ReplayVideoCaptureSession.cs"] =
                "128:Info,236:Warn,259:Error,266:Info,324:Error,342:Debug,385:Debug,479:Debug",
            ["Game/CombatReplay/Warmup/AudioBankWarmer.cs"] =
                "26:Warn,37:Warn,61:Warn,79:Info,88:Warn,99:Info,109:Warn,116:Info,127:Warn,155:Warn,164:Info,172:Info,190:Warn,201:Warn,212:Info,229:Warn,238:Info,245:Warn",
            ["Game/CombatReplay/Warmup/CombatVfxWarmer.cs"] = "29:Warn,232:Debug",
            ["Game/CombatReplay/Warmup/PresentationWarmer.cs"] =
                "37:Info,56:Warn,73:Warn,137:Warn,171:Debug",
            ["Game/CombatReplay/Warmup/SoundtrackWarmer.cs"] =
                "48:Warn,63:Warn,73:Warn,86:Info,134:Warn,158:Warn,196:Warn,228:Warn,251:Warn,259:Warn,317:Warn,342:Warn",
            ["Game/CombatStatusBar/CombatStatusBar.Config.cs"] = "22:Info",
            ["Game/EventPreview/EventPreviewPlanController.cs"] =
                "64:Warn,110:Info,147:Error,154:Info,165:Error",
            ["Game/HistoryPanel/Data/HistoryBattlePreviewProjection.cs"] = "319:Warn,344:Error",
            ["Game/HistoryPanel/Ghost/GhostBattleSyncService.cs"] = "118:Debug,161:Warn,206:Warn",
            ["Game/HistoryPanel/HistoryPanel.cs"] =
                "151:Warn,179:Warn,186:Error,438:Info,455:Warn,466:Info,473:Error",
            ["Game/HistoryPanel/HistoryPanelCoordinator.cs"] =
                "123:Error,161:Error,350:Info,399:Error,455:Error,535:Error,551:Info,558:Warn,631:Error,656:Info,727:Info,774:Error,790:Error,863:Error",
            ["Game/HistoryPanel/HistoryPanelMount.cs"] = "39:Warn,46:Warn,63:Warn",
            ["Game/HistoryPanel/HistoryPanelReplayService.cs"] = "245:Warn",
            ["Game/HistoryPanel/Storage/HistoryPanelRepository.cs"] = "201:Warn",
            ["Game/Input/BppHotkeyService.cs"] = "85:Warn,97:Warn,304:Info",
            ["Game/ItemEnchantPreview/ItemEnchantPreviewFormatting.cs"] = "96:Debug",
            ["Game/ItemEnchantPreview/Preview/ItemEnchantPreviewRenderer.cs"] =
                "72:Debug,91:Debug,158:Debug",
            ["Game/LiveBuildPanel/LiveBuildPanel.cs"] = "226:Info,234:Warn",
            ["Game/LiveBuildPanel/LiveBuildPanelMount.cs"] = "25:Warn,31:Info",
            ["Game/LiveBuildPanel/Recommendations/BuildRecommendationRefreshService.cs"] =
                "32:Info",
            ["Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs"] =
                "232:Debug,253:Warn,266:Info,286:Warn,315:Info,343:Warn,359:Warn,390:Info,398:Warn,438:Info,445:Warn,490:Info,499:Warn,520:Info,528:Warn,541:Warn,564:Warn",
            ["Game/Lobby/MainMenuVersionCheckController.cs"] =
                "80:Warn,101:Warn,113:Warn,122:Info,130:Warn,137:Warn",
            ["Game/Lobby/RandomHeroPool/RandomHeroPoolNativeController.cs"] = "64:Warn,222:Warn",
            ["Game/Lobby/RandomPoolPrefsHelpers.cs"] = "31:Warn,64:Warn",
            ["Game/OverlayPanels/OverlayPanelHost.cs"] = "59:Warn,111:Warn,146:Warn",
            ["Game/OverlayPanels/OverlayPanelHostMount.cs"] = "18:Info",
            ["Game/PvpBattles/PvpBattleSnapshotCollector.cs"] =
                "226:Warn,242:Warn,324:Warn,343:Warn,364:Warn,408:Warn",
            ["Game/RunLifecycle/RunLifecycleModule.cs"] = "39:Info,64:Info,105:Debug",
            ["Game/RunLogging/RunLogStoreLoggerBridge.cs"] = "10:Warn,13:Error",
            ["Game/RunLogging/RunLoggingController.cs"] = "81:Info",
            ["Game/RunLogging/RunLoggingModule.cs"] =
                "115:Error,145:Error,182:Error,225:Error,238:Error,289:Warn,305:Warn,314:Warn,352:Warn,359:Warn",
            ["Game/RunLogging/Upload/RunBundleUploadFeed.cs"] = "44:Warn,65:Info,88:Error",
            ["Game/RunLogging/Upload/RunBundleUploadService.cs"] = "51:Info,60:Info,128:Debug",
            ["Game/Screenshots/EndOfRunCaptureReadinessDetector.cs"] = "72:Warn,98:Warn",
            ["Game/Screenshots/EndOfRunScreenshotController.cs"] =
                "62:Warn,211:Debug,218:Warn,256:Warn,269:Warn,305:Warn,317:Error,392:Warn,399:Error,430:Warn,432:Error,444:Warn,488:Warn,500:Warn,536:Error,577:Error",
            ["Game/Screenshots/EndOfRunSummaryRevealDetector.cs"] = "288:Warn",
            ["Game/Screenshots/ScreenshotService.cs"] = "60:Error,71:Info,79:Error,218:Debug",
            ["Game/Screenshots/Upload/BazaarDbSnapshotUploadFeed.cs"] = "36:Warn,63:Info,77:Error",
            ["Game/Screenshots/Upload/BazaarDbSnapshotUploadService.cs"] =
                "65:Info,74:Info,86:Warn,94:Debug",
            ["Game/Screenshots/Upload/BazaarDbSnapshotUploadSettingsDockEntry.cs"] = "39:Info",
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
            ["Game/Upload/BackgroundUploadPump.cs"] = "40:Info,120:Warn",
            ["Game/Upload/StartupUploadAttemptRunner.cs"] = "127:Info,138:Info,151:Error,166:Error",
            ["Game/VoiceSubtitles/VoiceSubtitlesLog.cs"] = "16:Info,18:Debug,20:Warn,22:Error",
            ["GameInterop/BppClientCacheBridge.cs"] = "51:Debug,153:Debug",
            ["GameInterop/CardPreview/NativeCardPreviewAssetLoader.cs"] =
                "35:Warn,60:Warn,71:Warn,86:Warn",
            ["GameInterop/CardPreview/NativeCardPreviewFactory.cs"] = "72:Warn,146:Debug,153:Warn",
            ["GameInterop/CardPreview/NativeCardPreviewHoverRelay.cs"] = "80:Debug,88:Debug",
            ["GameInterop/CardPreview/NativeCardPreviewReflection.cs"] =
                "131:Debug,139:Debug,146:Debug",
            ["GameInterop/CardPreview/NativeCardPreviewRuntime.cs"] =
                "52:Warn,68:Warn,101:Warn,109:Warn",
            ["GameInterop/Encounter/EncounterStateProbe.cs"] = "131:Error,156:Error",
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
            ["GameInterop/VoiceSubtitles/VoiceSubtitlesInteropLog.cs"] = "16:Info,18:Debug,20:Warn",
            ["Infrastructure/FileBackedPayloadStore.cs"] = "71:Warn,79:Warn",
            ["Infrastructure/Fonts/BppTmpFont.cs"] = "70:Info,91:Debug,123:Warn",
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
            ["Patches/Tooltips/BppTooltipSections.cs"] = "136:Info,177:Info",
            ["Patches/Tooltips/EncounterEventTooltipPatch.cs"] = "63:Error,137:Warn",
            ["Patches/Tooltips/HeroLevelRewardsTooltipPatch.cs"] =
                "42:Debug,74:Debug,86:Debug,98:Error",
            ["Patches/Tooltips/ItemEnchantPreviewPatch.cs"] = "87:Error",
            ["Patches/Tooltips/QuestRewardPreviewTooltipPatch.cs"] = "71:Error",
            ["Plugin.cs"] =
                "47:Info,58:Info,60:Warn,66:Info,83:Info,85:Info,87:Info,91:Error,149:Error,211:Warn,221:Info,226:Info,246:Error,251:Warn,255:Info",
        };

    private static readonly IReadOnlyDictionary<string, string> ExpectedVoiceSubtitlesMembers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Game/VoiceSubtitles/FontDiagnostics.cs"] =
                "69:Warn,77:Info,80:Field,81:Field,87:Warn,104:Info,124:Info,126:Field,133:Warn",
            ["Game/VoiceSubtitles/VersionLabelScanner.cs"] = "113:Info",
            ["Game/VoiceSubtitles/VoiceLineDisplay.cs"] =
                "87:Info,97:Info,104:Warn,123:Verbose,125:Debug,140:Verbose,142:Debug,150:Field,151:Field,209:Warn,222:Verbose,224:Debug,296:Warn,650:Field",
            ["Game/VoiceSubtitles/VoiceLineOverlayLifetime.cs"] =
                "47:Verbose,49:Debug,80:Verbose,91:Verbose,104:Verbose,107:Debug,130:Warn,143:Warn",
            ["Game/VoiceSubtitles/VoiceLinesDocument.cs"] = "50:Warn,58:Warn,68:Warn",
            ["Game/VoiceSubtitles/VoiceLinesRepository.cs"] =
                "48:Debug,59:Info,77:Warn,82:Info,105:Info,160:Info,168:Warn,188:Info,195:Warn,211:Warn,226:Info,231:Warn,271:Info,277:Warn,309:Warn",
            ["GameInterop/VoiceSubtitles/VoiceLineVoObserverBridge.cs"] =
                "40:Info,41:ObjectId,48:Verbose,50:Debug,54:ObjectId,57:Field,58:Field,87:Warn,118:Warn,140:Verbose,142:Debug,176:Warn,178:ObjectId,197:Verbose,199:Debug,202:ObjectId,203:ObjectId,206:Field,208:Field,214:Verbose,216:Debug,221:Field,228:Verbose,230:Debug,235:Field,242:Field,243:Field,251:Verbose,253:Debug,256:ObjectId,272:Warn,274:Field,283:Verbose,286:Debug,294:Field,295:Field,296:Field,307:Verbose,309:Debug,314:Field,319:Field,320:Field,326:Verbose,328:Debug,342:Verbose,344:Debug,371:Verbose,393:Warn,408:Warn,458:Warn,471:Warn,484:Warn",
            ["Patches/VoiceSubtitles/VOPlayerPatches.cs"] =
                "27:Warn,71:Warn,87:Warn,102:Info,104:Warn",
        };

    private static readonly IReadOnlyDictionary<string, string> ExpectedAgentLoggerCalls =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Runtime/BazaarAgentReplayControlProcessor.cs"] = "38:Error",
            ["Runtime/BazaarAgentRuntimeController.cs"] =
                "80:Info,98:Error,119:Info,138:Info,142:Error,155:Warning,164:Warning,173:Warning,205:Error,305:Error",
            ["Transport/BazaarAgentHttpServer.cs"] =
                "76:Warning,84:Warning,92:Warning,116:Warning,165:Error,172:Warning,183:Warning,323:Error,374:Warning,411:Warning",
        };

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

    // The host entry is the #54 sink bypass. CollectionPanel's call is a non-BepInEx Log-shaped
    // method; pinning it here lets the broad generic .Log scan catch any new ManualLogSource
    // receiver name without misclassifying this known call.
    private static readonly IReadOnlyDictionary<string, string> ExpectedNonAdapterLogShapedCalls =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BazaarPlusPlus.BazaarAgentHost/BazaarAgentHostPlugin.cs"] = "31:LogError",
            ["BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs"] = "862:Log",
        };

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
    public void Agent_and_Storage_free_text_ports_match_the_shrinking_allowlists()
    {
        var agentRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.BazaarAgent");
        var storageRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.Storage");

        AssertFingerprintsEqual(
            ExpectedAgentLoggerCalls,
            FingerprintMatches(agentRoot, LegacyAgentLoggerCall, relativeTo: agentRoot),
            "The BazaarAgent free-text logger surface is frozen until #54 replaces it."
        );
        AssertFingerprintsEqual(
            ExpectedStorageLoggerCalls,
            FingerprintMatches(storageRoot, LegacyStorageLoggerCall, relativeTo: storageRoot),
            "The Storage free-text logger surface is frozen until #53 replaces it."
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
