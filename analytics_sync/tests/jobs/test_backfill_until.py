from pathlib import Path

from analytics_sync.config import CloudflareConfig, R2Config, SyncConfig
from analytics_sync.jobs.backfill_until import BackfillUntilResult, run_backfill_until
from analytics_sync.jobs.result import SyncJobResult


def test_run_backfill_until_loops_until_target_checkpoint(tmp_path: Path):
    config = SyncConfig(
        sql_provider="sqlite",
        sqlite_path=tmp_path / "analytics.db",
        mysql=None,
        cloudflare=CloudflareConfig(api_token="token", account_id="account", d1_database_id="db"),
        r2=R2Config(account_id="r2-account", access_key_id="key", secret_access_key="secret", bucket="bucket"),
    )
    checkpoints = iter(
        [
            ("1970-01-01T00:00:00Z", ""),
            ("2026-04-01T00:00:00Z", "run-1"),
            ("2026-04-03T00:00:00Z", "run-2"),
        ]
    )
    run_calls: list[str] = []

    result = run_backfill_until(
        config,
        until_updated_at="2026-04-02T00:00:00Z",
        dry_run=False,
        progress=None,
        read_checkpoint=lambda: next(checkpoints),
        run_once=lambda: run_calls.append("once")
        or SyncJobResult(
            runs_seen=1,
            runs_written=1,
            battles_seen=8,
            battles_written=8,
            skipped_runs=0,
            checkpoint_source="runs_d1",
            checkpoint_updated_at="2026-04-01T00:00:00Z",
            checkpoint_entity_id="run-1",
        ),
    )

    assert result == BackfillUntilResult(
        iterations=2,
        reached_target=True,
        stalled=False,
        checkpoint_updated_at="2026-04-03T00:00:00Z",
        checkpoint_entity_id="run-2",
    )
    assert run_calls == ["once", "once"]


def test_run_backfill_until_stops_when_checkpoint_does_not_advance(tmp_path: Path):
    config = SyncConfig(
        sql_provider="sqlite",
        sqlite_path=tmp_path / "analytics.db",
        mysql=None,
        cloudflare=CloudflareConfig(api_token="token", account_id="account", d1_database_id="db"),
        r2=R2Config(account_id="r2-account", access_key_id="key", secret_access_key="secret", bucket="bucket"),
    )
    checkpoints = iter(
        [
            ("1970-01-01T00:00:00Z", ""),
            ("1970-01-01T00:00:00Z", ""),
        ]
    )

    result = run_backfill_until(
        config,
        until_updated_at="2026-04-02T00:00:00Z",
        dry_run=False,
        progress=None,
        read_checkpoint=lambda: next(checkpoints),
        run_once=lambda: SyncJobResult(
            runs_seen=0,
            runs_written=0,
            battles_seen=0,
            battles_written=0,
            skipped_runs=0,
            checkpoint_source="runs_d1",
            checkpoint_updated_at="1970-01-01T00:00:00Z",
            checkpoint_entity_id="",
        ),
    )

    assert result.reached_target is False
    assert result.stalled is True
