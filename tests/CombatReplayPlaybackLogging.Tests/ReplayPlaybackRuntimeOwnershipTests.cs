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

    private static string RuntimeSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(
            Path.Combine(
                directory!.FullName,
                "src",
                "BazaarPlusPlus",
                "Game",
                "CombatReplay",
                "CombatReplayRuntime.cs"
            )
        );
    }

    private static string Segment(string source, string startToken, string endToken)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Missing source segment: {startToken}");
        return source.Substring(start, end - start);
    }
}
