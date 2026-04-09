from pathlib import Path

from analytics_sync.config import CloudflareConfig, R2Config, SyncConfig
from analytics_sync.jobs.sync_job import run_sync_job
from analytics_sync.compat.v3_models import ParsedBattleComponents, RunBundleUploadRequestV2
from analytics_sync.source.base import SourceRunBundleItem, SourceRunRef


class _FakeProvider:
    def discover_run_refs(self, cursor, limit: int) -> list[SourceRunRef]:
        if cursor.updated_at != "1970-01-01T00:00:00Z":
            return []
        return [
            SourceRunRef(source_updated_at="2026-04-09T00:00:00Z", source_entity_id="run-1", run_id="run-1"),
            SourceRunRef(source_updated_at="2026-04-09T01:00:00Z", source_entity_id="run-2", run_id="run-2"),
        ][:limit]

    def fetch_run_bundles(self, run_ids: list[str]) -> list[SourceRunBundleItem]:
        return [
            SourceRunBundleItem(
                source_updated_at="2026-04-09T00:00:00Z",
                source_entity_id="run-1",
                bundle=RunBundleUploadRequestV2(
                    run_projection={
                        "run_id": "run-1",
                        "player_account_id": "player-1",
                        "status": "completed",
                        "hero_name": "Karnok",
                        "ended_at_utc": "2026-04-09T00:10:00Z",
                        "final_day": 8,
                        "final_wins": 1,
                        "final_losses": 7,
                        "player_rank": "Bronze",
                        "player_rating": 500,
                        "final_player_rank": "Bronze",
                        "final_player_rating": 500,
                    },
                    battle_projections=[
                        {
                            "battle_id": "battle-1",
                            "run_id": "run-1",
                            "recorded_at_utc": "2026-04-09T00:05:00Z",
                        }
                    ],
                    battle_components={
                        "battle-1": ParsedBattleComponents(cards=[], skills=[], temperatures=[]),
                    },
                ),
            ),
            SourceRunBundleItem(
                source_updated_at="2026-04-09T01:00:00Z",
                source_entity_id="run-2",
                bundle=RunBundleUploadRequestV2(
                    run_projection={
                        "run_id": "run-2",
                        "player_account_id": "player-2",
                        "status": "abandoned",
                        "hero_name": "Pygmalien",
                        "ended_at_utc": "2026-04-09T01:10:00Z",
                        "final_day": 4,
                        "final_wins": 0,
                        "final_losses": 2,
                        "player_rank": "Silver",
                        "player_rating": 600,
                        "final_player_rank": "Silver",
                        "final_player_rating": 600,
                    },
                    battle_projections=[
                        {
                            "battle_id": "battle-2",
                            "run_id": "run-2",
                            "recorded_at_utc": "2026-04-09T01:05:00Z",
                        }
                    ],
                    battle_components={
                        "battle-2": ParsedBattleComponents(cards=[], skills=[], temperatures=[]),
                    },
                ),
            ),
        ][: len(run_ids)]


def test_run_sync_job_processes_batch_and_advances_checkpoint(tmp_path: Path, monkeypatch):
    database_path = tmp_path / "analytics.db"
    config = SyncConfig(
        sql_provider="sqlite",
        sqlite_path=database_path,
        mysql=None,
        cloudflare=CloudflareConfig(api_token="token", account_id="account", d1_database_id="db"),
        r2=R2Config(account_id="r2-account", access_key_id="key", secret_access_key="secret", bucket="bucket"),
    )

    monkeypatch.setattr("analytics_sync.jobs.sync_job.build_provider", lambda config: _FakeProvider())

    first = run_sync_job(config, dry_run=False)
    second = run_sync_job(config, dry_run=False)

    assert first.runs_seen == 2
    assert first.runs_written == 2
    assert first.battles_written == 2
    assert second.runs_seen == 0

    from analytics_sync.load.sqlite_client import SqliteClient

    rows = SqliteClient(database_path).fetch_all(
        """
        SELECT status, runs_seen, runs_written, checkpoint_source, checkpoint_entity_id
        FROM sync_job_runs
        ORDER BY id
        """
    )
    assert rows == [
        {
            "status": "success",
            "runs_seen": 2,
            "runs_written": 2,
            "checkpoint_source": "runs_d1",
            "checkpoint_entity_id": "run-2",
        },
        {
            "status": "success",
            "runs_seen": 0,
            "runs_written": 0,
            "checkpoint_source": "runs_d1",
            "checkpoint_entity_id": "run-2",
        },
    ]


def test_run_sync_job_reports_progress(tmp_path: Path, monkeypatch):
    database_path = tmp_path / "analytics.db"
    config = SyncConfig(
        sql_provider="sqlite",
        sqlite_path=database_path,
        mysql=None,
        cloudflare=CloudflareConfig(api_token="token", account_id="account", d1_database_id="db"),
        r2=R2Config(account_id="r2-account", access_key_id="key", secret_access_key="secret", bucket="bucket"),
    )
    messages: list[str] = []

    monkeypatch.setattr("analytics_sync.jobs.sync_job.build_provider", lambda config: _FakeProvider())

    run_sync_job(config, dry_run=False, progress=messages.append)

    assert any("phase=discover" in message for message in messages)
    assert any("phase=execute" in message for message in messages)
