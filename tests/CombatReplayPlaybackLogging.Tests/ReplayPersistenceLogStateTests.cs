#nullable enable
using System.Collections.Concurrent;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class ReplayPersistenceLogStateTests : IDisposable
{
    public ReplayPersistenceLogStateTests() => BppLog.Reset();

    public void Dispose() => BppLog.Reset();

    [Fact]
    public void Persistence_completion_is_one_shot_under_worker_shutdown_races()
    {
        var gate = new ReplayPersistenceCompletionGate();
        var winners = new ConcurrentBag<int>();

        Parallel.For(
            0,
            128,
            index =>
            {
                if (gate.TryComplete())
                    winners.Add(index);
            }
        );

        Assert.Single(winners);
    }

    [Fact]
    public void Orphan_delete_failures_are_aggregated_without_identity_fields()
    {
        var accumulator = new ReplayOrphanCleanupAccumulator();
        var first = new IOException("first");
        accumulator.ReportDeleteFailure(first);
        accumulator.ReportDeleteFailure(new IOException("second"));

        Assert.True(accumulator.TryBuildResult(out var result));
        Assert.Equal(ReplayPersistenceReasonCode.OrphanDeleteFailed, result.ReasonCode);
        Assert.Equal(2, result.FailedCount);
        Assert.Null(result.Exception);

        ReplayPersistenceLogWriter.EmitOrphanCleanupDegraded(result);
        var captured = Assert.Single(BppLog.Events);
        Assert.Equal(
            "combat_replay.persistence.orphan_cleanup_degraded",
            captured.Definition.EventId
        );
        Assert.Null(captured.Exception);
    }

    [Fact]
    public void Scan_failure_overrides_delete_reason_but_preserves_the_aggregate_count()
    {
        var accumulator = new ReplayOrphanCleanupAccumulator();
        accumulator.ReportDeleteFailure(new IOException("delete"));
        var scan = new IOException("scan");
        accumulator.ReportScanFailure(scan);

        Assert.True(accumulator.TryBuildResult(out var result));
        Assert.Equal(ReplayPersistenceReasonCode.OrphanScanFailed, result.ReasonCode);
        Assert.Equal(1, result.FailedCount);
        Assert.Same(scan, result.Exception);

        ReplayPersistenceLogWriter.EmitOrphanCleanupDegraded(result);
        var captured = Assert.Single(BppLog.Events);
        Assert.Equal(
            "combat_replay.persistence.orphan_cleanup_degraded",
            captured.Definition.EventId
        );
        Assert.Same(scan, captured.Exception);
    }
}
