#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class ReplayPlaybackStateExitCoordinatorTests
{
    [Fact]
    public void Startup_interruption_survives_fallible_cleanup_and_prevents_a_started_result()
    {
        var operation = new ReplayPlaybackLogOperation(
            "battle-private-12345678",
            CombatReplayPlaybackSource.LocalSaved,
            recordVideo: false,
            () => 100
        );
        var interruptionReason = ReplayPlaybackReasonCode.None;
        Exception? interruptionException = null;
        var restoreFailure = new InvalidOperationException("hero restore failed");
        var portraitFailure = new InvalidOperationException("portrait cleanup failed");
        var cleanupFailures = new List<(string Stage, Exception Exception)>();

        var ended = ReplayPlaybackStateExitCoordinator.Handle(
            startCoordinatorOwnsTerminal: true,
            ReplayPlaybackPublishOutcome.Success,
            (reason, exception) =>
            {
                interruptionReason = reason;
                interruptionException = exception;
            },
            (stage, exception) => cleanupFailures.Add((stage, exception)),
            new ReplayPlaybackCleanupStep(
                "hero_restore",
                () =>
                {
                    if (interruptionReason == ReplayPlaybackReasonCode.None)
                        throw new InvalidOperationException(
                            "cleanup ran before interruption latch"
                        );
                    throw restoreFailure;
                }
            ),
            new ReplayPlaybackCleanupStep("opponent_portrait", () => throw portraitFailure)
        );

        Assert.True(ended.Succeeded);
        Assert.Equal(ReplayPlaybackReasonCode.StartException, interruptionReason);
        Assert.Null(interruptionException);
        Assert.Collection(
            cleanupFailures,
            failure =>
            {
                Assert.Equal("hero_restore", failure.Stage);
                Assert.Same(restoreFailure, failure.Exception);
            },
            failure =>
            {
                Assert.Equal("opponent_portrait", failure.Stage);
                Assert.Same(portraitFailure, failure.Exception);
            }
        );

        Assert.True(
            operation.TryComplete(
                ReplayPlaybackEndReasonCode.StartFailed,
                ReplayRollbackStatus.NotRequired,
                interruptionReason,
                interruptionException,
                out var terminal
            )
        );
        Assert.Equal(ReplayPlaybackTerminalStatus.Failed, terminal.Status);
        Assert.False(operation.TryMarkStarted(out _));
    }
}
