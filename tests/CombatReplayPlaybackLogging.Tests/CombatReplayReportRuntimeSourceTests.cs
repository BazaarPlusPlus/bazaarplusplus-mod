#nullable enable
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class CombatReplayReportRuntimeSourceTests
{
    [Fact]
    public void Report_assets_are_prepared_before_replay_without_gating_exit()
    {
        var source = RuntimeSource();
        var currentStart = Segment(
            source,
            "private IEnumerator StartCurrentReplayWhenReady(",
            "private bool TryInvokeCurrentReplay("
        );
        var prepare = currentStart.IndexOf("PrepareReportAssets(", StringComparison.Ordinal);
        var complete = currentStart.IndexOf(
            "_pendingPreparedReportAssets = preparedAssets",
            StringComparison.Ordinal
        );
        Assert.True(prepare >= 0 && complete > prepare);

        var savedStart = Segment(
            source,
            "private async Task StartReplayAsync(",
            "private void OnStateChanged(StateChangedEvent data)"
        );
        Assert.True(
            savedStart.IndexOf("PrepareReportAssetsAsync(manifest)", StringComparison.Ordinal)
                < savedStart.IndexOf(
                    "ReplayBootstrap.InjectSavedReplayAsync(",
                    StringComparison.Ordinal
                )
        );
        Assert.Contains("_pendingReportAssetManifest = recordVideo ? manifest : null", savedStart);
        Assert.Contains("_pendingReportAssetRecordingId = null", savedStart);
        Assert.Contains("_currentRecording.TrackManagedReplay(battleId, source);", savedStart);

        var recordingStart = Segment(
            source,
            "private void OnVideoRecordingStarted(",
            "private void OnVideoRecordingCompleted("
        );
        Assert.Contains("_reportPublication?.ObserveVideoStarted(started)", recordingStart);
        Assert.Contains("_pendingReportAssetRecordingId = started.RecordingId", recordingStart);
        Assert.Contains("_pendingPreparedReportAssets", recordingStart);
        Assert.DoesNotContain("StartReportCardPreviewMaterialization", source);
        Assert.DoesNotContain("PostCombatReportMaterializationExitGate", source);
        Assert.DoesNotContain("PostCombatReportAssetWorkQueue", source);
    }

    [Fact]
    public void Native_pvp_presentation_is_rebuilt_only_after_replay_state_enters()
    {
        var runtimeSource = RuntimeSource();
        var savedStart = Segment(
            runtimeSource,
            "private async Task StartReplayAsync(",
            "private async Task PrepareSavedReplayNativePresentationAsync("
        );
        Assert.DoesNotContain("RebuildOpponentCollectiblesAsync", savedStart);
        Assert.DoesNotContain("EnsureTemporaryOpponentPortraitAsync", savedStart);
        Assert.Contains(
            "() => PrepareSavedReplayNativePresentationAsync(manifest, operation)",
            savedStart
        );

        var bootstrapSource = Source(
            "src",
            "BazaarPlusPlus",
            "Game",
            "CombatReplay",
            "Bootstrap",
            "ReplayBootstrap.cs"
        );
        var pushReplayState = bootstrapSource.IndexOf(
            "await AppState.TryPushState<ReplayState>();",
            StringComparison.Ordinal
        );
        var prepareNativePresentation = bootstrapSource.IndexOf(
            "await prepareNativePresentation();",
            StringComparison.Ordinal
        );
        Assert.True(
            pushReplayState >= 0 && prepareNativePresentation > pushReplayState,
            "Opponent collectibles and portrait must be rebuilt in the combat frame, after ReplayState enters."
        );

        var nativePresentationSource = Source(
            "src",
            "BazaarPlusPlus",
            "Game",
            "CombatReplay",
            "PlaybackUi",
            "ReplayNativeBoardPresentation.cs"
        );
        var clear = nativePresentationSource.IndexOf(
            "boardManager.TryClearOpponentCollectables();",
            StringComparison.Ordinal
        );
        var load = nativePresentationSource.IndexOf(
            "await boardManager.LoadOpponentCollectibles();",
            StringComparison.Ordinal
        );
        Assert.True(
            clear >= 0 && load > clear,
            "Saved replay collectible rebuild must invalidate the stale native cache before loading."
        );
        Assert.Contains(
            "SetOpponentStashVisible(boardManager, isVisible: true);",
            nativePresentationSource
        );
        Assert.Contains(
            "SetOpponentBankVisible(boardManager, ShouldShowOpponentBank(replayControlsVisible));",
            nativePresentationSource
        );
        Assert.Contains("encounterPortrait.gameObject.SetActive(false);", nativePresentationSource);

        var healthBarSource = Source(
            "src",
            "BazaarPlusPlus",
            "Game",
            "CombatReplay",
            "PlaybackUi",
            "HealthBarBinder.cs"
        );
        var ensurePortrait = Segment(
            healthBarSource,
            "internal static void EnsureOpponentPortraitVisible()",
            "internal static async Task PrepareHealthBarsAsync("
        );
        Assert.Contains(
            "ReplayNativeBoardPresentation.HideNativeEncounterPortrait();",
            ensurePortrait
        );
        Assert.DoesNotContain(
            "Data.CurrentEncounterController.ShowCard(show: true)",
            ensurePortrait
        );

        var presentation = Segment(
            runtimeSource,
            "private async Task PrepareSavedReplayNativePresentationAsync(",
            "private void OnStateChanged(StateChangedEvent data)"
        );
        Assert.Contains(
            "ReplayNativeBoardPresentation.Normalize(replayControlsVisible: true);",
            presentation
        );
        Assert.Contains(
            "var collectibles = PrepareSavedReplayOpponentCollectiblesAsync(operation);",
            presentation
        );
        Assert.Contains(
            "var portrait = PrepareSavedReplayOpponentPortraitAsync(manifest, operation);",
            presentation
        );
        Assert.Contains("await Task.WhenAll(collectibles, portrait);", presentation);

        var visualPatchSource = Source(
            "src",
            "BazaarPlusPlus",
            "Patches",
            "Combat",
            "CombatReplayVisualPatches.cs"
        );
        Assert.Contains(
            "ReplayNativeBoardPresentation.Normalize(replayControlsVisible: true);",
            visualPatchSource
        );
    }

    [Fact]
    public void Saved_replay_loading_scene_covers_parallel_startup_warmup()
    {
        var bootstrapSource = Source(
            "src",
            "BazaarPlusPlus",
            "Game",
            "CombatReplay",
            "Bootstrap",
            "ReplayBootstrap.cs"
        );
        var ensure = Segment(
            bootstrapSource,
            "internal static async Task<bool> EnsureBootstrapReadyAsync()",
            "internal static async Task HideReplayLoadingSceneAsync()"
        );
        Assert.Contains("SceneLoader.LoadSceneAdditive(SceneID.GameplayLoading)", ensure);
        Assert.DoesNotContain("UnloadScene(SceneID.GameplayLoading)", ensure);

        var inject = Segment(
            bootstrapSource,
            "internal static async Task InjectSavedReplayAsync(",
            "private static void ObserveQualityStep("
        );
        var captureCards = inject.IndexOf(
            "CaptureExpectedCombatCardInstanceIds(sequence)",
            StringComparison.Ordinal
        );
        var pushReplay = inject.IndexOf(
            "await AppState.TryPushState<ReplayState>();",
            StringComparison.Ordinal
        );
        var parallelWarmup = inject.IndexOf("await Task.WhenAll(", StringComparison.Ordinal);
        var hideLoading = inject.IndexOf(
            "await HideReplayLoadingSceneAsync();",
            StringComparison.Ordinal
        );
        var replay = inject.IndexOf("replayState.Replay();", StringComparison.Ordinal);
        Assert.True(parallelWarmup >= 0, "Independent replay warmups must run concurrently.");
        Assert.True(
            captureCards >= 0 && captureCards < pushReplay,
            "Expected combat cards must be captured before ReplayState disposes the pre-replay hand."
        );
        Assert.Contains(
            "WaitForPresentationReadyAsync(\n            expectedCombatCardInstanceIds",
            inject
        );
        Assert.True(
            hideLoading > parallelWarmup && replay > hideLoading,
            "The loading scene must remain visible until warmup completes, then hide before replay starts."
        );
        Assert.Contains("healthBarPreparation", inject);
        Assert.Contains("presentationReady", inject);
        Assert.Contains("presentationWarmup", inject);
        Assert.Contains("audioWarmup", inject);
    }

    [Fact]
    public void Startup_report_recovery_is_paged_and_cache_only()
    {
        var source = RuntimeSource();
        var recovery = Segment(
            source,
            "private void RecoverIncompleteStaticReports(",
            "private void Update()"
        );

        Assert.Contains("ListCompletedForReportRecovery(limit, cursor)", recovery);
        Assert.Contains("RecoverNextBatch(", recovery);
        Assert.Contains("yield return null;", recovery);
        Assert.Contains("StartupReportRecoveryBatchSize", source);
        Assert.Contains("cache-only", recovery);
        Assert.DoesNotContain("MaterializeBattle", recovery);
        Assert.DoesNotContain("EnqueueDeferredReportRecovery", recovery);
        Assert.DoesNotContain("RetryAfterMaterialization", recovery);
        Assert.DoesNotContain("int.MaxValue", recovery);
    }

    [Fact]
    public void Recorded_saved_replay_propagates_its_recording_identity_to_report_assets()
    {
        var recorderSource = Source(
            "src",
            "BazaarPlusPlus",
            "Game",
            "CombatReplay",
            "Video",
            "CombatReplayVideoRecorder.cs"
        );
        var savedReplayStart = Segment(
            recorderSource,
            "private void OnPlaybackStarting(CombatReplayPlaybackStarting evt)",
            "private void BeginPreparedCurrentReplay(CombatReplayPlaybackStarting evt)"
        );
        Assert.Contains("if (!evt.RecordVideo)", savedReplayStart);
        var beginRecording = savedReplayStart.IndexOf(
            "BeginRecording(operation, request, services)",
            StringComparison.Ordinal
        );
        var publishStarted = savedReplayStart.IndexOf(
            "PublishRecordingStarted(services, operation)",
            StringComparison.Ordinal
        );
        Assert.True(
            beginRecording >= 0 && publishStarted > beginRecording,
            "A saved replay must publish its concrete recording identity after recording begins."
        );

        var runtimeSource = RuntimeSource();
        var recordingStart = Segment(
            runtimeSource,
            "private void OnVideoRecordingStarted(",
            "private void OnVideoRecordingCompleted("
        );
        Assert.Contains("_reportPublication?.ObserveVideoStarted(started)", recordingStart);
        Assert.Contains("_pendingReportAssetRecordingId = started.RecordingId", recordingStart);

        Assert.Contains("_pendingPreparedReportAssets", recordingStart);
        Assert.Contains("MarkAssetsReady(", recordingStart);
    }

    [Fact]
    public void Recording_scope_blocks_native_hover_motion_and_tooltips()
    {
        var recorderSource = Source(
            "src",
            "BazaarPlusPlus",
            "Game",
            "CombatReplay",
            "Video",
            "CombatReplayVideoRecorder.cs"
        );
        var beginSuppression = Segment(
            recorderSource,
            "private static IDisposable? BeginUiSuppression(",
            "private void DisposeUiState()"
        );
        Assert.Contains("UiSuppressionScope.Begin(", beginSuppression);
        Assert.Contains("ReplayRecordingHoverSuppression.Begin", beginSuppression);
        Assert.Contains("ReplayRecordingMotionSuppression.Begin", beginSuppression);

        var suppressionSource = Source(
            "src",
            "BazaarPlusPlus",
            "Game",
            "CombatReplay",
            "Video",
            "ReplayRecordingHoverSuppression.cs"
        );
        Assert.Contains("tooltipParent.UnlockCardTooltipController();", suppressionSource);
        Assert.Contains("tooltipParent.HideCardTooltipController();", suppressionSource);
        Assert.Contains("controller.TriggerUnhover();", suppressionSource);
        Assert.Contains("controller.ResetPosition();", suppressionSource);
        Assert.Contains("renderer.OnPointerExit(null)", suppressionSource);

        var patchSource = Source(
            "src",
            "BazaarPlusPlus",
            "Patches",
            "Combat",
            "ReplayRecordingHoverPatches.cs"
        );
        Assert.Contains(
            "typeof(CardController), nameof(CardController.OnPointerEnter)",
            patchSource
        );
        Assert.Contains(
            "typeof(ItemController), nameof(ItemController.OnPointerMove)",
            patchSource
        );

        var combatSimulationPatchSource = Source(
            "src",
            "BazaarPlusPlus",
            "Patches",
            "Combat",
            "CombatSimulationPatches.cs"
        );
        Assert.Contains(
            "if (!ReplayRecordingMotionSuppression.IsActive)",
            combatSimulationPatchSource
        );
        Assert.Contains(
            "Singleton<GameServiceManager>.Instance?.EnforceMaxTimeScale(1f);",
            combatSimulationPatchSource
        );
        Assert.Contains("__result = Task.CompletedTask;", combatSimulationPatchSource);
        Assert.Contains(
            "typeof(SkillProxyRenderer), nameof(SkillProxyRenderer.OnPointerEnter)",
            patchSource
        );
        Assert.Contains(
            "typeof(RecapItemVisualController), nameof(RecapItemVisualController.OnPointerEnter)",
            patchSource
        );
        Assert.Contains("nameof(TooltipParentComponent.ShowCardTooltipController)", patchSource);
        Assert.Contains(
            "nameof(TooltipParentComponent.ShowAuxiliaryTooltipController)",
            patchSource
        );
    }

    private static string RuntimeSource()
    {
        return Source("src", "BazaarPlusPlus", "Game", "CombatReplay", "CombatReplayRuntime.cs");
    }

    private static string Source(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var path = new string[pathSegments.Length + 1];
        path[0] = directory!.FullName;
        Array.Copy(pathSegments, 0, path, 1, pathSegments.Length);
        return File.ReadAllText(Path.Combine(path));
    }

    private static string Segment(string source, string startToken, string endToken)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Missing source segment: {startToken}");
        return source.Substring(start, end - start);
    }
}
