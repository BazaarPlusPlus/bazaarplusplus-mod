#nullable enable
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class ReplayPlaybackRuntimeOwnershipTests
{
    [Fact]
    public void Startup_state_exit_cannot_steal_the_start_coordinators_terminal()
    {
        var source = RuntimeSource();
        var stateChanged = Segment(
            source,
            "private void OnStateChanged(StateChangedEvent data)",
            "internal static bool TryExitBootstrappedSavedReplayToMenu()"
        );

        var guard = stateChanged.IndexOf("startCoordinatorOwnsTerminal", StringComparison.Ordinal);
        var earlyReturn = stateChanged.IndexOf(
            "if (startCoordinatorOwnsTerminal)",
            StringComparison.Ordinal
        );
        var terminal = stateChanged.IndexOf("CompletePlaybackOperation(", StringComparison.Ordinal);

        Assert.True(guard >= 0, "State exit must inspect startup ownership.");
        Assert.True(
            earlyReturn > guard,
            "Startup ownership must return before terminal selection."
        );
        Assert.True(terminal > earlyReturn, "Only the normal state-exit path may complete here.");
        Assert.Contains("_startupInterruptionReason", stateChanged);
        Assert.Contains("ReplayPlaybackStateExitCoordinator.Handle", stateChanged);

        var start = Segment(
            source,
            "private async Task StartReplayAsync(",
            "private void OnStateChanged(StateChangedEvent data)"
        );
        Assert.True(
            start.IndexOf("ReplayPlaybackStartInterruptedException", StringComparison.Ordinal)
                < start.IndexOf("TryMarkStarted", StringComparison.Ordinal),
            "A startup state exit must become an explicit failed outcome before start can emit."
        );
    }

    [Fact]
    public void Async_void_menu_return_waits_for_scene_confirmation_or_deadline()
    {
        var source = RuntimeSource();
        var observer = Segment(
            source,
            "private void ObservePendingMenuReturn()",
            "private static ReplayMenuReturnOutcome TryBeginReturnToMainMenu()"
        );

        Assert.Contains("SceneLoader.IsSceneLoaded(SceneID.HeroSelectScene)", observer);
        Assert.Contains("pending.DeadlineRealtimeSeconds", observer);
        Assert.Contains("ReplayPlaybackReasonCode.MenuReturnFailed", observer);

        var exit = Segment(
            source,
            "private void ExitBootstrappedSavedReplayToMenu()",
            "private void BeginPendingMenuReturn("
        );
        Assert.Contains("BeginPendingMenuReturn(", exit);
        Assert.DoesNotContain("CompletePlaybackOperation(", exit);
    }

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
    public void Startup_report_recovery_is_paged_and_cache_only()
    {
        var source = RuntimeSource();
        var recovery = Segment(
            source,
            "private void RecoverIncompleteStaticReports(",
            "private void Update()"
        );

        Assert.Contains("ListCompletedForReportRecovery(limit, offset)", recovery);
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
