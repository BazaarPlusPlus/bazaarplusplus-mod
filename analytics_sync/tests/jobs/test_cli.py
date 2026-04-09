from pathlib import Path

from analytics_sync.cli import main, parse_args
from analytics_sync.config import CloudflareConfig, R2Config, SyncConfig
from analytics_sync.jobs.result import SyncJobResult


def test_parse_args_supports_dry_run():
    args = parse_args(["once", "--dry-run"])

    assert args.command == "once"
    assert args.dry_run is True


def test_parse_args_supports_provider_override():
    args = parse_args(["once", "--provider", "sqlite"])

    assert args.command == "once"
    assert args.provider == "sqlite"


def test_parse_args_supports_backfill_until():
    args = parse_args(["backfill-until", "--until-updated-at", "2026-04-02T00:00:00Z"])

    assert args.command == "backfill-until"
    assert args.until_updated_at == "2026-04-02T00:00:00Z"


def test_main_prints_checkpoint_summary_for_sqlite(tmp_path: Path, monkeypatch, capsys):
    database_path = tmp_path / "analytics.db"

    config = SyncConfig(
        sql_provider="sqlite",
        sqlite_path=database_path,
        mysql=None,
        cloudflare=CloudflareConfig(api_token="token", account_id="account", d1_database_id="db"),
        r2=R2Config(account_id="r2-account", access_key_id="key", secret_access_key="secret", bucket="bucket"),
    )

    monkeypatch.setattr("analytics_sync.cli.SyncConfig.from_env", classmethod(lambda cls: config))
    monkeypatch.setattr(
        "analytics_sync.cli.run_sync_job",
        lambda config, dry_run, progress=None: SyncJobResult(
            runs_seen=2,
            runs_written=1,
            battles_seen=8,
            battles_written=8,
            skipped_runs=1,
            pending_tasks=3,
            failed_tasks=2,
            status="success",
            duration_ms=1234,
            checkpoint_source="runs_d1",
            checkpoint_updated_at="2026-04-10T00:00:00Z",
            checkpoint_entity_id="run-42",
        ),
    )

    exit_code = main(["once"])

    captured = capsys.readouterr().out
    assert exit_code == 0
    assert "provider=sqlite" in captured
    assert f"sqlite_path={database_path}" in captured
    assert "status=success" in captured
    assert "duration_ms=1234" in captured
    assert "runs_seen=2" in captured
    assert "runs_written=1" in captured
    assert "battles_written=8" in captured
    assert "pending_tasks=3" in captured
    assert "failed_tasks=2" in captured
    assert "checkpoint_source=runs_d1" in captured
    assert "checkpoint_updated_at=2026-04-10T00:00:00Z" in captured
    assert "checkpoint_entity_id=run-42" in captured
