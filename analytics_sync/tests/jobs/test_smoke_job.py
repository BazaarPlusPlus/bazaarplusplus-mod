from analytics_sync.jobs.result import SyncJobResult


def test_sync_job_result_tracks_processed_counts():
    result = SyncJobResult(
        runs_seen=3,
        runs_written=0,
        battles_seen=2,
        battles_written=0,
        skipped_runs=1,
    )

    assert result.runs_seen == 3
    assert result.battles_seen == 2
    assert result.skipped_runs == 1
