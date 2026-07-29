#nullable enable
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class CombatReplayReportRuntimeSourceTests
{
    [Fact]
    public void Native_pvp_presentation_is_rebuilt_after_replay_state_enters()
    {
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
        var parallelWarmupEnd = inject.IndexOf(");", parallelWarmup, StringComparison.Ordinal);
        var presentationReadyParticipant = inject.IndexOf(
            "presentationReady",
            parallelWarmup,
            StringComparison.Ordinal
        );
        var hideLoading = inject.IndexOf(
            "await HideReplayLoadingSceneAsync();",
            StringComparison.Ordinal
        );
        var replay = inject.IndexOf("replayState.Replay();", StringComparison.Ordinal);
        Assert.True(
            captureCards >= 0 && captureCards < pushReplay,
            "Expected combat cards must be captured before ReplayState disposes the pre-replay hand."
        );
        var presentationReadySetup = Segment(
            inject,
            "var presentationReady =",
            "var presentationWarmup ="
        );
        Assert.Contains("WaitForPresentationReadyAsync(", presentationReadySetup);
        Assert.Contains("expectedCombatCardInstanceIds", presentationReadySetup);
        Assert.True(
            pushReplay > captureCards
                && parallelWarmup > pushReplay
                && presentationReadyParticipant > parallelWarmup
                && presentationReadyParticipant < parallelWarmupEnd
                && hideLoading > parallelWarmupEnd
                && replay > hideLoading,
            "The loading scene must remain visible until warmup completes, then hide before replay starts."
        );
    }

    [Fact]
    public void Recording_scope_patches_all_native_hover_and_terminal_motion_entry_points()
    {
        var recorderSource = Source(
            "src",
            "BazaarPlusPlus",
            "Game",
            "CombatReplay",
            "Video",
            "CombatReplayVideoRecorder.cs"
        );
        var beginUiSuppression = Segment(
            recorderSource,
            "private static IDisposable? BeginUiSuppression(",
            "private void DisposeUiState()"
        );
        var requiredSuppressionScopes = new[]
        {
            "UiSuppressionScope.Begin(",
            "ReplayRecordingHoverSuppression.Begin",
            "ReplayRecordingMotionSuppression.Begin",
        };
        Assert.All(requiredSuppressionScopes, scope => Assert.Contains(scope, beginUiSuppression));

        var patchSource = Source(
            "src",
            "BazaarPlusPlus",
            "Patches",
            "Combat",
            "ReplayRecordingHoverPatches.cs"
        );
        var requiredHoverTargets = new[]
        {
            "typeof(CardController), nameof(CardController.OnPointerEnter)",
            "typeof(ItemController), nameof(ItemController.OnPointerMove)",
            "typeof(SkillProxyRenderer), nameof(SkillProxyRenderer.OnPointerEnter)",
            "typeof(RecapItemVisualController), nameof(RecapItemVisualController.OnPointerEnter)",
            "nameof(TooltipParentComponent.ShowCardTooltipController)",
            "nameof(TooltipParentComponent.ShowAuxiliaryTooltipController)",
        };
        Assert.All(requiredHoverTargets, target => Assert.Contains(target, patchSource));

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
            "__result = ReplayRecordingMotionSuppression.HoldTerminalPresentationAsync();",
            combatSimulationPatchSource
        );
        Assert.DoesNotContain("__result = Task.CompletedTask;", combatSimulationPatchSource);
    }

    [Fact]
    public void Recording_start_forwards_identity_and_prepared_assets_to_report_publication()
    {
        var runtimeSource = Source(
            "src",
            "BazaarPlusPlus",
            "Game",
            "CombatReplay",
            "CombatReplayRuntime.cs"
        );
        var recordingStart = Segment(
            runtimeSource,
            "private void OnVideoRecordingStarted(",
            "private void OnVideoRecordingCompleted("
        );
        Assert.Contains("_reportPublication?.ObserveVideoStarted(started)", recordingStart);

        var assetPublication = Segment(
            recordingStart,
            "_reportPublication?.MarkAssetsReady(",
            ");"
        );
        Assert.Contains("started.RecordingId", assetPublication);
        Assert.Contains("started.BattleId", assetPublication);
        Assert.Contains("_pendingPreparedReportAssets", assetPublication);
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
